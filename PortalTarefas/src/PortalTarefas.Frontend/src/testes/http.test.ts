import { describe, expect, it } from 'vitest'
import { normalizarErro } from '../api/http'
import { ApiException } from '../api/gerado/portal-tarefas-client'

describe('normalizarErro', () => {
  it('trata falha de rede', () => {
    expect(normalizarErro(new TypeError('Failed to fetch')).tipo).toBe('rede')
  })
  it('trata cancelamento', () => {
    expect(normalizarErro(new DOMException('x', 'AbortError'))).toEqual({ tipo: 'cancelado' })
  })
  it('trata Problem Details 404 lançado pelo cliente gerado', () => {
    expect(normalizarErro({ status: 404, title: 'Tarefa não encontrada.', correlationId: 'abc' }))
      .toMatchObject({ tipo: 'http', status: 404, titulo: 'Tarefa não encontrada.', correlationId: 'abc' })
  })
  it('trata Problem Details de validação 400', () => {
    const e = normalizarErro({ status: 400, title: 'Inválido', errors: { Titulo: ['obrigatório'] } })
    expect(e).toMatchObject({ tipo: 'validacao', erros: { Titulo: ['obrigatório'] } })
  })
  it('lê Problem Details no corpo de status não documentado', () => {
    const corpo = JSON.stringify({ status: 500, title: 'Erro interno', correlationId: 'xyz' })
    expect(normalizarErro(new ApiException('erro', 500, corpo, {}, null)))
      .toMatchObject({ tipo: 'http', status: 500, titulo: 'Erro interno', correlationId: 'xyz' })
  })
  it('status não documentado sem corpo JSON', () => {
    expect(normalizarErro(new ApiException('erro', 502, '<html>', {}, null))).toMatchObject({ tipo: 'http', status: 502 })
  })
})
