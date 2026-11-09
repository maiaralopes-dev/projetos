from base import Relatorio, AMBIENTE, REPO, acesso, trecho

EV = "docs/evidencias/atividade2"
COMMIT = "e6a3427"


def gerar():
    r = Relatorio(2, "Controllers, validação, filtros e interfaces Razor reutilizáveis",
                  "Unidade 2 – MVC e Razor",
                  "Construir uma interface MVC para cadastrar e consultar tarefas, usando validação, filtros, layouts, partials e Tag Helpers.")
    r.capa()
    r.cabecalho_atividade()

    r.h1("1. Identificação e ambiente")
    r.p(f"Repositório: {REPO}; código desta unidade no commit `{COMMIT}`. Mesmo ambiente do Relatório 1 (SDK .NET 10.0.114, "
        "runtime 10.0.14, xUnit 2.9.3). Execução: `dotnet run --project src/PortalTarefas.Web` e acesso a `https://localhost:7080/`. "
        "Capturas geradas com Playwright (`scripts/capturas/atividade2.js`).")

    r.h1("2. Resumo teórico aplicado ao projeto")
    r.p("**Model, View e Controller.** O *controller* recebe a requisição, chama o serviço e escolhe a resposta; o *model* "
        "carrega os dados (aqui separados em modelo de domínio `Tarefa` e *ViewModels*); a *view* apenas apresenta. No "
        "`TarefasController` cada ação tem poucas linhas: valida, chama `ITarefaServico` e devolve `View(...)` ou "
        "`RedirectToAction(...)` (padrão *Post/Redirect/Get*, que evita reenvio do formulário ao atualizar a página).")
    r.p("**Binding e validação.** O *model binding* preenche `TarefaInputModel` a partir dos campos do formulário. As Data "
        "Annotations (`[Required]`, `[StringLength]`, mensagens em português) e a regra customizada em `IValidatableObject` "
        "alimentam o `ModelState`; o controller só grava quando `ModelState.IsValid`. Detalhe observado na prática: o MVC só "
        "executa a validação de nível de classe (`Validate`) quando as propriedades já são válidas — por isso o erro de prazo "
        "aparece depois que o título é corrigido (Figuras B2 e B3). Uma validação **assíncrona dependente de dados** (título "
        "duplicado) é feita no controller, pois depende do serviço.")
    r.p("**Filtros.** Executam em pontos fixos do pipeline MVC: autorização (antes de tudo, decide acesso), recurso, **ação** "
        "(antes/depois da ação, com acesso aos argumentos e ao resultado), exceção e resultado. O risco de uso indevido é "
        "esconder regra de negócio em filtros (difícil de testar e de enxergar) ou duplicar validação. O `RegistroAcaoFilter` "
        "apenas mede e registra ação, duração, validade do `ModelState` e resultado.")
    r.p("**Razor e views fortemente tipadas.** Razor mistura HTML e C# (`@model`, `@if`, `@foreach`) e é compilado; com "
        "`@model TarefaListaViewModel` o compilador verifica nomes de propriedades e o IntelliSense funciona.")
    r.p("**Reutilização.** `_Layout.cshtml` define cabeçalho, navegação, área de mensagens e rodapé de todas as páginas; a "
        "*partial* `_TarefaResumo` é usada na lista e nos detalhes; a partial `_TarefaCampos` é compartilhada por cadastro e "
        "edição; o *View Component* `TarefasVencidas` tem lógica própria (consulta o serviço) e aparece no layout; os *Tag "
        "Helpers* (`asp-for`, `asp-validation-for`, `asp-action`, `asp-route-id`) geram `name`, `id`, valores, URLs e o token antifalsificação.")
    r.p("**Domínio × DTO × ViewModel.** A entidade `Tarefa` não chega à view: `TarefaInputModel` expõe só o que o usuário "
        "pode digitar (impede *overposting* de `Id`/`CriadaEm`), e `TarefaResumoViewModel` traz campos calculados (`Vencida`).")

    r.h1("3. Código-fonte e evidências")
    r.tabela_dados(["Artefato", "Responsabilidade"], [
        ["`Controllers/TarefasController.cs`", "Ações `Index`, `Details`, `Create` (GET/POST), `Edit` (GET/POST); `[ServiceFilter<RegistroAcaoFilter>]`"],
        ["`ViewModels/TarefaInputModel.cs`", "Data Annotations + `IValidatableObject` (Alta exige prazo; prazo passado só se concluída)"],
        ["`ViewModels/TarefaViewModels.cs`", "`TarefaResumoViewModel`, `TarefaListaViewModel`, `TarefaDetalhesViewModel`, `TarefaEdicaoViewModel`"],
        ["`Filtros/RegistroAcaoFilter.cs`", "Filtro de ação assíncrono (log de ação, duração, ModelState e resultado)"],
        ["`Servicos/ITarefaServico.cs` + `TarefaServicoMemoria.cs`", "Contrato assíncrono e implementação em memória (isola a persistência futura)"],
        ["`Views/Shared/_Layout`, `_TarefaResumo`, `_TarefaCampos`", "Layout comum e partials reutilizadas"],
        ["`ViewComponents/TarefasVencidasViewComponent.cs`", "Componente do layout com tarefas vencidas"],
    ], larguras=[6.2, 9.8], titulo="Arquivos produzidos")
    r.p("**Escopo do filtro:** registrado por atributo `[ServiceFilter<RegistroAcaoFilter>]` no `TarefasController`, e não "
        "globalmente, porque o objetivo é auditar a interface MVC; o controller de diagnóstico da unidade 1 não precisa desse log. "
        "Por ser `ServiceFilter`, o filtro recebe `ILogger` por injeção. O trecho abaixo mostra o filtro antes e depois de cada "
        "ação; note que, no POST inválido, ele apenas registra `ModelState válido: False` — quem decide é o controller:")
    r.codigo(trecho(f"{EV}/log-filtro.txt", contem=["Create [POST]", "Details/999", "NaoEncontrada"], max_linhas=12), tamanho=7)
    r.figura_img(f"{EV}/05b-cadastro-alta-sem-prazo.png", "Validação customizada envolvendo prioridade e prazo; os dados digitados foram preservados", 11.5)

    r.h1("4. Testes e validações obrigatórias")
    r.tabela_dados(["Caso exigido", "Resultado obtido", "Evidência"], [
        ["Cadastro válido e com título vazio", "Válido: 302 → Detalhes com mensagem. Vazio: a view volta com “Informe o título da tarefa.” e a descrição preservada", "Fig. B2, B6; `Post_com_titulo_vazio_*`, `Create_valido_*`"],
        ["Validação customizada de prazo", "Alta sem prazo e prazo no passado rejeitados; prazo passado aceito se `Concluida`", "Fig. 1, B3; 4 testes unitários"],
        ["Identificador existente e inexistente", "`/Tarefas/Details/2` → 200; `/Tarefas/Details/999` → view `NaoEncontrada` com status 404", "Fig. B1; log do filtro; `Details_inexistente_*`"],
        ["Layout consistente em ≥ 3 páginas", "Lista, detalhes, cadastro e edição contêm o mesmo cabeçalho e o View Component", "`Paginas_compartilham_layout` (4 páginas)"],
        ["Filtro sem duplicar regra de negócio", "Filtro só registra; validação permanece no ModelState/controller", "Log do filtro (seção 3)"],
    ], larguras=[3.8, 8, 4.2], titulo="Testes obrigatórios (14 testes: 4 de validação, 4 do controller, 6 de páginas)")

    r.h1("5. Aprimoramentos")
    r.itens([
        "**View Component `TarefasVencidas`** no layout, alimentado por `ITarefaServico.ListarVencidasAsync`.",
        "**Validação assíncrona no servidor dependente de dados:** título duplicado (comparação sem diferenciar maiúsculas) é rejeitado no cadastro e na edição (Fig. B4).",
        "**Testes unitários** do controller (instância direta com serviço em memória e relógio fixo) e da validação customizada (`Validator.TryValidateObject` com data de referência injetável), além de testes de integração das páginas.",
        "**Proteção CSRF** global (`AutoValidateAntiforgeryTokenAttribute`): o teste `Post_sem_token_antiforgery_e_rejeitado` confirma 400 sem o token gerado pelo Tag Helper `<form>`.",
    ], tamanho=10.5)

    r.referencias([
        acesso("Overview of ASP.NET Core MVC", "https://learn.microsoft.com/aspnet/core/mvc/overview"),
        acesso("Model validation in ASP.NET Core MVC and Razor Pages", "https://learn.microsoft.com/aspnet/core/mvc/models/validation"),
        acesso("Filters in ASP.NET Core", "https://learn.microsoft.com/aspnet/core/mvc/controllers/filters"),
        acesso("Layout in ASP.NET Core", "https://learn.microsoft.com/aspnet/core/mvc/views/layout"),
        acesso("Partial views in ASP.NET Core", "https://learn.microsoft.com/aspnet/core/mvc/views/partial"),
        acesso("View components in ASP.NET Core", "https://learn.microsoft.com/aspnet/core/mvc/views/view-components"),
        acesso("Tag Helpers in ASP.NET Core", "https://learn.microsoft.com/aspnet/core/mvc/views/tag-helpers/intro"),
    ])

    r.nova_pagina()
    r.h1("Apêndice A – Código-fonte integral")
    for arq in ("src/PortalTarefas.Web/Controllers/TarefasController.cs", "src/PortalTarefas.Web/ViewModels/TarefaInputModel.cs",
                "src/PortalTarefas.Web/ViewModels/TarefaViewModels.cs", "src/PortalTarefas.Web/Filtros/RegistroAcaoFilter.cs",
                "src/PortalTarefas.Web/ViewComponents/TarefasVencidasViewComponent.cs", "src/PortalTarefas.Web/Views/Shared/_Layout.cshtml",
                "src/PortalTarefas.Web/Views/Shared/_TarefaResumo.cshtml", "src/PortalTarefas.Web/Views/Shared/_TarefaCampos.cshtml",
                "src/PortalTarefas.Web/Views/Shared/Components/TarefasVencidas/Default.cshtml", "src/PortalTarefas.Web/Views/Tarefas/Index.cshtml",
                "src/PortalTarefas.Web/Views/Tarefas/Details.cshtml", "src/PortalTarefas.Web/Views/Tarefas/Create.cshtml",
                "src/PortalTarefas.Web/Views/Tarefas/Edit.cshtml"):
        r.arquivo_git(COMMIT, arq)

    r.h1("Apêndice B – Capturas de tela")
    r.figuras_apendice(EV, [
        ("04-detalhes-inexistente.png", "B1", "Identificador inexistente (status 404, view própria)"),
        ("05-cadastro-titulo-vazio.png", "B2", "Cadastro com título vazio; descrição preservada"),
        ("06-cadastro-prazo-passado.png", "B3", "Prazo no passado para tarefa não concluída"),
        ("07-cadastro-titulo-duplicado.png", "B4", "Validação assíncrona: título duplicado"),
        ("01-lista.png", "B5", "Lista com partial de resumo e View Component de vencidas"),
        ("08-cadastro-valido-detalhes.png", "B6", "Cadastro válido redirecionado para Detalhes (PRG)"),
        ("09-edicao-erro.png", "B7", "Edição com erro de validação"),
        ("10-edicao-salva.png", "B8", "Edição salva"),
    ])
    return r.salvar("Relatorio-02-MVC-e-Razor.docx")
