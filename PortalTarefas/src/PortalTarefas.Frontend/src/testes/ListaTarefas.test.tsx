import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ListaTarefas } from '../pages/ListaTarefas'
import { DetalhesTarefa } from '../pages/DetalhesTarefa'
import { AuthContext, type Auth } from '../auth/AuthContext'

export const authFalsa = (parcial: Partial<Auth['sessao']> = {}): Auth => {
  const sessao = { autenticado: true, usuario: 'ana@portal.local', equipe: 'Backend', papeis: ['Usuario'], podeCriar: true, podeExcluir: false, podeEditarOutrasEquipes: false, ...parcial }
  return { carregando: false, sessao, entrar: async () => null, sair: async () => {},
    podeEditar: (eq) => sessao.podeCriar && (sessao.equipe === eq || sessao.podeEditarOutrasEquipes) }
}

const pagina = (itens: object[], total = itens.length) =>
  new Response(JSON.stringify({ itens, pagina: 1, tamanho: 5, totalItens: total, totalPaginas: Math.ceil(total / 5) }),
    { status: 200, headers: { 'Content-Type': 'application/json' } })
const tarefa = { id: 1, titulo: 'Escrever relatório', situacao: 'Pendente', prioridade: 'Alta', prazo: '2026-11-20', vencida: false, equipe: 'Backend' }

function UrlAtual() { const l = useLocation(); return <output data-testid="url">{l.pathname + l.search}</output> }
function montar(url: string, auth: Auth = authFalsa()) {
  return render(
    <AuthContext.Provider value={auth}>
    <MemoryRouter initialEntries={[url]}>
      <Routes>
        <Route path="/tarefas" element={<ListaTarefas />} />
        <Route path="/tarefas/:id" element={<DetalhesTarefa />} />
      </Routes>
      <UrlAtual />
    </MemoryRouter>
    </AuthContext.Provider>)
}
afterEach(() => vi.restoreAllMocks())

describe('ListaTarefas', () => {
  it('passa de carregando para sucesso', async () => {
    let liberar!: (r: Response) => void
    vi.spyOn(globalThis, 'fetch').mockReturnValue(new Promise((ok) => { liberar = ok }))
    montar('/tarefas')
    expect(await screen.findByText('Carregando tarefas…')).toBeInTheDocument()
    liberar(pagina([tarefa]))
    expect(await screen.findByText('#1 · Escrever relatório')).toBeInTheDocument()
  })

  it('mostra estado vazio', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(pagina([]))
    montar('/tarefas')
    expect(await screen.findByText('Nenhuma tarefa encontrada com esses filtros.')).toBeInTheDocument()
  })

  it('mostra erro de rede com opção de tentar novamente', async () => {
    const f = vi.spyOn(globalThis, 'fetch').mockRejectedValueOnce(new TypeError('Failed to fetch')).mockResolvedValue(pagina([tarefa]))
    montar('/tarefas')
    expect(await screen.findByRole('alert')).toHaveTextContent('Falha de conexão')
    await userEvent.click(screen.getByRole('button', { name: 'Tentar novamente' }))
    expect(await screen.findByText('#1 · Escrever relatório')).toBeInTheDocument()
    expect(f).toHaveBeenCalledTimes(2)
  })

  it('mostra Problem Details de resposta HTTP de erro', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      status: 400, title: 'Parâmetros de consulta inválidos.', errors: { ordenarPor: ['Campo de ordenação não permitido.'] },
    }), { status: 400, headers: { 'Content-Type': 'application/problem+json' } }))
    montar('/tarefas?ordenarPor=versao')
    expect(await screen.findByRole('alert')).toHaveTextContent('Campo de ordenação não permitido.')
  })

  it('lê filtros e página da URL e preserva-os nas chamadas', async () => {
    const f = vi.spyOn(globalThis, 'fetch').mockResolvedValue(pagina([tarefa], 12))
    montar('/tarefas?situacao=Pendente&prioridade=Alta&pagina=2')
    await screen.findByText('#1 · Escrever relatório')
    const url = String(f.mock.calls[0][0])
    expect(url).toContain('pagina=2'); expect(url).toContain('situacao=Pendente'); expect(url).toContain('prioridade=Alta')
    await userEvent.click(screen.getByRole('button', { name: 'Próxima' }))
    await waitFor(() => expect(screen.getByTestId('url')).toHaveTextContent('/tarefas?situacao=Pendente&prioridade=Alta&pagina=3'))
  })

  it('navega da lista para os detalhes pela URL', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(pagina([tarefa]))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...tarefa, descricao: 'detalhe', criadaEm: '2026-11-09T10:00:00Z', versao: 'AA==' }), { status: 200 }))
    montar('/tarefas')
    await userEvent.click(await screen.findByRole('link', { name: '#1 · Escrever relatório' }))
    expect(screen.getByTestId('url')).toHaveTextContent('/tarefas/1')
    expect(await screen.findByText('detalhe')).toBeInTheDocument()
  })

  it('descarta resposta obsoleta quando o filtro muda (AbortController)', async () => {
    const sinais: AbortSignal[] = []
    vi.spyOn(globalThis, 'fetch').mockImplementation((_u, init) => { sinais.push(init!.signal!); return new Promise(() => {}) })
    montar('/tarefas')
    await waitFor(() => expect(sinais.length).toBeGreaterThan(0))
    await userEvent.selectOptions(screen.getByLabelText('Situação'), 'Concluida')
    await waitFor(() => expect(sinais.at(-2)?.aborted).toBe(true))
  })
})

