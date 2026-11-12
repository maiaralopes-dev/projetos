using System.Reflection;
using System.Xml.Linq;
using HubPedidos.Application.Contratos;
using HubPedidos.Application.Persistencia;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Notificacoes;
using HubPedidos.Processamento;

namespace HubPedidos.Tests;

/// <summary>
/// Testes arquiteturais ENTRE ASSEMBLIES (extensão da atividade 6). A direção das
/// dependências é verificada no projeto real, em dois níveis:
///  1) referências declaradas nos .csproj (o que o projeto PODE usar);
///  2) referências gravadas no assembly compilado (o que ele REALMENTE usa).
/// </summary>
public class ArquiteturaTests
{
    // Matriz de dependências permitidas entre os módulos/camadas do Hub.
    private static readonly Dictionary<string, string[]> Permitidas = new()
    {
        ["HubPedidos.Domain"] = [],
        ["HubPedidos.Application"] = ["HubPedidos.Domain"],                                   // módulo Pedidos
        ["HubPedidos.Processamento"] = ["HubPedidos.Application", "HubPedidos.Domain"],
        ["HubPedidos.Notificacoes"] = ["HubPedidos.Processamento", "HubPedidos.Application", "HubPedidos.Domain"],
        ["HubPedidos.Infrastructure"] = ["HubPedidos.Application", "HubPedidos.Domain"],
        ["HubPedidos.Api"] = ["HubPedidos.Application", "HubPedidos.Domain", "HubPedidos.Processamento", "HubPedidos.Notificacoes", "HubPedidos.Infrastructure"],
    };

    // Tecnologias proibidas fora da infraestrutura / composição.
    private static readonly string[] Infra = ["Microsoft.EntityFrameworkCore", "Microsoft.Data.Sqlite", "Microsoft.AspNetCore", "SQLitePCLRaw"];
    private static readonly string[] SemInfra = ["HubPedidos.Domain", "HubPedidos.Application", "HubPedidos.Processamento", "HubPedidos.Notificacoes"];

    public static TheoryData<string> Modulos => [.. Permitidas.Keys];

    [Theory]
    [MemberData(nameof(Modulos))]
    public void Referencias_de_projeto_respeitam_a_matriz(string modulo)
    {
        var declaradas = LerProjectReferences(modulo);

        var proibidas = declaradas.Except(Permitidas[modulo]).ToArray();
        Assert.True(proibidas.Length == 0, $"{modulo}.csproj referencia {string.Join(", ", proibidas)} (proibido pela matriz).");
    }

    [Theory]
    [MemberData(nameof(Modulos))]
    public void Assembly_compilado_so_usa_dependencias_permitidas(string modulo)
    {
        var usadas = Assembly.Load(modulo).GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        var internasProibidas = usadas.Where(n => n.StartsWith("HubPedidos.", StringComparison.Ordinal)).Except(Permitidas[modulo]).ToArray();
        Assert.True(internasProibidas.Length == 0, $"{modulo} usa {string.Join(", ", internasProibidas)}.");

        if (SemInfra.Contains(modulo))
        {
            var tecnologia = usadas.Where(n => Infra.Any(p => n.StartsWith(p, StringComparison.Ordinal))).ToArray();
            Assert.True(tecnologia.Length == 0, $"{modulo} depende de infraestrutura: {string.Join(", ", tecnologia)}.");
        }
    }

    [Fact]
    public void Portas_ficam_com_quem_consome_e_implementacoes_fora()
    {
        // (porta, assembly que declara, assembly que implementa)
        (Type Porta, string Declara, string Implementa)[] esperadas =
        [
            (typeof(IRepositorioPedidos), "HubPedidos.Application", "HubPedidos.Infrastructure"),
            (typeof(IConsultasPedidos), "HubPedidos.Application", "HubPedidos.Infrastructure"),
            (typeof(INotificadorPedidos), "HubPedidos.Processamento", "HubPedidos.Notificacoes"),
        ];

        foreach (var (porta, declara, implementa) in esperadas)
        {
            Assert.Equal(declara, porta.Assembly.GetName().Name);
            var implementacoes = Permitidas.Keys.Select(Assembly.Load)
                .SelectMany(a => a.GetTypes())
                .Where(t => t is { IsClass: true, IsAbstract: false } && porta.IsAssignableFrom(t))
                .Where(t => t != typeof(NotificadorNulo)) // padrão nulo do próprio módulo
                .Select(t => t.Assembly.GetName().Name)
                .Distinct();
            Assert.Equal([implementa], implementacoes);
        }

        Assert.True(typeof(INotificadorPedidos).IsAssignableFrom(typeof(RegistroNotificacoes)));
    }

    [Fact]
    public void Dtos_nao_expoem_tipos_do_dominio()
    {
        var dtos = typeof(CriarPedidoRequest).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith(typeof(CriarPedidoRequest).Namespace!, StringComparison.Ordinal) == true && t.IsPublic && !t.IsAbstract);

        var violacoes =
            from dto in dtos
            from prop in dto.GetProperties()
            from tipo in Expandir(prop.PropertyType)
            where tipo.Namespace?.StartsWith("HubPedidos.Domain", StringComparison.Ordinal) == true
            select $"{dto.Name}.{prop.Name} usa {tipo.Name}";

        Assert.Empty(violacoes);
    }

    [Fact]
    public void Dtos_sao_records_selados()
    {
        var dtos = typeof(CriarPedidoRequest).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith(typeof(CriarPedidoRequest).Namespace!, StringComparison.Ordinal) == true && t.IsPublic && !t.IsAbstract);

        Assert.All(dtos, t =>
        {
            Assert.True(t.IsSealed, $"{t.Name} deve ser sealed");
            Assert.NotNull(t.GetMethod("<Clone>$"));
            Assert.All(t.GetProperties(), p => Assert.True(p.SetMethod is null || p.SetMethod.ReturnParameter.GetRequiredCustomModifiers()
                .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)), $"{t.Name}.{p.Name} deve ser init-only"));
        });
    }

    [Fact]
    public void Dominio_continua_sem_dependencias()
    {
        Assert.DoesNotContain(typeof(Pedido).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("HubPedidos.", StringComparison.Ordinal));
    }

    private static string[] LerProjectReferences(string modulo)
    {
        var raiz = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(raiz, "HubPedidos.slnx")))
        {
            raiz = Path.GetDirectoryName(raiz) ?? throw new InvalidOperationException("Raiz da solução não encontrada.");
        }

        var csproj = Path.Combine(raiz, "src", modulo, modulo + ".csproj");
        return [.. XDocument.Load(csproj).Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', '/')))];
    }

    private static IEnumerable<Type> Expandir(Type tipo)
    {
        yield return tipo;
        foreach (var arg in tipo.GetGenericArguments().SelectMany(Expandir))
        {
            yield return arg;
        }
    }
}
