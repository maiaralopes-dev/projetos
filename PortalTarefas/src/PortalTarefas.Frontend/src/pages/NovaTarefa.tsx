import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { chamar, type CriarTarefaDto, type ErroApi, type PrioridadeTarefa } from '../api/http'
import { MensagemErro, TituloPagina } from '../components/Comuns'

/** Formulário de cadastro. Erros de validação vêm da API (Problem Details 400) e são exibidos por campo. */
export function NovaTarefa() {
  const navegar = useNavigate()
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState<ErroApi | null>(null)

  const errosCampo = erro?.tipo === 'validacao'
    ? Object.fromEntries(Object.entries(erro.erros).map(([k, v]) => [k.toLowerCase(), v.join(' ')]))
    : {}

  async function enviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const f = new FormData(e.currentTarget)
    const dto: CriarTarefaDto = {
      titulo: String(f.get('titulo') ?? ''),
      descricao: String(f.get('descricao') ?? '') || undefined,
      prioridade: f.get('prioridade') as PrioridadeTarefa,
      prazo: String(f.get('prazo') ?? '') || undefined,
    }
    setEnviando(true); setErro(null)
    const r = await chamar((c, s) => c.criarTarefa(dto, s))
    setEnviando(false)
    if (r.ok) navegar(`/tarefas/${r.dados.id}`, { state: { mensagem: `Tarefa “${r.dados.titulo}” cadastrada.` } })
    else setErro(r.erro)
  }

  const campo = (nome: string) => ({ 'aria-invalid': !!errosCampo[nome], 'aria-describedby': errosCampo[nome] ? `erro-${nome}` : undefined })
  const msg = (nome: string) => errosCampo[nome] && <span id={`erro-${nome}`} className="erro-campo">{errosCampo[nome]}</span>

  return (
    <>
      <TituloPagina>Nova tarefa</TituloPagina>
      <form onSubmit={enviar} noValidate className="formulario">
        <label>Título<input name="titulo" {...campo('titulo')} /></label>{msg('titulo')}
        <label>Descrição<textarea name="descricao" rows={3} {...campo('descricao')} /></label>{msg('descricao')}
        <div className="linha">
          <label>Prioridade
            <select name="prioridade" defaultValue="Media"><option value="Baixa">Baixa</option><option value="Media">Média</option><option value="Alta">Alta</option></select>
          </label>
          <label>Prazo<input name="prazo" type="date" {...campo('prazo')} /></label>
        </div>
        {msg('prazo')}
        {erro && erro.tipo !== 'validacao' && <MensagemErro erro={erro} />}
        {erro?.tipo === 'validacao' && <p className="erro-campo" role="alert">Corrija os campos destacados.</p>}
        <button type="submit" disabled={enviando}>{enviando ? 'Salvando…' : 'Cadastrar'}</button>
      </form>
    </>
  )
}
