from base import Relatorio, REPO, acesso, trecho

EV = "docs/evidencias/atividade3"
COMMIT = "614fc20"


def gerar():
    r = Relatorio(3, "EF Core, migrations, seed, pooling, transações, concorrência e acesso leve",
                  "Unidade 3 – Persistência e conexão com banco de dados",
                  "Persistir as tarefas em banco relacional, aplicar migrations e seed, tratar concorrência e comparar EF Core com Dapper ou ADO.NET.")
    r.capa()
    r.cabecalho_atividade()

    r.h1("1. Identificação e ambiente")
    r.p(f"Repositório: {REPO}; commit `{COMMIT}`. Provedor **SQL Server 2022** em contêiner Docker (`mcr.microsoft.com/mssql/server:2022-latest`), "
        "**EF Core 10.0.14** (`Microsoft.EntityFrameworkCore.SqlServer` e `.Design`), **dotnet-ef 10.0.14** e **Dapper 2.1.89**. "
        "A connection string de desenvolvimento fica em **User Secrets** (`dotnet user-secrets set ConnectionStrings:PortalTarefas ...`) "
        "e a senha do contêiner em `.env`, ambos fora do Git; o repositório traz apenas `.env.example` sem valores.")

    r.h1("2. Resumo teórico aplicado ao projeto")
    r.p("**DbContext.** `PortalTarefasDbContext` é a unidade de trabalho: rastreia as entidades lidas, detecta mudanças e "
        "gera os comandos no `SaveChanges`. Seu ciclo de vida é **por requisição** (escopo de DI); não é *thread-safe* e não "
        "deve ser compartilhado. Leituras que não serão alteradas usam `AsNoTracking()` para não ocupar o *change tracker*.")
    r.p("**Mapeamento.** Convenções definem chave (`Id`) e nomes; a **Fluent API** (`TarefaConfiguration`) ajusta limites "
        "(`Titulo` 120, `Descricao` 1000), enums gravados como texto, `Prazo` como `date`, valor padrão `SYSDATETIMEOFFSET()`, "
        "índice único em `Titulo`, relação 1:N `Tarefa` → `EventoAuditoria` com exclusão em cascata e a `rowversion`.")
    r.p("**Migrations e seed.** Migrations versionam a *evolução do esquema* em código revisável; não são backup — não "
        "guardam dados nem substituem cópias de segurança, e um `Down` pode apagar colunas com dados. O seed usa "
        "`UseSeeding/UseAsyncSeeding` (EF Core 9+), executado no `Migrate()` e no `dotnet ef database update`, e é "
        "**idempotente**: só insere se a tabela estiver vazia.")
    r.p("**Pooling.** `AddDbContextPool` reaproveita instâncias de contexto (menos alocação); por isso o contexto não "
        "guarda estado próprio nem recebe serviços com estado no construtor — o pool só reinicia o que o EF conhece. "
        "O pool de **conexões** é do provedor ADO.NET (SqlClient) e funciona independentemente: o EF e o Dapper abrem e "
        "fecham conexões que voltam ao mesmo pool.")
    r.p("**Transações.** Cada `SaveChanges` já é atômico (transação implícita). Quando uma operação precisa de **vários** "
        "`SaveChanges` (ou SQL manual) como unidade, usa-se `Database.BeginTransactionAsync()`; sem `Commit` o `Dispose` faz "
        "rollback. Com `EnableRetryOnFailure`, a transação explícita precisa rodar dentro de `CreateExecutionStrategy().ExecuteAsync` "
        "para que a unidade inteira seja repetida. Uma `DbTransaction` explícita (ADO.NET) é o mesmo mecanismo em nível mais baixo, "
        "útil para compartilhar a transação entre EF e Dapper.")
    r.p("**Concorrência otimista.** Em vez de bloquear, cada linha tem um token; no SQL Server, `rowversion` muda a cada "
        "`UPDATE`. O EF gera `UPDATE ... WHERE Id = @id AND Versao = @versaoOriginal`; se nenhuma linha é afetada, lança "
        "`DbUpdateConcurrencyException`. Estratégia adotada: **o banco vence** — nada é sobrescrito, o usuário vê os valores "
        "atuais e decide se salva de novo com a nova versão.")
    r.p("**EF Core × Dapper/ADO.NET.** EF: LINQ tipado, rastreamento, migrations e menos código para CRUD. Dapper: SQL "
        "escrito à mão, controle total do comando, menor sobrecarga, ideal para consultas de leitura específicas (relatórios, painéis).")

    r.h1("3. Código-fonte e evidências")
    r.p("Comandos aplicados (saídas completas em `docs/evidencias/atividade3/`): `dotnet ef migrations add Inicial`, revisão do "
        "script com `dotnet ef migrations script --idempotent` e `dotnet ef database update`. Antes: o banco não existia; depois: "
        "tabelas `Tarefas`, `EventosAuditoria` e `__EFMigrationsHistory` e 8 tarefas do seed. Trecho do SQL revisado:", tamanho=10.5)
    r.codigo(trecho(f"{EV}/02-script-inicial.sql", 18, 28), tamanho=7)
    r.p("**Transação explícita** (`TarefaServicoEf.ConcluirAsync`): 1º `SaveChanges` faz o `UPDATE` da tarefa, 2º faz o "
        "`INSERT` da auditoria; uma falha simulada entre os dois comandos é lançada para provar o rollback (Tabela 2). "
        "**Concorrência:** a edição envia a versão lida em um campo oculto; o serviço a define como `OriginalValue` e o SQL gerado foi:", tamanho=10.5)
    r.codigo(trecho(f"{EV}/13-sql-update-concorrencia.txt", cortar=230), tamanho=7)
    r.figura_img(f"{EV}/06-sessao2-conflito.png", "Segunda sessão recebe o conflito com os valores atuais (nada foi sobrescrito)", 10.5)
    r.p("**Consulta de painel** (totais por situação, vencidas e próximo prazo) em duas versões: EF Core com `GroupBy` + projeção "
        "+ `AsNoTracking`, traduzida em **um único** `SELECT ... GROUP BY` com parâmetro `@hoje`; e Dapper com SQL parametrizado "
        "(`@Hoje`, `@Concluida`), sem interpolar valores. Os resultados foram comparados automaticamente (`diff`): **idênticos**.", tamanho=10.5)
    r.tabela_dados(["Aspecto", "EF Core", "Dapper"], [
        ["Código", "~15 linhas de LINQ, tipado e refatorável", "~20 linhas (SQL + mapeamento para record)"],
        ["Rastreamento", "Desligado com `AsNoTracking` e projeção", "Não existe (só materializa)"],
        ["Controle do SQL", "Gerado pelo provedor (revisado no log)", "Total; mesmo SQL em qualquer versão"],
        ["Clareza / manutenção", "Erros de nome no compilador", "Erros de SQL só em execução (coberto por teste)"],
        ["Uso indicado", "CRUD, regras de domínio, transações", "Leituras específicas e performance crítica"],
    ], larguras=[3.6, 6.2, 6.2], titulo="Comparação das duas versões da consulta de painel")

    r.h1("4. Testes e validações obrigatórias")
    r.tabela_dados(["Caso exigido", "Resultado obtido", "Evidência"], [
        ["Banco a partir das migrations em ambiente limpo", "Banco removido e recriado: `Inicial` + `IndiceSituacaoPrazo` aplicadas", "`00-banco-limpo.txt`; teste `Migrations_criam_*`"],
        ["Seed idempotente ao reiniciar", "Após 1º e 2º início da aplicação: 8 tarefas (sem duplicar)", "`08-seed-idempotente.txt`; `Seed_e_idempotente`"],
        ["Transação com sucesso e rollback", "Falha simulada → 500; tarefa 6 continua `Pendente` e 0 auditorias. Sem falha → 204; `Concluida` + 1 auditoria", "`09-transacao.txt`; 2 testes"],
        ["Conflito com duas edições", "Sessão 1 salva; sessão 2 (versão antiga) recebe aviso e valores atuais; versão `…07D7` → `…07DC`", "Fig. 1, B1–B3; `10-concorrencia.txt`"],
        ["Consulta eficiente com projeção", "1 `SELECT ... GROUP BY`; `ChangeTracker` vazio após leitura", "`12-sql-gerado-painel.txt`; `Leitura_sem_rastreamento_*`"],
    ], larguras=[4, 8, 4], titulo="Testes obrigatórios (9 testes de integração em banco SQL Server isolado)")

    r.h1("5. Aprimoramentos e análise")
    r.itens([
        "**Índice por migration:** a 2ª migration (`IndiceSituacaoPrazo`) cria `IX_Tarefas_Situacao_Prazo`, que apoia o filtro por situação e a busca de vencidas; o `Down` remove o índice. Evidência: `04-migration-indice.txt`.",
        "**Teste de integração com banco isolado:** cada classe cria `PortalTarefas_Testes_<guid>` pelas migrations e o remove ao final (`EnsureDeleted`); a conexão vem da variável `PORTAL_SQL_TESTES`.",
        "**Medição local** (500 execuções após aquecimento, log de SQL desligado): mediana EF 1,28 ms × Dapper 0,94 ms; p95 1,60 × 1,42 ms. Com 8 linhas, na mesma máquina e em Debug, isso **não** permite concluir desempenho geral; só indica a sobrecarga fixa de tradução/materialização do EF.",
        "**Defeito encontrado depois (unidade 4) e corrigido:** se nenhuma coluna mudava, o EF não emitia `UPDATE` (a `rowversion` é gerada pelo banco) e a versão antiga não era conferida. A correção marca `Titulo` como modificado, forçando o `WHERE Versao = @original`; o teste de regressão `Versao_antiga_sem_mudancas_gera_conflito` cobre o caso.",
    ], tamanho=10.5)

    r.referencias([
        acesso("DbContext Lifetime, Configuration, and Initialization", "https://learn.microsoft.com/ef/core/dbcontext-configuration/"),
        acesso("Migrations Overview", "https://learn.microsoft.com/ef/core/managing-schemas/migrations/"),
        acesso("Data Seeding", "https://learn.microsoft.com/ef/core/modeling/data-seeding"),
        acesso("Advanced Performance Topics: DbContext pooling", "https://learn.microsoft.com/ef/core/performance/advanced-performance-topics"),
        acesso("Transactions", "https://learn.microsoft.com/ef/core/saving/transactions"),
        acesso("Handling Concurrency Conflicts", "https://learn.microsoft.com/ef/core/saving/concurrency"),
        acesso("Connection Resiliency", "https://learn.microsoft.com/ef/core/miscellaneous/connection-resiliency"),
        "DAPPERLIB. **Dapper**. GitHub, 2025. Disponível em: https://github.com/DapperLib/Dapper. Acesso em: 9 nov. 2026.",
    ])

    r.nova_pagina()
    r.h1("Apêndice A – Código-fonte integral")
    for commit, arq in ((COMMIT, "src/PortalTarefas.Web/Modelos/Tarefa.cs"), (COMMIT, "src/PortalTarefas.Web/Dados/PortalTarefasDbContext.cs"),
                        (COMMIT, "src/PortalTarefas.Web/Dados/Configuracoes/TarefaConfiguration.cs"), (COMMIT, "src/PortalTarefas.Web/Dados/SementeDados.cs"),
                        (COMMIT, "src/PortalTarefas.Web/Dados/Migrations/20261109011900_Inicial.cs"),
                        ("65c1594", "src/PortalTarefas.Web/Servicos/TarefaServicoEf.cs"), (COMMIT, "src/PortalTarefas.Web/Servicos/PainelServico.cs")):
        r.arquivo_git(commit, arq)
    r.p("Registro no `Program.cs` (commit `614fc20`):", tamanho=10)
    r.codigo('''var conexao = builder.Configuration.GetConnectionString("PortalTarefas")
    ?? throw new InvalidOperationException("Connection string 'PortalTarefas' não configurada. ...");
builder.Services.AddDbContextPool<PortalTarefasDbContext>(o => o
    .UseSqlServer(conexao, sql => sql.EnableRetryOnFailure(maxRetryCount: 3))
    .UseSeeding((ctx, _) => SementeDados.Semear(ctx))
    .UseAsyncSeeding((ctx, _, ct) => SementeDados.SemearAsync(ctx, ct)),
    poolSize: 128);
builder.Services.AddScoped<ITarefaServico, TarefaServicoEf>();
builder.Services.AddScoped<PainelServico>();''', tamanho=7)

    r.h1("Apêndice B – Evidências de execução")
    r.codigo(open(__import__("os").path.join(__import__("base").RAIZ, f"{EV}/09-transacao.txt"), encoding="utf-8").read().replace("\n\n", "\n"), "**Transação: rollback e commit (antes/depois no banco)**", 6.5)
    r.codigo(trecho(f"{EV}/10-concorrencia.txt"), "**Concorrência com duas sessões**", 7)
    r.codigo(trecho(f"{EV}/08-seed-idempotente.txt") + "\n\n" + trecho(f"{EV}/12-sql-gerado-painel.txt", cortar=400), "**Seed idempotente e SQL gerado pelo EF para o painel**", 7)
    r.figuras_apendice(EV, [("05-sessao1-salvou.png", "B1", "Sessão 1 salvou a alteração"),
                            ("06-sessao2-conflito.png", "B2", "Sessão 2 com versão antiga: conflito detectado"),
                            ("07-sessao2-salvou-apos-revisar.png", "B3", "Sessão 2 salvou após revisar os valores atuais")], 9.5)
    return r.salvar("Relatorio-03-Persistencia-EF-Core.docx")
