namespace HubPedidos.Domain.Pedidos;

public enum StatusPedido
{
    Recebido = 1,
    EmProcessamento = 2,
    Concluido = 3,
    Cancelado = 4,
}

public static class TransicoesStatus
{
    /// <summary>Máquina de estados com padrão de tupla: (de, para).</summary>
    public static bool Permitida(StatusPedido de, StatusPedido para) => (de, para) switch
    {
        (StatusPedido.Recebido, StatusPedido.EmProcessamento) => true,
        (StatusPedido.EmProcessamento, StatusPedido.Concluido) => true,
        (StatusPedido.Recebido or StatusPedido.EmProcessamento, StatusPedido.Cancelado) => true,
        _ => false,
    };
}
