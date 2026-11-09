from base import Relatorio, REPO, acesso, trecho

EV = "docs/evidencias/atividade5"
COMMIT = "28b4e8b"


def gerar():
    r = Relatorio(5, "Front-end React consumindo a API com estados de requisição e roteamento",
                  "Unidade 5 – Integração React/Node",
                  "Construir uma SPA React que consuma o contrato da API, trate estados de requisição e ofereça páginas de lista e detalhes.")
    r.capa()
    r.cabecalho_atividade()

    r.h1("1. Identificação e ambiente")
    r.p(f"Repositório: {REPO}; commit `{COMMIT}`; projeto `src/PortalTarefas.Frontend`. **Node.js 22.23.4 (LTS) e npm 10.9.9**; "
        "Vite 8.3, React 19.3, React Router 8.5, TypeScript 7.0; testes com Vitest 5.1, Testing Library e jsdom. "
        "Execução: API em `https://localhost:7080` e `npm run dev` (Vite em `http://localhost:5173`). Variáveis em "
        "`.env.development` (versionado, sem segredo): `VITE_API_URL=` (vazio = mesma origem) e `API_PROXY_TARGET=https://localhost:7080`.")

    r.h1("2. Resumo teórico aplicado ao projeto")
    r.p("**Node.js e ferramenta de build.** O Node não roda no navegador: executa o Vite, que serve o código em desenvolvimento "
        "com recarga instantânea, transforma TypeScript/JSX e, no build, gera arquivos estáticos otimizados (`dist/`). O Node "
        "também executa o NSwag (cliente gerado) e os testes.")
    r.p("**Componentes, props, estado e efeitos.** Componentes são funções que recebem *props* e devolvem a interface; o "
        "*estado* (`useState`) guarda o que muda. Buscar dados é um efeito colateral, então fica em `useEffect`, cujas "
        "**dependências** dizem quando refazer a busca. O hook `useRequisicao` cria um `AbortController` a cada execução e o "
        "aborta na limpeza do efeito: se o filtro muda ou a página é desmontada, a resposta antiga é **cancelada/ignorada** e não sobrescreve a nova.")
    r.p("**CORS × proxy de desenvolvimento.** CORS é uma regra que o *servidor* informa ao *navegador* sobre quais origens "
        "podem ler suas respostas. Já o proxy do Vite faz o navegador falar só com `localhost:5173`; o servidor do Vite "
        "repassa `/api` à API — não há chamada cross-origin, logo não há preflight. **Decisão:** usar o proxy em dev, porque "
        "em produção a SPA será servida pela própria API (mesma origem, unidade 8) e porque cookies de sessão (unidade 6) "
        "funcionam sem `SameSite=None`. A política CORS explícita da unidade 1 (somente `http://localhost:5173`, sem `AllowAnyOrigin`) "
        "continua como alternativa; liberar qualquer origem com credenciais nunca é aceito.")
    r.p("**Cliente HTTP.** O `fetch` só rejeita a *promise* em falha de rede; status 4xx/5xx chegam como resposta normal. O "
        "cliente gerado pelo NSwag lê o status, desserializa o JSON e **lança** o objeto tipado para respostas de erro "
        "documentadas (ex.: `ProblemDetails` no 404). A camada `api/http.ts` centraliza isso: `chamar()` nunca lança e "
        "devolve `{ok:true,dados}` ou `{ok:false,erro}`, com o erro normalizado em `rede`, `http`, `validacao` ou `cancelado`.")
    r.p("**Estados explícitos.** `idle` (antes da primeira busca), `carregando`, `sucesso`, `vazio` e `erro` são um tipo união "
        "TypeScript; a interface renderiza um ramo para cada um, o que impede telas “presas” em carregamento.")
    r.p("**Roteamento no cliente.** O React Router associa URLs a componentes sem recarregar a página: `/tarefas`, "
        "`/tarefas/nova` e `/tarefas/:id` (parâmetro lido com `useParams`). Filtros, ordenação e página ficam na **query "
        "string** (`useSearchParams`), então a URL é compartilhável e o F5 restaura a mesma tela.")

    r.h1("3. Código-fonte e evidências")
    r.tabela_dados(["Arquivo", "Responsabilidade"], [
        ["`src/api/gerado/portal-tarefas-client.ts`", "Cliente e tipos gerados pelo NSwag a partir do OpenAPI (não editado à mão)"],
        ["`src/api/http.ts`", "Instância única do cliente, `chamar()`, `normalizarErro()`, simulação de atraso/falha só em DEV"],
        ["`src/hooks/useRequisicao.ts`", "Máquina de estados da requisição + cancelamento com `AbortController`"],
        ["`src/pages/ListaTarefas.tsx`", "Filtros, ordenação e paginação sincronizados com a URL"],
        ["`src/pages/DetalhesTarefa.tsx`", "Detalhes por id, 404 próprio e atualização otimista"],
        ["`src/pages/NovaTarefa.tsx`", "Formulário que exibe por campo os erros de validação da API"],
        ["`vite.config.ts` / `.env.development`", "Proxy `/api` → API e URL configurável por variável"],
    ], larguras=[5.8, 10.2], titulo="Organização do front-end")
    r.figura_img(f"{EV}/05-erro-http-problem-details.png", "Resposta HTTP 400 em Problem Details exibida com a mensagem e o código de suporte (correlationId)", 10.5)
    r.p("Os demais estados (carregando, sucesso, vazio, erro de rede, 404, cadastro com erros e cadastro válido) estão nas "
        "Figuras B1 a B9. Navegação registrada pelo Playwright:", tamanho=10.5)
    r.codigo(trecho(f"{EV}/12-navegacao.txt"), tamanho=7.5)

    r.h1("4. Testes e validações obrigatórias")
    r.tabela_dados(["Caso exigido", "Resultado obtido", "Evidência"], [
        ["Estado inicial, carregamento e lista vazia", "`idle` renderizado antes do efeito; “Carregando tarefas…” com atraso simulado; mensagem de vazio com filtro sem resultado", "Teste SSR do `idle`; Fig. B1–B3"],
        ["Erro de rede e resposta HTTP de erro", "Falha de rede → “Falha de conexão” + tentar novamente; 400/404 → Problem Details legível", "Fig. 1, B4, B6; 3 testes"],
        ["Lista → detalhes por URL", "Link leva a `/tarefas/3`; URL direta e voltar no histórico funcionam", "Fig. B5; `navega_da_lista_*`"],
        ["Filtro e paginação na URL", "`?ordenarPor=titulo&pagina=2` sobrevive ao F5; “Próxima” mantém filtros", "`12-navegacao.txt`; `le_filtros_*`"],
        ["Cadastro válido sem recarregar a página", "Navega para os detalhes com aviso; marcador em `window` preservado (sem recarga)", "Fig. B8; `12-navegacao.txt`"],
    ], larguras=[4, 8, 4], titulo="Testes obrigatórios (16 testes Vitest na unidade; build de produção sem erros)")

    r.h1("5. Aprimoramentos")
    r.itens([
        "**Testes de componentes e da camada HTTP** (Vitest + Testing Library): normalização de erros, estados, URL, cancelamento de resposta obsoleta e 404 — 16 testes.",
        "**Atualização otimista com reversão:** “Marcar como concluída” muda a tela na hora; se a API recusar (ex.: 409 por versão antiga), o estado anterior é restaurado e o erro é exibido (teste dedicado).",
        "**Acessibilidade:** o `<h1>` recebe foco a cada página; carregamento usa `role=\"status\"` + `aria-live`, erros usam `role=\"alert\"`, campos inválidos recebem `aria-invalid` e `aria-describedby`; link “Pular para o conteúdo”; animação respeita `prefers-reduced-motion`.",
        "**Simulação controlada** de atraso e falha (`?simular=atraso|falha`), disponível só em `import.meta.env.DEV`.",
    ], tamanho=10.5)

    r.referencias([
        "REACT. **Synchronizing with Effects**. react.dev, 2025. Disponível em: https://react.dev/learn/synchronizing-with-effects. Acesso em: 9 nov. 2026.",
        "REACT. **You Might Not Need an Effect**. react.dev, 2025. Disponível em: https://react.dev/learn/you-might-not-need-an-effect. Acesso em: 9 nov. 2026.",
        "VITE. **Server Options: server.proxy**. vite.dev, 2025. Disponível em: https://vite.dev/config/server-options. Acesso em: 9 nov. 2026.",
        "REMIX SOFTWARE. **React Router: useSearchParams**. reactrouter.com, 2025. Disponível em: https://reactrouter.com/api/hooks/useSearchParams. Acesso em: 9 nov. 2026.",
        "MDN WEB DOCS. **Cross-Origin Resource Sharing (CORS)**. Mozilla, 2025. Disponível em: https://developer.mozilla.org/docs/Web/HTTP/CORS. Acesso em: 9 nov. 2026.",
        "MDN WEB DOCS. **AbortController**. Mozilla, 2025. Disponível em: https://developer.mozilla.org/docs/Web/API/AbortController. Acesso em: 9 nov. 2026.",
        "W3C. **WAI-ARIA 1.2: status and alert roles**. 2023. Disponível em: https://www.w3.org/TR/wai-aria-1.2/. Acesso em: 9 nov. 2026.",
    ])

    r.nova_pagina()
    r.h1("Apêndice A – Código-fonte integral")
    for arq in ("src/PortalTarefas.Frontend/src/api/http.ts", "src/PortalTarefas.Frontend/src/hooks/useRequisicao.ts",
                "src/PortalTarefas.Frontend/src/pages/ListaTarefas.tsx", "src/PortalTarefas.Frontend/src/pages/DetalhesTarefa.tsx",
                "src/PortalTarefas.Frontend/src/pages/NovaTarefa.tsx", "src/PortalTarefas.Frontend/src/components/Comuns.tsx",
                "src/PortalTarefas.Frontend/src/App.tsx", "src/PortalTarefas.Frontend/vite.config.ts",
                "src/PortalTarefas.Frontend/.env.development"):
        r.arquivo_git(COMMIT, arq, tamanho=6.8)

    r.h1("Apêndice B – Capturas e saídas")
    r.codigo(trecho(f"{EV}/13-proxy.txt") + "\n\n" + trecho(f"{EV}/14-testes-e-build.txt"), "**Proxy de desenvolvimento, testes e build de produção**", 7)
    r.figuras_apendice(EV, [
        ("01-carregando.png", "B1", "Estado carregando (atraso simulado)"),
        ("02-sucesso-lista.png", "B2", "Sucesso: lista paginada"),
        ("03-vazio.png", "B3", "Lista vazia para um filtro sem resultados"),
        ("04-erro-rede.png", "B4", "Erro de rede com opção de tentar novamente"),
        ("07-detalhes.png", "B5", "Detalhes acessados por link/URL"),
        ("08-detalhes-404.png", "B6", "Detalhes de identificador inexistente (404)"),
        ("09-cadastro-erros-api.png", "B7", "Cadastro com erros de validação devolvidos pela API"),
        ("10-cadastro-valido.png", "B8", "Cadastro válido: navegação para os detalhes sem recarregar a página"),
        ("11-concluida-otimista.png", "B9", "Atualização otimista concluída"),
    ], 9.5)
    return r.salvar("Relatorio-05-Front-end-React.docx")
