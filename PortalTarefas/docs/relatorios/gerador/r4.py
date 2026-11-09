from base import Relatorio, REPO, acesso, trecho, bloco_entre

EV = "docs/evidencias/atividade4"
COMMIT = "65c1594"


def gerar():
    r = Relatorio(4, "Recursos REST, paginação, filtros, Problem Details, OpenAPI e cliente TypeScript",
                  "Unidade 4 – APIs REST e OpenAPI",
                  "Expor o domínio de tarefas como API HTTP previsível, documentar o contrato e gerar um cliente TypeScript reproduzível.")
    r.capa()
    r.cabecalho_atividade()

    r.h1("1. Identificação e ambiente")
    r.p(f"Repositório: {REPO}; commit `{COMMIT}`. Pacotes: `Microsoft.AspNetCore.OpenApi` 10.0.14 (`AddOpenApi`/`MapOpenApi`, "
        "OpenAPI 3.1), `Microsoft.Extensions.ApiDescription.Server` 10.0.14 (gera o documento no build), `Scalar.AspNetCore` 2.19.0 "
        "(UI interativa só em Development), **NSwag 14.7.1** (npm, runtime Net100), Node 22.23.4 e TypeScript 7.0.")

    r.h1("2. Resumo teórico aplicado ao projeto")
    r.p("**Recursos, URIs e métodos.** O recurso é a coleção `/api/v2/tarefas` e cada item `/api/v2/tarefas/{id}`. GET lê "
        "(seguro e idempotente), POST cria (não idempotente; responde **201** com `Location`), PUT substitui o item "
        "(idempotente; 200), DELETE remove (idempotente; 204 e depois 404). Erros: 400 (entrada inválida), 404 (inexistente), "
        "409 (conflito de concorrência ou título duplicado).")
    r.p("**Separação de modelos.** A entidade `Tarefa` (com `Versao` binária, auditoria e navegações) nunca é serializada. A API "
        "usa `CriarTarefaDto` e `AtualizarTarefaDto` (entrada), `TarefaResumoDto` e `TarefaDetalhesDto` (saída) e "
        "`PaginaDto<T>` (representação paginada com `itens`, `pagina`, `tamanho`, `totalItens`, `totalPaginas`).")
    r.p("**Paginação, ordenação e filtros.** `pagina` ≥ 1 (padrão 1), `tamanho` de 1 a 50 (padrão 10); valores fora do limite "
        "são **rejeitados com 400** em vez de corrigidos silenciosamente. `ordenarPor` aceita só a **lista permitida** "
        "(`id`, `titulo`, `prazo`, `prioridade`, `situacao`, `criadaEm`) mapeada para expressões conhecidas — nunca para "
        "nomes de coluna vindos do usuário. Filtros: `situacao`, `prioridade`, `busca`, `prazoAte`. Tudo é aplicado no "
        "`IQueryable` antes do `ToListAsync`, ou seja, vira `WHERE/ORDER BY/OFFSET FETCH` no SQL.")
    r.p("**Problem Details.** Formato JSON padronizado (`type`, `title`, `status`, `detail`, `instance` + extensões) para que "
        "clientes tratem erros automaticamente. A **RFC 9457** substitui a RFC 7807 citada no plano. Extensões usadas: "
        "`errors` (validação), `correlationId`, `traceId` e `versaoAtual` (409 de concorrência) — nenhuma com dado sensível.")
    r.p("**OpenAPI × UI interativa.** O documento OpenAPI (`/openapi/v2.json`) é o **contrato** legível por máquina; Swagger UI, "
        "Scalar ou ReDoc são apenas visualizadores que leem esse documento. Por isso o documento é publicado em todos os "
        "ambientes, enquanto a UI Scalar só existe em Development.")
    r.p("**Cliente gerado e compatibilidade.** O cliente TypeScript é derivado do documento: se o contrato muda de forma "
        "incompatível (remover/renomear campo, mudar tipo, tornar obrigatório), o código que usa o cliente deixa de compilar "
        "ou falha em execução; mudanças compatíveis (novo campo de saída, novo parâmetro opcional) só exigem regenerar.")

    r.h1("3. Código-fonte e evidências")
    r.tabela_dados(["Método e URI", "Sucesso", "Erros previstos"], [
        ["`GET /api/v2/tarefas?pagina&tamanho&ordenarPor&direcao&situacao&prioridade&busca&prazoAte`", "200 `PaginaTarefaResumo` + `X-Total-Count` + `Link`", "400"],
        ["`GET /api/v2/tarefas/{id}`", "200 `TarefaDetalhesDto`", "404"],
        ["`POST /api/v2/tarefas`", "201 + `Location`", "400, 409 (título duplicado)"],
        ["`PUT /api/v2/tarefas/{id}` (com `versao`)", "200 `TarefaDetalhesDto`", "400, 404, 409 (`versaoAtual`)"],
        ["`DELETE /api/v2/tarefas/{id}`", "204", "404"],
    ], larguras=[8.4, 4.4, 3.2], titulo="Contrato definido antes da implementação")
    r.p("**Revisão do documento OpenAPI** (etapa 3): o primeiro documento gerado tinha problemas corrigidos antes da entrega — "
        "(1) `pagina`/`tamanho` saíam como **obrigatórios** e em PascalCase → propriedades anuláveis com `[FromQuery(Name=...)]` e `[DefaultValue]`; "
        "(2) números como `type: [integer, string]` com *pattern* → `NumberHandling = Strict` no JSON; "
        "(3) nome genérico `PaginaDtoOfTarefaResumoDto` → `CreateSchemaReferenceId` gera `PaginaTarefaResumo`; "
        "(4) respostas 404/409 ausentes → `ProducesProblem(...)`; (5) `descricao` obrigatória no POST → parâmetro com valor padrão. "
        "Nulabilidade conferida: `descricao` e `prazo` como `[\"null\",\"string\"]`.", tamanho=10.5)
    r.p("**Cliente TypeScript reproduzível:** o build do .NET grava `docs/openapi/portal-tarefas_v2.json` (artefato versionado); "
        "`npm run gerar-cliente` executa `nswag run nswag.json`, que gera `src/api/gerado/portal-tarefas-client.ts` (classe "
        "`PortalTarefasClient`, template Fetch, enums como *string literal*). Chamada real usando só o cliente gerado:", tamanho=10.5)
    r.codigo(trecho(f"{EV}/04-cliente-ts-chamada-real.txt"), tamanho=7.5)
    r.p("Exemplos de resposta (saídas completas no Apêndice B): **sucesso** com cabeçalhos de paginação, **página vazia** "
        "(`pagina=99` → `itens: []`), **validação** (`tamanho=51` → 400 com `errors.Tamanho`) e **inexistente** (`/999` → 404).", tamanho=10.5)

    r.h1("4. Testes e validações obrigatórias")
    r.tabela_dados(["Caso exigido", "Resultado obtido", "Evidência"], [
        ["Paginação com limites mínimo e máximo", "`tamanho=0`, `tamanho=51`, `pagina=0` → 400; `tamanho=1` e `tamanho=50` → 200", "`01-requisicoes.txt`; 5 testes"],
        ["Ordenação permitida e não permitida", "`ordenarPor=prazo` ordena; `ordenarPor=versao` → 400 com a lista aceita", "`Ordenacao_*` (2 testes)"],
        ["Combinação de dois filtros", "`situacao=Pendente&prioridade=Alta` e `situacao + prazoAte` retornam só itens que atendem a ambos", "`Combinacao_de_dois_filtros`"],
        ["Problem Details 400, 404 e 409", "400 validação; 404 inexistente; 409 título duplicado e 409 versão antiga com `versaoAtual`", "`Crud_completo_*`, `Post_invalido_*`"],
        ["Regeneração após mudança compatível", "Novo campo `vencida` → diff do OpenAPI e do cliente; `tsc` sem erros", "`06-regeneracao-cliente.txt`"],
    ], larguras=[4, 8, 4], titulo="Testes obrigatórios (14 testes de API e contrato, todos aprovados)")

    r.h1("5. Aprimoramentos")
    r.itens([
        "**Teste de contrato (snapshot):** `Documento_openapi_corresponde_ao_snapshot_versionado` compara o documento servido com o artefato em `docs/openapi`; qualquer mudança não revisada no contrato quebra o teste.",
        "**Versionamento com mudança incompatível controlada:** a v1 (unidade 1) devolvia um *array*; a v2 devolve uma página. A v1 continua ativa, mas responde `Deprecation: @1798761600` (RFC 9745) e `Link: </api/v2/tarefas>; rel=\"successor-version\"`.",
        "**Metadados de paginação** documentados: `X-Total-Count` e `Link` com `first`, `prev`, `next`, `last` (RFC 8288), preservando os demais filtros da URL.",
        "**Defeito real encontrado pelos testes:** um PUT repetido com a versão antiga e os mesmos valores retornava 200, pois o EF não emitia `UPDATE`; corrigido (ver Relatório 3).",
    ], tamanho=10.5)

    r.referencias([
        acesso("Create responses in Minimal API applications", "https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/responses"),
        acesso("Generate OpenAPI documents", "https://learn.microsoft.com/aspnet/core/fundamentals/openapi/aspnetcore-openapi"),
        acesso("Customize OpenAPI documents", "https://learn.microsoft.com/aspnet/core/fundamentals/openapi/customize-openapi"),
        "NOTTINGHAM, M.; WILDE, E.; DALAL, S. **RFC 9457: Problem Details for HTTP APIs**. IETF, 2023. Disponível em: https://www.rfc-editor.org/rfc/rfc9457. Acesso em: 9 nov. 2026.",
        "NOTTINGHAM, M. **RFC 8288: Web Linking**. IETF, 2017. Disponível em: https://www.rfc-editor.org/rfc/rfc8288. Acesso em: 9 nov. 2026.",
        "DALAL, S.; WILDE, E. **RFC 9745: The Deprecation HTTP Response Header Field**. IETF, 2025. Disponível em: https://www.rfc-editor.org/rfc/rfc9745. Acesso em: 9 nov. 2026.",
        "OPENAPI INITIATIVE. **OpenAPI Specification v3.1.1**. 2024. Disponível em: https://spec.openapis.org/oas/v3.1.1. Acesso em: 9 nov. 2026.",
        "SUTER, R. **NSwag**. GitHub, 2025. Disponível em: https://github.com/RicoSuter/NSwag. Acesso em: 9 nov. 2026.",
    ])

    r.nova_pagina()
    r.h1("Apêndice A – Código-fonte integral")
    for arq in ("src/PortalTarefas.Web/Endpoints/TarefasApiV2Endpoints.cs", "src/PortalTarefas.Web/Contratos/TarefasApiContratos.cs",
                "src/PortalTarefas.Web/Servicos/ConsultaTarefasServico.cs", "src/PortalTarefas.Frontend/nswag.json",
                "src/PortalTarefas.Frontend/scripts/chamada-real.ts"):
        r.arquivo_git(COMMIT, arq)
    r.codigo('''// Program.cs (trecho): documento OpenAPI v2 e UI somente em Development
builder.Services.AddOpenApi("v2", o => o.AddDocumentTransformer((doc, _, _) => { ... })
    .ShouldInclude = d => d.RelativePath?.StartsWith("api/v2") == true);
builder.Services.Configure<OpenApiOptions>("v2", o => o.CreateSchemaReferenceId = tipo => /* PaginaTarefaResumo */ ...);
app.MapTarefasApiV2();
app.MapOpenApi();                                      // /openapi/v2.json
if (app.Environment.IsDevelopment()) app.MapScalarApiReference(...);
// PortalTarefas.Web.csproj: <OpenApiDocumentsDirectory>../../docs/openapi</OpenApiDocumentsDirectory>''', "**Configuração do OpenAPI**", 7)

    r.h1("Apêndice B – Evidências de execução")
    r.codigo(bloco_entre(f"{EV}/01-requisicoes.txt", "### Listagem padrão", "### Limite máximo violado", 34), "**Sucesso com metadados, página vazia e limite mínimo**", 6.5)
    r.codigo(bloco_entre(f"{EV}/01-requisicoes.txt", "### GET por id inexistente", "### POST inválido", 30), "**404 e 201 com Location**", 6.5)
    r.codigo(bloco_entre(f"{EV}/01-requisicoes.txt", "### POST conflito", "### PUT em id inexistente", 40), "**409 por título duplicado e por versão desatualizada**", 6.5)
    r.codigo(trecho(f"{EV}/06-regeneracao-cliente.txt", max_linhas=60), "**Mudança compatível e regeneração do cliente**", 6.5)
    r.codigo(trecho(f"{EV}/05-sql-paginacao.txt", 2, 2, cortar=400), "**SQL gerado: paginação aplicada no banco**", 6.5)
    r.figuras_apendice(EV, [("03-scalar-ui.png", "B1", "Interface interativa Scalar (somente Development) lendo /openapi/v2.json")], 14)
    return r.salvar("Relatorio-04-API-REST-e-OpenAPI.docx")
