using System.Text.Json;
using System.Text.Json.Nodes;

namespace HubPedidos.Tests.Contratos;

/// <summary>
/// Extensão (atividade 8): contrato HTTP congelado em snapshot versionado
/// (Contratos/Snapshots/openapi-v{n}.json). Qualquer mudança no OpenAPI gerado FALHA o teste
/// e grava "*.recebido.json" ao lado. A mudança só entra com revisão explícita:
///   git diff --no-index Contratos/Snapshots/openapi-v1.json Contratos/Snapshots/openapi-v1.recebido.json
///   ATUALIZAR_SNAPSHOTS=1 dotnet test --filter ContratoSnapshotTests   (aceita e regrava)
/// </summary>
public class ContratoSnapshotTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private static readonly JsonSerializerOptions Indentado = new() { WriteIndented = true };

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task Documento_openapi_corresponde_ao_snapshot_aprovado(string versao)
    {
        var gerado = Normalizar(await factory.CreateClient().GetStringAsync($"/openapi/{versao}.json"));
        var pasta = Path.Combine(RaizDoProjetoDeTestes(), "Contratos", "Snapshots");
        var aprovado = Path.Combine(pasta, $"openapi-{versao}.json");
        var recebido = Path.Combine(pasta, $"openapi-{versao}.recebido.json");

        if (Environment.GetEnvironmentVariable("ATUALIZAR_SNAPSHOTS") == "1")
        {
            await File.WriteAllTextAsync(aprovado, gerado);
            File.Delete(recebido);
            return;
        }

        Assert.True(File.Exists(aprovado), $"Snapshot inexistente: {aprovado}. Gere com ATUALIZAR_SNAPSHOTS=1 e revise antes de versionar.");
        var esperado = await File.ReadAllTextAsync(aprovado);
        if (esperado.ReplaceLineEndings("\n") != gerado)
        {
            await File.WriteAllTextAsync(recebido, gerado);
            Assert.Fail($"O contrato {versao} mudou. Revise: git diff --no-index {aprovado} {recebido} — " +
                        "se a mudança for intencional e compatível com a política de versão, aceite com ATUALIZAR_SNAPSHOTS=1.");
        }

        File.Delete(recebido);
    }

    /// <summary>Remove o que varia entre ambientes (servidores) e padroniza a formatação.</summary>
    private static string Normalizar(string json)
    {
        var no = JsonNode.Parse(json)!.AsObject();
        no.Remove("servers");
        return no.ToJsonString(Indentado).ReplaceLineEndings("\n") + "\n";
    }

    private static string RaizDoProjetoDeTestes()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "HubPedidos.Tests.csproj")))
        {
            dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("Projeto de testes não encontrado.");
        }

        return dir;
    }
}
