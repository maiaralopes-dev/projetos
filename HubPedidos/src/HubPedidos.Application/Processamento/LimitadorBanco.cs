namespace HubPedidos.Application.Processamento;

/// <summary>
/// Limita quantas operações usam o banco ao mesmo tempo. O slot é liberado em
/// <c>finally</c> e só se foi realmente obtido (se WaitAsync for cancelado, não há
/// Release). Registra o máximo observado para os testes provarem o teto.
/// </summary>
public sealed class LimitadorBanco
{
    private readonly SemaphoreSlim _semaforo;
    private int _emUso;
    private int _maximoObservado;

    public LimitadorBanco(int limite)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limite, 1);
        Limite = limite;
        _semaforo = new SemaphoreSlim(limite, limite);
    }

    public int Limite { get; }

    public int Disponiveis => _semaforo.CurrentCount;

    public int EmUso => Volatile.Read(ref _emUso);

    public int MaximoObservado => Volatile.Read(ref _maximoObservado);

    public async Task ExecutarAsync(Func<CancellationToken, Task> operacao, CancellationToken cancellationToken)
    {
        await _semaforo.WaitAsync(cancellationToken);
        var atual = Interlocked.Increment(ref _emUso);
        AtualizarMaximo(atual);
        try
        {
            await operacao(cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _emUso);
            _semaforo.Release();
        }
    }

    public async Task<T> ExecutarAsync<T>(Func<CancellationToken, Task<T>> operacao, CancellationToken cancellationToken)
    {
        var resultado = default(T)!;
        // Corpo em BLOCO de propósito: "async ct => resultado = await ..." é uma expressão
        // que devolve T, e a resolução de sobrecarga escolheria ESTE método de novo
        // (recursão infinita, StackOverflow). Com { } a lambda é Func<CancellationToken, Task>.
        await ExecutarAsync(async ct => { resultado = await operacao(ct); }, cancellationToken);
        return resultado;
    }

    // "Máximo atômico": compare-and-swap em laço; um simples if/atribuição perderia valores.
    private void AtualizarMaximo(int valor)
    {
        int atual;
        while (valor > (atual = Volatile.Read(ref _maximoObservado))
               && Interlocked.CompareExchange(ref _maximoObservado, valor, atual) != atual)
        {
        }
    }
}
