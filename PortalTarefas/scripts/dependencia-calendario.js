// Serviço de calendário FALSO (dependência externa simulada) para demonstrar health checks e resiliência.
// GET /health                -> 200 (ou 503 no modo "fora")
// GET /feriados/:ano         -> lista de feriados, conforme o modo
// POST /controle?modo=...    -> ok | falha503 | falha400 | lento | fora
// GET /estatisticas          -> total de requisições recebidas em /feriados
const http = require('http');
const porta = Number(process.env.PORTA || 5099);
let modo = 'ok', recebidas = 0;
const feriados = (ano) => [
  { data: `${ano}-01-01`, nome: 'Confraternização Universal' }, { data: `${ano}-04-21`, nome: 'Tiradentes' },
  { data: `${ano}-05-01`, nome: 'Dia do Trabalho' }, { data: `${ano}-09-07`, nome: 'Independência do Brasil' },
  { data: `${ano}-10-12`, nome: 'Nossa Senhora Aparecida' }, { data: `${ano}-11-02`, nome: 'Finados' },
  { data: `${ano}-11-15`, nome: 'Proclamação da República' }, { data: `${ano}-11-20`, nome: 'Dia Nacional de Zumbi e da Consciência Negra' },
  { data: `${ano}-12-25`, nome: 'Natal' } ];
http.createServer((req, res) => {
  const url = new URL(req.url, 'http://x');
  const json = (status, corpo) => { res.writeHead(status, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(corpo)); };
  if (url.pathname === '/controle') { modo = url.searchParams.get('modo') || 'ok'; recebidas = 0; console.log(`[calendario] modo=${modo}`); return json(200, { modo }); }
  if (url.pathname === '/estatisticas') return json(200, { modo, recebidas });
  if (url.pathname === '/health') return modo === 'fora' ? json(503, { status: 'fora' }) : json(200, { status: 'ok' });
  const m = url.pathname.match(/^\/feriados\/(\d{4})$/);
  if (!m) return json(404, {});
  recebidas++;
  console.log(`[calendario] #${recebidas} ${req.method} ${url.pathname} modo=${modo} correlation=${req.headers['x-correlation-id'] || '-'} traceparent=${req.headers['traceparent'] || '-'}`);
  if (modo === 'falha503') return json(503, { erro: 'indisponível' });
  if (modo === 'falha400') return json(400, { erro: 'requisição inválida' });
  if (modo === 'fora') return req.socket.destroy();          // conexão derrubada
  if (modo === 'lento') return setTimeout(() => json(200, feriados(m[1])), 5000);
  json(200, feriados(m[1]));
}).listen(porta, () => console.log(`[calendario] ouvindo em http://localhost:${porta}`));
