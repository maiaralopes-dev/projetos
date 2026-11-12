using HubPedidos.Notificacoes;

namespace HubPedidos.Api.Modulos.Notificacoes;

public static class EndpointsNotificacoes
{
    public static IEndpointRouteBuilder MapModuloNotificacoes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/notificacoes/recentes", (RegistroNotificacoes registro, int? maximo) =>
            registro.Recentes(Math.Clamp(maximo ?? 50, 1, 200))).WithName("NotificacoesRecentes");
        return app;
    }
}
