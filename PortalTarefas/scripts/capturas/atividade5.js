// Capturas da SPA React (Vite dev em http://localhost:5173, proxy -> API).
const { chromium } = require('playwright');
const dir = process.argv[2]; const S = 'http://localhost:5173';
(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ locale: 'pt-BR', viewport: { width: 1000, height: 700 } });
  const foto = (n) => page.screenshot({ path: `${dir}/${n}.png`, fullPage: true });
  const log = [];

  await page.goto(`${S}/tarefas?simular=atraso`); await page.waitForSelector('text=Carregando tarefas…'); await foto('01-carregando');
  await page.goto(`${S}/tarefas`); await page.waitForSelector('.lista'); await foto('02-sucesso-lista');
  await page.goto(`${S}/tarefas?busca=inexistente`); await page.waitForSelector('text=Nenhuma tarefa encontrada'); await foto('03-vazio');
  await page.goto(`${S}/tarefas?simular=falha`); await page.waitForSelector('[role=alert]'); await foto('04-erro-rede');
  await page.goto(`${S}/tarefas?ordenarPor=versao`); await page.waitForSelector('[role=alert]'); await foto('05-erro-http-problem-details');

  // Filtros + paginação preservados na URL
  await page.goto(`${S}/tarefas`); await page.waitForSelector('.lista');
  await page.selectOption('select[aria-label="Situação"]', 'Pendente');
  await page.selectOption('select[aria-label="Ordenar por"]', 'prazo');
  await page.waitForSelector('.lista');
  await page.goto(`${S}/tarefas?ordenarPor=titulo`); await page.waitForSelector('.lista');
  await page.click('text=Próxima'); await page.waitForURL(/pagina=2/); await page.waitForSelector('.lista');
  log.push('URL após filtros + próxima página: ' + page.url());
  await foto('06-pagina2-url'); 
  await page.reload(); await page.waitForSelector('.lista');
  log.push('Após recarregar a URL, página exibida: ' + await page.textContent('.paginacao span'));

  // Lista -> detalhes por link e por URL direta
  await page.goto(`${S}/tarefas`); await page.waitForSelector('.lista');
  await page.click('.lista li:nth-child(3) a'); await page.waitForSelector('article.cartao');
  log.push('Navegação por link levou a: ' + page.url()); await foto('07-detalhes');
  await page.goto(`${S}/tarefas/999`); await page.waitForSelector('[role=alert]'); await foto('08-detalhes-404');

  // Cadastro: erros de validação devolvidos pela API
  await page.goto(`${S}/tarefas/nova`);
  await page.evaluate(() => { window.__semRecarga = 'marcador'; });
  await page.fill('input[name=titulo]', 'ab'); await page.selectOption('select[name=prioridade]', 'Alta');
  await page.click('button[type=submit]'); await page.waitForSelector('.erro-campo'); await foto('09-cadastro-erros-api');
  await page.fill('input[name=titulo]', 'Tarefa criada pela SPA'); await page.selectOption('select[name=prioridade]', 'Media');
  await page.click('button[type=submit]'); await page.waitForSelector('text=cadastrada'); await foto('10-cadastro-valido');
  log.push('Marcador na janela após cadastrar (sem recarregar a página): ' + await page.evaluate(() => window.__semRecarga));

  // Atualização otimista
  await page.click('text=Marcar como concluída'); await page.waitForSelector('text=Concluída'); await foto('11-concluida-otimista');

  // Ida e volta no histórico
  await page.goBack(); log.push('Voltar no histórico: ' + page.url());
  require('fs').writeFileSync(`${dir}/12-navegacao.txt`, log.join('\n') + '\n');
  await browser.close();
})();
