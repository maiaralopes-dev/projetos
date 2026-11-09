using Microsoft.AspNetCore.Mvc;
using PortalTarefas.Web.Servicos;
using PortalTarefas.Web.ViewModels;

namespace PortalTarefas.Web.ViewComponents;

/// <summary>Componente reutilizável que mostra tarefas vencidas no layout.</summary>
public sealed class TarefasVencidasViewComponent(ITarefaServico servico, TimeProvider relogio) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int maximo = 5)
    {
        var hoje = DateOnly.FromDateTime(relogio.GetLocalNow().Date);
        var vencidas = await servico.ListarVencidasAsync(hoje, HttpContext.RequestAborted);
        return View(vencidas.Take(maximo).Select(t => TarefaResumoViewModel.De(t, hoje)).ToList());
    }
}
