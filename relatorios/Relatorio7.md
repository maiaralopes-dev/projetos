# PROGRAMAÇÃO APLICADA EM .NET — RELATÓRIO DE AULAS PRÁTICAS

**Nome:** ______________________ **RA:** ______________ **Polo:** ______________
**Local da realização da aula prática:** ______________ **Ano da postagem:** 2026

---

## Relatório 7 — SignalR, reconexão, streaming, cache de resposta e ETags

**Unidade 7 – Tempo real e UX**

**Objetivo:** notificar o cliente sobre o processamento de pedidos e reduzir transferências de leitura com cache e validação condicional corretos.

## 1. Entrega e ambiente

- **Repositório:** `HubPedidos/`. Itens novos desta atividade:

| Onde | O quê |
|---|---|
| `src/HubPedidos.Notificacoes/TempoReal/` | hub, autorização por pedido, registro de conexões e revogação |
| `src/HubPedidos.Notificacoes/Canais.cs` | notificador composto e canais |
| `src/HubPedidos.Api/Seguranca/` | autenticação JWT |
| `src/HubPedidos.Api/Http/` | ETag e output cache |
| `clientes/HubPedidos.ClienteTempoReal/` | cliente .NET com reconexão |
| `tests/.../TempoReal/` | testes do hub, incluindo o proxy de falhas |
| `tests/.../Http/` | testes de ETag e cache |

- **Reproduzir:**
  - `dotnet test` (208 testes);
  - `URL_CONSULTA=/painel/regioes carga/medir.sh com-cache 20 20 saida/` e o mesmo comando com `Cache__Habilitado=false`, para o efeito do cache.

| Item | Versão |
|---|---|
| SDK / runtime | .NET SDK 10.0.114 / ASP.NET Core 10.0.14 (SignalR e OutputCaching do framework) |
| Pacotes | Microsoft.AspNetCore.Authentication.JwtBearer 10.0.14; Microsoft.AspNetCore.SignalR.Client 10.0.14 |
| Banco | SQLite 3.53.3 / EF Core 10.0.14 |
| IDE | Visual Studio 2026 versão ______ *(preencher)* |

**Segredos.** A chave de assinatura dos tokens vem de `Autenticacao:ChaveAssinatura`, por variável de ambiente ou cofre, e nunca do repositório. Em Development, sem chave configurada, a API gera uma aleatória por execução e oferece `POST /dev/token` para obter tokens de teste; essa rota não existe fora de Development. Em Production, sem chave, **a API não sobe**. O próprio script de carga esbarrou nisso (`InvalidOperationException: Configure Autenticacao:ChaveAssinatura`) e passou a gerar uma chave aleatória a cada execução.

## 2. Resumo teórico aplicado à atividade

**SignalR.** É uma abstração de comunicação em tempo real. Ele escolhe o transporte (WebSocket, Server-Sent Events ou long polling) e expõe chamadas de método nos dois sentidos:

- **Hub:** a classe no servidor. Uma instância é criada por invocação.
- **Conexão:** um cliente conectado, com `ConnectionId` próprio.
- **Grupo:** um conjunto nomeado de conexões, que serve para endereçar mensagens.
- **Método de cliente:** o que o servidor chama do outro lado. Aqui é tipado pela interface `IClientePedidos`, então não há strings mágicas.

**Grupo não é segurança.** Ele só endereça mensagens. Quem decide quem entra é o servidor, antes do `AddToGroupAsync`.

**Notificação × fonte da verdade.** Uma notificação pode se perder: numa queda, durante uma reconexão ou com o processo reiniciando. Pode chegar duplicada (uma conexão em dois grupos) ou fora de ordem. O SignalR **não faz *replay*** do que foi perdido. Por isso o evento é só um **aviso** com dados mínimos, e o estado de verdade vem de uma consulta **HTTP autoritativa**. O cliente descarta duplicatas pela sequência e, depois de reconectar, **reconcilia** consultando o estado atual.

**Estados da conexão e reconexão.** O cliente passa por *Conectando → Conectado → Reconectando → Conectado/Desconectado*. A reconexão automática usa *backoff*, com intervalos crescentes e um teto. Depois de reconectar, a conexão é **nova**, com outro `ConnectionId` e sem grupos. O cliente precisa pedir de novo, e o servidor precisa **revalidar a autorização**, porque a permissão pode ter sido revogada no meio do caminho. O usuário deve ver o estado, e as ações que dependem da conexão ficam desabilitadas.

