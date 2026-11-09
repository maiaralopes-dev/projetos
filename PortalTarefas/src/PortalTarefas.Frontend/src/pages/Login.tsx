import { useState, type FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router'
import type { ErroApi } from '../api/http'
import { useAuth } from '../auth/AuthContext'
import { MensagemErro, TituloPagina } from '../components/Comuns'

export function Login() {
  const { entrar, sessao } = useAuth()
  const navegar = useNavigate()
  const destino = (useLocation().state as { de?: string } | null)?.de ?? '/tarefas'
  const [erro, setErro] = useState<ErroApi | null>(null)
  const [enviando, setEnviando] = useState(false)

  if (sessao.autenticado) return <Navigate to={destino} replace />

  async function enviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const f = new FormData(e.currentTarget)
    setEnviando(true)
    const falha = await entrar(String(f.get('email')), String(f.get('senha')))
    setEnviando(false)
    if (falha) setErro(falha)
    else navegar(destino, { replace: true })   // volta para a URL que o usuário tentou abrir
  }

  return (
    <>
      <TituloPagina>Entrar</TituloPagina>
      <form onSubmit={enviar} className="formulario login" noValidate>
        <label>E-mail<input name="email" type="email" autoComplete="username" required /></label>
        <label>Senha<input name="senha" type="password" autoComplete="current-password" required /></label>
        {erro && <MensagemErro erro={erro} />}
        <button type="submit" disabled={enviando}>{enviando ? 'Entrando…' : 'Entrar'}</button>
      </form>
    </>
  )
}
