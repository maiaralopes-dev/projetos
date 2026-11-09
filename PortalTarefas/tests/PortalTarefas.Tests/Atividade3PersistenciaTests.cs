using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PortalTarefas.Web.Contratos;
using PortalTarefas.Web.Dados;
using PortalTarefas.Web.Modelos;
using PortalTarefas.Web.Servicos;

namespace PortalTarefas.Tests;

/// <summary>Testes de integração com banco isolado: migrations, seed, transação, concorrência e consultas.</summary>
public class Atividade3PersistenciaTests : IAsyncLifetime
{
    private readonly string _conexao = BancoTeste.NovaConexao();
    private PortalTarefasDbContext NovoContexto() => BancoTeste.CriarContexto(_conexao);
    private static readonly DateOnly Hoje = DateOnly.FromDateTime(DateTime.Today);

    public async Task InitializeAsync()
    {
        await using var ctx = NovoContexto();
        await ctx.Database.MigrateAsync();   // banco limpo criado somente a partir das migrations
    }

    public async Task DisposeAsync()
    {
        await using var ctx = NovoContexto();
        await ctx.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Migrations_criam_esquema_completo_em_banco_limpo()
    {
        await using var ctx = NovoContexto();
        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
        Assert.Equal(2, (await ctx.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task Seed_e_idempotente()
    {
        await using (var ctx = NovoContexto()) await ctx.Database.MigrateAsync();   // "reinício" da aplicação
        await using (var ctx = NovoContexto()) await ctx.Database.MigrateAsync();
        await using var leitura = NovoContexto();
        Assert.Equal(8, await leitura.Tarefas.CountAsync());
    }

    [Fact]
    public async Task Concluir_com_falha_faz_rollback_completo()
    {
        await using var ctx = NovoContexto();
        var servico = new TarefaServicoEf(ctx, NullLogger<TarefaServicoEf>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => servico.ConcluirAsync(3, "teste", simularFalha: true));

        await using var leitura = NovoContexto();
        Assert.Equal(SituacaoTarefa.Pendente, (await leitura.Tarefas.SingleAsync(t => t.Id == 3)).Situacao);
        Assert.False(await leitura.Auditoria.AnyAsync(e => e.TarefaId == 3));
    }

    [Fact]
    public async Task Concluir_com_sucesso_grava_tarefa_e_auditoria()
    {
        await using var ctx = NovoContexto();
        Assert.True(await new TarefaServicoEf(ctx, NullLogger<TarefaServicoEf>.Instance).ConcluirAsync(3, "teste"));

        await using var leitura = NovoContexto();
        Assert.Equal(SituacaoTarefa.Concluida, (await leitura.Tarefas.SingleAsync(t => t.Id == 3)).Situacao);
        Assert.Single(await leitura.Auditoria.Where(e => e.TarefaId == 3 && e.Acao == "Concluida").ToListAsync());
    }

    [Fact]
    public async Task Segunda_edicao_com_versao_antiga_gera_conflito_sem_sobrescrever()
    {
        await using var ctxA = NovoContexto();
        await using var ctxB = NovoContexto();
        var sessaoA = new TarefaServicoEf(ctxA, NullLogger<TarefaServicoEf>.Instance);
        var sessaoB = new TarefaServicoEf(ctxB, NullLogger<TarefaServicoEf>.Instance);

        var lidaA = (await sessaoA.ObterAsync(7))!;
        var lidaB = (await sessaoB.ObterAsync(7))!;   // mesma versão

        lidaA.Situacao = SituacaoTarefa.EmAndamento;
        await sessaoA.AtualizarAsync(lidaA, "ana");

        lidaB.Prioridade = PrioridadeTarefa.Baixa;
        var conflito = await Assert.ThrowsAsync<ConflitoConcorrenciaException>(() => sessaoB.AtualizarAsync(lidaB, "bruno"));
        Assert.Equal(SituacaoTarefa.EmAndamento, conflito.ValoresAtuais!.Situacao);

        await using var leitura = NovoContexto();
        var final = await leitura.Tarefas.SingleAsync(t => t.Id == 7);
        Assert.Equal(SituacaoTarefa.EmAndamento, final.Situacao);    // alteração de A preservada
        Assert.Equal(PrioridadeTarefa.Alta, final.Prioridade);        // alteração de B não aplicada
    }

    [Fact]
    public async Task Painel_ef_e_dapper_retornam_o_mesmo_resultado()
    {
        await using var ctx = NovoContexto();
        var painel = new PainelServico(ctx);
        Assert.Equal(await painel.ResumoEfAsync(Hoje), await painel.ResumoDapperAsync(Hoje));
    }

    [Fact]
    public async Task Leitura_sem_rastreamento_nao_popula_change_tracker()
    {
        await using var ctx = NovoContexto();
        await new TarefaServicoEf(ctx, NullLogger<TarefaServicoEf>.Instance).ListarAsync(null, null);
        Assert.Empty(ctx.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Titulo_duplicado_e_barrado_pelo_indice_unico()
    {
        await using var ctx = NovoContexto();
        ctx.Tarefas.Add(new Tarefa { Titulo = "Testar concorrência" });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
