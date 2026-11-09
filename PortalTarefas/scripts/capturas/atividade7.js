// Capturas do painel local (Aspire Dashboard) com traces, métricas e logs recebidos via OTLP.
const { chromium } = require('playwright');
const dir = process.argv[2]; const P = 'http://localhost:18888';
(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1500, height: 900 } });
  const foto = (n) => page.screenshot({ path: `${dir}/${n}.png` });
  await page.goto(`${P}/traces`); await page.waitForTimeout(4000); await foto('09-painel-traces');
  // Abre o trace da conclusão de tarefa (span customizado + comandos SQL)
  const link = page.locator('text=/concluir/').first();
  if (await link.count()) { await link.click(); await page.waitForTimeout(3000); await foto('10-painel-trace-concluir'); }
  await page.goto(`${P}/structuredlogs`); await page.waitForTimeout(3000);
  const filtro = page.locator('input[placeholder*="ilter"], input[type=search]').first();
  if (await filtro.count()) { await filtro.fill('demo-erro-7'); await page.waitForTimeout(2500); }
  await foto('11-painel-logs-correlacao');
  for (const [arq, inst] of [['12-painel-metrica-calendario', 'portal.dependencia.calendario.duracao'], ['13-painel-metrica-tarefas-criadas', 'portal.tarefas.criadas']]) {
    await page.goto(`${P}/metrics/resource/portal-tarefas-api?meter=PortalTarefas&instrument=${inst}&duration=5`);
    await page.waitForTimeout(4000); await foto(arq);
  }
  await browser.close();
})();
