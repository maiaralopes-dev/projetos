using HubPedidos.Application.Classificacao;
using HubPedidos.Domain.Pedidos;
using static HubPedidos.Application.Classificacao.CategoriaProcessamento;

namespace HubPedidos.Tests;

public class ClassificadorTests
{
    // Tabela de classes de equivalência e valores-limite (também reproduzida no relatório).
    public static TheoryData<string, decimal, int, Regiao, Prioridade, CategoriaProcessamento> Casos => new()
    {
        // caso                                   valor     itens região                 prioridade          esperado
        { "normal nacional",                      100m,     1,    Regiao.Sudeste,        Prioridade.Normal,  Padrao },
        { "valor mínimo positivo",                0.01m,    1,    Regiao.Norte,          Prioridade.Normal,  Padrao },
        { "19 itens (abaixo do atacado)",         300m,     19,   Regiao.Sul,            Prioridade.Normal,  Padrao },
        { "20 itens (limite atacado)",            300m,     20,   Regiao.Sul,            Prioridade.Normal,  Atacado },
        { "urgente barato",                       10m,      1,    Regiao.Nordeste,       Prioridade.Urgente, Expresso },
        { "alta 499,99 (abaixo do limite)",       499.99m,  1,    Regiao.Sul,            Prioridade.Alta,    Padrao },
        { "alta 500 (limite expresso)",           500m,     1,    Regiao.Sul,            Prioridade.Alta,    Expresso },
        { "urgente com 20 itens: expresso vence", 300m,     20,   Regiao.Sul,            Prioridade.Urgente, Expresso },
        { "4.999,99 (abaixo do alto valor)",      4999.99m, 1,    Regiao.CentroOeste,    Prioridade.Normal,  Padrao },
        { "5.000 (limite alto valor)",            5000m,    1,    Regiao.CentroOeste,    Prioridade.Normal,  AltoValor },
        { "alto valor vence urgência",            8000m,    1,    Regiao.Sudeste,        Prioridade.Urgente, AltoValor },
        { "internacional",                        100m,     1,    Regiao.Internacional,  Prioridade.Normal,  Internacional },
        { "internacional urgente",                100m,     1,    Regiao.Internacional,  Prioridade.Urgente, Internacional },
        { "internacional 9.999,99",               9999.99m, 1,    Regiao.Internacional,  Prioridade.Normal,  Internacional },
        { "internacional 10.000 (limite revisão)",10000m,   1,    Regiao.Internacional,  Prioridade.Normal,  RevisaoManual },
        { "valor zero",                           0m,       1,    Regiao.Sul,            Prioridade.Normal,  Invalido },
        { "valor negativo",                       -1m,      1,    Regiao.Sul,            Prioridade.Normal,  Invalido },
        { "sem itens",                            100m,     0,    Regiao.Sul,            Prioridade.Normal,  Invalido },
        { "região desconhecida (99)",             100m,     1,    (Regiao)99,            Prioridade.Normal,  Invalido },
        { "região default (0)",                   100m,     1,    default,               Prioridade.Normal,  Invalido },
        { "prioridade desconhecida",              100m,     1,    Regiao.Sul,            (Prioridade)42,     Invalido },
    };

    [Theory]
    [MemberData(nameof(Casos))]
    public void Classifica_cada_classe_de_equivalencia(string caso, decimal valor, int itens, Regiao regiao, Prioridade prioridade, CategoriaProcessamento esperado)
    {
        var categoria = ClassificadorPedidos.Classificar(new DadosClassificacao(valor, itens, regiao, prioridade));

        Assert.True(categoria == esperado, $"Caso '{caso}': esperado {esperado}, obtido {categoria}.");
    }

    [Fact]
    public void Pedido_nulo_e_invalido() =>
        Assert.Equal(Invalido, ClassificadorPedidos.Classificar((Pedido?)null));

    [Fact]
    public void Classifica_a_partir_da_entidade()
    {
        var pedido = Construtores.Pedido(Regiao.Sul, Prioridade.Alta, Construtores.Item("A", 2, 250m));

        Assert.Equal(Expresso, ClassificadorPedidos.Classificar(pedido));
    }

    [Fact]
    public void Toda_categoria_exceto_invalido_e_alcancavel()
    {
        var alcancadas = Casos.Select(c => (CategoriaProcessamento)c[5]).ToHashSet();

        Assert.Equal(Enum.GetValues<CategoriaProcessamento>().ToHashSet(), alcancadas);
    }
}
