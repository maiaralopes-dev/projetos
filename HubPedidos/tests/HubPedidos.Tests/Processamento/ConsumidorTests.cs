using HubPedidos.Application.Processamento;

namespace HubPedidos.Tests.Processamento;

public class ConsumidorTests
{
    [Fact]
    public async Task Concorrencia_no_banco_nunca_passa_do_limite()
    {
        var repositorio = new RepositorioFalso { Atraso = TimeSpan.FromMilliseconds(10) };
        await using var b = new Bancada(new OpcoesFila { Capacidade = 100, Consumidores = 8, LimiteBanco = 3 }, repositorio);
        await b.EnfileirarAsync(40);

        await b.IniciarAsync();
        await Bancada.AguardarAsync(() => b.Metricas.Processados == 40, "40 processados");
        await b.PararAsync();

        Assert.Equal(3, repositorio.MaximoConcorrente);   // medido no próprio "banco"
        Assert.Equal(3, b.Limitador.MaximoObservado);
        Assert.Equal(3, b.Limitador.Disponiveis);
        b.AssertBalanco();
    }

    [Fact]
    public async Task Falha_de_um_item_nao_derruba_o_consumidor_nem_perde_slot()
    {
        var repositorio = new RepositorioFalso();
        await using var b = new Bancada(new OpcoesFila { Capacidade = 100, Consumidores = 1, LimiteBanco = 2 }, repositorio);
        var ids = await b.EnfileirarAsync(30);
        foreach (var id in ids.Where((_, i) => i % 6 == 0))
        {
            repositorio.FalharPara.Add(id);
        }

        await b.IniciarAsync();
        await Bancada.AguardarAsync(() => b.Metricas.Processados + b.Metricas.Falhas == 30, "30 itens concluídos");

        Assert.Equal(5, b.Metricas.Falhas);
        Assert.Equal(25, b.Metricas.Processados);
        Assert.Equal(2, b.Limitador.Disponiveis); // nenhum slot perdido nas exceções

        // O único consumidor continua vivo depois das falhas:
        await b.EnfileirarAsync(1);
        await Bancada.AguardarAsync(() => b.Metricas.Processados == 26, "item após falhas");
        await b.PararAsync();
        b.AssertBalanco();
    }

    [Fact]
    public async Task Nenhum_pedido_e_processado_duas_vezes()
    {
        var repositorio = new RepositorioFalso();
        await using var b = new Bancada(new OpcoesFila { Capacidade = 600, Consumidores = 4, LimiteBanco = 4 }, repositorio);
        var ids = await b.EnfileirarAsync(500);

        await b.IniciarAsync();
        await Bancada.AguardarAsync(() => b.Metricas.Processados == 500, "500 processados");
        await b.PararAsync();

        Assert.Equal(ids.Order(), repositorio.Gravacoes.Keys.Order());
        Assert.All(repositorio.Gravacoes.Values, n => Assert.Equal(1, n));
        Assert.Equal(500, b.Metricas.ProcessadosPorCategoria["Padrao"]);
    }

    [Fact]
    public async Task Encerramento_drenar_recusa_novos_e_processa_os_pendentes()
    {
        var repositorio = new RepositorioFalso { UsarPortao = true };
        await using var b = new Bancada(new OpcoesFila { Capacidade = 50, Consumidores = 2, Encerramento = PoliticaEncerramento.Drenar }, repositorio);
        await b.EnfileirarAsync(10);
        await b.IniciarAsync();
        await Bancada.AguardarAsync(() => b.Metricas.EmAndamento > 0, "processamento começou");

        var parada = b.PararAsync();
        await Bancada.AguardarAsync(() => b.Fila.Encerrada, "fila encerrada");
        Assert.Equal(ResultadoEnfileiramento.RejeitadoEncerrando, await b.Fila.EnfileirarAsync(Bancada.NovoPedido(), Application.Classificacao.CategoriaProcessamento.Padrao, default));
        Assert.False(parada.IsCompleted); // esperando drenar

        repositorio.Portao.SetResult();
        await parada.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(10, b.Metricas.Processados);
        Assert.Equal(0, b.Metricas.Abandonados);
        Assert.Equal(1, b.Metricas.RejeitadosEncerrando);
        b.AssertBalanco();
    }

