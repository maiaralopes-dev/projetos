// SPA servida pelo contêiner (implantação integrada, mesma origem da API).
const { chromium } = require('playwright');
const dir = process.argv[2]; const S = 'https://localhost:8443';
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ ignoreHTTPSErrors: true, locale: 'pt-BR', viewport: { width: 1000, height: 650 } });
  await p.goto(`${S}/app/tarefas`); await p.waitForURL(/\/app\/login/);
  await p.fill('input[name=email]', 'ana@portal.local'); await p.fill('input[name=senha]', process.env.SEED_SENHA);
  await p.click('button[type=submit]'); await p.waitForSelector('.lista');
  await p.screenshot({ path: `${dir}/09-spa-no-conteiner.png`, fullPage: true });
  await b.close();
})();
