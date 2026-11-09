import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { chamar, registrarAoNaoAutenticado, type ErroApi, type SessaoDto } from '../api/http'

const anonima: SessaoDto = { autenticado: false, usuario: undefined, equipe: undefined, papeis: [], podeCriar: false, podeExcluir: false, podeEditarOutrasEquipes: false }

export interface Auth {
  carregando: boolean
  sessao: SessaoDto
  entrar(email: string, senha: string): Promise<ErroApi | null>
  sair(): Promise<void>
  /** Reflexo, na interface, da mesma regra aplicada pelo servidor (que continua sendo a autoridade). */
  podeEditar(equipeDaTarefa: string): boolean
}

export const AuthContext = createContext<Auth | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [sessao, setSessao] = useState<SessaoDto>(anonima)
  const [carregando, setCarregando] = useState(true)

  // Restaura a sessão ao abrir/recarregar a SPA: o cookie HttpOnly não é legível, então pergunta à API.
  const restaurar = useCallback(async () => {
    const r = await chamar((c, s) => c.obterSessao(s))
    setSessao(r.ok ? r.dados : anonima)
    setCarregando(false)
  }, [])

  useEffect(() => { restaurar() }, [restaurar])
  useEffect(() => {
    registrarAoNaoAutenticado(() => setSessao(anonima))   // 401 em qualquer chamada => sessão encerrada
    return () => registrarAoNaoAutenticado(undefined)
  }, [])

  const valor = useMemo<Auth>(() => ({
    carregando,
    sessao,
    async entrar(email, senha) {
      const r = await chamar((c, s) => c.entrar({ email, senha }, s))
      if (!r.ok) return r.erro
      setSessao(r.dados)
      return null
    },
    async sair() {
      await chamar((c, s) => c.sair(s))
      setSessao(anonima)
      await restaurar()   // obtém novo token antiforgery anônimo
    },
    podeEditar(equipe) {
      return sessao.podeCriar && (sessao.equipe === equipe || sessao.podeEditarOutrasEquipes)
    },
  }), [carregando, sessao, restaurar])

  return <AuthContext.Provider value={valor}>{children}</AuthContext.Provider>
}

export function useAuth(): Auth {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth deve ser usado dentro de <AuthProvider>')
  return ctx
}
