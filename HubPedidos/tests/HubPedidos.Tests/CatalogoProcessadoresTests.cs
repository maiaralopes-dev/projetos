using HubPedidos.Application.Classificacao;
using HubPedidos.Processamento.Processadores;
using HubPedidos.Processamento.Processadores.Implementacoes;
using HubPedidos.Tests.Fixtures.Versionados;

namespace HubPedidos.Tests;

public class CatalogoProcessadoresTests
{
    private static CatalogoProcessadores CatalogoReal() =>
        CatalogoProcessadores.Construir(typeof(ProcessadorPadrao).Assembly, typeof(ProcessadorPadrao).Namespace!);

    [Fact]
    public void Catalogo_real_cobre_todas_as_categorias_processaveis()
    {
        var catalogo = CatalogoReal();

        Assert.Equal(
            Enum.GetValues<CategoriaProcessamento>().Where(c => c != CategoriaProcessamento.Invalido),
            catalogo.Registros.Select(r => r.Categoria));
        Assert.False(catalogo.TentarObter(CategoriaProcessamento.Invalido, out _));
    }

    [Fact]
    public void Catalogo_e_deterministico()
    {
        // ImmutableArray<T>.Equals compara a REFERÊNCIA do array interno; para comparar
        // conteúdo é preciso tratar como sequência.
        Assert.False(CatalogoReal().Registros.Equals(CatalogoReal().Registros));
        Assert.Equal(CatalogoReal().Registros.AsEnumerable(), CatalogoReal().Registros.AsEnumerable());
    }

    [Fact]
    public void Catalogo_real_nao_enxerga_tipos_de_outros_assemblies()
    {
        // Os processadores falsos do assembly de testes estão carregados agora.
        Assert.All(CatalogoReal().Registros, r => Assert.Equal(typeof(ProcessadorPadrao).Assembly, r.Tipo.Assembly));
    }

    [Fact]
    public void Um_tipo_pode_atender_mais_de_uma_categoria()
    {
        var catalogo = CatalogoReal();

        Assert.True(catalogo.TentarObter(CategoriaProcessamento.Atacado, out var atacado));
        Assert.Equal(typeof(ProcessadorPadrao), atacado.Tipo);
        Assert.True(catalogo.TentarObter(CategoriaProcessamento.Expresso, out var expresso));
        Assert.Equal(2, expresso.Versao);
    }

    [Fact]
    public void Duplicidade_falha_de_forma_legivel()
    {
        var ex = Assert.Throws<CatalogoInvalidoException>(() =>
            CatalogoProcessadores.Construir(typeof(CatalogoProcessadoresTests).Assembly, "HubPedidos.Tests.Fixtures.Duplicados", exigirTodasCategorias: false));

        Assert.Contains("Categoria Padrao v1 declarada por mais de um tipo: PadraoA, PadraoB.", ex.Erros);
    }

    [Fact]
    public void Metadados_inconsistentes_sao_todos_reportados()
    {
        var ex = Assert.Throws<CatalogoInvalidoException>(() =>
            CatalogoProcessadores.Construir(typeof(CatalogoProcessadoresTests).Assembly, "HubPedidos.Tests.Fixtures.Invalidos", exigirTodasCategorias: false));

        Assert.Collection(ex.Erros.Order(),
            e => Assert.EndsWith("Abstrato é abstrato.", e),
            e => Assert.EndsWith("CategoriaInvalida declara categoria não processável 'Invalido'.", e),
            e => Assert.EndsWith("NaoImplementaInterface não implementa IProcessadorPedido.", e),
            e => Assert.EndsWith("SemConstrutorPublico não possui construtor público.", e),
            e => Assert.EndsWith("VersaoZero declara versão 0; a versão deve ser >= 1.", e));
    }

    [Fact]
    public void Maior_versao_vence_e_prefixo_de_namespace_e_exato()
    {
        var catalogo = CatalogoProcessadores.Construir(typeof(CatalogoProcessadoresTests).Assembly, "HubPedidos.Tests.Fixtures.Versionados", exigirTodasCategorias: false);

        var registro = Assert.Single(catalogo.Registros);
        Assert.Equal(typeof(PadraoV3), registro.Tipo);
        Assert.Equal(3, registro.Versao);
    }

    [Fact]
    public void Cobertura_incompleta_falha_quando_exigida()
    {
        var ex = Assert.Throws<CatalogoInvalidoException>(() =>
            CatalogoProcessadores.Construir(typeof(CatalogoProcessadoresTests).Assembly, "HubPedidos.Tests.Fixtures.Versionados"));

        Assert.Contains("Nenhum processador para a categoria Expresso.", ex.Erros);
        Assert.DoesNotContain(ex.Erros, e => e.Contains("Padrao", StringComparison.Ordinal));
    }
}
