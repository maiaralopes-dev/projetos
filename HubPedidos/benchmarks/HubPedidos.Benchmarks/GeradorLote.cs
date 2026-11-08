using System.Globalization;
using System.Text;

namespace HubPedidos.Benchmarks;

/// <summary>
/// Gera lotes determinísticos (semente fixa) para testes e benchmarks. Inclui nomes
/// com caracteres UTF-8 multibyte e, opcionalmente, linhas inválidas de vários tipos.
/// </summary>
public static class GeradorLote
{
    private static readonly string[] Regioes = ["Sul", "Sudeste", "CentroOeste", "Nordeste", "Norte", "Internacional", "sudeste"];
    private static readonly string[] Prioridades = ["Normal", "Normal", "Normal", "Alta", "Urgente"];
    private static readonly string[] Clientes = ["CLI", "JOÃO", "MARÍA", "ÇÉU", "客户"];

    private static readonly string[] Invalidas =
    [
        "CLI-X;Sul;Normal",                 // 3 campos
        "CLI-X;Sul;Normal;A:1:1;extra",     // 5 campos
        "CLI-X;Sul;Normal;A:1x:2.00",       // quantidade inválida
        "CLI-X;Sul;Normal;A:1:2,50",        // vírgula decimal
        "CLI-X;Sul;Normal;A:1",             // item com 2 partes
        "CLI-X;Sul;Normal;",                // sem itens (sintaxe ok; mapeador rejeita)
        "   ",                              // linha em branco
        "CLI-X;Marte;Normal;A:-1:2.00",     // quantidade negativa
    ];

    public static string Gerar(int linhas, int semente = 42, bool incluirInvalidas = false, string quebra = "\n")
    {
        var rnd = new Random(semente);
        var sb = new StringBuilder(linhas * 80);
        for (var n = 0; n < linhas; n++)
        {
            if (incluirInvalidas && rnd.Next(20) == 0)
            {
                sb.Append(Invalidas[rnd.Next(Invalidas.Length)]).Append(quebra);
                continue;
            }

            sb.Append(Clientes[rnd.Next(Clientes.Length)]).Append('-').Append(n).Append(';')
              .Append(Regioes[rnd.Next(Regioes.Length)]).Append(';')
              .Append(Prioridades[rnd.Next(Prioridades.Length)]).Append(';');

            var itens = rnd.Next(1, 6);
            for (var i = 0; i < itens; i++)
            {
                if (i > 0)
                {
                    sb.Append('|');
                }

                var preco = rnd.Next(1, 500_000) / 100m;
                sb.Append("SKU-Ç").Append(i).Append(':')
                  .Append(rnd.Next(1, 20)).Append(':')
                  .Append(preco.ToString("0.00", CultureInfo.InvariantCulture));
            }

            sb.Append(quebra);
        }

        return sb.ToString();
    }

    public static byte[] GerarUtf8(int linhas, int semente = 42, bool incluirInvalidas = false) =>
        Encoding.UTF8.GetBytes(Gerar(linhas, semente, incluirInvalidas));
}
