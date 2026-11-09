import os
from base import Relatorio, REPO, RAIZ, acesso, trecho, bloco_entre

EV = "docs/evidencias/atividade8"
COMMIT = "6c526ad"


def gerar():
    r = Relatorio(8, "Configuração por ambientes, segredos, contêineres e pipeline reprodutível",
                  "Unidade 8 – Build, implantação e CI/CD full-stack",
                  "Empacotar API e front-end, separar configuração de código e automatizar build, testes e publicação em um pipeline verificável.")
    r.capa()
    r.cabecalho_atividade()

    r.h1("1. Identificação e ambiente")
    r.p(f"Repositório: {REPO}; commits `3f0201f`, `87ccb46`, `ac17bf4` e `{COMMIT}`. Docker 29.9.2 com BuildKit, Docker Compose 5.6.1. "
        "Imagens base com **tag exata + digest**: `node:22.23.4-alpine`, `mcr.microsoft.com/dotnet/sdk:10.0.403`, "
        "`mcr.microsoft.com/dotnet/aspnet:10.0.14`, `caddy:2.10.2-alpine` (proxy TLS) e Syft 1.33 (SBOM). Workflow "
        "`.github/workflows/portal-tarefas.yml` (GitHub Actions) e seu espelho executável `scripts/ci-local.sh`.")

    r.h1("2. Resumo teórico aplicado ao projeto")
    r.p("**Build, publish, imagem, contêiner, implantação e execução.** *Build* compila e verifica o código; *publish* gera a "
        "pasta pronta para rodar (DLLs, dependências, `wwwroot`); a *imagem* é um pacote imutável e versionado com sistema de "
        "arquivos e comando de início; o *contêiner* é uma instância em execução da imagem; *implantar* é colocar uma versão "
        "específica da imagem em um ambiente com sua configuração; *executar* é o processo atendendo requisições.")
    r.p("**Configuração por ambiente e precedência.** No ASP.NET Core as fontes são lidas em ordem e a última vence: "
        "`appsettings.json` → `appsettings.{Ambiente}.json` → User Secrets (só em Development) → variáveis de ambiente → "
        "argumentos de linha de comando. Assim a **mesma imagem** roda em Staging e Production mudando só "
        "`ASPNETCORE_ENVIRONMENT` e variáveis (ex.: `ConnectionStrings__PortalTarefas`). Configurações obrigatórias são validadas "
        "na inicialização (`ValidateDataAnnotations().ValidateOnStart()`), e a mensagem cita a **chave** ausente, nunca o valor.")
    r.p("**Segredos fora da imagem, do repositório e dos logs.** Imagens e repositórios são copiados, armazenados em cache e "
        "lidos por muitas pessoas; um segredo gravado em uma camada permanece no histórico mesmo se “apagado” depois, e um "
        "segredo em log se espalha para ferramentas de observabilidade. Aqui: `.env` e User Secrets locais (no `.gitignore` e "
        "no `.dockerignore`), *secrets* do GitHub/ambientes no CI e, durante o build, a CA do proxy deste ambiente entra só "
        "como *BuildKit secret* — montada no `RUN` e ausente da imagem final.")
    r.p("**Build multi-stage.** O estágio `node` gera o bundle do React; o estágio `sdk` restaura e publica a API; o estágio "
        "final usa apenas o runtime `aspnet` e copia os artefatos. Ferramentas de build (Node, npm, SDK, código-fonte, "
        "testes) não chegam à imagem final, reduzindo tamanho e superfície de ataque; a camada de `restore` fica em cache enquanto o `.csproj` não muda.")
    r.p("**CI e CD.** *Integração contínua* compila e testa cada alteração (PR e `main`) e só gera artefatos se tudo passar "
        "(*gate*). *Entrega contínua* promove o **mesmo** artefato por ambientes (staging → produção com aprovação). "
        "Rastreabilidade: a imagem recebe tag com o commit e rótulos OCI (`revision`, `version`), e `GET /api/versao` informa "
        "o commit em execução. *Rollback* = reimplantar a tag anterior, sem rebuild.")

    r.h1("3. Código-fonte e evidências")
    r.tabela_dados(["Critério", "Integrada: API serve a SPA em `/app` (escolhida)", "Separada: SPA em CDN/serviço próprio"], [
        ["Origem e autenticação", "Mesma origem: cookie `SameSite=Strict`, sem CORS em produção", "Origem distinta: CORS com credenciais ou tokens no cliente"],
        ["Implantação e versão", "Uma imagem = API + SPA sempre compatíveis", "Dois artefatos; risco de versões descasadas do contrato"],
        ["Escala e cache", "Estáticos servidos pelo Kestrel (suficiente aqui)", "CDN escala estáticos à parte; melhor para muito tráfego"],
        ["Pipeline", "Um build multi-stage", "Pipelines independentes, deploys mais frequentes do front"],
    ], larguras=[3.4, 6.3, 6.3], titulo="Implantação integrada × separada")
    r.p("**Decisão:** integrada, porque a segurança da unidade 6 (cookie `HttpOnly` sem tokens no JavaScript) depende da mesma "
        "origem e o volume de acesso não justifica CDN. Em produção, o TLS termina num proxy/ingress; no compose isso é "
        "simulado pelo **Caddy** (`https://localhost:8443` → `api:8080`) e a API confia no `X-Forwarded-Proto` "
        "(`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`).", tamanho=10.5)
    r.p("**Dockerfile:** 3 estágios; usuário não root (`USER $APP_UID`, UID 1654); arquivos da aplicação pertencentes ao root "
        "(somente leitura para o processo); única pasta gravável `/chaves` (chaves do Data Protection em volume); "
        "`HEALTHCHECK` que executa `dotnet PortalTarefas.Web.dll --sondar-saude` (consulta `/health/live` sem instalar curl). "
        "No compose: `read_only: true`, `tmpfs /tmp`, `cap_drop: [ALL]` e `no-new-privileges`. Imagem final: **129 MB comprimidos "
        "(462 MB em disco)**, sem Node, npm, curl, wget nem SDK; a busca pelas senhas locais em todo o sistema de arquivos da "
        "imagem retornou 0 ocorrências.", tamanho=10.5)
    r.p("**Problemas reais encontrados ao executar o contêiner** e corrigidos: (1) o volume de chaves pertencia ao root e o "
        "app (UID 1654) não conseguia gravar → pasta criada na imagem com dono `app`; (2) cookies `Secure` e antiforgery recusavam "
        "HTTP puro → TLS terminado no proxy reverso; (3) o build limpo acusou o aviso ASP0026 (`[AllowAnonymous]` da classe "
        "anulava o `[Authorize]` do logout) → atributos movidos para as ações públicas.", tamanho=10.5)
    r.figura_img(f"{EV}/09-spa-no-conteiner.png", "SPA servida pelo contêiner da API atrás do proxy TLS, após login", 9.5)

    r.h1("4. Testes e validações obrigatórias")
    r.tabela_dados(["Caso exigido", "Resultado observado", "Evidência"], [
        ["Build limpo a partir de clone novo", "`dotnet build -c Release` 0 avisos; 70 testes .NET e 22 do front aprovados; `npm ci` + build ok; árvore Git limpa após o build", "`02-build-clone-limpo.txt`"],
        ["Contêiner com configuração externa, sem segredo na imagem", "Conexão, senha de seed e URL vindas de variáveis/`.env`; `ENV` e histórico da imagem sem segredos; 0 ocorrências das senhas na imagem", "`03-inspecao-imagem.txt`, `04-compose-execucao.txt`"],
        ["Health Check do serviço", "Docker: `healthy`; `/health/live` e `/health/ready` 200 via proxy TLS; mesma imagem em Production também `Healthy`", "`04…`, `07-config-ambientes-e-validacao.txt`"],
        ["Falha de teste bloqueia a publicação", "`CI_FALHA_DELIBERADA=1`: teste `Gate_de_publicacao` falha na etapa 3/8, código de saída 1, nenhum artefato publicado", "`06-ci-local-falha-bloqueia.txt`"],
        ["Pipeline para alteração válida + rastreabilidade", "8/8 etapas; imagem `portal-tarefas:ac17bf4`, smoke test 200, SBOM e `manifesto.json` com commit e id da imagem", "`05-ci-local-sucesso.txt`"],
    ], larguras=[3.8, 8.4, 3.8], titulo="Testes obrigatórios")
    r.p("**Observação de transparência:** o workflow do GitHub Actions está versionado, mas só dispara em *pull request* ou "
        "*push* na `main` (e exige o secret `CI_SQL_SA_PASSWORD`); por isso as execuções registradas foram feitas com "
        "`scripts/ci-local.sh`, que executa as mesmas etapas na mesma ordem. Ao abrir o PR, o workflow roda no GitHub.", tamanho=10)

    r.h1("5. Aprimoramentos")
    r.itens([
        "**SBOM** (SPDX, Syft): 160 pacotes inventariados (97 do Debian, 56 NuGet...), publicado como artefato.",
        "**Análise de vulnerabilidades com política de bloqueio:** `dotnet list package --vulnerable --include-transitive` (falha em High/Critical), `npm audit --audit-level=high` e Trivy na imagem (falha em Critical com correção disponível). Resultado local: nenhuma vulnerabilidade; antes, a análise apontou `System.Security.Cryptography.Pkcs 9.0.11` (High), corrigido fixando a 10.0.14.",
        "**Staging com aprovação antes de produção:** jobs `staging` e `producao` com *environments* do GitHub (aprovação manual em produção) que promovem a **mesma** tag de imagem; rollback documentado.",
        "**Publicação sem credencial longa:** push ao GHCR com o `GITHUB_TOKEN` efêmero do job, com permissões mínimas (`contents: read`, `packages: write` só onde necessário).",
    ], tamanho=10.5)

    r.referencias([
        acesso("Configuration in ASP.NET Core", "https://learn.microsoft.com/aspnet/core/fundamentals/configuration/"),
        acesso("Safe storage of app secrets in development in ASP.NET Core", "https://learn.microsoft.com/aspnet/core/security/app-secrets"),
        acesso("Options pattern in ASP.NET Core", "https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options"),
        acesso("Containerize a .NET app", "https://learn.microsoft.com/dotnet/core/docker/build-container"),
        acesso("Configure ASP.NET Core to work with proxy servers and load balancers", "https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer"),
        "DOCKER. **Multi-stage builds** e **Build secrets**. Docker Docs, 2025. Disponível em: https://docs.docker.com/build/building/multi-stage/. Acesso em: 9 nov. 2026.",
        "GITHUB. **Using environments for deployment** e **Publishing Docker images**. GitHub Docs, 2025. Disponível em: https://docs.github.com/actions. Acesso em: 9 nov. 2026.",
        "OPEN CONTAINER INITIATIVE. **Image Format Specification: Annotations**. 2024. Disponível em: https://github.com/opencontainers/image-spec/blob/main/annotations.md. Acesso em: 9 nov. 2026.",
        "SPDX. **SPDX Specification 2.3**. Linux Foundation, 2022. Disponível em: https://spdx.github.io/spdx-spec/v2.3/. Acesso em: 9 nov. 2026.",
    ])

    r.nova_pagina()
    r.h1("Apêndice A – Arquivos de build, configuração e pipeline")
    for arq in ("Dockerfile", ".dockerignore", "compose.yaml", ".env.example", "scripts/ci-local.sh", "scripts/docker-build.sh",
                "src/PortalTarefas.Web/appsettings.Production.json", "src/PortalTarefas.Web/Configuracao/Opcoes.cs",
                "src/PortalTarefas.Web/Infraestrutura/SondaSaude.cs"):
        r.arquivo_git(COMMIT, arq, tamanho=6.8)
    import subprocess
    wf = subprocess.run(["git", "show", f"{COMMIT}:.github/workflows/portal-tarefas.yml"], cwd=RAIZ, capture_output=True, text=True).stdout
    r.codigo(wf, f"**Arquivo:** `.github/workflows/portal-tarefas.yml` (commit `{COMMIT}`)", 6.5)

    r.h1("Apêndice B – Evidências")
    r.codigo(trecho(f"{EV}/05-ci-local-sucesso.txt"), "**Pipeline local: alteração válida (8/8 etapas, publicação e manifesto)**", 6.8)
    r.codigo(trecho(f"{EV}/06-ci-local-falha-bloqueia.txt"), "**Pipeline local: falha deliberada bloqueia a publicação**", 6.8)
    r.codigo(trecho(f"{EV}/03-inspecao-imagem.txt"), "**Inspeção da imagem**", 6.5)
    r.codigo(trecho(f"{EV}/04-compose-execucao.txt", max_linhas=75), "**Compose: contêineres, health check e requisições via proxy TLS**", 6.3)
    r.codigo(trecho(f"{EV}/07-config-ambientes-e-validacao.txt"), "**Mesma imagem em Production e validação de configuração**", 6.5)
    r.codigo(trecho(f"{EV}/08-sbom-e-vulnerabilidades.txt"), "**SBOM e análise de vulnerabilidades**", 6.8)
    r.codigo(trecho(f"{EV}/02-build-clone-limpo.txt"), "**Build a partir de clone novo**", 6.8)
    return r.salvar("Relatorio-08-Build-Conteineres-e-CI-CD.docx")
