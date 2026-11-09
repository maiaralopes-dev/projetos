// SPA: rota protegida -> login -> retorno à URL; permissões refletidas na interface; logout.
const { chromium } = require('playwright');
const dir = process.argv[2]; const S = 'http://localhost:5173'; const senha = process.env.SEED_SENHA;
(async () => {
  const browser = await chromium.launch();
  const log = [];
  async function sessao(nome) {
    const page = await (await browser.newContext({ locale: 'pt-BR', viewport: { width: 1000, height: 650 } })).newPage();
    page.foto = (n) => page.screenshot({ path: `${dir}/${n}.png`, fullPage: true });
    return page;
  }
  const ana = await sessao();
  await ana.goto(`${S}/tarefas/8`);
  await ana.waitForURL(/\/login/); log.push('Acesso anônimo a /tarefas/8 redirecionou para: ' + ana.url());
  await ana.foto('06-redirecionado-login');
  await ana.fill('input[name=email]', 'ana@portal.local'); await ana.fill('input[name=senha]', 'senha-errada-1!');
  await ana.click('button[type=submit]'); await ana.waitForSelector('[role=alert]'); await ana.foto('07-login-invalido');
  await ana.fill('input[name=senha]', senha); await ana.click('button[type=submit]');
  await ana.waitForSelector('article.cartao'); log.push('Após login, voltou para: ' + ana.url());
  await ana.foto('08-ana-tarefa-outra-equipe-sem-acoes');
  await ana.goto(`${S}/tarefas/5`); await ana.waitForSelector('article.cartao'); await ana.foto('09-ana-tarefa-mesma-equipe');
  await ana.reload(); await ana.waitForSelector('article.cartao'); log.push('Após recarregar (F5), sessão restaurada: ' + await ana.textContent('.usuario'));
  log.push('Cookies visíveis ao JavaScript: ' + (await ana.evaluate(() => document.cookie.split('; ').map(c => c.split('=')[0]).join(', '))));

  const carla = await sessao();
  await carla.goto(`${S}/login`); await carla.fill('input[name=email]', 'carla@portal.local'); await carla.fill('input[name=senha]', senha);
  await carla.click('button[type=submit]'); await carla.waitForURL(/\/tarefas/);
  await carla.goto(`${S}/tarefas/8`); await carla.waitForSelector('article.cartao'); await carla.foto('10-carla-admin-com-acoes');

  const visitante = await sessao();
  await visitante.goto(`${S}/login`); await visitante.fill('input[name=email]', 'visitante@portal.local'); await visitante.fill('input[name=senha]', senha);
  await visitante.click('button[type=submit]'); await visitante.waitForURL(/\/tarefas/); await visitante.waitForSelector('.lista');
  await visitante.foto('11-visitante-sem-nova-tarefa');
  await visitante.goto(`${S}/tarefas/nova`); await visitante.waitForSelector('[role=alert]'); await visitante.foto('12-visitante-rota-nova-bloqueada');

  await ana.click('text=Sair'); await ana.waitForSelector('text=Entrar');
  await ana.goto(`${S}/tarefas`); await ana.waitForURL(/\/login/); log.push('Após logout, /tarefas redireciona para: ' + ana.url());
  require('fs').writeFileSync(`${dir}/13-navegacao-spa.txt`, log.join('\n') + '\n');
  await browser.close();
})();
