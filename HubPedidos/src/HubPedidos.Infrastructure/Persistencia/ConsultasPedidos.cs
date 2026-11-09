using System.Buffers.Text;
using System.Linq.Expressions;
using System.Text;
using HubPedidos.Application.Contratos;
using HubPedidos.Application.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace HubPedidos.Infrastructure.Persistencia;

public sealed class ConsultasPedidos(HubDbContext db) : IConsultasPedidos
{
    /// <summary>
    /// Projeção reutilizável. O EF traduz para SQL só as colunas usadas aqui; a contagem
    /// de itens vira subconsulta correlacionada (sem carregar os itens).
    /// Os ToString() de enum ficam na projeção final e rodam no cliente. O Id sai como
    /// Guid: p.Id.ToString() seria traduzido para SQL e devolveria o TEXT em maiúsculas.
    /// </summary>
    private static readonly Expression<Func<PedidoRegistro, PedidoResumoResponse>> ProjecaoResumo = p =>
        new PedidoResumoResponse(
            p.Id,
            p.ClienteId,
            p.Regiao.ToString(),
            p.Status.ToString(),
            p.Categoria.ToString(),
            p.Total,
            p.Itens.Count,
            p.CriadoEmUtc);

    /// <summary>
    /// Consulta compilada (EF.CompileAsyncQuery): a árvore LINQ é traduzida uma vez e o
    /// delegate é reaproveitado, pulando a busca no cache de consultas a cada chamada.
    /// Restrição: fica presa ao primeiro modelo com que é executada.
    /// </summary>
    private static readonly Func<HubDbContext, Guid, CancellationToken, Task<PedidoResumoResponse?>> ResumoCompilado =
        EF.CompileAsyncQuery((HubDbContext ctx, Guid id, CancellationToken _) =>
            ctx.Pedidos.AsNoTracking().Where(p => p.Id == id).Select(ProjecaoResumo).FirstOrDefault());

    public async Task<PaginaResponse<PedidoResumoResponse>> ListarAsync(FiltroPedidos filtro, CancellationToken cancellationToken)
    {
        // AsNoTracking é redundante para projeção em DTO (DTO não é rastreado), mas deixa
        // explícita a intenção de "somente leitura" se alguém trocar a projeção por entidade.
        var consulta = db.Pedidos.AsNoTracking();

        // Cada filtro só entra se informado. Os valores viram PARÂMETROS no SQL (capturados
        // de variáveis locais), então valores diferentes reaproveitam o mesmo plano.
        if (filtro.ClienteId is { } clienteId)
        {
            consulta = consulta.Where(p => p.ClienteId == clienteId);
        }

        if (filtro.Regiao is { } regiao)
        {
            consulta = consulta.Where(p => p.Regiao == regiao);
        }

        if (filtro.Status is { } status)
        {
            consulta = consulta.Where(p => p.Status == status);
        }

        var total = await consulta.CountAsync(cancellationToken);
        var deslocamento = (filtro.Pagina - 1) * filtro.Tamanho;
        var tamanho = filtro.Tamanho;

        var itens = await consulta
            .OrderByDescending(p => p.CriadoEmUtc)
            .ThenByDescending(p => p.Id) // desempate: ordenação determinística
            .Skip(deslocamento)
            .Take(tamanho)
            .Select(ProjecaoResumo)
            .ToListAsync(cancellationToken);

        return new PaginaResponse<PedidoResumoResponse>(itens, filtro.Pagina, filtro.Tamanho, total);
    }

    /// <summary>
    /// Paginação por cursor (keyset): em vez de OFFSET, filtra "depois do último visto".
    /// O custo não cresce com a profundidade da página e inserções não deslocam a lista.
    /// </summary>
    public async Task<PaginaCursorResponse<PedidoResumoResponse>> ListarPorCursorAsync(string? cursor, int tamanho, CancellationToken cancellationToken)
    {
        tamanho = Math.Clamp(tamanho, 1, FiltroPedidos.TamanhoMaximo);
        var consulta = db.Pedidos.AsNoTracking();

        if (Cursor.TentarLer(cursor, out var data, out var id))
        {
            consulta = consulta.Where(p => p.CriadoEmUtc < data || (p.CriadoEmUtc == data && p.Id.CompareTo(id) < 0));
        }

        var linhas = await consulta
            .OrderByDescending(p => p.CriadoEmUtc)
            .ThenByDescending(p => p.Id)
            .Take(tamanho + 1) // um a mais para saber se existe próxima página
            .Select(ProjecaoResumo)
            .ToListAsync(cancellationToken);

        var temMais = linhas.Count > tamanho;
        var pagina = linhas.Take(tamanho).ToList();
        var proximo = temMais ? Cursor.Escrever(pagina[^1].CriadoEmUtc, pagina[^1].Id) : null;
        return new PaginaCursorResponse<PedidoResumoResponse>(pagina, proximo);
    }

    public Task<PedidoResumoResponse?> ObterResumoAsync(Guid id, CancellationToken cancellationToken) =>
        db.Pedidos.AsNoTracking().Where(p => p.Id == id).Select(ProjecaoResumo).FirstOrDefaultAsync(cancellationToken);

