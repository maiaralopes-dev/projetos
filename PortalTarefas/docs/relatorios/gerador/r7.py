from base import Relatorio, REPO, acesso, trecho, bloco_entre

EV = "docs/evidencias/atividade7"
COMMIT = "6a1dece"


def gerar():
    r = Relatorio(7, "Logging, métricas, tracing, Health Checks, resiliência, rate limiting e output caching",
                  "Unidade 7 – Observabilidade e resiliência",
                  "Instrumentar a aplicação e aplicar controles de resiliência e capacidade, produzindo evidências úteis para diagnóstico.")
    r.capa()
    r.cabecalho_atividade()

    r.h1("1. Identificação e ambiente")
    r.p(f"Repositório: {REPO}; commit `{COMMIT}`. OpenTelemetry .NET 1.20 (Extensions.Hosting, instrumentações AspNetCore, Http e "
        "SqlClient, exportadores Console e OTLP), `Microsoft.Extensions.Http.Resilience` 10.11 (Polly v8), Health Checks com "
        "`AddDbContextCheck`. Painel local: **Aspire Dashboard 9.5** em contêiner recebendo OTLP em `localhost:4317`. Dependência "
        "externa simulada: `scripts/dependencia-calendario.js` (Node), com modos `ok`, `falha503`, `falha400`, `lento` e `fora`.")

    r.h1("2. Resumo teórico aplicado ao projeto")
    r.p("**Logs, métricas e traces.** *Logs* são eventos discretos com contexto (o que aconteceu); *métricas* são números "
        "agregados ao longo do tempo (quanto/quão rápido); *traces* mostram o caminho de uma requisição em *spans* encadeados "
        "(onde o tempo foi gasto). O **identificador de correlação** liga os três: o `X-Correlation-Id` está no escopo de todos "
        "os logs da requisição, no Problem Details devolvido ao usuário e é propagado à dependência; o `TraceId` do OpenTelemetry "
        "também aparece nos logs JSON.")
    r.p("**Logging estruturado.** Mensagens com *templates* (`{TarefaId}`) geram campos pesquisáveis; em produção o console "
        "usa o formatador JSON com escopos. Níveis: `Debug` para entrada/saída de middleware, `Information` para operações, "
        "`Warning` para falhas tratadas (dependência, 429), `Error` para exceções. É proibido registrar senha, cookie, token, "
        "connection string ou corpo de requisição; dados pessoais só quando necessários.")
    r.p("**Tipos de métrica e cardinalidade.** *Counter* só cresce (tarefas criadas); *gauge* é um valor instantâneo "
        "(requisições ativas); *histograma* distribui medidas em faixas (duração → p50/p90/p99). Cada combinação de dimensões "
        "vira uma série; por isso as dimensões são de **cardinalidade controlada**: `prioridade` (3 valores) e `resultado` (4) — "
        "nunca id de tarefa, usuário ou URL completa.")
    r.p("**Tracing distribuído.** Cada operação é um *span* com pai; o contexto viaja no cabeçalho W3C `traceparent` "
        "(visível no log da dependência). A instrumentação cria spans para a requisição HTTP, cada comando SQL e cada chamada "
        "`HttpClient`; o span próprio `tarefa.concluir` agrupa os comandos da transação.")
    r.p("**Liveness × readiness.** *Liveness* responde “o processo está vivo?” — não consulta dependências, senão um banco "
        "fora faria o orquestrador reiniciar contêineres sem resolver nada. *Readiness* responde “posso receber tráfego?” e "
        "verifica banco e calendário. A resposta pública traz só `Healthy/Unhealthy`; nomes, descrições e durações ficam em "
        "`/health/detalhes`, restrito ao Administrador.")
    r.p("**Retry, timeout e circuit breaker.** *Timeout* limita a espera; *retry* repete falhas **transitórias** (5xx, 408, "
        "429, erro de rede) com backoff exponencial e jitter; *circuit breaker* para de chamar uma dependência que está falhando, "
        "devolvendo erro imediato até um teste de recuperação (meio-aberto). Repetir operações **não idempotentes** (POST de "
        "pagamento, por exemplo) pode duplicar efeitos — por isso o retry foi desativado para métodos inseguros (`DisableForUnsafeHttpMethods`).")
    r.p("**Rate limiting × output caching.** Rate limiting **protege** a capacidade limitando requisições por usuário/IP (429 "
        "+ `Retry-After`); output caching **melhora desempenho** guardando a resposta inteira de uma leitura pública e repetindo-a "
        "sem executar o endpoint. São independentes: uma resposta em cache ainda conta para o limite.")

    r.h1("3. Código-fonte e evidências")
    r.tabela_dados(["Sinal / controle", "Implementação"], [
        ["Logs", "JSON no console (prod.) com escopo `CorrelationId`; OTLP para o painel; log de início/resultado/duração por requisição e operação"],
        ["Métrica de negócio", "`portal.tarefas.criadas{prioridade}`, `portal.tarefas.concluidas` (Meter `PortalTarefas`)"],
        ["Métrica técnica", "Histograma `portal.dependencia.calendario.duracao{resultado}` (ms) + métricas nativas `http.server.request.duration` e de rate limiting"],
        ["Traces", "AspNetCore + HttpClient + SqlClient + span `tarefa.concluir` (tag `tarefa.id`)"],
        ["Health", "`/health/live` (sem verificações), `/health/ready` (banco + calendário), `/health/detalhes` (Admin)"],
        ["Resiliência", "Timeout total 8 s › retry 3× exponencial (só GET) › circuit breaker (50 %, mín. 4, 30 s, pausa 15 s) › timeout por tentativa 2 s"],
        ["Rate limiting", "Janela fixa: `api` 100/10 s por usuário; `publica` 30/10 s por IP; `login` 10/min por IP"],
        ["Output cache", "Política `feriados`: 60 s, varia por `ano`, tag para invalidação (`POST /api/v2/calendario/cache/invalidar`, Admin)"],
    ], larguras=[3.4, 12.6], titulo="Instrumentação e controles configurados")
    r.figura_img(f"{EV}/10-painel-trace-concluir.png", "Trace no painel local: requisição → span customizado tarefa.concluir → SELECT, UPDATE e INSERT da transação", 13.5)
    r.p("**Erro localizado pela correlação:** a requisição com `X-Correlation-Id: demo-erro-7` devolveu 500 genérico; filtrando os "
        "logs por esse valor aparecem a entrada, as consultas, o erro do `TratadorExcecoesGlobal` e a saída, todos com o mesmo "
        "`TraceId`; a busca por senha, cookie ou token no log retornou **0** ocorrências (`01-correlacao-erro.txt`).", tamanho=10.5)

    r.h1("4. Testes e validações obrigatórias")
    r.tabela_dados(["Caso exigido", "Resultado observado", "Evidência"], [
        ["Erro identificável por correlação sem segredo", "500 com `correlationId: demo-erro-7`; todas as linhas de log da requisição com o mesmo id e `TraceId`; 0 segredos", "`01-correlacao-erro.txt`; Fig. B2"],
        ["Dependência fora: readiness × liveness", "Calendário `fora`: `/health/ready` 503 e `/health/live` 200; detalhes (Admin) apontam `calendario` Unhealthy", "`03-health.txt`; 2 testes"],
        ["Retry só em falha transitória e abertura do circuito", "400 → 1 requisição (sem retry), 502. 503 → 3 tentativas; a 3ª retentativa já encontrou o circuito aberto → 503; chamadas seguintes falham rápido sem tocar a dependência; após 16 s, sucesso", "`04-resiliencia.txt`; Fig. B4"],
        ["429 após exceder o limite", "Política pública reduzida a 10/10 s: 10 × 200 e 4 × 429 com `Retry-After: 10`; teste por usuário (5 × 200, 2 × 429, outro usuário 200)", "`06-rate-limiting.txt`; teste"],
        ["Cache em endpoint público não personalizado", "5 GET anônimos → 1 chamada à dependência; 2 GET autenticados → 2 chamadas (não usam cache); após invalidar → nova chamada", "`05-cache.txt`"],
    ], larguras=[3.8, 8.4, 3.8], titulo="Testes obrigatórios")
    r.p("**Carga controlada local** (autocannon, 10 conexões, 10 s, `/health/live`): ~11,9 mil req/s em média (119 mil requisições), latência "
        "p97,5 de 3 ms. O teste rodou em contêiner de desenvolvimento com 4 vCPU, build Debug e banco no mesmo host; o número "
        "**não** indica capacidade de produção — serve apenas para observar o comportamento e as métricas sob carga.", tamanho=10.5)

    r.h1("5. Aprimoramentos")
    r.itens([
        "**Painel local** com métricas, traces e logs (Aspire Dashboard via OTLP) — Figuras 1 e B1–B4.",
        "**Teste automatizado da política de rate limiting** (`Excesso_de_requisicoes_retorna_429_com_retry_after_por_usuario`) com limites reduzidos por configuração em uma fábrica de teste própria, além de testes de health checks.",
    ], tamanho=10.5)
    r.tabela_dados(["Alerta conceitual", "Limiar", "Ação esperada"], [
        ["Erros do servidor", "5xx > 2 % de `http.server.request.duration` por 5 min", "Abrir incidente; filtrar traces/logs pelo `correlationId`; avaliar rollback da última versão"],
        ["Dependência degradada", "`portal.dependencia.calendario.duracao{resultado=circuito_aberto}` > 0 por 5 min", "Avisar equipe; checar status do serviço; comunicar indisponibilidade parcial"],
        ["Instância não pronta", "`/health/ready` ≠ 200 por 2 min", "Retirar do balanceador; verificar banco e calendário"],
        ["Abuso ou cota mal dimensionada", "Taxa de 429 > 5 % por 10 min", "Investigar origem; ajustar limites com base em dados"],
    ], larguras=[3.4, 6.2, 6.4], titulo="Alertas propostos (conceituais)")

    r.referencias([
        acesso("Logging in .NET and ASP.NET Core", "https://learn.microsoft.com/aspnet/core/fundamentals/logging/"),
        acesso(".NET observability with OpenTelemetry", "https://learn.microsoft.com/dotnet/core/diagnostics/observability-with-otel"),
        acesso("Health checks in ASP.NET Core", "https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks"),
        acesso("Build resilient HTTP apps: Key development patterns", "https://learn.microsoft.com/dotnet/core/resilience/http-resilience"),
        acesso("Rate limiting middleware in ASP.NET Core", "https://learn.microsoft.com/aspnet/core/performance/rate-limit"),
        acesso("Output caching middleware in ASP.NET Core", "https://learn.microsoft.com/aspnet/core/performance/caching/output"),
        "OPENTELEMETRY. **Observability primer** e **Semantic conventions for HTTP metrics**. opentelemetry.io, 2025. Disponível em: https://opentelemetry.io/docs/concepts/observability-primer/. Acesso em: 9 nov. 2026.",
        "W3C. **Trace Context**. W3C Recommendation, 2021. Disponível em: https://www.w3.org/TR/trace-context/. Acesso em: 9 nov. 2026.",
    ])

    r.nova_pagina()
    r.h1("Apêndice A – Código-fonte integral")
    for arq in ("src/PortalTarefas.Web/Observabilidade/Telemetria.cs", "src/PortalTarefas.Web/Observabilidade/ObservabilidadeExtensions.cs",
                "src/PortalTarefas.Web/Servicos/CalendarioCliente.cs", "src/PortalTarefas.Web/Endpoints/OperacaoEndpoints.cs",
                "src/PortalTarefas.Web/appsettings.json", "scripts/dependencia-calendario.js"):
        r.arquivo_git(COMMIT, arq, tamanho=6.8)
    import subprocess, os
    from base import RAIZ
    prog = subprocess.run(["git", "show", f"{COMMIT}:PortalTarefas/src/PortalTarefas.Web/Program.cs"], cwd=RAIZ, capture_output=True, text=True).stdout
    i = prog.index("// ---------- Dependência externa com resiliência"); j = prog.index("// OpenAPI 3.1 nativo")
    r.codigo(prog[i:j], "**Program.cs (trecho): HttpClient resiliente, health checks, rate limiting e output cache**", 6.8)

    r.h1("Apêndice B – Evidências")
    r.codigo(trecho(f"{EV}/04-resiliencia.txt", contem=["###", "HTTP/", "Requisições", "aguardando", '"title"']), "**Resiliência (resumo)**", 7)
    r.codigo(trecho(f"{EV}/log-dependencia.txt", contem=["#"], cortar=200), "**Log da dependência: tentativas recebidas, correlation id e traceparent propagados**", 6.5)
    r.codigo(trecho(f"{EV}/05-cache.txt", contem=["###", "HTTP/", "Requisições"]) + "\n\n" + trecho(f"{EV}/06-rate-limiting.txt", max_linhas=14), "**Cache e rate limiting**", 7)
    r.codigo(trecho(f"{EV}/03-health.txt", contem=["###", "HTTP/", "Healthy", "Unhealthy", '"status"', '"nome"']), "**Health checks**", 7)
    r.codigo(trecho(f"{EV}/08-traces-e-metricas-console.txt", max_linhas=40), "**Exportador de console: span customizado e métricas próprias**", 6.5)
    r.codigo(trecho(f"{EV}/07-carga.txt"), "**Carga controlada**", 6.5)
    r.figuras_apendice(EV, [
        ("09-painel-traces.png", "B1", "Lista de traces no painel local"),
        ("11-painel-logs-correlacao.png", "B2", "Logs estruturados filtrados pelo identificador de correlação"),
        ("13-painel-metrica-tarefas-criadas.png", "B3", "Métrica de negócio portal.tarefas.criadas"),
        ("12-painel-metrica-calendario.png", "B4", "Histograma de duração da dependência por resultado (sucesso, falha, circuito_aberto)"),
    ], 14)
    return r.salvar("Relatorio-07-Observabilidade-e-Resiliencia.docx")