**Cache de resposta × cache de dados.**

- O **output cache** guarda a resposta HTTP pronta no servidor. A próxima requisição igual nem chega ao handler.
- O **cache de dados** (`IMemoryCache`, Redis) guarda objetos que o código ainda processa.

O risco do primeiro é servir a um usuário a resposta montada para outro. Por isso só leituras **públicas e iguais para todos** podem ir para o output cache. O cache do ASP.NET também não guarda requisições com `Authorization` ou cookie, e precisa entrar **depois** de `UseAuthentication`/`UseAuthorization`.

**ETag e pré-condições.** A ETag identifica uma **representação**: os bytes enviados para um recurso numa versão. As pré-condições usam essa identificação:

- **`If-None-Match`:** "só me mande se mudou". Se a ETag ainda vale, a resposta é **304 sem corpo**. Usa comparação **fraca** (RFC 9110 §13.1.2).
- **`If-Match`:** "só grave se ninguém mudou desde que eu li". Se a ETag não vale mais, a resposta é **412** e nada é gravado. Usa comparação **forte**.

**Invalidação e coerência.** Cache por tempo é simples, mas fica velho. Cache invalidado por **tag** quando o dado muda é mais coerente, mas só vale para mudanças que passam pela aplicação, **nesta instância**. Também é preciso coerência entre cache, versão da API e tempo real: cada versão tem sua representação (logo sua ETag), e o mesmo evento que vai pelo SignalR também invalida o cache.

## 3. Código-fonte, decisões e evidências

### 3.1 Hub, grupos e evento tipado (Etapa 1)

```csharp
public sealed record PedidoAtualizadoMensagem(Guid PedidoId, string Status, long Sequencia, DateTimeOffset Em);

public interface IClientePedidos
{
    Task PedidoAtualizado(PedidoAtualizadoMensagem mensagem);
    Task AcessoRevogado(AcessoRevogadoMensagem mensagem);
}

[Authorize]
public sealed class PedidosHub(AutorizadorPedidos autorizador, RegistroConexoes conexoes, RegistroRevogacoes revogacoes) : Hub<IClientePedidos>
{
    public async Task AssinarPedido(Guid pedidoId)
    {
        if (!await autorizador.PodeVerAsync(Context.User, pedidoId, Context.ConnectionAborted))
            throw new HubException("Acesso negado ao pedido."); // mesma mensagem p/ inexistente e alheio
        await Groups.AddToGroupAsync(Context.ConnectionId, Grupos.Pedido(pedidoId));
        ...
```

**Decisões:**

- **`[Authorize]` no hub.** Sem token válido, a própria negociação recebe **401** (evidência e teste). O token chega pelo cabeçalho `Authorization` ou, só na rota `/hubs`, pela query `access_token`, porque WebSocket no navegador não envia cabeçalhos.
- **Autorização antes do grupo.** O `AutorizadorPedidos` é a **mesma regra** usada pela consulta HTTP v2: operador vê tudo, cliente só vê os próprios pedidos (`cliente_id` do token igual ao dono do pedido), cliente revogado não vê nada. Pedido alheio e inexistente dão a **mesma resposta**, para não revelar quais ids existem.
- **Publicação depois de persistir.** Quem chama `INotificadorPedidos` já gravou a mudança: o consumidor da fila (Atividade 4), o `POST /pedidos` e o `PUT /pedidos/{id}/status`. Um teste confirma que, no instante em que o evento chega, a consulta HTTP já mostra o novo status.
- **Dados mínimos.** O evento leva id, status, sequência e instante. Não leva o objeto de domínio nem dados do cliente.
- **Canal SignalR dentro de um notificador composto.** O módulo de Notificações implementa a porta da Atividade 6 com um `NotificadorComposto`, que entrega o evento a cada `ICanalNotificacao`: memória, SignalR e invalidação de cache (este último implementado pela API). A falha de um canal não impede os outros nem desfaz a mudança. O processamento **não mudou nada** para ganhar o tempo real, como previsto no ADR 0002.
- **Regra arquitetural ajustada de forma explícita.** O módulo de Notificações passou a usar ASP.NET Core (SignalR), como exceção declarada no teste arquitetural. EF Core continua proibido nele.

### 3.2 Cliente com reconexão e reconciliação (Etapa 2)

