namespace PortalTarefas.Web.Infraestrutura;

/// <summary>
/// Modo "sonda": `dotnet PortalTarefas.Web.dll --sondar-saude` consulta /health/live do próprio contêiner
/// e encerra com 0 (saudável) ou 1. Evita instalar curl/wget na imagem final apenas para o HEALTHCHECK.
/// </summary>
public static class SondaSaude
{
    public static async Task<int> ExecutarAsync()
    {
        var porta = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0] ?? "8080";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var resp = await http.GetAsync($"http://127.0.0.1:{porta}/health/live");
            return resp.IsSuccessStatusCode ? 0 : 1;
        }
        catch
        {
            return 1;
        }
    }
}
