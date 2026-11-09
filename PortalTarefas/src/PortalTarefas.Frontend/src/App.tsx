import { NavLink, Navigate, Route, Routes } from 'react-router'
import { RotaProtegida } from './auth/RotaProtegida'
import { useAuth } from './auth/AuthContext'
import { TituloPagina } from './components/Comuns'
import { Login } from './pages/Login'
import { DetalhesTarefa } from './pages/DetalhesTarefa'
import { ListaTarefas } from './pages/ListaTarefas'
import { NovaTarefa } from './pages/NovaTarefa'

export default function App() {
  const { sessao, sair } = useAuth()
  return (
    <>
      <a className="pular" href="#conteudo">Pular para o conteúdo</a>
      <header className="topo">
        <span className="marca">Portal de Tarefas</span>
        <nav aria-label="Principal">
          {sessao.autenticado ? (
            <>
              <NavLink to="/tarefas" end>Lista</NavLink>
              {sessao.podeCriar && <NavLink to="/tarefas/nova">Nova tarefa</NavLink>}
              <span className="usuario">{sessao.usuario} · {sessao.equipe}{sessao.papeis.length ? ` · ${sessao.papeis.join(', ')}` : ''}</span>
              <button type="button" className="link" onClick={sair}>Sair</button>
            </>
          ) : <NavLink to="/login">Entrar</NavLink>}
        </nav>
      </header>
      <main id="conteudo">
        <Routes>
          <Route path="/" element={<Navigate to="/tarefas" replace />} />
          <Route path="/login" element={<Login />} />
          <Route path="/tarefas" element={<RotaProtegida><ListaTarefas /></RotaProtegida>} />
          <Route path="/tarefas/nova" element={<RotaProtegida exigeCriar><NovaTarefa /></RotaProtegida>} />
          <Route path="/tarefas/:id" element={<RotaProtegida><DetalhesTarefa /></RotaProtegida>} />
          <Route path="*" element={<><TituloPagina>Página não encontrada</TituloPagina><p>Verifique o endereço.</p></>} />
        </Routes>
      </main>
      <footer>SPA React · consome a API v2 via cliente gerado (NSwag)</footer>
    </>
  )
}