`ClientePedidosTempoReal` (`clientes/HubPedidos.ClienteTempoReal`) representa o front-end. Ele tem os próprios tipos e não referencia o servidor.

| Situação | Comportamento |
|---|---|
| Conexão perdida | estado **Reconectando**, `AcoesHabilitadas = false`, mensagem: *"Conexão perdida. Tentando reconectar… As ações estão desabilitadas até voltar."* |
| Política de retentativa | 0 s, 0,25 s, 0,5 s, 1 s, 2 s, 5 s e então 10 s, até 2 minutos; depois disso, **Desconectado**: *"Sem conexão com o servidor. Recarregue a página…"* |
| Reconectou | para cada pedido assinado, chama `AssinarPedido` de novo (o **servidor revalida**; se a permissão foi revogada, o pedido sai da lista local) e **reconcilia por HTTP** (`GET /api/v2/pedidos/{id}` com `If-None-Match`: 304 se nada mudou, 200 com o estado atual se mudou) |
| Evento duplicado ou atrasado | descartado se a sequência for menor ou igual à última vista para aquele pedido |
| Acesso revogado | o servidor tira a conexão dos grupos e envia `AcessoRevogado`; o cliente limpa as assinaturas |

**Teste de reconexão com proxy de falha controlado** (extensão do roteiro). A API sobe em **Kestrel real**, usando o `WebApplicationFactory.UseKestrel` do .NET 10, com um **proxy TCP** entre o cliente e o servidor. O proxy derruba todas as conexões com RST e recusa as novas, simulando uma queda de rede de verdade, inclusive no WebSocket. Roteiro do teste:

1. O cliente assina um pedido (status local: `Recebido`).
2. Queda de rede. O estado vai para **Reconectando** e as ações ficam **desabilitadas**.
3. Durante a queda, o pedido muda para `EmProcessamento`. O evento é **perdido**, porque não há replay.
4. A rede volta. O cliente reconecta, assina de novo e **reconcilia por HTTP**. O estado local passa a `EmProcessamento` e é disparado o evento `Reconciliado`.
5. Uma nova mudança (`Concluido`) **chega pelo hub**, o que prova que a reassinatura funcionou.

O teste passou em 3 execuções seguidas. A primeira versão do proxy falhava com `ObjectDisposedException`, ao configurar o RST num socket que a bomba de cópia já tinha fechado, e foi corrigida.

**Revogação.** `POST /admin/clientes/{id}/revogar` exige o papel operador (cliente recebe 403). A revogação:

- marca o cliente como revogado;
- tira todas as conexões dele de todos os grupos;
- envia `AcessoRevogado`.

Depois disso, eventos daquele pedido não chegam mais, um novo `AssinarPedido` é negado e uma reconexão é encerrada em `OnConnectedAsync`.

### 3.3 ETag (Etapa 3)

A ETag é **forte**: os primeiros 128 bits do SHA-256 dos **bytes JSON exatamente como são enviados**, em base64url. Ela é aplicada ao detalhe na v1, na v2 e na rota legada.

- **Campos que compõem a representação:** todos os do DTO daquela versão. Mesmos valores geram os mesmos bytes, logo a mesma ETag. Mudou o status (ou qualquer campo), mudou a ETag.
- **Cada versão tem a própria ETag**, porque a v1 e a v2 são representações diferentes. A rota legada tem a mesma ETag da v1.
- **`Cache-Control: private, no-cache`:** o navegador pode guardar, mas revalida sempre, e proxies compartilhados não guardam, porque a v2 é de um usuário específico.

Evidência (`http-tempo-real-etag-cache.txt`):

```
GET /api/v2/pedidos/{id}                        → 200  ETag: "r20c1NDQoVfg4YmexkfQ8w"  (430 bytes)
GET ... If-None-Match: "r20c1NDQoVfg4YmexkfQ8w" → 304  sem corpo, mesma ETag
PUT /pedidos/{id}/status If-Match: (atual)       → 204
GET ... If-None-Match: (antiga)                  → 200  ETag: "U1ogeS8gpLMQgqMCoxoo9w"  status EmProcessamento
PUT ... If-Match: (antiga)                       → 412  "O pedido mudou desde a sua última leitura…"
```

O tratamento segue a RFC 9110:

