namespace HubPedidos.Domain.Pedidos;

/// <summary>
/// Violação de invariante do domínio. É a "última linha de defesa": a camada de
/// aplicação deve validar o DTO antes, para que esta exceção nunca chegue ao cliente.
/// </summary>
public sealed class DomainException(string mensagem) : Exception(mensagem);
