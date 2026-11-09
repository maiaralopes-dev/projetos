using Microsoft.AspNetCore.Mvc;
using PortalTarefas.Web.Contratos;
using PortalTarefas.Web.Infraestrutura;

namespace PortalTarefas.Web.Controllers;

/// <summary>Controller MVC equivalente ao endpoint diagnóstico em Minimal API.</summary>
[ApiController]
[Route("api/mvc/diagnostico")]
public sealed class DiagnosticoController(IWebHostEnvironment env) : ControllerBase
{
    [HttpGet]
    public ActionResult<DiagnosticoDto> Get([FromHeader(Name = "X-Cliente-Id")] string? clienteId) =>
        Ok(new DiagnosticoDto("MVC", env.EnvironmentName,
            HttpContext.Items[CorrelationIdMiddleware.ChaveItem]?.ToString() ?? "", clienteId, DateTimeOffset.UtcNow));
}