- **`If-None-Match`:** comparação fraca. `W/"x"` casa com `"x"`, `*` casa com qualquer representação e listas são aceitas.
- **`If-Match`:** comparação forte.
- **Valor malformado ou desconhecido:** a resposta é normal (200 com corpo), e não um erro.

### 3.4 Output cache com limites (Etapa 4)

**Leituras escolhidas** (públicas e iguais para todos os usuários):

| Rota | Duração | Tag | Por quê |
|---|---|---|---|
| `GET /processadores` | 5 min | `catalogo` | muda só com deploy |
| `GET /painel/regioes` | 30 s | `painel`, **invalidada** a cada mudança de pedido | agregação cara (cerca de 65 ms) e lida com frequência |

**Rotas excluídas de propósito:**

- detalhe e listagem de pedido: são de um cliente; usam ETag no lugar do cache;
- métricas da fila e notificações: precisam estar atuais;
- toda requisição com `Authorization` ou cookie: a política base do ASP.NET não guarda nem serve do cache.

A ordem dos middlewares é `UseAuthentication()` → `UseAuthorization()` → `UseOutputCache()`. A invalidação é feita pelo `CanalInvalidacaoCache` (`EvictByTagAsync("painel")`), um canal do mesmo notificador que alimenta o SignalR.

**Medição** (gerador da Atividade 5, modelo aberto, API em Release, banco com 20 mil pedidos; `carga-output-cache.md`). As respostas vindas do cache foram identificadas pelo cabeçalho `Age`.

| Cenário (painel/s + criações/s) | Painel p50 | p95 | p99 | Criação p99 | Vindas do cache* |
|---|---:|---:|---:|---:|---:|
| Sem cache, 20 + 20 | 64,8 ms | 87,8 ms | 90,9 ms | 15,8 ms | 0% |
| Com cache, 20 + 20 (invalidação a cada criação) | **11,2 ms** | 75,9 ms | 85,4 ms | 7,9 ms | cerca de 66% |
| Com cache, 200 + 0 (sem invalidação) | **2,2 ms** | **3,8 ms** | **5,4 ms** | — | cerca de 100% |
| Sem cache, 200 + 20 | **colapso**: 5.995 timeouts no painel e 599 na criação | | | | 0% |
| Com cache, 200 + 20 | 16,9 ms | 65,1 ms | 80,9 ms | 7,9 ms | cerca de 95% |

\* Respostas com `Age` sobre o total enviado, incluindo o aquecimento.

**Leitura:**

- O cache **reduz a mediana** de 65 ms para 2 a 17 ms, e é o que **permite** atender 200 leituras/s do painel. Sem ele, a mesma carga derruba a API inteira, inclusive as criações.
- Com invalidação a cada escrita, o **p95 continua perto do custo de uma leitura sem cache** (76 ms contra 88 ms): toda leitura logo depois de uma invalidação paga a consulta inteira. O ganho é proporcional à razão entre leituras e escritas.
- Para reduzir a cauda seria preciso **aceitar dados um pouco desatualizados**: não invalidar por tag e deixar expirar em poucos segundos. **Decisão:** manter a invalidação por tag, porque o painel é usado por operadores para acompanhar mudanças. Se a latência da cauda virar problema, reavaliar com uma janela de 5 s.

**Risco de inconsistência, demonstrado por teste.** A invalidação só vale para mudanças que passam pelo notificador **desta instância**. Uma alteração direta no banco (ou feita por outra instância da API) não invalida nada, e o painel fica desatualizado até expirar (30 s). Com várias instâncias, seriam necessários um cache distribuído (por exemplo, Redis) e a invalidação propagada a todas.

## 4. Testes e validações obrigatórias

Resultado: **208 testes, 208 aprovados** (23 novos), em 3 execuções seguidas; lista em `docs/evidencias/atividade7/testes.txt`.

