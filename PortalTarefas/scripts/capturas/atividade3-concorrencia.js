// Reproduz conflito de concorrência: duas sessões abrem a mesma tarefa e salvam em sequência.
const { chromium } = require('playwright');
const dir = process.argv[2]; const id = process.argv[3] || '7';
(async () => {
  const browser = await chromium.launch();
  const opts = { ignoreHTTPSErrors: true, locale: 'pt-BR', viewport: { width: 1100, height: 760 } };
  const ana = await (await browser.newContext(opts)).newPage();     // sessão 1
  const bruno = await (await browser.newContext(opts)).newPage();   // sessão 2
  const url = `https://localhost:7080/Tarefas/Edit/${id}`;
  await ana.goto(url); await bruno.goto(url);
  console.log('versão vista pelas duas sessões:', await ana.inputValue('input[name=versao]'), await bruno.inputValue('input[name=versao]'));

  await ana.selectOption('#Entrada_Situacao', '1');               // Ana: EmAndamento
  await ana.click('button[type=submit]');
  await ana.screenshot({ path: `${dir}/05-sessao1-salvou.png`, fullPage: true });

  await bruno.selectOption('#Entrada_Prioridade', '0');           // Bruno: prioridade Baixa, com versão antiga
  await bruno.click('button[type=submit]');
  await bruno.screenshot({ path: `${dir}/06-sessao2-conflito.png`, fullPage: true });
  console.log('sessão 2 após conflito - nova versão no formulário:', await bruno.inputValue('input[name=versao]'));

  // Bruno revisa: incorpora a situação gravada pela Ana e mantém a sua prioridade, agora com a versão atual
  await bruno.selectOption('#Entrada_Situacao', '1');
  await bruno.click('button[type=submit]');
  await bruno.screenshot({ path: `${dir}/07-sessao2-salvou-apos-revisar.png`, fullPage: true });
  await browser.close();
})();
