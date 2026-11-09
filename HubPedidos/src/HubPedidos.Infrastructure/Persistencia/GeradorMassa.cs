using HubPedidos.Application.Classificacao;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Infrastructure.Persistencia;

/// <summary>
/// Massa de dados determinística (semente fixa). Os pedidos passam pelo domínio
/// (Pedido.Criar) e pelo classificador, então respeitam as mesmas invariantes da API.
/// </summary>
public static class GeradorMassa
{
    public static readonly DateTime Inicio = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly string[] Canais = ["api", "web", "app", "loja", "marketplace"];
    private static readonly string[] Cidades = ["São Paulo", "Curitiba", "Recife", "Manaus", "Goiânia", "Porto Alegre"];
    private static readonly Regiao[] Regioes = Enum.GetValues<Regiao>();
    private static readonly Prioridade[] Prioridades = [Prioridade.Normal, Prioridade.Normal, Prioridade.Normal, Prioridade.Alta, Prioridade.Urgente];
    private static readonly StatusPedido[] Status = Enum.GetValues<StatusPedido>();

    public static async Task SemearAsync(HubDbContext db, int quantidade, int semente = 123, CancellationToken cancellationToken = default)
    {
        var rnd = new Random(semente);
        const int lote = 2_000;
        for (var inicio = 0; inicio < quantidade; inicio += lote)
        {
            for (var n = inicio; n < Math.Min(quantidade, inicio + lote); n++)
            {
                db.Pedidos.Add(Gerar(rnd, n));
            }

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear(); // não acumular milhares de entidades rastreadas
        }
    }

    private static PedidoRegistro Gerar(Random rnd, int n)
    {
        var criadoEm = new DateTimeOffset(Inicio.AddMinutes(rnd.Next(0, 300 * 24 * 60)).AddSeconds(n % 60));
        var itens = Enumerable.Range(0, rnd.Next(1, 6))
            .Select(i => new ItemPedido($"SKU-{rnd.Next(1, 400):D4}-{i}", rnd.Next(1, 10), rnd.Next(100, 200_000) / 100m));
        var entrega = rnd.Next(4) == 0
            ? null
            : new Endereco($"Rua {rnd.Next(1, 900)}", Cidades[rnd.Next(Cidades.Length)], "SP", $"{rnd.Next(1_000_000, 99_999_999):D8}");
        var metadados = new MetadadosPedido(Canais[rnd.Next(Canais.Length)], rnd.Next(3) == 0 ? "parceiro-x" : null, rnd.Next(5) == 0 ? "black-friday" : null);

        var pedido = Pedido.Criar($"CLI-{rnd.Next(1, 2_000):D5}", Regioes[rnd.Next(Regioes.Length)], Prioridades[rnd.Next(Prioridades.Length)], itens, entrega, metadados, criadoEm)
            with
            {
                Id = PedidoId.De(Guid.CreateVersion7(criadoEm)),
                Status = Status[rnd.Next(Status.Length)],
            };

        return MapeadorRegistro.ParaRegistro(pedido, ClassificadorPedidos.Classificar(pedido));
    }
}