    public Task<PedidoResumoResponse?> ObterResumoCompiladoAsync(Guid id, CancellationToken cancellationToken) =>
        ResumoCompilado(db, id, cancellationToken);

    public Task<PedidoDetalheResponse?> ObterDetalheAsync(Guid id, CancellationToken cancellationToken) =>
        db.Pedidos.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PedidoDetalheResponse(
                p.Id,
                p.ClienteId,
                p.Regiao.ToString(),
                p.Prioridade.ToString(),
                p.Status.ToString(),
                p.Categoria.ToString(),
                p.Total,
                p.CriadoEmUtc,
                p.Entrega == null ? null : new EnderecoDto(p.Entrega.Logradouro, p.Entrega.Cidade, p.Entrega.Uf, p.Entrega.Cep),
                new MetadadosDto(p.Metadados.Canal, p.Metadados.Origem, p.Metadados.Campanha),
                p.Itens.OrderBy(i => i.Sku)
                    .Select(i => new ItemPedidoResponse(i.Sku, i.Quantidade, i.PrecoUnitario, i.Quantidade * i.PrecoUnitario))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Relatório de pedidos com itens: UMA consulta (sem N+1), só as colunas usadas.</summary>
    public Task<IReadOnlyList<PedidoComItensResponse>> RelatorioComItensAsync(int pagina, int tamanho, CancellationToken cancellationToken) =>
        Relatorio(pagina, tamanho, dividir: false, cancellationToken);

    /// <summary>Mesma consulta com AsSplitQuery: um comando para pedidos e outro para itens.</summary>
    public Task<IReadOnlyList<PedidoComItensResponse>> RelatorioComItensDivididoAsync(int pagina, int tamanho, CancellationToken cancellationToken) =>
        Relatorio(pagina, tamanho, dividir: true, cancellationToken);

    public async Task<IReadOnlyList<ResumoRegiaoResponse>> PainelPorRegiaoAsync(CancellationToken cancellationToken)
    {
        // Agregações executadas no banco: SUM/COUNT sobre INTEGER (centavos).
        var pedidos = await db.Pedidos
            .GroupBy(p => p.Regiao)
            .Select(g => new { Regiao = g.Key, Pedidos = g.Count(), Total = g.Sum(p => p.Total) })
            .ToListAsync(cancellationToken);

        var unidades = await (
                from i in db.Itens
                join p in db.Pedidos on i.PedidoId equals p.Id
                group i by p.Regiao into g
                select new { Regiao = g.Key, Unidades = g.Sum(i => i.Quantidade) })
            .ToDictionaryAsync(x => x.Regiao, x => x.Unidades, cancellationToken);

        return [.. pedidos
            .OrderBy(x => x.Regiao)
            .Select(x => new ResumoRegiaoResponse(
                x.Regiao.ToString(),
                x.Pedidos,
                unidades.GetValueOrDefault(x.Regiao),
                x.Total,
                x.Pedidos == 0 ? 0 : decimal.Round(x.Total / x.Pedidos, 2)))];
    }

    private async Task<IReadOnlyList<PedidoComItensResponse>> Relatorio(int pagina, int tamanho, bool dividir, CancellationToken cancellationToken)
    {
        tamanho = Math.Clamp(tamanho, 1, FiltroPedidos.TamanhoMaximo);
        var deslocamento = (Math.Max(1, pagina) - 1) * tamanho;

        var consulta = db.Pedidos.AsNoTracking()
            .OrderByDescending(p => p.CriadoEmUtc)
            .ThenByDescending(p => p.Id)
            .Skip(deslocamento)
            .Take(tamanho)
            .Select(p => new PedidoComItensResponse(
                p.Id,
                p.ClienteId,
                p.Total,
                p.Itens.OrderBy(i => i.Sku)
                    .Select(i => new ItemRelatorioResponse(i.Sku, i.Quantidade, i.Quantidade * i.PrecoUnitario))
                    .ToList()));

        return await (dividir ? consulta.AsSplitQuery() : consulta.AsSingleQuery()).ToListAsync(cancellationToken);
    }
}

/// <summary>Cursor opaco: base64url("ticks:guid"). O cliente não deve interpretar o conteúdo.</summary>
internal static class Cursor
{
    public static string Escrever(DateTime criadoEmUtc, Guid id) =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes($"{criadoEmUtc.Ticks}:{id:N}"));

    public static bool TentarLer(string? cursor, out DateTime criadoEmUtc, out Guid id)
    {
        criadoEmUtc = default;
        id = default;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 100)
        {
            return false;
        }

        try
        {
            var partes = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(cursor)).Split(':');
            if (partes.Length == 2 && long.TryParse(partes[0], out var ticks) && Guid.TryParseExact(partes[1], "N", out id)
                && ticks is >= 0 and <= 3155378975999999999)
            {
                criadoEmUtc = new DateTime(ticks, DateTimeKind.Utc);
                return true;
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }
}
