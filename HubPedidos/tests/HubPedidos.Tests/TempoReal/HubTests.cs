using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using HubPedidos.ClienteTempoReal;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using HubPedidos.Tests.Processamento;

namespace HubPedidos.Tests.TempoReal;

[Collection(nameof(TempoRealColecao))]
public class HubTests(ServidorReal servidor)
{
    private static readonly BackoffComTeto RetentativaRapida = new([TimeSpan.Zero, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400)], TimeSpan.FromSeconds(10));

    private ClientePedidosTempoReal Cliente(string? cliente, bool operador = false, Uri? via = null)
    {
        var token = servidor.Token(cliente, operador);
        return new ClientePedidosTempoReal(via ?? servidor.Endereco, () => token, RetentativaRapida);
    }

    [Fact]
    public async Task Sem_token_a_negociacao_e_recusada()
    {
        await using var conexao = new HubConnectionBuilder().WithUrl(new Uri(servidor.Endereco, "/hubs/pedidos")).Build();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => conexao.StartAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task Cliente_so_assina_e_recebe_os_proprios_pedidos()
    {
        var meu = await servidor.CriarPedidoAsync("CLI-A1");
        var alheio = await servidor.CriarPedidoAsync("CLI-B1");
        await using var a = Cliente("CLI-A1");
        await using var operador = Cliente(null, operador: true);
        var recebidosA = new ConcurrentQueue<PedidoAtualizadoMensagem>();
        var recebidosOperador = new ConcurrentQueue<PedidoAtualizadoMensagem>();
        a.PedidoAtualizado += recebidosA.Enqueue;
        operador.PedidoAtualizado += recebidosOperador.Enqueue;
        await a.IniciarAsync();
        await operador.IniciarAsync();
        await a.AssinarAsync(meu);
        await operador.AssinarAsync(alheio);

        // Autorização no servidor: o grupo do pedido alheio é inacessível.
        var negado = await Assert.ThrowsAsync<HubException>(() => a.AssinarAsync(alheio));
        Assert.Contains("Acesso negado", negado.Message);
        await Assert.ThrowsAsync<HubException>(() => a.AssinarAsync(Guid.NewGuid())); // inexistente: mesma resposta

        await servidor.AlterarStatusAsync(alheio, "EmProcessamento");
        await servidor.AlterarStatusAsync(meu, "EmProcessamento");

        await Bancada.AguardarAsync(() => recebidosA.Any(m => m.PedidoId == meu), "A recebe o próprio pedido");
        await Bancada.AguardarAsync(() => recebidosOperador.Any(m => m.PedidoId == alheio), "operador recebe o pedido de B");
        Assert.DoesNotContain(recebidosA, m => m.PedidoId == alheio);
    }

    [Fact]
    public async Task Evento_chega_depois_da_persistencia_e_o_http_confirma()
    {
        var id = await servidor.CriarPedidoAsync("CLI-P1");
        await using var c = Cliente("CLI-P1");
        var recebido = new TaskCompletionSource<PedidoAtualizadoMensagem>(TaskCreationOptions.RunContinuationsAsynchronously);
        c.PedidoAtualizado += m => recebido.TrySetResult(m);
        await c.IniciarAsync();
        await c.AssinarAsync(id);

        await servidor.AlterarStatusAsync(id, "Cancelado");
        var mensagem = await recebido.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // No instante em que o evento chega, o estado já está gravado.
        using var get = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/pedidos/{id}");
        get.Headers.Authorization = new AuthenticationHeaderValue("Bearer", servidor.Token("CLI-P1"));
        Assert.Contains("\"status\":\"Cancelado\"", await (await servidor.Http.SendAsync(get)).Content.ReadAsStringAsync());
        Assert.Equal("Cancelado", mensagem.Status);
        Assert.True(mensagem.Sequencia > 0);
    }

    [Fact]
    public async Task Reconexao_reassina_e_reconcilia_eventos_perdidos_pela_queda()
    {
        var id = await servidor.CriarPedidoAsync("CLI-R1");
        await using var proxy = new ProxyFalhas(servidor.Endereco.Port);
        await using var c = Cliente("CLI-R1", via: proxy.Endereco);
        var estados = new ConcurrentQueue<(EstadoConexao Estado, string Mensagem)>();
        var reconciliados = new ConcurrentQueue<PedidoResumo>();
        c.EstadoMudou += (e, m) => estados.Enqueue((e, m));
        c.Reconciliado += reconciliados.Enqueue;
        await c.IniciarAsync();
        await c.AssinarAsync(id);
        Assert.Equal("Recebido", c.StatusLocal(id));

        // Queda de rede: conexões derrubadas e novas recusadas.
        proxy.Recusar(true);
        proxy.DerrubarTudo();
        await Bancada.AguardarAsync(() => c.Estado == EstadoConexao.Reconectando, "estado Reconectando");
        Assert.False(c.AcoesHabilitadas); // a interface desabilita ações dependentes da conexão
        Assert.Contains(estados, e => e.Estado == EstadoConexao.Reconectando && e.Mensagem.Contains("reconectar"));

        // Durante a queda o pedido muda: o evento é PERDIDO (não há replay).
        await servidor.AlterarStatusAsync(id, "EmProcessamento");

        proxy.Recusar(false);
        await Bancada.AguardarAsync(() => c.Estado == EstadoConexao.Conectado, "reconectado", 10_000);

        // Reconciliação via HTTP recupera o estado correto.
        await Bancada.AguardarAsync(() => c.StatusLocal(id) == "EmProcessamento", "estado reconciliado por HTTP");
        Assert.Contains(reconciliados, r => r.Id == id && r.Status == "EmProcessamento");
        Assert.True(c.AcoesHabilitadas);

        // E a reassinatura funcionou: eventos novos voltam a chegar.
        var novo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        c.PedidoAtualizado += m => { if (m.Status == "Concluido") novo.TrySetResult(); };
        await servidor.AlterarStatusAsync(id, "Concluido");
        await novo.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Revogacao_tira_dos_grupos_avisa_e_impede_reassinar()
    {
        var id = await servidor.CriarPedidoAsync("CLI-X1");
        await using var c = Cliente("CLI-X1");
        var revogado = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventos = new ConcurrentQueue<PedidoAtualizadoMensagem>();
        c.AcessoRevogado += m => revogado.TrySetResult(m);
        c.PedidoAtualizado += eventos.Enqueue;
        await c.IniciarAsync();
        await c.AssinarAsync(id);

        using var admin = new HttpRequestMessage(HttpMethod.Post, "/admin/clientes/CLI-X1/revogar");
        admin.Headers.Authorization = new AuthenticationHeaderValue("Bearer", servidor.Token(null, operador: true));
        (await servidor.Http.SendAsync(admin)).EnsureSuccessStatusCode();
        Assert.Contains("revogado", await revogado.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        await servidor.AlterarStatusAsync(id, "Cancelado");
        await Task.Delay(300); // janela curta: nenhum evento deve chegar
        Assert.Empty(eventos);
        Assert.Empty(c.Assinados);
        await Assert.ThrowsAsync<HubException>(() => c.AssinarAsync(id));

        using var restaurar = new HttpRequestMessage(HttpMethod.Post, "/admin/clientes/CLI-X1/restaurar");
        restaurar.Headers.Authorization = new AuthenticationHeaderValue("Bearer", servidor.Token(null, operador: true));
        (await servidor.Http.SendAsync(restaurar)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Revogar_exige_operador()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/admin/clientes/CLI-1/revogar");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", servidor.Token("CLI-1"));

        Assert.Equal(HttpStatusCode.Forbidden, (await servidor.Http.SendAsync(req)).StatusCode);
    }
}

[CollectionDefinition(nameof(TempoRealColecao))]
public sealed class TempoRealColecao : ICollectionFixture<ServidorReal>;

public class ClienteTempoRealTests
{
    [Fact]
    public void Duplicatas_e_eventos_fora_de_ordem_sao_descartados()
    {
        var cliente = new ClientePedidosTempoReal(new Uri("http://127.0.0.1:1"), () => null);
        var id = Guid.NewGuid();
        cliente.AssinarLocal(id, "Recebido");
        var aplicados = new List<string>();
        cliente.PedidoAtualizado += m => aplicados.Add(m.Status);

        Assert.True(cliente.Aplicar(new(id, "EmProcessamento", 5, DateTimeOffset.UtcNow)));
        Assert.False(cliente.Aplicar(new(id, "EmProcessamento", 5, DateTimeOffset.UtcNow))); // duplicata
        Assert.False(cliente.Aplicar(new(id, "Recebido", 3, DateTimeOffset.UtcNow)));         // atrasado
        Assert.True(cliente.Aplicar(new(id, "Concluido", 9, DateTimeOffset.UtcNow)));
        Assert.False(cliente.Aplicar(new(Guid.NewGuid(), "Concluido", 10, DateTimeOffset.UtcNow))); // não assinado

        Assert.Equal(["EmProcessamento", "Concluido"], aplicados);
        Assert.Equal("Concluido", cliente.StatusLocal(id));
    }
}
