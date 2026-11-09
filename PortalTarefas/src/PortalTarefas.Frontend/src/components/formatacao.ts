export const formatarData = (iso?: string) => (iso ? iso.split('-').reverse().join('/') : '—')
export const rotuloSituacao: Record<string, string> = { Pendente: 'Pendente', EmAndamento: 'Em andamento', Concluida: 'Concluída' }
