using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HubPedidos.Infrastructure.Persistencia;

/// <summary>
/// Interceptor que registra o texto de cada comando SQL executado (sem valores de
/// parâmetros). Usado nos testes para provar ausência de N+1, colunas projetadas e
/// parametrização, e como "guarda" que falha se uma operação exceder N comandos.
/// </summary>
public sealed class ContadorComandos : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _comandos = new();

    public IReadOnlyList<string> Comandos => [.. _comandos];

    public void Limpar() => _comandos.Clear();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        _comandos.Enqueue(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        _comandos.Enqueue(command.CommandText);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        _comandos.Enqueue(command.CommandText);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        _comandos.Enqueue(command.CommandText);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <summary>Guarda contra regressões de roundtrips (extensão do roteiro).</summary>
    public async Task<T> NoMaximoAsync<T>(int maximo, Func<Task<T>> operacao)
    {
        Limpar();
        var resultado = await operacao();
        if (_comandos.Count > maximo)
        {
            throw new InvalidOperationException(
                $"Esperado no máximo {maximo} comando(s) SQL, executados {_comandos.Count}:{Environment.NewLine}"
                + string.Join(Environment.NewLine + "---" + Environment.NewLine, _comandos));
        }

        return resultado;
    }
}