| Validação exigida | Teste(s) |
|---|---|
| Cliente recebe atualização só depois da autorização validada no servidor; grupo não substitui autorização | `Sem_token_a_negociacao_e_recusada` (401); `Cliente_so_assina_e_recebe_os_proprios_pedidos` (pedido alheio e inexistente → `HubException`; o cliente A não recebe o evento de B, que o operador recebe); `Evento_chega_depois_da_persistencia_e_o_http_confirma` |
| Reconexão revalida a autorização, restaura a assinatura e reconcilia eventos perdidos | `Reconexao_reassina_e_reconcilia_eventos_perdidos_pela_queda` (proxy de falhas); `Revogacao_tira_dos_grupos_avisa_e_impede_reassinar`; `Revogar_exige_operador`; `Duplicatas_e_eventos_fora_de_ordem_sao_descartados` |
| ETag muda quando a representação muda e fica estável quando não muda | `Etag_e_estavel_quando_nada_muda_e_304_vem_sem_corpo` (v1 e v2); `Etag_muda_quando_a_representacao_muda...`; `Cada_versao_tem_a_propria_etag_e_a_rota_legada_e_igual_a_v1` |
| `If-None-Match` válido → 304 sem corpo; obsoleto → representação atual | os testes acima; `If_none_match_invalido_ou_desconhecido_devolve_a_representacao` (×3); `Comparacao_fraca_e_asterisco_e_lista_dao_304` |
| Output cache depois de autenticação/autorização e sem compartilhar respostas privadas | `Requisicao_autenticada_nao_e_guardada_nem_servida_do_cache`; `Detalhe_de_pedido_nunca_e_compartilhado_entre_usuarios`; `Segunda_leitura_vem_do_cache_sem_tocar_no_banco`; `Mudanca_de_pedido_invalida_pela_tag`; `Mudanca_fora_do_fluxo_da_aplicacao_fica_invisivel_ate_expirar`; `Cache_pode_ser_desligado_por_configuracao` |
| Autorização da v2 | `V2_exige_identidade_e_esconde_pedido_alheio` (401 anônimo, 404 alheio ou inexistente, 200 dono e operador) |

### Aprimoramentos (extensões do roteiro)

1. **`If-Match` para atualização otimista, com 412** (`If_match_atual_permite_e_obsoleto_da_412_sem_gravar`). Sem `If-Match`, o comportamento antigo é mantido por compatibilidade. Limitação: verificar e gravar são dois passos, então uma mudança exatamente entre eles não é detectada; o repositório ainda valida a transição de status.
2. **Teste de reconexão com proxy de falha controlado**, com Kestrel real e queda TCP abrupta (seção 3.2).
3. *(Não realizado)* Streaming de progresso com cancelamento (`IAsyncEnumerable` no hub).

### Limitações conhecidas

- A **sequência** dos eventos é por processo e reinicia junto com a aplicação. O cliente zera a sequência ao reconciliar por HTTP, que é a fonte da verdade.
- **Revogações, conexões e output cache ficam na memória de uma instância.** Escalar exige um *backplane* do SignalR (Redis ou Azure SignalR) e armazenamento compartilhado.
- O **token na query string** (exigência do WebSocket no navegador) pode aparecer em logs de acesso. A mitigação é usar tokens de vida curta e não registrar a query da rota `/hubs`.
- A **v1 continua anônima** até o sunset, por compatibilidade. Esse risco foi aceito e registrado.
- O cliente foi implementado em .NET, como modelo do contrato de UX (estados, mensagens, ações habilitadas). Um front-end em JavaScript seguiria o mesmo padrão (`withAutomaticReconnect`, `onreconnecting`, `onreconnected`).

## 5. Referências

> Mantenha somente as fontes que você realmente consultou.

MICROSOFT. **Overview of ASP.NET Core SignalR**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/aspnet/core/signalr/introduction. Acesso em: 13 nov. 2026.

MICROSOFT. **Authentication and authorization in ASP.NET Core SignalR**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz. Acesso em: 13 nov. 2026.

MICROSOFT. **ASP.NET Core SignalR .NET Client: automatic reconnect**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/aspnet/core/signalr/dotnet-client. Acesso em: 13 nov. 2026.

MICROSOFT. **Output caching middleware in ASP.NET Core**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/aspnet/core/performance/caching/output. Acesso em: 13 nov. 2026.

MICROSOFT. **Integration tests in ASP.NET Core (WebApplicationFactory, Kestrel)**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/aspnet/core/test/integration-tests. Acesso em: 13 nov. 2026.

IETF. **RFC 9110: HTTP Semantics** (seções 8.8.3 ETag e 13 Conditional Requests). 2022. Disponível em: https://www.rfc-editor.org/rfc/rfc9110. Acesso em: 13 nov. 2026.

IETF. **RFC 9111: HTTP Caching**. 2022. Disponível em: https://www.rfc-editor.org/rfc/rfc9111. Acesso em: 13 nov. 2026.
