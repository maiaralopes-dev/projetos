using HubPedidos.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HubPedidos.Tests.Infra;

/// <summary>
/// Banco SQLite em memória para testes de consulta. A conexão fica ABERTA durante
/// todo o teste: no SQLite, o banco em memória deixa de existir quando ela fecha.
/// </summary>
public sealed class BancoEmMemoria : IAsyncDisposable
{
    private readonly SqliteConnection _conexao;

    private BancoEmMemoria(SqliteConnection conexao, MapeamentoMetadados mapeamento)
    {
        _conexao = conexao;
        Mapeamento = mapeamento;
    }

    public ContadorComandos Contador { get; } = new();

    public MapeamentoMetadados Mapeamento { get; }

    public static async Task<BancoEmMemoria> CriarAsync(int semear = 0, MapeamentoMetadados mapeamento = MapeamentoMetadados.Json)
    {
        var conexao = new SqliteConnection("DataSource=:memory:");
        await conexao.OpenAsync();
        var banco = new BancoEmMemoria(conexao, mapeamento);

        await using var db = banco.NovoContexto();
        if (mapeamento == MapeamentoMetadados.Json)
        {
            await db.Database.MigrateAsync(); // valida a migration real
        }
        else
        {
            await db.Database.EnsureCreatedAsync(); // modelo alternativo não tem migration
        }

        if (semear > 0)
        {
            await GeradorMassa.SemearAsync(db, semear);
        }

        banco.Contador.Limpar();
        return banco;
    }

    public HubDbContext NovoContexto() =>
        new(new DbContextOptionsBuilder<HubDbContext>().UseSqlite(_conexao).AddInterceptors(Contador).Options, new OpcoesModeloHub(Mapeamento));

    public ValueTask DisposeAsync() => _conexao.DisposeAsync();
}
