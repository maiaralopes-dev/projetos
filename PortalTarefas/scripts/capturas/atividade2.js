// Captura as telas MVC da Atividade 2 com Playwright. Requer a API em https://localhost:7080.
const { chromium } = require('playwright');
const dir = process.argv[2];
(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ ignoreHTTPSErrors: true, locale: 'pt-BR', viewport: { width: 1100, height: 720 } });
  const B = 'https://localhost:7080';
  const foto = async (nome) => page.screenshot({ path: `${dir}/${nome}.png`, fullPage: true });

  await page.goto(B + '/'); await foto('01-lista');
  await page.goto(B + '/Tarefas?situacao=0'); await foto('02-lista-filtrada-pendente');
  await page.goto(B + '/Tarefas/Details/2'); await foto('03-detalhes');
  await page.goto(B + '/Tarefas/Details/999'); await foto('04-detalhes-inexistente');

  // Cadastro com título vazio (regras de propriedade são avaliadas antes da regra entre campos)
  await page.goto(B + '/Tarefas/Create');
  await page.fill('#Descricao', 'Este texto deve ser preservado após o erro');
  await page.selectOption('#Prioridade', '2');
  await page.click('button[type=submit]'); await foto('05-cadastro-titulo-vazio');

  // Prioridade Alta sem prazo (validação customizada envolvendo dois campos)
  await page.fill('#Titulo', 'Tarefa urgente sem prazo');
  await page.click('button[type=submit]'); await foto('05b-cadastro-alta-sem-prazo');

  // Prazo no passado
  await page.fill('#Titulo', 'Tarefa com prazo vencido');
  await page.fill('#Prazo', '2020-01-10');
  await page.click('button[type=submit]'); await foto('06-cadastro-prazo-passado');

  // Título duplicado (validação assíncrona dependente de dados)
  await page.fill('#Titulo', 'Montar pipeline HTTP');
  await page.fill('#Prazo', '2030-01-10');
  await page.click('button[type=submit]'); await foto('07-cadastro-titulo-duplicado');

  // Cadastro válido -> PRG para detalhes
  await page.fill('#Titulo', 'Apresentar MVC para a turma');
  await page.click('button[type=submit]'); await foto('08-cadastro-valido-detalhes');

  // Edição
  await page.goto(B + '/Tarefas/Edit/3');
  await page.selectOption('#Entrada_Situacao', '1');
  await page.fill('#Entrada_Titulo', '');
  await page.click('button[type=submit]'); await foto('09-edicao-erro');
  await page.fill('#Entrada_Titulo', 'Escrever relatório 1 (revisado)');
  await page.click('button[type=submit]'); await foto('10-edicao-salva');
  await browser.close();
})();
