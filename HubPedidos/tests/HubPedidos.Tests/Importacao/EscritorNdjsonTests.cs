using System.Text;
using HubPedidos.Application.Contratos;
using HubPedidos.Application.Importacao;

namespace HubPedidos.Tests.Importacao;

public class EscritorNdjsonTests
{
    [Theory]
    [InlineData(1, 250)]
    [InlineData(100, 3)]
    [InlineData(1_000, 1)]
    public async Task Flush_agrupado_reduz_chamadas_sem_mudar_a_saida(int itensPorFlush, int flushesEsperados)
    {
        var destino = new StreamContador();
        await using (var escritor = new EscritorNdjson(destino, itensPorFlush))
        {
            for (var i = 1; i <= 250; i++)
            {
                await escritor.EscreverAsync(new ResultadoImportacaoResponse(i, "aceito", null, "Padrao", null));
            }
        }

        Assert.Equal(flushesEsperados, destino.Flushes);
        var linhas = Encoding.UTF8.GetString(destino.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(250, linhas.Length);
        Assert.Equal("""{"linha":250,"status":"aceito","id":null,"categoria":"Padrao","erros":null}""", linhas[^1]);
    }
}
