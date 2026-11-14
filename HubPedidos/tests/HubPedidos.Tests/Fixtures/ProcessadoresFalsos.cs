using HubPedidos.Application.Classificacao;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Processamento.Processadores;

// Tipos usados SOMENTE pelos testes do catálogo. Cada namespace simula um cenário.
// Eles estão carregados no processo durante os testes, o que prova que o catálogo real
// (assembly Application) não os enxerga.

namespace HubPedidos.Tests.Fixtures
{
    public abstract class ProcessadorFalsoBase : IProcessadorPedido
    {
        public ValueTask<ResultadoProcessamento> ProcessarAsync(Pedido pedido, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ResultadoProcessamento(GetType().Name, 1, "falso"));
    }
}

namespace HubPedidos.Tests.Fixtures.Duplicados
{
    [ProcessadorPedido(CategoriaProcessamento.Padrao)]
    public sealed class PadraoA : ProcessadorFalsoBase;

    [ProcessadorPedido(CategoriaProcessamento.Padrao)]
    public sealed class PadraoB : ProcessadorFalsoBase;
}

namespace HubPedidos.Tests.Fixtures.Invalidos
{
    [ProcessadorPedido(CategoriaProcessamento.Padrao)]
    public sealed class NaoImplementaInterface;

    [ProcessadorPedido(CategoriaProcessamento.Expresso)]
    public abstract class Abstrato : ProcessadorFalsoBase;

    [ProcessadorPedido(CategoriaProcessamento.Invalido)]
    public sealed class CategoriaInvalida : ProcessadorFalsoBase;

    [ProcessadorPedido(CategoriaProcessamento.Atacado, versao: 0)]
    public sealed class VersaoZero : ProcessadorFalsoBase;

    [ProcessadorPedido(CategoriaProcessamento.AltoValor)]
    public sealed class SemConstrutorPublico : ProcessadorFalsoBase
    {
        private SemConstrutorPublico() { }
    }
}

namespace HubPedidos.Tests.Fixtures.Versionados
{
    [ProcessadorPedido(CategoriaProcessamento.Padrao, versao: 1)]
    public sealed class PadraoV1 : ProcessadorFalsoBase;

    [ProcessadorPedido(CategoriaProcessamento.Padrao, versao: 3)]
    public sealed class PadraoV3 : ProcessadorFalsoBase;

    public sealed class SemAtributo : ProcessadorFalsoBase; // ignorado
}

// Prefixo parecido ("Versionados" + "Extra"): um StartsWith ingênuo incluiria este
// namespace e geraria duplicidade de Padrao v3.
namespace HubPedidos.Tests.Fixtures.VersionadosExtra
{
    [ProcessadorPedido(CategoriaProcessamento.Padrao, versao: 3)]
    public sealed class PadraoV3Concorrente : ProcessadorFalsoBase;
}
