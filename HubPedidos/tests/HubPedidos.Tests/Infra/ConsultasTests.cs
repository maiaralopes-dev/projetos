using HubPedidos.Application.Persistencia;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace HubPedidos.Tests.Infra;

/// <summary>Banco com 2.000 pedidos, compartilhado (somente leitura) entre os testes da classe.</summary>
public sealed class BancoSemeado : IAsyncLifetime
{
    public BancoEmMemoria Banco { get; private set; } = null!;

    public async Task InitializeAsync() => Banco = await BancoEmMemoria.CriarAsync(semear: 2_000);

    public async Task DisposeAsync() => await Banco.DisposeAsync();
}

public class ConsultasTests(BancoSemeado fixture) : IClassFixture<BancoSemeado>
{
    private readonly BancoEmMemoria _banco = fixture.Banco;

    [Fact]
    public async Task Pagina_traz_somente_colunas_e_linhas_necessarias()
    {
        await using var db = _banco.NovoContexto();
        _banco.Contador.Limpar();

        var pagina = await new ConsultasPedidos(db).ListarAsync(new FiltroPedidos { Pagina = 3, Tamanho = 10 }, default);

        Assert.Equal(10, pagina.Itens.Count);
        Assert.Equal(2_000, pagina.Total);
        Assert.Equal(200, pagina.TotalPaginas);
        Assert.Equal(2, _banco.Contador.Comandos.Count); // COUNT + página

        var sql = _banco.Contador.Comandos[1];
        Assert.Contains("LIMIT @", sql);
        Assert.Contains("OFFSET @", sql);
        Assert.Contains("SELECT COUNT(*)", sql); // contagem de itens como subconsulta, sem carregar itens
        foreach (var coluna in new[] { "Metadados", "Entrega_", "Prioridade", "PrecoUnitarioCentavos", "\"i\".\"Sku\"" })
        {
            Assert.DoesNotContain(coluna, sql);
        }
    }

    [Fact]
    public async Task Ordenacao_e_deterministica_e_paginas_cobrem_tudo_sem_repetir()
    {
        await using var db = _banco.NovoContexto();
        var consultas = new ConsultasPedidos(db);
        var filtro = new FiltroPedidos { Regiao = Regiao.Sul, Tamanho = 37 };

        var primeira = await consultas.ListarAsync(filtro, default);
        var todos = new List<Application.Contratos.PedidoResumoResponse>();
        for (var p = 1; p <= primeira.TotalPaginas; p++)
        {
            todos.AddRange((await consultas.ListarAsync(filtro with { Pagina = p }, default)).Itens);
        }

        Assert.Equal(primeira.Total, todos.Count);
        Assert.Equal(todos.Count, todos.Select(t => t.Id).Distinct().Count());
        Assert.All(todos, t => Assert.Equal("Sul", t.Regiao));
        Assert.Equal(todos.OrderByDescending(t => t.CriadoEmUtc).ThenByDescending(t => t.Id.ToString("D").ToUpperInvariant(), StringComparer.Ordinal), todos);
    }

    [Fact]
    public async Task Valores_do_usuario_viram_parametros_e_nao_mudam_a_forma_do_sql()
    {
        await using var db = _banco.NovoContexto();
        var consultas = new ConsultasPedidos(db);

        _banco.Contador.Limpar();
        await consultas.ListarAsync(new FiltroPedidos { ClienteId = "CLI-00001", Status = StatusPedido.Concluido }, default);
        var sqlA = _banco.Contador.Comandos.ToArray();

        _banco.Contador.Limpar();
        await consultas.ListarAsync(new FiltroPedidos { ClienteId = "x' OR 1=1 --", Status = StatusPedido.Cancelado }, default);
        var sqlB = _banco.Contador.Comandos.ToArray();

        Assert.Equal(sqlA, sqlB); // mesmo texto => mesmo plano e mesma entrada no cache do EF
        Assert.All(sqlA.Concat(sqlB), s =>
        {
            Assert.DoesNotContain("CLI-00001", s);
            Assert.DoesNotContain("OR 1=1", s);
            Assert.Contains("\"p\".\"ClienteId\" = @", s);
        });
    }

    [Theory]
    [InlineData(5)]
    [InlineData(50)]
    public async Task Relatorio_de_pedidos_e_itens_usa_um_unico_comando(int tamanho)
    {
        await using var db = _banco.NovoContexto();
        var consultas = new ConsultasPedidos(db);

        var relatorio = await _banco.Contador.NoMaximoAsync(1, () => consultas.RelatorioComItensAsync(2, tamanho, default));

        Assert.Equal(tamanho, relatorio.Count);
        Assert.All(relatorio, p =>
        {
            Assert.NotEmpty(p.Itens);
            Assert.Equal(p.Total, p.Itens.Sum(i => i.Subtotal)); // subtotal calculado no SQL bate com o total gravado
        });
    }

