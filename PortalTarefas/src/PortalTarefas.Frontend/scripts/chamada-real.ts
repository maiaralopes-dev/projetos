// Chamada real à API usando somente o cliente gerado pelo NSwag (sem DTOs escritos à mão).
// Uso: API_URL=https://localhost:7080 npx tsx scripts/chamada-real.ts
import { PortalTarefasClient, ApiException, type ProblemDetails } from '../src/api/gerado/portal-tarefas-client';

const cliente = new PortalTarefasClient(process.env.API_URL ?? 'https://localhost:7080', { fetch: globalThis.fetch.bind(globalThis) });

const pagina = await cliente.listarTarefas(1, 3, 'prazo', 'asc', 'Pendente');
console.log(`Página ${pagina.pagina}/${pagina.totalPaginas} — ${pagina.totalItens} tarefas pendentes`);
for (const t of pagina.itens) console.log(`  #${t.id} ${t.titulo} (prazo ${t.prazo ?? '—'})`);

const criada = await cliente.criarTarefa({ titulo: `Criada pelo cliente TS ${Date.now()}`, prioridade: 'Baixa' });
console.log('Criada:', criada.id, criada.titulo, 'versão', criada.versao);

try {
  await cliente.obterTarefa(999999);
} catch (e) {
  // Para respostas documentadas (404 → ProblemDetails), o cliente gerado lança o próprio objeto tipado.
  if (e instanceof ApiException) console.log('Erro não documentado:', e.status);
  else { const p = e as ProblemDetails; console.log('Erro esperado (Problem Details):', p.status, p.title); }
}
