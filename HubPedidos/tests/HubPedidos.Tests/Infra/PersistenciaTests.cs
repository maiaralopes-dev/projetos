using HubPedidos.Application.Classificacao;
using HubPedidos.Application.Persistencia;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using static HubPedidos.Tests.Construtores;

namespace HubPedidos.Tests.Infra;

public class PersistenciaTests
{
    private static async Task<Guid> InserirAsync(BancoEmMemoria banco, Pedido pedido)
    {
        await using var db = banco.NovoContexto();
        await new RepositorioPedidos(db).AdicionarAsync(pedido, ClassificadorPedidos.Classificar(pedido), default);
        return pedido.Id.Valor;
    }

    [Fact]
    public async Task Migration_corresponde_ao_modelo_e_ao_provedor_registrado()
    {
        await using var banco = await BancoEmMemoria.CriarAsync();
        await using var db = banco.NovoContexto();

        Assert.False(db.Database.HasPendingModelChanges(), "Modelo mudou sem migration correspondente.");
        // As decisões de mapeamento (JSON, centavos) foram verificadas para este provedor/versão.
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
        Assert.Equal(10, typeof(DbContext).Assembly.GetName().Version!.Major);
    }

    [Fact]
    public async Task Ida_e_volta_preserva_valores_monetarios_endereco_e_metadados()
    {
        await using var banco = await BancoEmMemoria.CriarAsync();
        var pedido = Domain.Pedidos.Pedido.Criar("CLI-1", Regiao.Sul, Prioridade.Normal,
            [Item("A", 1, 0.01m), Item("B", 1000, 999_999.99m)],
            new Endereco("Rua das Flores, 10", "Curitiba", "pr", "80000000"),
            new MetadadosPedido("WEB", "parceiro", null));
        var id = await InserirAsync(banco, pedido);

        await using var db = banco.NovoContexto();
        var detalhe = await new ConsultasPedidos(db).ObterDetalheAsync(id, default);

        Assert.Equal(999_999_990.01m, detalhe!.Total);
        Assert.Equal([0.01m, 999_999_990m], detalhe.Itens.Select(i => i.Subtotal));
        Assert.Equal(new Application.Contratos.EnderecoDto("Rua das Flores, 10", "Curitiba", "PR", "80000000"), detalhe.Entrega);
        Assert.Equal(new Application.Contratos.MetadadosDto("web", "parceiro", null), detalhe.Metadados);
    }

    [Fact]
    public async Task Endereco_opcional_nulo_volta_nulo()
    {
        await using var banco = await BancoEmMemoria.CriarAsync();
        var id = await InserirAsync(banco, Pedido());

        await using var db = banco.NovoContexto();
        var detalhe = await new ConsultasPedidos(db).ObterDetalheAsync(id, default);

        Assert.Null(detalhe!.Entrega);
        Assert.Equal("api", detalhe.Metadados.Canal);
    }

    [Fact]
    public async Task Alteracao_com_tracking_gera_update_somente_do_status()
    {
        await using var banco = await BancoEmMemoria.CriarAsync();
        var id = await InserirAsync(banco, Pedido());
        banco.Contador.Limpar();

        await using (var db = banco.NovoContexto())
        {
            Assert.Equal(ResultadoAlteracaoStatus.Alterado, await new RepositorioPedidos(db).AlterarStatusAsync(id, StatusPedido.EmProcessamento, default));
            Assert.Single(db.ChangeTracker.Entries<PedidoRegistro>()); // a consulta usou tracking
        }

        Assert.Equal(2, banco.Contador.Comandos.Count);
        Assert.StartsWith("UPDATE \"Pedidos\" SET \"Status\" = @", banco.Contador.Comandos[1]);
        Assert.DoesNotContain(",", banco.Contador.Comandos[1].Split("WHERE")[0]); // uma única coluna no SET

        await using var leitura = banco.NovoContexto();
        Assert.Equal(StatusPedido.EmProcessamento, (await leitura.Pedidos.SingleAsync(p => p.Id == id)).Status);
    }