    [Fact]
    public async Task Abordagem_ingenua_gera_N_mais_1_e_a_guarda_detecta()
    {
        await using var db = _banco.NovoContexto();

        // Anti-exemplo: carrega a página e busca os itens de cada pedido em um loop.
        async Task<int> Ingenua()
        {
            var pedidos = await db.Pedidos.AsNoTracking().OrderByDescending(p => p.CriadoEmUtc).Take(20).ToListAsync();
            var itens = 0;
            foreach (var p in pedidos)
            {
                itens += await db.Itens.CountAsync(i => i.PedidoId == p.Id);
            }

            return itens;
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _banco.Contador.NoMaximoAsync(1, Ingenua));
        Assert.StartsWith("Esperado no máximo 1 comando(s) SQL, executados 21", ex.Message);
    }

    [Fact]
    public async Task Consulta_dividida_usa_dois_comandos_e_produz_o_mesmo_resultado()
    {
        await using var db = _banco.NovoContexto();
        var consultas = new ConsultasPedidos(db);

        var unica = await consultas.RelatorioComItensAsync(1, 30, default);
        _banco.Contador.Limpar();
        var dividida = await consultas.RelatorioComItensDivididoAsync(1, 30, default);

        Assert.Equal(2, _banco.Contador.Comandos.Count);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(unica), System.Text.Json.JsonSerializer.Serialize(dividida));
    }

    [Fact]
    public async Task Cursor_percorre_a_mesma_sequencia_que_o_offset()
    {
        await using var db = _banco.NovoContexto();
        var consultas = new ConsultasPedidos(db);

        var porOffset = (await consultas.ListarAsync(new FiltroPedidos { Tamanho = 100 }, default)).Itens.Select(i => i.Id).ToList();
        var porCursor = new List<Guid>();
        string? cursor = null;
        while (porCursor.Count < 100)
        {
            var pagina = await consultas.ListarPorCursorAsync(cursor, 7, default);
            porCursor.AddRange(pagina.Itens.Select(i => i.Id));
            cursor = pagina.ProximoCursor;
        }

        Assert.Equal(porOffset, porCursor.Take(100));
    }

    [Theory]
    [InlineData("lixo")]
    [InlineData("")]
    [InlineData("AAAA")]
    public async Task Cursor_invalido_e_tratado_como_primeira_pagina(string cursor)
    {
        await using var db = _banco.NovoContexto();
        var consultas = new ConsultasPedidos(db);

        var primeira = await consultas.ListarPorCursorAsync(null, 5, default);
        var comLixo = await consultas.ListarPorCursorAsync(cursor, 5, default);

        Assert.Equal(primeira.Itens.Select(i => i.Id), comLixo.Itens.Select(i => i.Id));
    }

    [Fact]
    public async Task Consulta_compilada_devolve_o_mesmo_que_a_normal()
    {
        await using var db = _banco.NovoContexto();
        var consultas = new ConsultasPedidos(db);
        var id = (await consultas.ListarAsync(new FiltroPedidos { Tamanho = 1 }, default)).Itens[0].Id;

        Assert.Equal(await consultas.ObterResumoAsync(id, default), await consultas.ObterResumoCompiladoAsync(id, default));
        Assert.Null(await consultas.ObterResumoCompiladoAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Painel_agrega_no_banco_e_bate_com_o_calculo_em_memoria()
    {
        await using var db = _banco.NovoContexto();
        var esperado = (await db.Pedidos.AsNoTracking().Include(p => p.Itens).ToListAsync())
            .GroupBy(p => p.Regiao)
            .ToDictionary(g => g.Key.ToString(), g => (Pedidos: g.Count(), Total: g.Sum(p => p.Total), Unidades: g.Sum(p => p.Itens.Sum(i => i.Quantidade))));
        _banco.Contador.Limpar();

        var painel = await new ConsultasPedidos(db).PainelPorRegiaoAsync(default);

        Assert.Equal(2, _banco.Contador.Comandos.Count);
        Assert.All(_banco.Contador.Comandos, c => Assert.Contains("GROUP BY", c));
        Assert.Equal(6, painel.Count);
        Assert.All(painel, r => Assert.Equal(esperado[r.Regiao], (r.Pedidos, r.Total, r.Unidades)));
    }
}
