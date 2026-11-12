using HubPedidos.Application.Contratos;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Application.Mapeamento;

/// <summary>
/// Conversões explícitas DTO ⇄ domínio. A entrada é validada por completo aqui
/// (todos os erros de uma vez) antes de qualquer construtor do domínio ser chamado.
/// </summary>
public static class MapeadorPedidos
{
    public static Resultado<Pedido> ParaDominio(CriarPedidoRequest? request)
    {
        if (request is null)
        {
            return Resultado<Pedido>.Falha([new("corpo", "Requisição vazia.")]);
        }

        var erros = new List<ErroValidacao>();

        if (string.IsNullOrWhiteSpace(request.ClienteId) || request.ClienteId.Length > Pedido.TamanhoMaximoCliente)
        {
            erros.Add(new("clienteId", $"Obrigatório, até {Pedido.TamanhoMaximoCliente} caracteres."));
        }

        var regiaoOk = TentarEnum<Regiao>(request.Regiao, out var regiao);
        if (!regiaoOk)
        {
            erros.Add(new("regiao", $"Valor inválido. Aceitos: {string.Join(", ", Enum.GetNames<Regiao>())}."));
        }

        var prioridadeOk = TentarEnum<Prioridade>(request.Prioridade, out var prioridade);
        if (!prioridadeOk)
        {
            erros.Add(new("prioridade", $"Valor inválido. Aceitos: {string.Join(", ", Enum.GetNames<Prioridade>())}."));
        }

        // Padrões de lista: nulo/vazio e excesso de itens.
        switch (request.Itens)
        {
            case null or []:
                erros.Add(new("itens", "Pedido deve ter ao menos um item."));
                break;
            case { Count: > Pedido.MaximoItens }:
                erros.Add(new("itens", $"Máximo de {Pedido.MaximoItens} itens."));
                break;
            default:
                ValidarItens(request.Itens, erros);
                break;
        }

        ValidarEntrega(request.Entrega, erros);
        ValidarMetadados(request.Metadados, erros);

        if (erros.Count > 0)
        {
            return Resultado<Pedido>.Falha(erros);
        }

        // Cópia defensiva: o domínio recebe um ImmutableArray próprio; alterar a lista
        // do DTO depois daqui não afeta o pedido.
        var itens = request.Itens!.Select(i => new ItemPedido(i.Sku!, i.Quantidade, i.PrecoUnitario));
        var entrega = request.Entrega is { } e ? new Endereco(e.Logradouro!, e.Cidade!, e.Uf!, e.Cep!) : null;
        var metadados = request.Metadados is { } m ? new MetadadosPedido(m.Canal!, m.Origem, m.Campanha) : null;
        return Resultado<Pedido>.Ok(Pedido.Criar(request.ClienteId!, regiao, prioridade, itens, entrega, metadados));
    }


    private static void ValidarItens(IReadOnlyList<ItemPedidoRequest> itens, List<ErroValidacao> erros)
    {
        var skus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < itens.Count; i++)
        {
            var campo = $"itens[{i}]";
            switch (itens[i])
            {
                case null:
                    erros.Add(new(campo, "Item nulo."));
                    continue;
                case { Sku: var s } when string.IsNullOrWhiteSpace(s) || s.Trim().Length > ItemPedido.TamanhoMaximoSku:
                    erros.Add(new($"{campo}.sku", $"Obrigatório, até {ItemPedido.TamanhoMaximoSku} caracteres."));
                    break;
                case { Sku: var sku } when !skus.Add(sku!.Trim()):
                    erros.Add(new($"{campo}.sku", "SKU repetido; agrupe a quantidade."));
                    break;
            }

            if (itens[i].Quantidade is <= 0 or > ItemPedido.QuantidadeMaxima)
            {
                erros.Add(new($"{campo}.quantidade", $"Deve estar entre 1 e {ItemPedido.QuantidadeMaxima}."));
            }

            if (itens[i].PrecoUnitario is <= 0m or > ItemPedido.PrecoMaximo
                || decimal.Round(itens[i].PrecoUnitario, 2) != itens[i].PrecoUnitario)
            {
                erros.Add(new($"{campo}.precoUnitario", "Deve ser positivo, com até 2 casas decimais."));
            }
        }
    }

    private static void ValidarEntrega(EnderecoDto? entrega, List<ErroValidacao> erros)
    {
        switch (entrega)
        {
            case null:
                return;
            case { Logradouro: var l } when string.IsNullOrWhiteSpace(l) || l.Length > 120:
                erros.Add(new("entrega.logradouro", "Obrigatório, até 120 caracteres."));
                break;
        }

        if (string.IsNullOrWhiteSpace(entrega.Cidade) || entrega.Cidade.Length > 60)
        {
            erros.Add(new("entrega.cidade", "Obrigatória, até 60 caracteres."));
        }

        if (entrega.Uf is not { Length: 2 } uf || !char.IsAsciiLetter(uf[0]) || !char.IsAsciiLetter(uf[1]))
        {
            erros.Add(new("entrega.uf", "Deve ter 2 letras."));
        }

        if (entrega.Cep is not { Length: 8 } cep || !cep.All(char.IsAsciiDigit))
        {
            erros.Add(new("entrega.cep", "Deve ter 8 dígitos, sem traço."));
        }
    }

    private static void ValidarMetadados(MetadadosDto? metadados, List<ErroValidacao> erros)
    {
        if (metadados is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(metadados.Canal) || metadados.Canal.Length > 20)
        {
            erros.Add(new("metadados.canal", "Obrigatório, até 20 caracteres."));
        }

        if (metadados.Origem is { Length: > 40 })
        {
            erros.Add(new("metadados.origem", "Até 40 caracteres."));
        }

        if (metadados.Campanha is { Length: > 40 })
        {
            erros.Add(new("metadados.campanha", "Até 40 caracteres."));
        }
    }

    // Enum.TryParse aceita "999" e "1,2"; aqui só nomes definidos são aceitos.
    public static bool TentarEnum<TEnum>(string? texto, out TEnum valor) where TEnum : struct, Enum
    {
        valor = default;
        return texto is { Length: > 0 }
            && !char.IsAsciiDigit(texto[0]) && texto[0] != '-'
            && !texto.Contains(',')
            && Enum.TryParse(texto.Trim(), ignoreCase: true, out valor)
            && Enum.IsDefined(valor);
    }
}