    [Fact]
    public async Task Encerramento_abandonar_interrompe_e_contabiliza_os_pendentes()
    {
        var repositorio = new RepositorioFalso { UsarPortao = true }; // portão nunca abre
        await using var b = new Bancada(new OpcoesFila { Capacidade = 50, Consumidores = 1, Encerramento = PoliticaEncerramento.Abandonar }, repositorio);
        await b.EnfileirarAsync(10);
        await b.IniciarAsync();
        await Bancada.AguardarAsync(() => b.Metricas.EmAndamento == 1, "um item em andamento");

        await b.PararAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, b.Metricas.Processados);
        Assert.Equal(10, b.Metricas.Abandonados); // 1 interrompido + 9 que nem saíram da fila
        Assert.Equal(0, b.Fila.Profundidade);
        b.AssertBalanco();
    }

    [Fact]
    public async Task Prazo_de_drenagem_esgotado_nao_trava_o_encerramento()
    {
        var repositorio = new RepositorioFalso { UsarPortao = true };
        await using var b = new Bancada(new OpcoesFila { Capacidade = 50, Consumidores = 2, Encerramento = PoliticaEncerramento.Drenar }, repositorio);
        await b.EnfileirarAsync(6);
        await b.IniciarAsync();
        await Bancada.AguardarAsync(() => b.Metricas.EmAndamento == 2, "dois em andamento");

        await b.PararAsync(TimeSpan.FromMilliseconds(200)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(6, b.Metricas.Abandonados);
        b.AssertBalanco();
    }
}

public class ProcessamentoEmLoteTests
{
    [Fact]
    public async Task Lote_processa_todos_uma_vez_com_menos_idas_ao_banco()
    {
        var repositorio = new RepositorioFalso();
        await using var b = new Bancada(new OpcoesFila { Capacidade = 600, Consumidores = 2, LimiteBanco = 1, TamanhoLote = 50 }, repositorio);
        var ids = await b.EnfileirarAsync(500); // fila já cheia antes de iniciar: lotes completos

        await b.IniciarAsync();
        await Bancada.AguardarAsync(() => b.Metricas.Processados == 500, "500 processados em lote");
        await b.PararAsync();

        Assert.Equal(ids.Order(), repositorio.Gravacoes.Keys.Order());
        Assert.All(repositorio.Gravacoes.Values, n => Assert.Equal(1, n));
        Assert.Equal(10, repositorio.ChamadasLote); // 500 / 50, em vez de 500 inserções
        b.AssertBalanco();
    }

    [Fact]
    public async Task Falha_no_lote_cai_para_item_a_item_e_isola_o_problematico()
    {
        var repositorio = new RepositorioFalso();
        await using var b = new Bancada(new OpcoesFila { Capacidade = 100, Consumidores = 1, TamanhoLote = 10 }, repositorio);
        var ids = await b.EnfileirarAsync(10);
        repositorio.FalharPara.Add(ids[3]);

        await b.IniciarAsync();
        await Bancada.AguardarAsync(() => b.Metricas.Processados + b.Metricas.Falhas == 10, "10 concluídos");
        await b.PararAsync();

        Assert.Equal(9, b.Metricas.Processados);
        Assert.Equal(1, b.Metricas.Falhas);
        Assert.DoesNotContain(ids[3], repositorio.Gravacoes.Keys);
        Assert.All(repositorio.Gravacoes.Values, n => Assert.Equal(1, n)); // lote desfeito: ninguém gravado duas vezes
        b.AssertBalanco();
    }
}
