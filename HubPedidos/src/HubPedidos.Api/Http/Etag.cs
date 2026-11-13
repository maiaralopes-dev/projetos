using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace HubPedidos.Api.Http;

/// <summary>
/// ETag FORTE da representação: SHA-256 dos bytes JSON exatamente como serão enviados.
/// Mesmos campos/valores → mesmos bytes → mesma ETag; qualquer campo que mude (status,
/// itens…) muda a ETag. Cada versão da API tem sua representação, logo sua ETag.
/// </summary>
public static class Etag
{
    public static string Calcular(ReadOnlySpan<byte> representacao) =>
        $"\"{Convert.ToBase64String(SHA256.HashData(representacao), 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_')}\"";

    public static byte[] Serializar<T>(HttpContext http, T valor) =>
        JsonSerializer.SerializeToUtf8Bytes(valor, http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions);

    /// <summary>
    /// GET condicional (RFC 9110 §13.1.2): If-None-Match usa comparação FRACA; "*" casa com
    /// qualquer representação existente. Casou → 304 sem corpo (com a ETag). Senão → 200.
    /// Cache-Control private + no-cache: o navegador pode guardar, mas revalida sempre, e
    /// proxies compartilhados não guardam (resposta pode ser de um usuário específico).
    /// </summary>
    public static IResult ResponderComEtag<T>(HttpContext http, T valor)
    {
        var corpo = Serializar(http, valor);
        var etag = Calcular(corpo);
        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl = "private, no-cache";

        return CasaIfNoneMatch(http.Request, etag)
            ? Results.StatusCode(StatusCodes.Status304NotModified)
            : Results.Bytes(corpo, "application/json; charset=utf-8");
    }

    public static bool CasaIfNoneMatch(HttpRequest request, string etag)
    {
        if (!EntityTagHeaderValue.TryParseList(request.Headers.IfNoneMatch, out var lista) || lista.Count == 0)
        {
            return false; // ausente ou malformado: responde normalmente
        }

        var atual = EntityTagHeaderValue.Parse(etag);
        return lista.Any(e => e.Equals(EntityTagHeaderValue.Any) || e.Compare(atual, useStrongComparison: false));
    }

    /// <summary>If-Match (RFC 9110 §13.1.1): comparação FORTE; "*" casa com qualquer existente.</summary>
    public static bool? CasaIfMatch(HttpRequest request, IEnumerable<string> etagsAtuais)
    {
        if (request.Headers.IfMatch.Count == 0)
        {
            return null; // sem pré-condição
        }

        if (!EntityTagHeaderValue.TryParseList(request.Headers.IfMatch, out var lista))
        {
            return false;
        }

        var atuais = etagsAtuais.Select(e => EntityTagHeaderValue.Parse(e)).ToArray();
        return lista.Any(e => e.Equals(EntityTagHeaderValue.Any) || atuais.Any(a => e.Compare(a, useStrongComparison: true)));
    }
}
