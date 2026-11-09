// Camada única de acesso à API. Os componentes nunca chamam fetch diretamente:
// usam `chamar`, que devolve dados tipados ou um erro normalizado.
import {
  ApiException,
  PortalTarefasClient,
  type HttpValidationProblemDetails,
  type ProblemDetails,
} from './gerado/portal-tarefas-client'

export type {
  AtualizarTarefaDto,
  CriarTarefaDto,
  PaginaTarefaResumo,
  PrioridadeTarefa,
  SituacaoTarefa,
  TarefaDetalhesDto,
  TarefaResumoDto,
} from './gerado/portal-tarefas-client'

export type ErroApi =
  | { tipo: 'rede'; mensagem: string }
  | { tipo: 'http'; status: number; titulo: string; detalhe?: string; correlationId?: string }
  | { tipo: 'validacao'; titulo: string; erros: Record<string, string[]>; correlationId?: string }
  | { tipo: 'cancelado' }

export type Resultado<T> = { ok: true; dados: T } | { ok: false; erro: ErroApi }

const ehProblemDetails = (e: unknown): e is ProblemDetails & { correlationId?: string } =>
  typeof e === 'object' && e !== null && 'status' in e && 'title' in e

/** Converte qualquer falha (rede, cancelamento, status HTTP, Problem Details) em um formato único. */
export function normalizarErro(e: unknown): ErroApi {
  if (e instanceof DOMException && e.name === 'AbortError') return { tipo: 'cancelado' }
  if (e instanceof TypeError) return { tipo: 'rede', mensagem: 'Não foi possível conectar à API. Verifique sua conexão.' }
  if (ehProblemDetails(e)) {
    const pd = e as HttpValidationProblemDetails & { correlationId?: string }
    if (pd.status === 400 && pd.errors)
      return { tipo: 'validacao', titulo: pd.title ?? 'Dados inválidos.', erros: pd.errors, correlationId: pd.correlationId }
    return { tipo: 'http', status: pd.status ?? 0, titulo: pd.title ?? 'Erro na API.', detalhe: pd.detail, correlationId: pd.correlationId }
  }
  if (e instanceof ApiException) {
    // Status não documentado no contrato: tenta ler um Problem Details do corpo.
    try {
      const corpo = JSON.parse(e.response)
      if (ehProblemDetails(corpo)) return normalizarErro(corpo)
    } catch { /* corpo não é JSON */ }
    return { tipo: 'http', status: e.status, titulo: `Erro ${e.status} ao chamar a API.` }
  }
  return { tipo: 'http', status: 0, titulo: 'Erro inesperado.' }
}

// Em desenvolvimento, ?simular=atraso ou ?simular=falha na URL permite observar os estados de carregamento e erro.
async function fetchComSimulacao(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  if (import.meta.env.DEV && typeof window !== 'undefined') {
    const simular = new URLSearchParams(window.location.search).get('simular')
    if (simular === 'atraso' || simular === 'falha') {
      await new Promise((ok, falha) => {
        const t = setTimeout(ok, 1500)
        init?.signal?.addEventListener('abort', () => { clearTimeout(t); falha(new DOMException('cancelado', 'AbortError')) })
      })
    }
    if (simular === 'falha') throw new TypeError('Failed to fetch (simulado)')
  }
  return fetch(input, { ...init, credentials: 'same-origin' })
}

export const cliente = new PortalTarefasClient(import.meta.env.VITE_API_URL ?? '', { fetch: fetchComSimulacao })

/** Executa uma operação do cliente gerado e devolve dados tipados ou erro normalizado (nunca lança). */
export async function chamar<T>(
  operacao: (c: PortalTarefasClient, signal: AbortSignal) => Promise<T>,
  signal: AbortSignal = new AbortController().signal,
): Promise<Resultado<T>> {
  try {
    return { ok: true, dados: await operacao(cliente, signal) }
  } catch (e) {
    return { ok: false, erro: signal.aborted ? { tipo: 'cancelado' } : normalizarErro(e) }
  }
}
