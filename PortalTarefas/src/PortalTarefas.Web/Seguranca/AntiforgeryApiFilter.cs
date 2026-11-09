using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace PortalTarefas.Web.Seguranca;

/// <summary>
/// Proteção CSRF da API autenticada por cookie: requisições que alteram estado (POST/PUT/PATCH/DELETE)
/// precisam do cabeçalho X-XSRF-TOKEN correspondente ao cookie antiforgery. Requisições autenticadas
/// por outro esquema (ex.: bearer em testes/clientes não-navegador) não dependem de cookie e são isentas.
/// </summary>
public sealed class AntiforgeryApiFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var alteraEstado = !(HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) || HttpMethods.IsOptions(http.Request.Method));
        var tipoAuth = http.User.Identity?.AuthenticationType;
        var usaCookieOuAnonimo = tipoAuth is null || tipoAuth == IdentityConstants.ApplicationScheme;

        if (alteraEstado && usaCookieOuAnonimo && !await antiforgery.IsRequestValidAsync(http))
            return TypedResults.Problem(statusCode: 400, title: "Token antiforgery ausente ou inválido.",
                detail: "Obtenha a sessão (GET /api/v2/auth/sessao) e envie o cabeçalho X-XSRF-TOKEN.");
        return await next(ctx);
    }
}

public static class AntiforgeryExtensions
{
    public const string CookieLegivel = "XSRF-TOKEN";

    /// <summary>Gera o par de tokens e grava o token de requisição num cookie legível pelo JavaScript da SPA.</summary>
    public static void EmitirTokenAntiforgery(this HttpContext http, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(http);
        http.Response.Cookies.Append(CookieLegivel, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, Secure = true, SameSite = SameSiteMode.Strict, Path = "/"
        });
    }
}
