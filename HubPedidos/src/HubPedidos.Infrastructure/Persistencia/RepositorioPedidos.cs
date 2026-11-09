using HubPedidos.Application.Classificacao;
using HubPedidos.Application.Persistencia;
using HubPedidos.Domain.Pedidos;
using Microsoft.EntityFrameworkCore;

namespace HubPedidos.Infrastructure.Persistencia;

public sealed class RepositorioPedidos(HubDbContext db) : IRepositorioPedidos
{
    public async Task AdicionarAsync(Pedido pedido, CategoriaProcessamento categoria, CancellationToken cancellationToken)
    {
        db.Pedidos.Add(MapeadorRegistro.ParaRegistro(pedido, categoria));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Estratégia padrão: consulta COM tracking, porque o mesmo DbContext vai alterar a
    /// entidade. O change tracker detecta que só Status mudou e gera um UPDATE de uma coluna.
    /// </summary>
    public async Task<ResultadoAlteracaoStatus> AlterarStatusAsync(Guid id, StatusPedido novo, CancellationToken cancellationToken)
    {
        var registro = await db.Pedidos.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (registro is null)
        {
            return ResultadoAlteracaoStatus.NaoEncontrado;
        }

        if (!TransicoesStatus.Permitida(registro.Status, novo))
        {
            return ResultadoAlteracaoStatus.TransicaoInvalida;
        }

        registro.Status = novo;
        await db.SaveChangesAsync(cancellationToken);
        return ResultadoAlteracaoStatus.Alterado;
    }

    /// <summary>
    /// Alternativa documentada SEM tracking: lê só o status (projeção escalar), anexa um
    /// "stub" com a chave e marca explicitamente a única propriedade modificada.
    /// Custo: o stub tem valores falsos nas demais propriedades, e um erro em
    /// IsModified gravaria esses valores. Por isso não é a estratégia padrão.
    /// </summary>
    public async Task<ResultadoAlteracaoStatus> AlterarStatusSemTrackingAsync(Guid id, StatusPedido novo, CancellationToken cancellationToken)
    {
        var atual = await db.Pedidos.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => (StatusPedido?)p.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (atual is null)
        {
            return ResultadoAlteracaoStatus.NaoEncontrado;
        }

        if (!TransicoesStatus.Permitida(atual.Value, novo))
        {
            return ResultadoAlteracaoStatus.TransicaoInvalida;
        }

        var stub = new PedidoRegistro { Id = id, Status = novo };
        db.Pedidos.Attach(stub);
        db.Entry(stub).Property(p => p.Status).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        db.Entry(stub).State = EntityState.Detached;
        return ResultadoAlteracaoStatus.Alterado;
    }
}
