import { useEffect, useRef, type ReactNode } from 'react'
import type { ErroApi } from '../api/http'

/** Título da página: recebe o foco ao montar, para leitores de tela anunciarem a navegação. */
export function TituloPagina({ children }: { children: ReactNode }) {
  const ref = useRef<HTMLHeadingElement>(null)
  useEffect(() => ref.current?.focus(), [])
  return <h1 ref={ref} tabIndex={-1}>{children}</h1>
}

export function Carregando({ texto = 'Carregando…' }: { texto?: string }) {
  return <p className="estado carregando" role="status" aria-live="polite"><span className="spinner" aria-hidden="true" />{texto}</p>
}

export function MensagemErro({ erro, onTentarNovamente }: { erro: ErroApi; onTentarNovamente?: () => void }) {
  if (erro.tipo === 'cancelado') return null
  const titulo = erro.tipo === 'rede' ? 'Falha de conexão' : erro.titulo
  return (
    <div className="estado erro" role="alert">
      <strong>{titulo}</strong>
      {erro.tipo === 'rede' && <p>{erro.mensagem}</p>}
      {erro.tipo === 'http' && <p>{erro.detalhe ?? `A API respondeu com status ${erro.status}.`}</p>}
      {erro.tipo === 'validacao' && (
        <ul>{Object.entries(erro.erros).map(([campo, msgs]) => <li key={campo}>{campo}: {msgs.join(' ')}</li>)}</ul>
      )}
      {'correlationId' in erro && erro.correlationId && <p className="corr">Código para suporte: {erro.correlationId}</p>}
      {onTentarNovamente && <button type="button" onClick={onTentarNovamente}>Tentar novamente</button>}
    </div>
  )
}

export function Selo({ children, classe }: { children: ReactNode; classe?: string }) {
  return <span className={`selo ${classe ?? ''}`}>{children}</span>
}

export const formatarData = (iso?: string) => (iso ? iso.split('-').reverse().join('/') : '—')
export const rotuloSituacao: Record<string, string> = { Pendente: 'Pendente', EmAndamento: 'Em andamento', Concluida: 'Concluída' }
