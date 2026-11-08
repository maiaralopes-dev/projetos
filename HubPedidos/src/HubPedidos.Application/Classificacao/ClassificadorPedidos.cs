using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Application.Classificacao;

public enum CategoriaProcessamento
{
    Invalido = 0,
    Padrao = 1,
    Atacado = 2,
    Expresso = 3,
    AltoValor = 4,
    Internacional = 5,
    RevisaoManual = 6,
}

/// <summary>
/// Projeção mínima usada na classificação. Separar este tipo da entidade permite testar
/// a regra com dados "impossíveis" (enum fora do intervalo, valor negativo), garantindo
/// que o braço de inválido existe mesmo que o domínio já proteja as invariantes.
/// </summary>
public readonly record struct DadosClassificacao(decimal Valor, int QuantidadeItens, Regiao Regiao, Prioridade Prioridade)
{
    public static DadosClassificacao De(Pedido pedido) =>
        new(pedido.Total, pedido.Itens.Length, pedido.Regiao, pedido.Prioridade);
}

public static class ClassificadorPedidos
{
    public const decimal LimiteAltoValor = 5_000m;
    public const decimal LimiteRevisaoInternacional = 10_000m;
    public const decimal LimiteExpressoAlta = 500m;
    public const int LimiteAtacado = 20;

    /// <summary>
    /// A ordem dos braços é a regra de negócio: do mais restritivo para o mais geral.
    /// 1. Inválido sempre primeiro (nada inválido pode cair em outra categoria).
    /// 2. Internacional caro exige revisão manual antes de qualquer outra regra.
    /// 3. Alto valor nacional vence prioridade (risco financeiro &gt; urgência).
    /// 4. Urgente, ou Alta com valor relevante, vai para o expresso.
    /// 5. Muitos itens diferentes = atacado.
    /// 6. Descarte (<c>_</c>) = padrão.
    /// O compilador avisa (CS8510) se um braço ficar inalcançável.
    /// </summary>
    public static CategoriaProcessamento Classificar(DadosClassificacao dados) => dados switch
    {
        { Valor: <= 0m } or { QuantidadeItens: <= 0 }
            or { Regiao: < Regiao.Sul or > Regiao.Internacional }
            or { Prioridade: < Prioridade.Normal or > Prioridade.Urgente }
            => CategoriaProcessamento.Invalido,

        { Regiao: Regiao.Internacional, Valor: >= LimiteRevisaoInternacional } => CategoriaProcessamento.RevisaoManual,
        { Regiao: Regiao.Internacional } => CategoriaProcessamento.Internacional,
        { Valor: >= LimiteAltoValor } => CategoriaProcessamento.AltoValor,
        { Prioridade: Prioridade.Urgente }
            or { Prioridade: Prioridade.Alta, Valor: >= LimiteExpressoAlta } => CategoriaProcessamento.Expresso,
        { QuantidadeItens: >= LimiteAtacado } => CategoriaProcessamento.Atacado,
        _ => CategoriaProcessamento.Padrao,
    };

    public static CategoriaProcessamento Classificar(Pedido? pedido) => pedido switch
    {
        null => CategoriaProcessamento.Invalido,
        _ => Classificar(DadosClassificacao.De(pedido)),
    };
}
