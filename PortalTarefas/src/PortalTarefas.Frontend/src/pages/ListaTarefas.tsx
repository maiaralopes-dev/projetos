import { Link, useSearchParams } from 'react-router'
import type { PaginaTarefaResumo, PrioridadeTarefa, SituacaoTarefa } from '../api/http'
import { Carregando, MensagemErro, Selo, TituloPagina, formatarData, rotuloSituacao } from '../components/Comuns'
import { useRequisicao } from '../hooks/useRequisicao'

const TAMANHO = 5

/** Lista com filtros, ordenação e paginação sincronizados com a URL (?situacao=&pagina=...). */
export function ListaTarefas() {
  const [params, setParams] = useSearchParams()
  const pagina = Number(params.get('pagina') ?? '1')
  const situacao = (params.get('situacao') || undefined) as SituacaoTarefa | undefined
  const prioridade = (params.get('prioridade') || undefined) as PrioridadeTarefa | undefined
  const busca = params.get('busca') || undefined
  const ordenarPor = params.get('ordenarPor') || undefined
  const direcao = params.get('direcao') || undefined

  const { estado, recarregar } = useRequisicao<PaginaTarefaResumo>(
    (c, signal) => c.listarTarefas(pagina, TAMANHO, ordenarPor, direcao, situacao, prioridade, busca, undefined, signal),
    [pagina, situacao, prioridade, busca, ordenarPor, direcao],
    (p) => p.itens.length === 0,
  )

  /** Altera um filtro na URL; qualquer filtro novo volta para a página 1. */
  const definir = (chave: string, valor: string) => {
    const novos = new URLSearchParams(params)
    if (valor) novos.set(chave, valor); else novos.delete(chave)
    if (chave !== 'pagina') novos.delete('pagina')
    setParams(novos)
  }

  return (
    <>
      <TituloPagina>Tarefas</TituloPagina>
      <form className="filtros" role="search" onSubmit={(e) => { e.preventDefault(); definir('busca', new FormData(e.currentTarget).get('busca') as string) }}>
        <input name="busca" defaultValue={busca} key={busca} placeholder="Buscar no título" aria-label="Buscar no título" />
        <button type="submit">Buscar</button>
        <select aria-label="Situação" value={situacao ?? ''} onChange={(e) => definir('situacao', e.target.value)}>
          <option value="">Todas as situações</option>
          <option value="Pendente">Pendente</option><option value="EmAndamento">Em andamento</option><option value="Concluida">Concluída</option>
        </select>
        <select aria-label="Prioridade" value={prioridade ?? ''} onChange={(e) => definir('prioridade', e.target.value)}>
          <option value="">Todas as prioridades</option>
          <option value="Baixa">Baixa</option><option value="Media">Média</option><option value="Alta">Alta</option>
        </select>
        <select aria-label="Ordenar por" value={ordenarPor ?? ''} onChange={(e) => definir('ordenarPor', e.target.value)}>
          <option value="">Ordenar por id</option><option value="titulo">Título</option><option value="prazo">Prazo</option><option value="prioridade">Prioridade</option>
        </select>
        <select aria-label="Direção" value={direcao ?? ''} onChange={(e) => definir('direcao', e.target.value)}>
          <option value="">Crescente</option><option value="desc">Decrescente</option>
        </select>
      </form>

      {estado.fase === 'idle' && <p className="estado" role="status">Pronto para consultar.</p>}
      {estado.fase === 'carregando' && <Carregando texto="Carregando tarefas…" />}
      {estado.fase === 'erro' && <MensagemErro erro={estado.erro} onTentarNovamente={recarregar} />}
      {estado.fase === 'vazio' && (
        <p className="estado vazio" role="status">Nenhuma tarefa encontrada com esses filtros.</p>
      )}
      {estado.fase === 'sucesso' && (
        <>
          <p className="resumo" role="status">{estado.dados.totalItens} tarefa(s) · página {estado.dados.pagina} de {estado.dados.totalPaginas}</p>
          <ul className="lista">
            {estado.dados.itens.map((t) => (
              <li key={t.id} className={`cartao ${t.vencida ? 'vencida' : ''}`}>
                <h3><Link to={`/tarefas/${t.id}`}>#{t.id} · {t.titulo}</Link></h3>
                <Selo classe={`situacao-${t.situacao}`}>{rotuloSituacao[t.situacao]}</Selo>
                <Selo classe={`prioridade-${t.prioridade}`}>Prioridade {t.prioridade}</Selo>
                <span className="prazo">Prazo: {formatarData(t.prazo)}</span>
                {t.vencida && <strong className="alerta">Vencida</strong>}
              </li>
            ))}
          </ul>
          <nav className="paginacao" aria-label="Paginação">
            <button type="button" disabled={pagina <= 1} onClick={() => definir('pagina', String(pagina - 1))}>Anterior</button>
            <span>Página {pagina}</span>
            <button type="button" disabled={pagina >= estado.dados.totalPaginas} onClick={() => definir('pagina', String(pagina + 1))}>Próxima</button>
          </nav>
        </>
      )}
    </>
  )
}
