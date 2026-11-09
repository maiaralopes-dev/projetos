import { useCallback, useEffect, useState, type DependencyList } from 'react'
import { chamar, type ErroApi } from '../api/http'
import type { PortalTarefasClient } from '../api/gerado/portal-tarefas-client'

/** Estados explícitos de uma requisição de leitura. */
export type EstadoRequisicao<T> =
  | { fase: 'idle' }
  | { fase: 'carregando' }
  | { fase: 'sucesso'; dados: T }
  | { fase: 'vazio'; dados: T }
  | { fase: 'erro'; erro: ErroApi }

/**
 * Executa a operação sempre que as dependências mudam. A requisição anterior é cancelada
 * (AbortController), então uma resposta obsoleta nunca sobrescreve a mais recente.
 */
export function useRequisicao<T>(
  operacao: (c: PortalTarefasClient, signal: AbortSignal) => Promise<T>,
  deps: DependencyList,
  ehVazio: (dados: T) => boolean = () => false,
) {
  const [estado, setEstado] = useState<EstadoRequisicao<T>>({ fase: 'idle' })
  const [tentativa, setTentativa] = useState(0)

  useEffect(() => {
    const controle = new AbortController()
    setEstado({ fase: 'carregando' })
    chamar(operacao, controle.signal).then((r) => {
      if (controle.signal.aborted) return            // componente desmontado ou filtros mudaram
      if (!r.ok) setEstado(r.erro.tipo === 'cancelado' ? { fase: 'idle' } : { fase: 'erro', erro: r.erro })
      else setEstado(ehVazio(r.dados) ? { fase: 'vazio', dados: r.dados } : { fase: 'sucesso', dados: r.dados })
    })
    return () => controle.abort()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tentativa])

  const recarregar = useCallback(() => setTentativa((t) => t + 1), [])
  return { estado, setEstado, recarregar }
}
