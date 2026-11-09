from base import Relatorio, AMBIENTE, REPO, acesso, bloco_entre, trecho

EV = "docs/evidencias/atividade1"
COMMIT = "aa99393"


def gerar():
    r = Relatorio(1, "Pipeline HTTP, middleware, roteamento, binding e escolha entre Minimal APIs e MVC",
                  "Unidade 1 – Pipeline HTTP e endpoints",
                  "Construir a base do Portal de Tarefas, observar o percurso de uma requisição HTTP e implementar "
                  "endpoints equivalentes com Minimal APIs e um controller MVC.")
    r.capa()
    r.cabecalho_atividade()

    r.h1("1. Identificação, ambiente e reprodução")
    r.p(f"Repositório: {REPO}. Código desta unidade no commit `{COMMIT}`. Projeto `PortalTarefas.Web` (alvo `net10.0`) "
        "na solução `PortalTarefas.slnx`, com `global.json` fixando o SDK 10.0.1xx. Para reproduzir: `dotnet run --project "
        "src/PortalTarefas.Web --launch-profile https` e executar o arquivo `PortalTarefas.Web.http` (ou `scripts/evidencias/atividade1.sh`).")
    r.tabela_dados(["Item", "Versão efetivamente utilizada"], AMBIENTE[:2] + [AMBIENTE[3]], larguras=[4, 12], titulo="Ambiente de desenvolvimento")

    r.h1("2. Resumo teórico aplicado ao projeto")
    r.p("**Requisição e resposta HTTP.** O cliente envia método, URI, cabeçalhos e, às vezes, corpo; o servidor devolve "
        "código de status, cabeçalhos e corpo. No ASP.NET Core o servidor Kestrel recebe a conexão, cria o `HttpContext` e o "
        "entrega ao pipeline; tudo o que a aplicação faz é ler `HttpContext.Request` e escrever em `HttpContext.Response`.")
    r.p("**Middleware e ordem.** Cada middleware recebe o contexto e decide se chama o próximo (`next`). O código antes de "
        "`await next()` roda na ida e o código depois roda na volta, como camadas de uma cebola; por isso a ordem de registro "
        "muda o comportamento. Exemplos no projeto: o middleware de correlação vem primeiro para que até respostas de erro tenham "
        "`X-Correlation-Id`; o `UseExceptionHandler` precisa estar *antes* dos endpoints para capturar suas exceções; o CORS fica "
        "entre `UseRouting` e os endpoints. **Encerrar antecipadamente** significa responder sem chamar `next`: o "
        "`UseStaticFiles` faz isso quando encontra o arquivo, e o endpoint `/ping` usa `.ShortCircuit()`, respondendo logo após o "
        "roteamento sem executar os middlewares seguintes.")
    r.p("**Roteamento, endpoints e binding.** `UseRouting` escolhe o endpoint pelo template (ex.: `/api/v1/tarefas/{id:int}`); "
        "o endpoint executa o handler. O binding converte partes da requisição em parâmetros: `id` vem da **rota** (com restrição "
        "`:int`), `situacao` e `busca` da **query string**, `X-Cliente-Id` de um **cabeçalho** (`[FromHeader]`) e o cadastro do "
        "**corpo JSON** (`CriarTarefaRequest`).")
    r.p("**Contrato previsível.** Validação por Data Annotations (`AddValidation()`, novidade do .NET 10 para Minimal APIs), "
        "códigos coerentes (200, 201 + `Location`, 400, 404, 500) e erros no formato *Problem Details* permitem que o cliente "
        "trate falhas de forma automática. O `TratadorExcecoesGlobal` (`IExceptionHandler`) converte exceções em 500 sem "
        "mensagem, tipo ou *stack trace*, e erros de binding (`BadHttpRequestException`) em 400.")
    r.p("**HTTPS, CORS e estáticos.** `UseHttpsRedirection` responde 307 para HTTPS; a política CORS nomeada `frontend-dev` "
        "libera apenas `http://localhost:5173`, métodos e cabeçalhos listados; `UseStaticFiles` serve `wwwroot`.")
    r.p("**Minimal APIs × MVC.** Minimal APIs têm menos cerimônia, startup menor e filtros de endpoint; controllers MVC "
        "organizam muitas ações por classe, têm filtros de ação/resultado/exceção maduros, `[ApiController]` com validação "
        "automática e convenções conhecidas por equipes grandes. Testabilidade é equivalente com `WebApplicationFactory`.")

    r.h1("3. Código-fonte e evidências")
    r.h2("3.1 Ordem do pipeline implementado")
    r.itens(["`UseCorrelationId` – reaproveita/gera `X-Correlation-Id` e abre escopo de log;",
             "`UseRegistroRequisicoes` – registra método, caminho, status e duração (sem query, cabeçalhos ou corpo);",
             "`UseExceptionHandler` + `UseStatusCodePages` – Problem Details para exceções e respostas vazias 4xx/5xx;",
             "`UseHsts` (fora de Development) e `UseHttpsRedirection`;",
             "`UseStaticFiles` → `UseRouting` → `UseCors(\"frontend-dev\")`;",
             "endpoints: grupo Minimal API `/api/v1/tarefas`, diagnóstico Minimal API e `MapControllers()` (MVC)."], numerada=True, tamanho=10)
    r.p("O log resumido confirma o percurso “cebola”: entrada no middleware 1, entrada no 2, saída do 2 com status e duração, "
        "saída do 1 — inclusive na requisição com erro, em que o tratador registra a falha *entre* a entrada e a saída do middleware de log:", tamanho=10.5)
    r.codigo(trecho(f"{EV}/log-resumido.txt", contem=["teste-erro-0"]), tamanho=7)

    r.h2("3.2 Comparação Minimal API × MVC e decisão")
    r.tabela_dados(["Critério", "Minimal API (`/api/minimal/diagnostico`)", "Controller MVC (`/api/mvc/diagnostico`)"], [
        ["Organização", "Grupos de rotas por recurso em métodos de extensão", "Uma classe com várias ações e atributos"],
        ["Dependências", "Injetadas por parâmetro no handler", "Injetadas no construtor do controller"],
        ["Validação", "`AddValidation()` (.NET 10) ou filtro de endpoint", "`[ApiController]` + ModelState automático"],
        ["Filtros", "`IEndpointFilter` por endpoint/grupo", "Autorização, recurso, ação, exceção e resultado"],
        ["Testes", "`WebApplicationFactory`; handler pode ser método estático", "`WebApplicationFactory` ou instância do controller"],
        ["Contrato", "Resposta idêntica (mesmo `DiagnosticoDto`) — verificado por teste", "Idem"],
    ], larguras=[3, 6.5, 6.5], titulo="Comparação das duas implementações equivalentes")
    r.p("**Decisão:** a API do Portal (unidades 3 a 8) será feita em **Minimal APIs**, organizada em grupos (`MapGroup`) e "
        "métodos estáticos testáveis, porque o domínio é pequeno, os endpoints são finos e os recursos de .NET 10 "
        "(validação, `TypedResults`, OpenAPI nativo) cobrem as necessidades sem a cerimônia dos controllers. O **MVC** fica "
        "reservado à interface Razor (unidade 2), onde views, filtros de ação e Tag Helpers trazem ganho real. Se a equipe "
        "crescer e o número de endpoints exigir convenções fortes, migrar para controllers continua possível porque a lógica está nos serviços.")

    r.h2("3.3 Requisições executadas (curl / arquivo .http)")
    r.tabela_dados(["Requisição", "Status", "Observação"], [
        ["`GET /api/v1/tarefas/1`", "200", "binding da rota; `X-Correlation-Id` na resposta"],
        ["`GET /api/v1/tarefas/999`", "404", "Problem Details “Tarefa não encontrada.”"],
        ["`GET /api/v1/tarefas/abc`", "404", "restrição `:int` – nem chega ao handler"],
        ["`GET ...?situacao=Pendente&busca=relat` + `X-Cliente-Id`", "200", "filtros da query + cabeçalho ecoado em `X-Cliente-Atendido`"],
        ["`GET ...?situacao=Arquivada`", "400", "enum inválido na query (antes da correção era 500)"],
        ["`POST` corpo válido", "201", "`Location: /api/v1/tarefas/4`"],
        ["`POST` título vazio / JSON malformado", "400 / 400", "`errors.Titulo` / “Requisição inválida.”"],
        ["`GET /api/minimal/diagnostico/erro`", "500", "sem mensagem interna; com `correlationId`"],
        ["`OPTIONS` origem `localhost:5173` / `evil.example`", "204 / 204", "com / **sem** `Access-Control-Allow-Origin`"],
        ["`GET http://localhost:5080/...`", "307", "redirecionamento para HTTPS"],
    ], larguras=[6.6, 1.8, 7.6], titulo="Resumo das requisições (saída completa no Apêndice B)")

    r.h1("4. Testes e validações obrigatórias")
    r.tabela_dados(["Caso exigido", "Resultado obtido", "Evidência"], [
        ["Parâmetro de rota válido e inválido", "200 para `1`; 404 Problem Details para `999` e `abc`", "Tab. 3; testes `Rota_*`"],
        ["Filtro por query + cabeçalho opcional", "Lista só `Pendente`; cabeçalho ecoado quando enviado e ausente quando omitido", "`Filtro_query_e_cabecalho_opcional`"],
        ["POST válido e inválido", "201 com `Location`; 400 com erros por campo", "`Post_valido_*`, `Post_invalido_*`"],
        ["Exceção controlada sem dados sensíveis", "500 genérico; o texto “db-interno” só aparece no log do servidor", "`Excecao_vira_500_sem_detalhes_sensiveis`"],
        ["CORS só para a origem autorizada", "Preflight com cabeçalhos CORS só para `localhost:5173`", "`Cors_somente_para_origem_autorizada`"],
    ], larguras=[4.6, 7, 4.4], titulo="Testes obrigatórios (10 testes de integração, todos aprovados)")

    r.h1("5. Aprimoramentos e análise")
    r.itens([
        "**Middleware reutilizável por classe** (`CorrelationIdMiddleware`, `RequestLoggingMiddleware`) registrado por extensões de `IApplicationBuilder` (`UseCorrelationId`, `UseRegistroRequisicoes`).",
        "**Identificador de correlação** devolvido em `X-Correlation-Id`, presente no escopo de log e no Problem Details (`correlationId`). Valores recebidos só são aceitos se curtos e alfanuméricos (evita injeção em logs).",
        "**Testes de integração** (`Atividade1PipelineTests`, 10 casos) com `WebApplicationFactory`, incluindo a equivalência de contrato entre Minimal API e MVC.",
        "**Falhas encontradas e corrigidas:** (1) enum inválido e JSON malformado geravam 500; o tratador passou a mapear `BadHttpRequestException` para 400; (2) `TypedResults.NotFound(ProblemDetails)` não passava pelo serviço de Problem Details (sem `correlationId`) e foi trocado por `TypedResults.Problem`.",
    ], tamanho=10.5)

    r.referencias([
        acesso("ASP.NET Core Middleware", "https://learn.microsoft.com/aspnet/core/fundamentals/middleware/"),
        acesso("Routing in ASP.NET Core", "https://learn.microsoft.com/aspnet/core/fundamentals/routing"),
        acesso("Parameter binding in Minimal API apps", "https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/parameter-binding"),
        acesso("Handle errors in ASP.NET Core APIs", "https://learn.microsoft.com/aspnet/core/fundamentals/error-handling-api"),
        acesso("Enable Cross-Origin Requests (CORS) in ASP.NET Core", "https://learn.microsoft.com/aspnet/core/security/cors"),
        acesso("Choose between controller-based APIs and minimal APIs", "https://learn.microsoft.com/aspnet/core/fundamentals/apis"),
        "FIELDING, R.; NOTTINGHAM, M.; RESCHKE, J. **RFC 9110: HTTP Semantics**. IETF, 2022. Disponível em: https://www.rfc-editor.org/rfc/rfc9110. Acesso em: 9 nov. 2026.",
        "NOTTINGHAM, M.; WILDE, E.; DALAL, S. **RFC 9457: Problem Details for HTTP APIs**. IETF, 2023. Disponível em: https://www.rfc-editor.org/rfc/rfc9457. Acesso em: 9 nov. 2026.",
    ])

    r.nova_pagina()
    r.h1("Apêndice A – Código-fonte integral")
    # Código na versão final da unidade 1 (inclui as correções descritas na seção 5)
    for arq in ("src/PortalTarefas.Web/Program.cs", "src/PortalTarefas.Web/Infraestrutura/CorrelationIdMiddleware.cs",
                "src/PortalTarefas.Web/Infraestrutura/RequestLoggingMiddleware.cs", "src/PortalTarefas.Web/Infraestrutura/TratadorExcecoesGlobal.cs",
                "src/PortalTarefas.Web/Infraestrutura/PipelineExtensions.cs", "src/PortalTarefas.Web/Endpoints/TarefasEndpoints.cs",
                "src/PortalTarefas.Web/Endpoints/DiagnosticoEndpoints.cs", "src/PortalTarefas.Web/Controllers/DiagnosticoController.cs"):
        r.arquivo_git(COMMIT, arq)

    r.h1("Apêndice B – Evidências de execução")
    r.p("Saídas completas em `docs/evidencias/atividade1/` (`requisicoes.txt`, `log-servidor.txt`, `log-resumido.txt`). Trechos:", tamanho=10)
    r.codigo(bloco_entre(f"{EV}/requisicoes.txt", "### Parâmetro de rota inexistente", "### Filtro sem cabeçalho"), "**Rota inexistente, rota inválida e filtro com cabeçalho**", 7)
    r.codigo(bloco_entre(f"{EV}/requisicoes.txt", "### POST com corpo válido", "### Diagnóstico Minimal API"), "**POST válido, inválido, JSON malformado e exceção controlada**", 7)
    r.codigo(bloco_entre(f"{EV}/requisicoes.txt", "### Diagnóstico Minimal API", "### Arquivo estático"), "**Minimal API × MVC, CORS e HTTPS**", 7)
    r.codigo(trecho(f"{EV}/log-resumido.txt", 7, 40), "**Log resumido (ordem de entrada e saída dos middlewares)**", 6.5)
    return r.salvar("Relatorio-01-Pipeline-HTTP-e-endpoints.docx")
