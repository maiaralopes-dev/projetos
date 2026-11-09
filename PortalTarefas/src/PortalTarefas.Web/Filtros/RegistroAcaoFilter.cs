using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace PortalTarefas.Web.Filtros;

/// <summary>
/// Filtro de ação: registra ação, duração, validade do ModelState e tipo/status do resultado.
/// Não altera o fluxo e não contém regra de negócio — a validação continua no controller via ModelState.
/// </summary>
public sealed class RegistroAcaoFilter(ILogger<RegistroAcaoFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var acao = context.ActionDescriptor is ControllerActionDescriptor cad
            ? $"{cad.ControllerName}.{cad.ActionName} [{context.HttpContext.Request.Method}]"
            : context.ActionDescriptor.DisplayName;
        var inicio = Stopwatch.GetTimestamp();
        logger.LogInformation("[Filtro] Executando {Acao} (ModelState válido: {Valido})", acao, context.ModelState.IsValid);

        var executado = await next();

        var resultado = executado.Result switch
        {
            ViewResult v => $"View({v.ViewName ?? "padrão"}) status {v.StatusCode ?? 200}",
            RedirectToActionResult r => $"Redirect -> {r.ActionName}",
            IStatusCodeActionResult s => $"{executado.Result.GetType().Name} status {s.StatusCode}",
            null when executado.Exception is not null => $"Exceção {executado.Exception.GetType().Name}",
            _ => executado.Result?.GetType().Name ?? "nenhum"
        };
        logger.LogInformation("[Filtro] Concluído {Acao} -> {Resultado} em {DuracaoMs:F1} ms",
            acao, resultado, Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
    }
}
