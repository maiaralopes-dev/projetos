using HubPedidos.Api.Seguranca;
using HubPedidos.Notificacoes;
using HubPedidos.Notificacoes.TempoReal;

namespace HubPedidos.Api.Modulos.Notificacoes;

public static class EndpointsNotificacoes
{
    public static IEndpointRouteBuilder MapModuloNotificacoes(this IEndpointRouteBuilder app, IHostEnvironment ambiente)
    {
        app.MapHub<PedidosHub>("/hubs/pedidos");

        app.MapGet("/notificacoes/recentes", (RegistroNotificacoes registro, int? maximo) =>
            registro.Recentes(Math.Clamp(maximo ?? 50, 1, 200))).WithName("NotificacoesRecentes");

        var admin = app.MapGroup("/admin/clientes/{clienteId}").RequireAuthorization(Autenticacao.PoliticaOperador);
        admin.MapPost("/revogar", async (string clienteId, ServicoRevogacao revogacao, CancellationToken ct) =>
            TypedResults.Ok(new { clienteId, conexoesAfetadas = await revogacao.RevogarAsync(clienteId, ct) })).WithName("RevogarCliente");
        admin.MapPost("/restaurar", (string clienteId, ServicoRevogacao revogacao) =>
        {
            revogacao.Restaurar(clienteId);
            return TypedResults.NoContent();
        }).WithName("RestaurarCliente");

        if (ambiente.IsDevelopment())
        {
            // SÓ em Development: facilita testar o painel/hub manualmente. Nunca em produção.
            app.MapPost("/dev/token", (string? cliente, bool? operador, EmissorTokens emissor) =>
                TypedResults.Ok(new { token = emissor.Emitir(cliente, operador == true) })).ExcludeFromDescription();
        }

        return app;
    }
}
