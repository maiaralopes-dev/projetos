import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router'
import { Carregando } from '../components/Comuns'
import { useAuth } from './AuthContext'

/**
 * Protege uma rota da SPA: sem sessão, redireciona para /login guardando a URL pretendida.
 * É só experiência do usuário — a API valida tudo de novo e responde 401/403.
 */
export function RotaProtegida({ children, exigeCriar = false }: { children: ReactNode; exigeCriar?: boolean }) {
  const { carregando, sessao } = useAuth()
  const local = useLocation()
  if (carregando) return <Carregando texto="Verificando sessão…" />
  if (!sessao.autenticado) return <Navigate to="/login" replace state={{ de: local.pathname + local.search }} />
  if (exigeCriar && !sessao.podeCriar)
    return <div className="estado erro" role="alert"><strong>Sem permissão</strong><p>Seu perfil não pode cadastrar tarefas.</p></div>
  return children
}
