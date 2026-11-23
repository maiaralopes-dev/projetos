// Roda os casos de casos-dados-pedido.js contra o leitor de dados da
// extensão (extensao-crm/order-data-content.js) num Chromium sem tela.
//
// Uso:  node testes-extensao/rodar-testes.js
// Requer: Node 18+ e Playwright (npm i -D playwright && npx playwright install chromium)
//
// Sai com código 1 se algum caso falhar.

const path = require("path");
const fs = require("fs");
const { chromium } = require("playwright");

const EXT = path.join(__dirname, "..", "extensao-crm", "order-data-content.js");
const casos = require("./casos-dados-pedido.js");

async function rodarCaso(browser, codigo, caso) {
  const page = await browser.newPage();
  const errosPagina = [];
  page.on("pageerror", e => errosPagina.push(e.message));

  await page.setContent("<html><body></body></html>");

  await page.evaluate(cep => {
    window.chrome = {
      runtime: {
        sendMessage: async m => {
          if (m?.type === "lookup-cep") {
            const d = String(m.zip || "").replace(/\D+/g, "");
            return cep[d] ? { ok: true, data: cep[d] } : { ok: false, error: "CEP não encontrado." };
          }
          return { ok: false, error: "não usado no teste" };
        },
        onMessage: { addListener() {} }
      },
      storage: { local: { get: async () => ({}), set: async () => {} } }
    };
  }, caso.cep || {});

  await page.addScriptTag({ content: codigo });

  const valores = await page.evaluate(async mensagens => {
    const post = (source, payload) => window.postMessage({ source, payload }, "*");
    post("LNL_V15_34_DIRECT", {
      type: "current-conversation",
      conversationId: "teste",
      conversation: { id: "teste", name: "Cliente", contactId: "5521900000000" }
    });
    post("LNL_V15_34_DIRECT", { type: "conversation-messages", conversationId: "teste", messages: mensagens });
    await new Promise(r => setTimeout(r, 150));
    post("CRM_ORDER_DATA_BRIDGE_V15_34", { type: "open-panel" });

    // Espera a ficha terminar de preencher (inclui a consulta de CEP).
    let out = {};
    for (let i = 0; i < 30; i++) {
      await new Promise(r => setTimeout(r, 100));
      out = {};
      for (const el of document.querySelectorAll("*")) {
        if (!el.shadowRoot) continue;
        for (const input of el.shadowRoot.querySelectorAll("input[data-field]")) {
          out[input.dataset.field] = input.value;
        }
      }
      if (Object.keys(out).length && i >= 6) break;
    }
    return out;
  }, caso.mensagens);

  await page.close();

  const diferencas = [];
  for (const [campo, esperado] of Object.entries(caso.esperado)) {
    const obtido = valores[campo] ?? "(campo não existe)";
    if (obtido !== esperado) diferencas.push({ campo, esperado, obtido });
  }

  return { diferencas, errosPagina };
}

(async () => {
  const codigo = fs.readFileSync(EXT, "utf8");
  const browser = await chromium.launch();
  let falhas = 0;

  for (const caso of casos) {
    const { diferencas, errosPagina } = await rodarCaso(browser, codigo, caso);
    const ok = !diferencas.length && !errosPagina.length;
    if (!ok) falhas += 1;

    console.log(`${ok ? "✓" : "✗"} ${caso.nome}`);
    for (const d of diferencas) {
      console.log(`    ${d.campo}: esperado "${d.esperado}", veio "${d.obtido}"`);
    }
    for (const e of errosPagina) console.log(`    erro na página: ${e}`);
  }

  await browser.close();
  console.log(`\n${casos.length - falhas}/${casos.length} casos passaram.`);
  process.exit(falhas ? 1 : 0);
})();
