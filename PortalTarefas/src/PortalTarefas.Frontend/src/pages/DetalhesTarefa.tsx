import { useState } from 'react'
import { Link, useLocation, useParams } from 'react-router'
import { chamar, type ErroApi, type TarefaDetalhesDto } from '../api/http'
import { Carregando, MensagemErro, Selo, TituloPagina, formatarData, rotuloSituacao } from '../components/Comuns'
import { useRequisicao } from '../hooks/useRequisicao'

export function DetalhesTarefa() {
  const { id } = useParams()
  const idNumerico = Number(id)
  const mensagem = (useLocation().state as { mensagem?: string } | null)?.mensagem
  const { estado, setEstado, recarregar } = useRequisicao<TarefaDetalhesDto>((c, s) => c.obterTarefa(idNumerico, s), [idNumerico])
  const [erroAcao, setErroAcao] = useState<ErroApi | null>(null)

  if (!Number.isInteger(idNumerico) || idNumerico <= 0)
    return <><TituloPagina>Endereço inválido</TituloPagina><p>O identificador “{id}” não é válido.</p></>

  /** Atualização otimista: a tela muda na hora; se a API recusar, o estado anterior é restaurado. */
  async function concluir(tarefa: TarefaDetalhesDto) {
    setErroAcao(null)
    setEstado({ fase: 'sucesso', dados: { ...tarefa, situacao: 'Concluida' } })
    const r = await chamar((c, s) => c.atualizarTarefa(tarefa.id, {
      titulo: tarefa.titulo, descricao: tarefa.descricao, situacao: 'Concluida',
      prioridade: tarefa.prioridade, prazo: tarefa.prazo, versao: tarefa.versao,
    }, s))
    if (r.ok) setEstado({ fase: 'sucesso', dados: r.dados })
    else { setEstado({ fase: 'sucesso', dados: tarefa }); setErroAcao(r.erro) }   // reversão
  }

  return (
    <>
      <TituloPagina>Detalhes da tarefa</TituloPagina>
      {mensagem && <p className="aviso" role="status">{mensagem}</p>}
      {(estado.fase === 'idle' || estado.fase === 'carregando') && <Carregando texto="Carregando tarefa…" />}
      {estado.fase === 'erro' && estado.erro.tipo === 'http' && estado.erro.status === 404 && (
        <div className="estado erro" role="alert"><strong>Tarefa não encontrada</strong><p>Não existe tarefa com o identificador {id}.</p></div>
      )}
      {estado.fase === 'erro' && !(estado.erro.tipo === 'http' && estado.erro.status === 404) && (
        <MensagemErro erro={estado.erro} onTentarNovamente={recarregar} />
      )}
      {estado.fase === 'sucesso' && (
        <article className="cartao">
          <h2>#{estado.dados.id} · {estado.dados.titulo}</h2>
          <p>
            <Selo classe={`situacao-${estado.dados.situacao}`}>{rotuloSituacao[estado.dados.situacao]}</Selo>
            <Selo classe={`prioridade-${estado.dados.prioridade}`}>Prioridade {estado.dados.prioridade}</Selo>
          </p>
          <dl>
            <dt>Descrição</dt><dd>{estado.dados.descricao ?? '(sem descrição)'}</dd>
            <dt>Prazo</dt><dd>{formatarData(estado.dados.prazo)}</dd>
            <dt>Criada em</dt><dd>{new Date(estado.dados.criadaEm).toLocaleString('pt-BR')}</dd>
          </dl>
          {estado.dados.situacao !== 'Concluida' && (
            <button type="button" onClick={() => concluir(estado.dados)}>Marcar como concluída</button>
          )}
          {erroAcao && <MensagemErro erro={erroAcao} />}
        </article>
      )}
      <p><Link to="/tarefas">← Voltar à lista</Link></p>
    </>
  )
}