    [Fact]
    public async Task Transicao_invalida_e_pedido_inexistente_nao_gravam_nada()
    {
        await using var banco = await BancoEmMemoria.CriarAsync();
        var id = await InserirAsync(banco, Pedido());
        banco.Contador.Limpar();
        await using var db = banco.NovoContexto();
        var repositorio = new RepositorioPedidos(db);

        Assert.Equal(ResultadoAlteracaoStatus.TransicaoInvalida, await repositorio.AlterarStatusAsync(id, StatusPedido.Concluido, default));
        Assert.Equal(ResultadoAlteracaoStatus.NaoEncontrado, await repositorio.AlterarStatusAsync(Guid.NewGuid(), StatusPedido.Cancelado, default));
        Assert.DoesNotContain(banco.Contador.Comandos, c => c.StartsWith("UPDATE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Alternativa_sem_tracking_anexa_explicitamente_e_nao_corrompe_outras_colunas()
    {
        await using var banco = await BancoEmMemoria.CriarAsync();
        var original = Domain.Pedidos.Pedido.Criar("CLI-9", Regiao.Norte, Prioridade.Alta, [Item("A", 3, 7.5m)], null, new MetadadosPedido("app", "x", "y"));
        var id = await InserirAsync(banco, original);
        banco.Contador.Limpar();

        await using (var db = banco.NovoContexto())
        {
            Assert.Equal(ResultadoAlteracaoStatus.Alterado, await new RepositorioPedidos(db).AlterarStatusSemTrackingAsync(id, StatusPedido.Cancelado, default));
            Assert.Empty(db.ChangeTracker.Entries()); // nada ficou rastreado
        }

        Assert.Equal(2, banco.Contador.Comandos.Count);
        Assert.DoesNotContain("\"Metadados\"", banco.Contador.Comandos[0]); // leitura só do status
        Assert.StartsWith("UPDATE \"Pedidos\" SET \"Status\" = @", banco.Contador.Comandos[1]);

        await using var leitura = banco.NovoContexto();
        var gravado = await leitura.Pedidos.AsNoTracking().SingleAsync(p => p.Id == id);
        Assert.Equal((StatusPedido.Cancelado, "CLI-9", 22.5m, "app", "x"), (gravado.Status, gravado.ClienteId, gravado.Total, gravado.Metadados.Canal, gravado.Metadados.Origem));
    }

    [Theory]
    [InlineData(MapeamentoMetadados.Json, "\"p\".\"Metadados\" ->> 'Canal'", "UPDATE \"Pedidos\" SET \"Metadados\" = @")]
    [InlineData(MapeamentoMetadados.Relacional, "\"p\".\"Metadados_Canal\"", "UPDATE \"Pedidos\" SET \"Metadados_Canal\" = @")]
    public async Task Json_e_relacional_filtram_igual_mas_geram_sql_diferente(MapeamentoMetadados modo, string trechoFiltro, string trechoUpdate)
    {
        await using var banco = await BancoEmMemoria.CriarAsync(semear: 300, mapeamento: modo);
        await using var db = banco.NovoContexto();

        // Filtro por propriedade do agregado
        var canal = "web";
        var quantidade = await db.Pedidos.CountAsync(p => p.Metadados.Canal == canal);
        Assert.Contains(trechoFiltro, banco.Contador.Comandos[^1]);
        var esperado = (await db.Pedidos.AsNoTracking().ToListAsync()).Count(p => p.Metadados.Canal == canal);
        Assert.Equal(esperado, quantidade); // SQL traduzido = filtro em memória
        Assert.Equal(50, quantidade);       // mesma semente => mesma massa nos dois mapeamentos

        // Atualização via change tracking de UMA propriedade do agregado
        var registro = await db.Pedidos.OrderBy(p => p.Id).FirstAsync();
        registro.Metadados.Canal = "canal-teste"; // valor ausente da massa: garante mudança real
        banco.Contador.Limpar();
        await db.SaveChangesAsync();
        Assert.StartsWith(trechoUpdate, banco.Contador.Comandos.Single());

    }

    [Fact]
    public async Task ExecuteUpdate_em_json_usa_json_set_sem_reescrever_o_documento_no_cliente()
    {
        await using var banco = await BancoEmMemoria.CriarAsync(semear: 50);
        await using var db = banco.NovoContexto();
        banco.Contador.Limpar();

        var afetados = await db.Pedidos.Where(p => p.Metadados.Canal == "web")
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Metadados.Campanha, "migrada"));

        Assert.True(afetados > 0);
        Assert.Contains("json_set(\"p\".\"Metadados\", '$.Campanha', @", banco.Contador.Comandos.Single());
        Assert.Equal(afetados, await db.Pedidos.CountAsync(p => p.Metadados.Campanha == "migrada"));
    }

    [Fact]
    public async Task Modelos_json_e_relacional_coexistem_no_mesmo_processo()
    {
        await using var json = await BancoEmMemoria.CriarAsync(mapeamento: MapeamentoMetadados.Json);
        await using var relacional = await BancoEmMemoria.CriarAsync(mapeamento: MapeamentoMetadados.Relacional);
        await using var a = json.NovoContexto();
        await using var b = relacional.NovoContexto();

        // Sem a IModelCacheKeyFactory customizada, o segundo contexto reutilizaria o modelo do primeiro.
        Assert.NotSame(a.Model, b.Model);
        Assert.True(a.Model.FindEntityType(typeof(PedidoRegistro))!.FindComplexProperty(nameof(PedidoRegistro.Metadados))!.ComplexType.IsMappedToJson());
        Assert.False(b.Model.FindEntityType(typeof(PedidoRegistro))!.FindComplexProperty(nameof(PedidoRegistro.Metadados))!.ComplexType.IsMappedToJson());
    }
}

/// <summary>
/// Restrição documentada do EF.CompileAsyncQuery. Roda fora do paralelismo para que o
/// modelo JSON seja sempre o primeiro a compilar a consulta estática.
/// </summary>
[Collection(nameof(Importacao.SemParalelismo))]
public class ConsultaCompiladaModeloTests
{
    [Fact]
    public async Task Consulta_compilada_fica_presa_ao_primeiro_modelo()
    {
        await using var json = await BancoEmMemoria.CriarAsync(semear: 5, mapeamento: MapeamentoMetadados.Json);
        await using var relacional = await BancoEmMemoria.CriarAsync(semear: 5, mapeamento: MapeamentoMetadados.Relacional);
        await using var dbJson = json.NovoContexto();
        await using var dbRelacional = relacional.NovoContexto();
        var idJson = await dbJson.Pedidos.Select(p => p.Id).FirstAsync();
        var idRelacional = await dbRelacional.Pedidos.Select(p => p.Id).FirstAsync();

        var normal = await new ConsultasPedidos(dbRelacional).ObterResumoAsync(idRelacional, default);
        await new ConsultasPedidos(dbJson).ObterResumoCompiladoAsync(idJson, default); // compila com o modelo JSON
        var excecao = await Record.ExceptionAsync(() => new ConsultasPedidos(dbRelacional).ObterResumoCompiladoAsync(idRelacional, default));

        // A consulta normal funciona nos dois modelos; a compilada lança exceção no segundo.
        Assert.NotNull(normal);
        var invalida = Assert.IsType<InvalidOperationException>(excecao);
        Assert.Contains("Compiled queries can only be used with a single model", invalida.Message);
    }
}
