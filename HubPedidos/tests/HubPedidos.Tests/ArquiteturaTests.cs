using System.Reflection;
using HubPedidos.Application.Contratos;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Tests;

/// <summary>Extensão: testes arquiteturais por reflexão, sem biblioteca externa.</summary>
public class ArquiteturaTests
{
    private static readonly string[] Proibidos = ["Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Microsoft.Data.Sqlite", "HubPedidos.Api", "HubPedidos.Infrastructure"];

    [Fact]
    public void Dominio_nao_depende_de_outras_camadas_nem_de_infraestrutura()
    {
        var referencias = Referencias(typeof(Pedido).Assembly);

        Assert.DoesNotContain(referencias, r => r.StartsWith("HubPedidos.Application", StringComparison.Ordinal));
        Assert.DoesNotContain(referencias, r => Proibidos.Any(p => r.StartsWith(p, StringComparison.Ordinal)));
    }

    [Fact]
    public void Application_e_contratos_nao_dependem_de_infraestrutura()
    {
        var referencias = Referencias(typeof(CriarPedidoRequest).Assembly);

        Assert.DoesNotContain(referencias, r => Proibidos.Any(p => r.StartsWith(p, StringComparison.Ordinal)));
    }

    [Fact]
    public void Dtos_nao_expoem_tipos_do_dominio()
    {
        var dtos = typeof(CriarPedidoRequest).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(CriarPedidoRequest).Namespace);

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
            .Where(t => t.Namespace == typeof(CriarPedidoRequest).Namespace && t.IsPublic);

        Assert.All(dtos, t =>
        {
            Assert.True(t.IsSealed, $"{t.Name} deve ser sealed");
            Assert.NotNull(t.GetMethod("<Clone>$")); // marca gerada pelo compilador em records
            Assert.All(t.GetProperties(), p => Assert.True(p.SetMethod is null || p.SetMethod.ReturnParameter.GetRequiredCustomModifiers()
                .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)), $"{t.Name}.{p.Name} deve ser init-only"));
        });
    }

    private static string[] Referencias(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(a => a.Name!)];

    private static IEnumerable<Type> Expandir(Type tipo)
    {
        yield return tipo;
        foreach (var arg in tipo.GetGenericArguments().SelectMany(Expandir))
        {
            yield return arg;
        }
    }
}
