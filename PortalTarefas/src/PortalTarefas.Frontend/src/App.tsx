import { NavLink, Navigate, Route, Routes } from 'react-router'
import { TituloPagina } from './components/Comuns'
import { DetalhesTarefa } from './pages/DetalhesTarefa'
import { ListaTarefas } from './pages/ListaTarefas'
import { NovaTarefa } from './pages/NovaTarefa'

export default function App() {
  return (
    <>
      <a className="pular" href="#conteudo">Pular para o conteúdo</a>
      <header className="topo">
        <span className="marca">Portal de Tarefas</span>
        <nav aria-label="Principal">
          <NavLink to="/tarefas" end>Lista</NavLink>
          <NavLink to="/tarefas/nova">Nova tarefa</NavLink>
        </nav>
      </header>
      <main id="conteudo">
        <Routes>
          <Route path="/" element={<Navigate to="/tarefas" replace />} />
          <Route path="/tarefas" element={<ListaTarefas />} />
          <Route path="/tarefas/nova" element={<NovaTarefa />} />
          <Route path="/tarefas/:id" element={<DetalhesTarefa />} />
          <Route path="*" element={<><TituloPagina>Página não encontrada</TituloPagina><p>Verifique o endereço.</p></>} />
        </Routes>
      </main>
      <footer>SPA React · consome a API v2 via cliente gerado (NSwag)</footer>
    </>
  )
}