describe('DetalhesTarefa', () => {
  it('trata 404 da API', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({ status: 404, title: 'Tarefa não encontrada.' }), { status: 404 }))
    montar('/tarefas/999')
    expect(await screen.findByRole('alert')).toHaveTextContent('Não existe tarefa com o identificador 999')
  })

  it('reverte a atualização otimista quando a API recusa (409)', async () => {
    const det = { ...tarefa, descricao: null, criadaEm: '2026-11-09T10:00:00Z', versao: 'AA==' }
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify(det), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ status: 409, title: 'A tarefa foi alterada por outra requisição.' }), { status: 409 }))
    montar('/tarefas/1')
    await userEvent.click(await screen.findByRole('button', { name: 'Marcar como concluída' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('alterada por outra requisição')
    expect(screen.getByText('Pendente')).toBeInTheDocument()   // voltou ao estado anterior
  })

  it('oculta ações não permitidas (outra equipe, sem papel de administrador)', async () => {
    const det = { ...tarefa, equipe: 'Frontend', descricao: null, criadaEm: '2026-11-09T10:00:00Z', versao: 'AA==' }
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify(det), { status: 200 }))
    montar('/tarefas/1')
    await screen.findByText('#1 · Escrever relatório')
    expect(screen.queryByRole('button', { name: 'Marcar como concluída' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Excluir' })).not.toBeInTheDocument()
  })

  it('mostra as ações para administrador com claim de outras equipes', async () => {
    const det = { ...tarefa, equipe: 'Frontend', descricao: null, criadaEm: '2026-11-09T10:00:00Z', versao: 'AA==' }
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify(det), { status: 200 }))
    montar('/tarefas/1', authFalsa({ papeis: ['Administrador'], podeExcluir: true, podeEditarOutrasEquipes: true }))
    expect(await screen.findByRole('button', { name: 'Marcar como concluída' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Excluir' })).toBeInTheDocument()
  })
})

describe('Estado inicial', () => {
  it('renderiza "idle" antes de qualquer efeito (renderização sem efeitos)', async () => {
    const { renderToString } = await import('react-dom/server')
    const html = renderToString(<MemoryRouter initialEntries={['/tarefas']}><ListaTarefas /></MemoryRouter>)
    expect(html).toContain('Pronto para consultar.')
    expect(html).not.toContain('Carregando')
  })
})
