import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AuthContext, AuthProvider, type Auth } from '../auth/AuthContext'
import { RotaProtegida } from '../auth/RotaProtegida'
import { Login } from '../pages/Login'

function UrlAtual() { const l = useLocation(); return <output data-testid="url">{l.pathname + l.search}</output> }
const json = (corpo: object, status = 200) => new Response(JSON.stringify(corpo), { status, headers: { 'Content-Type': 'application/json' } })
const anonima = { autenticado: false, papeis: [], podeCriar: false, podeExcluir: false, podeEditarOutrasEquipes: false }
afterEach(() => vi.restoreAllMocks())

describe('Rotas protegidas', () => {
  it('redireciona para /login e volta à URL pretendida após entrar', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(json(anonima))   // restauração da sessão: anônima
      .mockResolvedValueOnce(json({ autenticado: true, usuario: 'ana@portal.local', equipe: 'Backend', papeis: ['Usuario'], podeCriar: true, podeExcluir: false, podeEditarOutrasEquipes: false }))
    render(
      <AuthProvider>
        <MemoryRouter initialEntries={['/tarefas?situacao=Pendente']}>
          <Routes>
            <Route path="/login" element={<Login />} />
            <Route path="/tarefas" element={<RotaProtegida><p>Lista protegida</p></RotaProtegida>} />
          </Routes>
          <UrlAtual />
        </MemoryRouter>
      </AuthProvider>)
    await waitFor(() => expect(screen.getByTestId('url')).toHaveTextContent('/login'))
    await userEvent.type(screen.getByLabelText('E-mail'), 'ana@portal.local')
    await userEvent.type(screen.getByLabelText('Senha'), 'qualquer')
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))
    expect(await screen.findByText('Lista protegida')).toBeInTheDocument()
    expect(screen.getByTestId('url')).toHaveTextContent('/tarefas?situacao=Pendente')
  })

  it('exibe erro genérico de login sem revelar se a conta existe', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(json(anonima))
      .mockResolvedValueOnce(json({ status: 401, title: 'Não foi possível entrar.', detail: 'E-mail ou senha inválidos, ou acesso temporariamente bloqueado.' }, 401))
    render(<AuthProvider><MemoryRouter initialEntries={['/login']}><Routes><Route path="/login" element={<Login />} /></Routes></MemoryRouter></AuthProvider>)
    await userEvent.type(await screen.findByLabelText('E-mail'), 'x@y.z')
    await userEvent.type(screen.getByLabelText('Senha'), 'errada')
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('E-mail ou senha inválidos')
  })

  it('bloqueia cadastro para perfil sem papel', () => {
    const auth: Auth = { carregando: false, sessao: { ...anonima, autenticado: true, usuario: 'visitante', equipe: 'Geral' }, entrar: async () => null, sair: async () => {}, podeEditar: () => false }
    render(<AuthContext.Provider value={auth}><MemoryRouter><RotaProtegida exigeCriar><p>Formulário</p></RotaProtegida></MemoryRouter></AuthContext.Provider>)
    expect(screen.getByRole('alert')).toHaveTextContent('Sem permissão')
    expect(screen.queryByText('Formulário')).not.toBeInTheDocument()
  })

  it('envia o cabeçalho X-XSRF-TOKEN em requisições que alteram estado', async () => {
    document.cookie = 'XSRF-TOKEN=token%2Fde%2Bteste'
    const f = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 204 }))
    const { chamar } = await import('../api/http')
    await chamar((c, s) => c.sair(s))
    expect(new Headers(f.mock.calls[0][1]!.headers).get('X-XSRF-TOKEN')).toBe('token/de+teste')
  })
})
