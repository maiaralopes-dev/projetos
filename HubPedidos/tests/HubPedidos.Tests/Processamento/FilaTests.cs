using System.Threading.Channels;
using HubPedidos.Application.Classificacao;
using HubPedidos.Processamento.Fila;

namespace HubPedidos.Tests.Processamento;

public class FilaTests
{
    private static OpcoesFila Opcoes(int capacidade, int esperaMs = 0, BoundedChannelFullMode modo = BoundedChannelFullMode.Wait, int consumidores = 0) =>
        new() { Capacidade = capacidade, TempoMaximoEsperaMs = esperaMs, ModoCheio = modo, Consumidores = consumidores };

    [Fact]
    public async Task Modo_wait_faz_o_produtor_aguardar_ate_haver_espaco()
    {
        await using var b = new Bancada(Opcoes(capacidade: 2, esperaMs: 5_000));
        await b.EnfileirarAsync(2);

        var terceiro = b.Fila.EnfileirarAsync(Bancada.NovoPedido(), CategoriaProcessamento.Padrao, default).AsTask();
        await Task.Delay(50);
        Assert.False(terceiro.IsCompleted); // produtor bloqueado: backpressure

        Assert.True(b.Fila.Leitor.TryRead(out _)); // um consumidor libera uma vaga
        Assert.Equal(ResultadoEnfileiramento.Aceito, await terceiro.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, b.Fila.Profundidade);
    }

    [Fact]
    public async Task Espera_esgotada_vira_rejeicao_explicita()
    {
        await using var b = new Bancada(Opcoes(capacidade: 1, esperaMs: 30));
        await b.EnfileirarAsync(1);

        Assert.Equal(ResultadoEnfileiramento.RejeitadoFilaCheia, await b.Fila.EnfileirarAsync(Bancada.NovoPedido(), CategoriaProcessamento.Padrao, default));
        Assert.Equal(1, b.Metricas.RejeitadosFilaCheia);
        Assert.Equal(0, b.Metricas.Descartados); // rejeitado != descartado
    }

    [Fact]
    public async Task Sem_espera_TryWrite_rejeita_imediatamente()
    {
        await using var b = new Bancada(Opcoes(capacidade: 1, esperaMs: 0));
        await b.EnfileirarAsync(1);

        var tentativa = b.Fila.EnfileirarAsync(Bancada.NovoPedido(), CategoriaProcessamento.Padrao, default);

        Assert.True(tentativa.IsCompletedSuccessfully); // nem chegou a esperar
        Assert.Equal(ResultadoEnfileiramento.RejeitadoFilaCheia, await tentativa);
    }

    [Fact]
    public async Task Cancelamento_do_cliente_propaga_e_nao_conta_como_rejeicao()
    {
        await using var b = new Bancada(Opcoes(capacidade: 1, esperaMs: 10_000));
        await b.EnfileirarAsync(1);
        using var cts = new CancellationTokenSource(30);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            b.Fila.EnfileirarAsync(Bancada.NovoPedido(), CategoriaProcessamento.Padrao, cts.Token).AsTask());
        Assert.Equal(0, b.Metricas.RejeitadosFilaCheia);
    }

    [Theory]
    [InlineData(BoundedChannelFullMode.DropOldest, new[] { 2, 3, 4 })]
    [InlineData(BoundedChannelFullMode.DropNewest, new[] { 0, 1, 4 })]
    [InlineData(BoundedChannelFullMode.DropWrite, new[] { 0, 1, 2 })]
    public async Task Modos_de_descarte_aceitam_tudo_e_descartam_conforme_a_politica(BoundedChannelFullMode modo, int[] restantes)
    {
        await using var b = new Bancada(Opcoes(capacidade: 3, modo: modo));

        var ids = await b.EnfileirarAsync(5); // todos "Aceito": o produtor não percebe o descarte

        Assert.Equal(5, b.Metricas.Aceitos);
        Assert.Equal(2, b.Metricas.Descartados);
        var naFila = new List<Guid>();
        while (b.Fila.Leitor.TryRead(out var job))
        {
            naFila.Add(job.PedidoId);
        }

        Assert.Equal(restantes.Select(i => ids[i]), naFila);
    }

    [Fact]
    public async Task Job_e_imutavel_e_nao_carrega_tipos_do_ef()
    {
        var tipo = typeof(ProcessarPedidoJob);

        Assert.NotNull(tipo.GetMethod("<Clone>$"));
        Assert.All(tipo.GetProperties(), p =>
        {
            Assert.False(p.SetMethod?.IsPublic == true && !p.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)), $"{p.Name} mutável");
            Assert.DoesNotContain("EntityFrameworkCore", p.PropertyType.Namespace ?? "");
        });
        await Task.CompletedTask;
    }
}
