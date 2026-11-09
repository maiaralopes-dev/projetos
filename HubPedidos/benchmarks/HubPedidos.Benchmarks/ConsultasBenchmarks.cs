using BenchmarkDotNet.Attributes;
using HubPedidos.Application.Contratos;
using HubPedidos.Application.Persistencia;
using HubPedidos.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HubPedidos.Benchmarks;

/// <summary>
/// SQLite em memória com 20.000 pedidos (~60 mil itens), mesma massa da API em Development.
/// Banco em memória remove o disco da medição: o que sobra é CPU do SQLite + overhead do EF.
/// </summary>
[MemoryDiagnoser]
public class ConsultasBenchmarks
{
    private SqliteConnection _conexao = null!;
    private HubDbContext _db = null!;
    private ConsultasPedidos _consultas = null!;
    private SqliteCommand _comandoAdo = null!;
    private Guid[] _ids = [];
    private int _proximo;
    private string _cursorProfundo = "";

    [GlobalSetup]
    public async Task PrepararAsync()
    {
        _conexao = new SqliteConnection("DataSource=:memory:");
        await _conexao.OpenAsync();
        _db = new HubDbContext(new DbContextOptionsBuilder<HubDbContext>().UseSqlite(_conexao).Options);
        await _db.Database.MigrateAsync();
        await GeradorMassa.SemearAsync(_db, 20_000);
        _consultas = new ConsultasPedidos(_db);

        _ids = await _db.Pedidos.OrderBy(p => p.ClienteId).Select(p => p.Id).Take(512).ToArrayAsync();

        // Cursor posicionado no mesmo ponto da página 900 por offset.
        var anterior = await _consultas.ListarAsync(new FiltroPedidos { Pagina = 899, Tamanho = 20 }, default);
        _cursorProfundo = CursorPublico(anterior.Itens[^1]);

        // Mesmo SQL que o EF gera para o resumo (sem os CASE de enum), executado direto.
        _comandoAdo = _conexao.CreateCommand();
        _comandoAdo.CommandText = """
            SELECT "p"."Id", "p"."ClienteId", "p"."Regiao", "p"."Status", "p"."Categoria", "p"."TotalCentavos",
                   (SELECT COUNT(*) FROM "ItensPedido" AS "i" WHERE "p"."Id" = "i"."PedidoId"), "p"."CriadoEmUtc"
            FROM "Pedidos" AS "p" WHERE "p"."Id" = $id LIMIT 1
            """;
        _comandoAdo.Parameters.Add("$id", SqliteType.Text);
        _comandoAdo.Prepare();
    }

    [GlobalCleanup]
    public async Task LimparAsync()
    {
        await _db.DisposeAsync();
        await _conexao.DisposeAsync();
    }

    private Guid ProximoId() => _ids[_proximo++ & 511];

    // ---- Consulta repetida por chave: normal × compilada × ADO.NET ----

    [Benchmark(Baseline = true), BenchmarkCategory("PorId")]
    public Task<PedidoResumoResponse?> PorId_Normal() => _consultas.ObterResumoAsync(ProximoId(), default);

    [Benchmark, BenchmarkCategory("PorId")]
    public Task<PedidoResumoResponse?> PorId_Compilada() => _consultas.ObterResumoCompiladoAsync(ProximoId(), default);

    [Benchmark, BenchmarkCategory("PorId")]
    public async Task<PedidoResumoResponse?> PorId_AdoNet()
    {
        _comandoAdo.Parameters[0].Value = ProximoId().ToString("D").ToUpperInvariant();
        await using var leitor = await _comandoAdo.ExecuteReaderAsync();
        if (!await leitor.ReadAsync())
        {
            return null;
        }

        return new PedidoResumoResponse(
            leitor.GetGuid(0), leitor.GetString(1),
            ((Domain.Pedidos.Regiao)leitor.GetInt32(2)).ToString(),
            ((Domain.Pedidos.StatusPedido)leitor.GetInt32(3)).ToString(),
            ((Application.Classificacao.CategoriaProcessamento)leitor.GetInt32(4)).ToString(),
            leitor.GetInt64(5) / 100m, leitor.GetInt32(6), leitor.GetDateTime(7));
    }

    // ---- Paginação: offset raso, offset profundo, cursor profundo ----

    [Benchmark, BenchmarkCategory("Paginacao")]
    public Task<PaginaResponse<PedidoResumoResponse>> Offset_Pagina1() =>
        _consultas.ListarAsync(new FiltroPedidos { Pagina = 1, Tamanho = 20 }, default);

    [Benchmark, BenchmarkCategory("Paginacao")]
    public Task<PaginaResponse<PedidoResumoResponse>> Offset_Pagina900() =>
        _consultas.ListarAsync(new FiltroPedidos { Pagina = 900, Tamanho = 20 }, default);

    [Benchmark, BenchmarkCategory("Paginacao")]
    public Task<PaginaCursorResponse<PedidoResumoResponse>> Cursor_Posicao900() =>
        _consultas.ListarPorCursorAsync(_cursorProfundo, 20, default);

    // ---- Grafo controlado: 100 pedidos com itens ----

    [Benchmark, BenchmarkCategory("Relatorio")]
    public Task<IReadOnlyList<PedidoComItensResponse>> Relatorio_ConsultaUnica() =>
        _consultas.RelatorioComItensAsync(1, 100, default);

    [Benchmark, BenchmarkCategory("Relatorio")]
    public Task<IReadOnlyList<PedidoComItensResponse>> Relatorio_ConsultaDividida() =>
        _consultas.RelatorioComItensDivididoAsync(1, 100, default);

    private static string CursorPublico(PedidoResumoResponse ultimo) =>
        System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.ASCII.GetBytes($"{ultimo.CriadoEmUtc.Ticks}:{ultimo.Id:N}"));
}
