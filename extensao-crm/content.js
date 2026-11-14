
(() => {
  if (window.__LNL_V15_34_DIRECT_CONTENT__) return;
  window.__LNL_V15_34_DIRECT_CONTENT__ = true;

  const SOURCE = "LNL_V15_34_DIRECT";
  const ANSWERED_STORAGE_KEY = "lnl_answered_at_v16";

  const state = {
    conversations: [],
    pending: [],
    totalLoaded: 0,
    pendingCount: 0,
    pagesLoaded: 0,
    totalHint: null,
    completed: false,
    apiError: null,
    filterActive: false,
    openingId: null,
    selectedId: null,
    refreshing: false,
    lastUpdatedAt: null,
    dark: null,
    listRect: null,
    overlayGeometry: null,
    geometryViewportWidth: null,
    geometryViewportHeight: null,
    themeCandidate: null,
    themeCandidateCount: 0,
    optimisticReadUntil: new Map(),
    answeredAt: new Map(),
    orderSummary: null
  };

  // ---------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------

  function normalize(s) {
    return String(s || "")
      .normalize("NFD")
      .replace(/[\u0300-\u036f]/g, "")
      .replace(/\s+/g, " ")
      .trim()
      .toLowerCase();
  }

  function onlyDigits(s) {
    return String(s || "").replace(/\D+/g, "");
  }

  function lastDigits(s, n = 6) {
    const d = onlyDigits(s);
    return d ? d.slice(-n) : "";
  }

  function formatAgo(iso) {
    if (!iso) return "";

    const ts = Date.parse(iso);
    if (!Number.isFinite(ts)) return "";

    const diff = Math.max(0, Date.now() - ts);
    const min = Math.floor(diff / 60000);

    if (min < 1) return "agora";
    if (min < 60) return `${min}min`;

    const h = Math.floor(min / 60);
    if (h < 24) return `${h}h`;

    return `${Math.floor(h / 24)}d`;
  }

  function parseRgb(value) {
    const m = String(value || "")
      .match(/rgba?\(\s*(\d+)[,\s]+(\d+)[,\s]+(\d+)(?:[,\s\/]+([\d.]+))?/i);

    if (!m) return null;

    return {
      r: Number(m[1]),
      g: Number(m[2]),
      b: Number(m[3]),
      a: m[4] == null ? 1 : Number(m[4])
    };
  }

  function visibleBackground(el) {
    let node = el;

    for (let i = 0; i < 10 && node; i++, node = node.parentElement) {
      try {
        const c = parseRgb(getComputedStyle(node).backgroundColor);
        if (c && c.a > 0.45) return c;
      } catch {}
    }

    return null;
  }

  function detectDarkTheme() {
    const stableElements = [];

    const search = document.querySelector(
      'input[placeholder*="Pesquise seus contatos" i], input[placeholder*="Pesquise" i]'
    );
    if (search) stableElements.push(search);

    const messageField = document.querySelector(
      'textarea[placeholder*="Mensagem" i], input[placeholder*="Mensagem" i], [contenteditable="true"]'
    );
    if (messageField) stableElements.push(messageField);

    const rightTextarea = [...document.querySelectorAll("textarea")]
      .find(el => {
        const r = el.getBoundingClientRect();
        return r.left > window.innerWidth * 0.65 && r.width > 180;
      });
    if (rightTextarea) stableElements.push(rightTextarea);

    const firstRow = document.querySelector(
      'div.flex.items-center.border-b.cursor-pointer.overflow-hidden.w-full'
    );
    if (firstRow) stableElements.push(firstRow);

    const luminances = [];

    for (const el of stableElements) {
      const c = visibleBackground(el);
      if (!c) continue;

      luminances.push(
        0.2126 * c.r +
        0.7152 * c.g +
        0.0722 * c.b
      );
    }

    if (luminances.length) {
      luminances.sort((a, b) => a - b);
      const median = luminances[Math.floor(luminances.length / 2)];
      return median < 150;
    }

    return (
      window.matchMedia?.("(prefers-color-scheme: dark)")?.matches ??
      state.dark ??
      false
    );
  }

  function themeVars(dark) {
    return dark
      ? {
          "--lnl-bg": "#1d1f21",
          "--lnl-bg2": "#202224",
          "--lnl-bg3": "#292c2f",
          "--lnl-hover": "#25282b",
          "--lnl-selected": "#23303c",
          "--lnl-border": "#34383c",
          "--lnl-border2": "#42464a",
          "--lnl-text": "#f3f5f7",
          "--lnl-muted": "#9aa2a9",
          "--lnl-preview": "#bcc3c9",
          "--lnl-blue": "#55a8f5",
          "--lnl-blue-soft": "rgba(85,168,245,.14)",
          "--lnl-shadow": "rgba(0,0,0,.40)",
          "--lnl-panel-bg": "rgba(31,33,35,.99)"
        }
      : {
          "--lnl-bg": "#ffffff",
          "--lnl-bg2": "#f8fafc",
          "--lnl-bg3": "#ffffff",
          "--lnl-hover": "#f3f7fb",
          "--lnl-selected": "#eaf5ff",
          "--lnl-border": "#e2e7ec",
          "--lnl-border2": "#d2d9df",
          "--lnl-text": "#1f2933",
          "--lnl-muted": "#78838d",
          "--lnl-preview": "#505b65",
          "--lnl-blue": "#3c93e6",
          "--lnl-blue-soft": "rgba(60,147,230,.12)",
          "--lnl-shadow": "rgba(0,0,0,.16)",
          "--lnl-panel-bg": "rgba(255,255,255,.99)"
        };
  }

  function setThemeOnHost(host, dark) {
    if (!host) return;

    for (const [k, v] of Object.entries(themeVars(dark))) {
      host.style.setProperty(k, v);
    }

    host.dataset.theme = dark ? "dark" : "light";
  }

  function syncTheme(force = false) {
    const detected = detectDarkTheme();
    let changed = false;

    if (state.dark === null) {
      state.dark = detected;
      state.themeCandidate = null;
      state.themeCandidateCount = 0;
      changed = true;
    } else if (detected !== state.dark) {
      if (state.themeCandidate === detected) {
        state.themeCandidateCount += 1;
      } else {
        state.themeCandidate = detected;
        state.themeCandidateCount = 1;
      }

      if (state.themeCandidateCount >= 3) {
        state.dark = detected;
        state.themeCandidate = null;
        state.themeCandidateCount = 0;
        changed = true;
      }
    } else {
      state.themeCandidate = null;
      state.themeCandidateCount = 0;
    }

    if (changed || force) {
      setThemeOnHost(
        document.getElementById("lnl-panel-host"),
        state.dark
      );

      setThemeOnHost(
        document.getElementById("lnl-overlay-host"),
        state.dark
      );
    }
  }

  // ---------------------------------------------------------
  // Shadow DOM: painel flutuante
  // ---------------------------------------------------------

  const PANEL_STYLE = `
    :host {
      all: initial;
      --lnl-bg: #1d1f21;
      --lnl-bg2: #202224;
      --lnl-bg3: #292c2f;
      --lnl-hover: #25282b;
      --lnl-selected: #23303c;
      --lnl-border: #34383c;
      --lnl-border2: #42464a;
      --lnl-text: #f3f5f7;
      --lnl-muted: #9aa2a9;
      --lnl-preview: #bcc3c9;
      --lnl-blue: #55a8f5;
      --lnl-blue-soft: rgba(85,168,245,.14);
      --lnl-shadow: rgba(0,0,0,.40);
      --lnl-panel-bg: rgba(31,33,35,.99);
    }

    * { box-sizing: border-box; }

    #lnl-panel {
      position: fixed;
      right: 16px;
      bottom: 16px;
      z-index: 2147483647;
      width: 218px;
      padding: 10px;
      border-radius: 13px;
      background: var(--lnl-panel-bg);
      color: var(--lnl-text);
      font-family: Arial, Helvetica, sans-serif;
      font-size: 12px;
      box-shadow: 0 10px 28px var(--lnl-shadow);
      border: 1px solid var(--lnl-border);
      pointer-events: auto;
    }

    #lnl-panel.compact {
      width: 218px;
      padding: 10px;
    }

    .lnl-head {
      display: flex;
      gap: 5px;
      align-items: center;
      font-size: 14px;
      margin-bottom: 7px;
      font-weight: 600;
    }

    .lnl-source,
    .lnl-loaded {
      display: none;
    }

    button {
      appearance: none;
      border: 0;
      cursor: pointer;
      font-weight: 700;
      background: var(--lnl-blue);
      color: #0d1720;
    }

    button:disabled {
      opacity: .5;
      cursor: wait;
    }

    .lnl-panel-actions {
      display: grid;
      grid-template-columns: repeat(6, 1fr);
      gap: 6px;
      margin-top: 0;
    }

    .lnl-panel-actions button {
      width: 100%;
      min-width: 0;
      height: 36px;
      margin: 0;
      padding: 0;
      border-radius: 9px;
      font-size: 15px;
      line-height: 1;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    #lnl-filter-toggle {
      background: var(--lnl-blue);
      color: #0d1720;
    }

    #lnl-api-settings,
    #lnl-order-data,
    #lnl-image-data,
    #lnl-five-delivery,
    #lnl-confirm-card,
    #lnl-copy-missing,
    #lnl-review-data,
    #lnl-panel-refresh {
      background: var(--lnl-bg3);
      color: var(--lnl-text);
      border: 1px solid var(--lnl-border2);
    }

    #lnl-five-delivery {
      background: var(--lnl-blue-soft);
      border-color: var(--lnl-blue);
    }

    #lnl-five-delivery.order-ready {
      background: rgba(75, 188, 118, .20);
      border-color: #5fca87;
      color: #9ee5b8;
    }

    #lnl-five-delivery.order-review {
      background: rgba(227, 184, 100, .13);
      border-color: rgba(227, 184, 100, .55);
    }

    #lnl-confirm-card.order-ready {
      background: rgba(85,168,245,.22);
      border-color: #6cb8ff;
      color: #b9dcff;
      box-shadow: 0 0 0 1px rgba(108,184,255,.10) inset;
    }

    #lnl-confirm-card.order-review {
      opacity: .82;
    }

    #lnl-copy-missing.order-attention {
      background: rgba(227, 184, 100, .20);
      border-color: #e7c273;
      color: #ffe1a0;
      box-shadow: 0 0 0 1px rgba(231,194,115,.12) inset;
    }

    #lnl-copy-missing.order-ready {
      opacity: .72;
    }

    #lnl-review-data.order-attention {
      background: rgba(227, 184, 100, .20);
      border-color: #e7c273;
      color: #ffe1a0;
      box-shadow: 0 0 0 1px rgba(231,194,115,.12) inset;
    }

    #lnl-review-data.order-ready {
      opacity: .62;
    }

    .lnl-order-summary {
      display: none;
    }

    .lnl-details {
      margin-top: 8px;
      font-size: 9px;
      color: var(--lnl-muted);
    }

    .lnl-details summary {
      cursor: pointer;
      user-select: none;
      line-height: 1.2;
    }

    #lnl-details-text {
      margin-top: 5px;
      line-height: 1.35;
    }

    #lnl-status {
      min-height: 0;
      margin-top: 6px;
      color: var(--lnl-muted);
      font-size: 9px;
      line-height: 1.3;
    }

    #lnl-status:empty {
      display: none;
    }

    #lnl-mobile-menu-toggle {
      display: none;
    }

    .lnl-mobile-menu {
      display: contents;
    }

    @media (max-width: 1600px) {
      #lnl-panel,
      #lnl-panel.compact {
        right: 10px;
        z-index: 2147483647;
        pointer-events: auto;
        bottom: var(--lnl-mobile-bottom, calc(78px + env(safe-area-inset-bottom, 0px)));
        width: auto;
        max-width: calc(100vw - 20px);
        padding: 0;
        border: 0;
        border-radius: 999px;
        background: transparent;
        box-shadow: none;
      }

      .lnl-head,
      .lnl-source,
      .lnl-loaded,
      .lnl-details,
      #lnl-status {
        display: none !important;
      }

      .lnl-panel-actions {
        position: relative;
        display: flex;
        flex-direction: row;
        align-items: center;
        justify-content: flex-end;
        gap: 6px;
        margin: 0;
      }

      .lnl-panel-actions button {
        width: 40px;
        min-width: 40px;
        height: 40px;
        margin: 0;
        padding: 0;
        border-radius: 999px;
        box-shadow: 0 5px 16px rgba(0,0,0,.34);
        font-size: 15px;
        line-height: 1;
        display: flex;
        align-items: center;
        justify-content: center;
        -webkit-tap-highlight-color: transparent;
        touch-action: manipulation;
      }

      #lnl-filter-toggle {
        width: auto;
        min-width: 62px;
        padding: 0 12px;
        font-size: 12px;
      }

      #lnl-mobile-menu-toggle {
        display: flex;
        width: 40px;
        min-width: 40px;
        height: 40px;
        font-size: 22px;
        line-height: 1;
        background: var(--lnl-bg3);
        color: var(--lnl-text);
        border: 1px solid var(--lnl-border2);
      }

      .lnl-mobile-menu {
        display: none;
        position: absolute;
        z-index: 3;
        pointer-events: auto;
        right: 0;
        left: auto;
        bottom: 48px;
        top: auto;
        grid-template-columns: repeat(3, 40px);
        gap: 7px;
        padding: 8px;
        width: max-content;
        max-width: calc(100vw - 16px);
        max-height: min(46dvh, 360px);
        overflow-y: auto;
        overflow-x: hidden;
        overscroll-behavior: contain;
        border: 1px solid var(--lnl-border);
        border-radius: 16px;
        background: var(--lnl-panel-bg);
        box-shadow: 0 10px 28px rgba(0,0,0,.35);
      }

      #lnl-panel.menu-align-right .lnl-mobile-menu {
        left: 0;
        right: auto;
      }

      #lnl-panel.menu-open-down .lnl-mobile-menu {
        top: 48px;
        bottom: auto;
      }

      .lnl-order-summary {
        display: block;
        grid-column: 1 / -1;
        min-width: 190px;
        max-width: 245px;
        padding: 8px 9px;
        border-radius: 11px;
        background: var(--lnl-bg2);
        border: 1px solid var(--lnl-border);
        color: var(--lnl-muted);
        font-size: 9px;
        line-height: 1.35;
      }

      .lnl-order-summary strong {
        display: block;
        margin-bottom: 3px;
        color: var(--lnl-text);
        font-size: 12px;
      }

      .lnl-order-summary.ready strong {
        color: #83d9a4;
      }

      .lnl-order-summary.review strong {
        color: #e7c273;
      }

      #lnl-panel.mobile-menu-open .lnl-mobile-menu {
        display: grid;
      }

      #lnl-panel.mobile-menu-open #lnl-mobile-menu-toggle {
        background: var(--lnl-blue-soft);
        border-color: var(--lnl-blue);
        color: var(--lnl-blue);
      }

      .lnl-mobile-menu button {
        display: flex;
        width: 40px;
        min-width: 40px;
        height: 40px;
        box-shadow: none;
      }
    }

    @media (min-width: 769px) and (max-width: 1600px) {
      #lnl-panel,
      #lnl-panel.compact {
        right: 12px;
        bottom: var(--lnl-mobile-bottom, 18px);
      }

      .lnl-mobile-menu {
        max-height: min(52dvh, 420px);
      }
    }

    @media (max-width: 420px) {
      #lnl-panel,
      #lnl-panel.compact {
        right: 8px;
        bottom: var(--lnl-mobile-bottom, calc(76px + env(safe-area-inset-bottom, 0px)));
      }

      .lnl-panel-actions button,
      #lnl-mobile-menu-toggle,
      .lnl-mobile-menu button {
        width: 38px;
        min-width: 38px;
        height: 38px;
      }

      #lnl-filter-toggle {
        min-width: 58px;
        padding: 0 10px;
      }

      .lnl-mobile-menu {
        right: 0;
        bottom: 47px;
        grid-template-columns: repeat(3, 38px);
        padding: 7px;
      }
    }
  `;

  function panelHost() {
    return document.getElementById("lnl-panel-host");
  }

  function panelRoot() {
    return panelHost()?.shadowRoot || null;
  }

  function panelElement() {
    return panelRoot()?.getElementById("lnl-panel") || null;
  }

  function panelQuery(selector) {
    return panelRoot()?.querySelector(selector) || null;
  }

  function isMobileViewport() {
    return window.matchMedia(
      "(max-width: 768px)"
    ).matches;
  }

  function isCompactViewport() {
    return window.matchMedia(
      "(max-width: 1600px)"
    ).matches;
  }

  let panelPositionRaf = 0;

  function updateFloatingPanelPosition() {
    const p = panelElement();
    if (!p) return;

    if (!isCompactViewport()) {
      p.style.removeProperty("--lnl-mobile-bottom");
      return;
    }

    const viewport = window.visualViewport;
    const visibleBottom = viewport
      ? viewport.offsetTop + viewport.height
      : window.innerHeight;

    const composer = findMessageComposer();
    let bottom = 76;

    if (composer) {
      const r = composer.getBoundingClientRect();
      if (r.top > 0 && r.top < visibleBottom) {
        // Mantém o controle acima do compositor real do CRM, sem cobrir a digitação.
        bottom = Math.max(12, window.innerHeight - r.top + 8);
      }
    } else if (viewport && viewport.height < window.innerHeight - 120) {
      // Teclado aberto sem compositor detectável: mantém o botão dentro da área visível.
      bottom = Math.max(12, window.innerHeight - visibleBottom + 12);
    }

    const maxBottom = Math.max(12, window.innerHeight - 56);
    bottom = Math.min(bottom, maxBottom);

    p.style.setProperty(
      "--lnl-mobile-bottom",
      `calc(${Math.round(bottom)}px + env(safe-area-inset-bottom, 0px))`
    );
  }

  function updateQuickMenuDirection() {
    const p = panelElement();
    const menu = panelQuery(".lnl-mobile-menu");
    if (!p || !menu || !isCompactViewport()) return;

    const r = p.getBoundingClientRect();
    const viewport = window.visualViewport;
    const visibleTop = viewport ? viewport.offsetTop : 0;
    const visibleBottom = viewport
      ? viewport.offsetTop + viewport.height
      : window.innerHeight;

    const spaceLeft = r.left;
    const spaceRight = window.innerWidth - r.right;
    const spaceAbove = r.top - visibleTop;
    const spaceBelow = visibleBottom - r.bottom;

    p.classList.toggle("menu-align-right", spaceRight > spaceLeft + 120);
    p.classList.toggle("menu-open-down", spaceBelow > spaceAbove && spaceAbove < 250);
  }

  function scheduleFloatingPanelPosition() {
    if (panelPositionRaf) cancelAnimationFrame(panelPositionRaf);
    panelPositionRaf = requestAnimationFrame(() => {
      panelPositionRaf = 0;
      updateFloatingPanelPosition();
      updateQuickMenuDirection();
    });
  }

  function setMobileMenuOpen(open) {
    const p =
      panelElement();

    const toggle =
      panelQuery(
        "#lnl-mobile-menu-toggle"
      );

    if (!p) return;

    const shouldOpen =
      Boolean(open) &&
      isCompactViewport();

    p.classList.toggle(
      "mobile-menu-open",
      shouldOpen
    );

    if (toggle) {
      toggle.setAttribute(
        "aria-expanded",
        shouldOpen
          ? "true"
          : "false"
      );

      toggle.textContent =
        shouldOpen
          ? "×"
          : "⋯";

      toggle.title =
        shouldOpen
          ? "Fechar ações"
          : "Mais ações";

      toggle.setAttribute(
        "aria-label",
        toggle.title
      );
    }
  }

  function closeMobileMenu() {
    setMobileMenuOpen(false);
  }

  function requestOrderSummary() {
    window.postMessage({
      source: "CRM_ORDER_DATA_BRIDGE_V15_34",
      payload: {
        type: "quick-summary-request"
      }
    }, "*");
  }

  function renderOrderSummary() {
    const summary = state.orderSummary;
    const box = panelQuery("#lnl-order-summary");
    const score = panelQuery("#lnl-order-score");
    const missing = panelQuery("#lnl-order-missing");
    const five = panelQuery("#lnl-five-delivery");
    const card = panelQuery("#lnl-confirm-card");
    const ask = panelQuery("#lnl-copy-missing");
    const review = panelQuery("#lnl-review-data");

    if (!box || !score || !missing) return;

    box.classList.remove("ready", "review");
    five?.classList.remove("order-ready", "order-review");
    card?.classList.remove("order-ready", "order-review");
    ask?.classList.remove("order-ready", "order-attention");
    review?.classList.remove("order-ready", "order-attention");

    if (!summary) {
      score.textContent = "Dados: —/9 essenciais";
      missing.textContent = "Toque em ⋯ para verificar a conversa.";
      if (five) five.title = "Enviar dados atuais para Five Delivery";
      if (card) card.title = "Gerar card de confirmação";
      if (ask) ask.title = "Copiar mensagem pedindo somente o que falta";
      if (review) review.title = "Abrir e corrigir pendências";
      return;
    }

    score.textContent =
      summary.ready
        ? `✅ ${summary.found}/${summary.total} • PRONTO`
        : `⚠️ ${summary.found}/${summary.total} • REVISAR`;

    const missingLabels = Array.isArray(summary.missing) ? summary.missing : [];
    const issues = Array.isArray(summary.issues) ? summary.issues : [];

    if (summary.ready) {
      const optionalMissing = Array.isArray(summary.optionalMissing) ? summary.optionalMissing : [];
      const optionalIssues = Array.isArray(summary.optionalIssues) ? summary.optionalIssues : [];
      const details = [];
      if (summary.confirmedByClient) details.push("✅ Cliente confirmou os dados");
      if (Array.isArray(summary.cepDivergences) && summary.cepDivergences.length) {
        details.push(`⚠️ CEP diverge em: ${summary.cepDivergences.join(", ")}`);
      } else if (summary.cepConfirmed) {
        details.push("CEP confirmado ✓");
      }
      if (optionalMissing.length) details.push(`Opcionais faltando: ${optionalMissing.join(", ")}`);
      if (optionalIssues.length) details.push(`Opcional revisar: ${optionalIssues.join(", ")}`);
      missing.textContent = details.join(" • ") || "Cadastro essencial validado para Five Delivery.";
      box.classList.add("ready");
      five?.classList.add("order-ready");
      card?.classList.add("order-ready");
      ask?.classList.add("order-ready");
      review?.classList.add("order-ready");
      if (five) five.title = "Cadastro pronto • abrir Five Delivery";
      if (card) card.title = "Cadastro pronto • gerar card de confirmação";
      if (ask) ask.title = "Dados completos • copiar mensagem de confirmação";
      if (review) review.title = summary.cepDivergences?.length ? "Conferir divergência do CEP" : "Cadastro sem pendências essenciais";
    } else {
      const parts = [];
      if (summary.confirmedByClient) parts.push("✅ Cliente confirmou");
      if (missingLabels.length) parts.push(`Faltam: ${missingLabels.join(", ")}`);
      if (issues.length) parts.push(`Revisar: ${issues.join(", ")}`);
      if (Array.isArray(summary.cepDivergences) && summary.cepDivergences.length) parts.push(`CEP diverge: ${summary.cepDivergences.join(", ")}`);
      missing.textContent = parts.join(" • ") || "Confira os dados antes de enviar.";
      box.classList.add("review");
      five?.classList.add("order-review");
      card?.classList.add("order-review");
      ask?.classList.add("order-attention");
      review?.classList.add("order-attention");
      if (five) five.title = "Há dados pendentes • abrir Five Delivery mesmo assim";
      if (card) card.title = "Gerar card com os dados encontrados";
      if (ask) ask.title = "Pedir somente os dados que faltam";
      if (review) review.title = "Abrir direto no primeiro campo pendente";
    }
  }

  function panel() {
    let host = panelHost();

    if (!host) {
      host = document.createElement("div");
      host.id = "lnl-panel-host";

      Object.assign(host.style, {
        position: "fixed",
        inset: "0",
        width: "0",
        height: "0",
        zIndex: "2147483647",
        pointerEvents: "none"
      });

      const shadow = host.attachShadow({ mode: "open" });

      shadow.innerHTML = `
        <style>${PANEL_STYLE}</style>

        <div id="lnl-panel">
          <div class="lnl-head">
            <span>Não lidas:</span>
            <strong id="lnl-count">0</strong>
          </div>

          <div class="lnl-source">
            Fonte: <strong>status interno do CRM</strong> ✓
          </div>

          <div id="lnl-loaded" class="lnl-loaded">
            Aguardando o CRM carregar…
          </div>

          <div class="lnl-panel-actions">
            <button
              id="lnl-filter-toggle"
              type="button"
              title="Mostrar somente não lidas"
              aria-label="Mostrar somente não lidas"
            >👁</button>

            <div class="lnl-mobile-menu">
              <div class="lnl-order-summary" id="lnl-order-summary">
                <strong id="lnl-order-score">Dados: —/10</strong>
                <span id="lnl-order-missing">Abra para verificar os dados.</span>
              </div>

              <button
                id="lnl-order-data"
                type="button"
                title="Dados do pedido"
                aria-label="Dados do pedido"
              >📋</button>

              <button
                id="lnl-image-data"
                type="button"
                title="Ler imagem do cliente com OCR da VPS"
                aria-label="Ler imagem do cliente"
              >📷</button>

              <button
                id="lnl-five-delivery"
                type="button"
                title="Enviar dados atuais para Five Delivery"
                aria-label="Enviar para Five Delivery"
              >🚚</button>

              <button
                id="lnl-confirm-card"
                type="button"
                title="Gerar card de confirmação"
                aria-label="Gerar card de confirmação"
              >🪪</button>

              <button
                id="lnl-review-data"
                type="button"
                title="Corrigir pendências"
                aria-label="Corrigir pendências"
              >✏️</button>


              <button
                id="lnl-api-settings"
                type="button"
                title="Configurar VPS"
                aria-label="Configurar VPS"
              >⚙</button>

              <button
                id="lnl-panel-refresh"
                type="button"
                title="Atualizar não lidas agora"
                aria-label="Atualizar não lidas agora"
              >↻</button>

              <button
                id="lnl-copy-missing"
                type="button"
                title="Copiar mensagem pedindo somente o que falta"
                aria-label="Pedir somente o que falta"
              >❓</button>
            </div>

            <button
              id="lnl-mobile-menu-toggle"
              type="button"
              title="Mais ações"
              aria-label="Mais ações"
              aria-expanded="false"
            >⋯</button>
          </div>

          <details class="lnl-details">
            <summary>Detalhes</summary>
            <div id="lnl-details-text"></div>
          </details>

          <div id="lnl-status"></div>
        </div>
      `;

      document.documentElement.appendChild(host);

      shadow
        .getElementById("lnl-filter-toggle")
        .addEventListener("click", () => {
          closeMobileMenu();
          toggleFilter();
        });

      shadow
        .getElementById("lnl-mobile-menu-toggle")
        .addEventListener("click", () => {
          const p = panelElement();
          const opening = !p?.classList.contains("mobile-menu-open");
          setMobileMenuOpen(opening);
          if (opening) requestOrderSummary();
        });

      shadow
        .getElementById("lnl-order-data")
        .addEventListener("click", () => {
          closeMobileMenu();

          window.postMessage({
            source: "CRM_ORDER_DATA_BRIDGE_V15_34",
            payload: {
              type: "open-panel"
            }
          }, "*");
        });

      shadow
        .getElementById("lnl-image-data")
        .addEventListener("click", () => {
          closeMobileMenu();
          status("Lendo imagem com OCR da VPS…");

          window.postMessage({
            source: "CRM_ORDER_DATA_BRIDGE_V15_34",
            payload: {
              type: "read-image-direct"
            }
          }, "*");
        });

      shadow
        .getElementById("lnl-five-delivery")
        .addEventListener("click", () => {
          closeMobileMenu();
          status("Preparando dados para a Five Delivery…");

          window.postMessage({
            source: "CRM_ORDER_DATA_BRIDGE_V15_34",
            payload: {
              type: "send-five-direct"
            }
          }, "*");
        });

      shadow
        .getElementById("lnl-confirm-card")
        .addEventListener("click", () => {
          closeMobileMenu();
          status("Gerando card de confirmação…");

          window.postMessage({
            source: "CRM_ORDER_DATA_BRIDGE_V15_34",
            payload: {
              type: "open-card-direct"
            }
          }, "*");
        });

      shadow
        .getElementById("lnl-panel-refresh")
        .addEventListener("click", () => {
          closeMobileMenu();
          requestRefresh("manual-panel");
        });

      shadow
        .getElementById("lnl-copy-missing")
        .addEventListener("click", () => {
          closeMobileMenu();
          window.postMessage({
            source: "CRM_ORDER_DATA_BRIDGE_V15_34",
            payload: { type: "copy-missing-direct" }
          }, "*");
        });

      shadow
        .getElementById("lnl-review-data")
        .addEventListener("click", () => {
          closeMobileMenu();
          status("Abrindo primeira pendência…");
          window.postMessage({
            source: "CRM_ORDER_DATA_BRIDGE_V15_34",
            payload: { type: "open-review-direct" }
          }, "*");
        });


      shadow
        .getElementById("lnl-api-settings")
        .addEventListener("click", () => {
          closeMobileMenu();

          window.postMessage({
            source: "CRM_AUDIO_SETTINGS_BRIDGE_V15_4",
            payload: {
              type: "open-settings"
            }
          }, "*");
        });
    }

    syncTheme(true);
    return panelElement();
  }

  // ---------------------------------------------------------
  // Shadow DOM: lista filtrada
  // ---------------------------------------------------------

  const OVERLAY_STYLE = `
    :host {
      all: initial;
      --lnl-bg: #1d1f21;
      --lnl-bg2: #202224;
      --lnl-bg3: #292c2f;
      --lnl-hover: #25282b;
      --lnl-selected: #23303c;
      --lnl-border: #34383c;
      --lnl-border2: #42464a;
      --lnl-text: #f3f5f7;
      --lnl-muted: #9aa2a9;
      --lnl-preview: #bcc3c9;
      --lnl-blue: #55a8f5;
      --lnl-blue-soft: rgba(85,168,245,.14);
      --lnl-shadow: rgba(0,0,0,.40);
    }

    * { box-sizing: border-box; }

    #lnl-pending-overlay {
      position: fixed;
      z-index: 2147483646;
      contain: layout paint size;
      display: none;
      flex-direction: column;
      overflow: hidden;
      background: var(--lnl-bg);
      color: var(--lnl-text);
      border: 1px solid var(--lnl-border);
      border-left: 0;
      box-shadow: none;
      font-family: Arial, Helvetica, sans-serif;
      pointer-events: auto;
    }

    .lnl-overlay-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      min-height: 58px;
      padding: 9px 10px 8px 12px;
      background: var(--lnl-bg2);
      border-bottom: 1px solid var(--lnl-border);
    }

    .lnl-overlay-title-block {
      min-width: 0;
      display: flex;
      flex-direction: column;
      gap: 3px;
    }

    .lnl-overlay-title-row {
      display: flex;
      align-items: center;
      gap: 7px;
      min-width: 0;
    }

    .lnl-overlay-title-row strong {
      color: var(--lnl-text);
      font-size: 13px;
      font-weight: 700;
    }

    .lnl-pending-dot,
    .lnl-row-dot {
      width: 7px;
      height: 7px;
      min-width: 7px;
      border-radius: 999px;
      background: var(--lnl-blue);
      box-shadow: 0 0 0 2px var(--lnl-blue-soft);
    }

    .lnl-count-badge {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      min-width: 24px;
      height: 20px;
      padding: 0 7px;
      border-radius: 999px;
      background: var(--lnl-blue-soft);
      color: var(--lnl-blue);
      font-size: 10px;
      font-weight: 700;
    }

    .lnl-overlay-sync {
      color: var(--lnl-muted);
      font-size: 9px;
      line-height: 1.2;
    }

    .lnl-overlay-actions {
      display: flex;
      align-items: center;
      gap: 4px;
    }

    .lnl-overlay-actions button {
      appearance: none;
      width: 30px;
      height: 30px;
      margin: 0;
      padding: 0;
      border: 0;
      border-radius: 8px;
      background: transparent;
      color: var(--lnl-muted);
      font-size: 18px;
      cursor: pointer;
    }

    .lnl-overlay-actions button:hover {
      background: var(--lnl-hover);
      color: var(--lnl-text);
    }

    #lnl-overlay-refresh.lnl-spinning {
      animation: spin .8s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    .lnl-overlay-search-wrap {
      padding: 9px 10px;
      background: var(--lnl-bg2);
      border-bottom: 1px solid var(--lnl-border);
    }

    #lnl-overlay-search {
      width: 100%;
      height: 36px;
      appearance: none;
      padding: 0 11px;
      border: 1px solid var(--lnl-border2);
      border-radius: 9px;
      outline: none;
      background: var(--lnl-bg3);
      color: var(--lnl-text);
      caret-color: var(--lnl-blue);
      font: 12px Arial, Helvetica, sans-serif;
    }

    #lnl-overlay-search::placeholder {
      color: var(--lnl-muted);
    }

    #lnl-overlay-search:focus {
      border-color: var(--lnl-blue);
      box-shadow: 0 0 0 2px var(--lnl-blue-soft);
    }

    #lnl-overlay-list {
      flex: 1 1 auto;
      min-height: 0;
      overflow-y: auto;
      overflow-x: hidden;
      background: var(--lnl-bg);
    }

    .lnl-pending-item {
      width: 100%;
      min-height: 82px;
      appearance: none;
      display: block;
      margin: 0;
      padding: 11px 12px;
      border: 0;
      border-bottom: 1px solid var(--lnl-border);
      border-radius: 0;
      background: var(--lnl-bg);
      color: var(--lnl-text);
      text-align: left;
      cursor: pointer;
      font-family: Arial, Helvetica, sans-serif;
    }

    .lnl-pending-item:hover {
      background: var(--lnl-hover);
    }

    .lnl-pending-item.lnl-selected-item {
      background: var(--lnl-selected);
      box-shadow: inset 3px 0 0 var(--lnl-blue);
    }

    .lnl-pending-item.lnl-opening-item {
      opacity: .68;
      cursor: progress;
    }

    .lnl-item-top {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 8px;
    }

    .lnl-name-wrap {
      min-width: 0;
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .lnl-item-name {
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      color: var(--lnl-text);
      font-size: 13px;
      font-weight: 700;
    }

    .lnl-item-time {
      flex: 0 0 auto;
      color: var(--lnl-muted);
      font-size: 10px;
    }

    .lnl-item-preview {
      margin-top: 6px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      color: var(--lnl-preview);
      font-size: 11px;
      line-height: 1.35;
    }

    .lnl-item-meta {
      margin-top: 7px;
      display: flex;
      align-items: center;
      gap: 7px;
      min-height: 18px;
    }

    .lnl-meta-chip {
      display: inline-flex;
      align-items: center;
      max-width: 190px;
      height: 18px;
      padding: 0 7px;
      border-radius: 999px;
      background: var(--lnl-blue-soft);
      color: var(--lnl-blue);
      font-size: 9px;
      font-weight: 600;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .lnl-phone-tail {
      color: var(--lnl-muted);
      font-size: 9px;
    }

    .lnl-empty {
      padding: 32px 16px;
      color: var(--lnl-muted);
      font-size: 12px;
      text-align: center;
    }

    @media (max-width: 768px) {
      #lnl-pending-overlay {
        border: 0;
        border-radius: 0;
        box-shadow: none;
      }

      .lnl-overlay-head {
        min-height: 56px;
        padding: 9px 12px;
      }

      .lnl-overlay-search-wrap {
        padding: 8px 10px;
      }

      #lnl-overlay-search {
        height: 38px;
        font-size: 13px;
      }

      .lnl-pending-item {
        min-height: 76px;
        padding: 10px 12px;
      }

      .lnl-item-name {
        font-size: 14px;
      }

      .lnl-item-preview {
        font-size: 12px;
      }
    }
  `;

  function overlayHost() {
    return document.getElementById("lnl-overlay-host");
  }

  function overlayRoot() {
    return overlayHost()?.shadowRoot || null;
  }

  function overlayElement() {
    return overlayRoot()?.getElementById("lnl-pending-overlay") || null;
  }

  function ensureOverlay() {
    let host = overlayHost();

    if (!host) {
      host = document.createElement("div");
      host.id = "lnl-overlay-host";

      Object.assign(host.style, {
        position: "fixed",
        inset: "0",
        width: "0",
        height: "0",
        zIndex: "2147483646",
        pointerEvents: "none"
      });

      const shadow = host.attachShadow({ mode: "open" });

      shadow.innerHTML = `
        <style>${OVERLAY_STYLE}</style>

        <div id="lnl-pending-overlay">
          <div class="lnl-overlay-head">
            <div class="lnl-overlay-title-block">
              <div class="lnl-overlay-title-row">
                <span class="lnl-pending-dot"></span>
                <strong>Conversas não lidas</strong>
                <span id="lnl-overlay-count" class="lnl-count-badge"></span>
              </div>

              <div id="lnl-overlay-sync" class="lnl-overlay-sync">
                Atualização automática ativa
              </div>
            </div>

            <div class="lnl-overlay-actions">
              <button
                id="lnl-overlay-order-data"
                type="button"
                title="Dados do pedido"
                aria-label="Dados do pedido"
              >📋</button>

              <button
                id="lnl-overlay-settings"
                type="button"
                title="Configurar transcrição e OCR"
                aria-label="Configurar transcrição e OCR"
              >⚙</button>

              <button
                id="lnl-overlay-refresh"
                type="button"
                title="Atualizar agora"
                aria-label="Atualizar agora"
              >↻</button>

              <button
                id="lnl-overlay-close"
                type="button"
                title="Fechar filtro"
                aria-label="Fechar filtro"
              >×</button>
            </div>
          </div>

          <div class="lnl-overlay-search-wrap">
            <input
              id="lnl-overlay-search"
              type="text"
              autocomplete="off"
              placeholder="Buscar nome, mensagem ou telefone..."
            />
          </div>

          <div id="lnl-overlay-list"></div>
        </div>
      `;

      document.documentElement.appendChild(host);

      shadow
        .getElementById("lnl-overlay-close")
        .addEventListener("click", () => {
          state.filterActive = false;
          hideOverlay();
          resetOverlayGeometry();
          updatePanel();
        });

      shadow
        .getElementById("lnl-overlay-order-data")
        .addEventListener("click", () => {
          window.postMessage({
            source: "CRM_ORDER_DATA_BRIDGE_V15_34",
            payload: {
              type: "open-panel"
            }
          }, "*");
        });

      shadow
        .getElementById("lnl-overlay-settings")
        .addEventListener("click", () => {
          window.postMessage({
            source: "CRM_AUDIO_SETTINGS_BRIDGE_V15_4",
            payload: {
              type: "open-settings"
            }
          }, "*");
        });

      shadow
        .getElementById("lnl-overlay-refresh")
        .addEventListener("click", () => {
          requestRefresh("manual");
        });

      shadow
        .getElementById("lnl-overlay-search")
        .addEventListener("input", renderOverlayList);
    }

    syncTheme(true);
    return overlayElement();
  }

  // ---------------------------------------------------------
  // Painel / lista
  // ---------------------------------------------------------

  function updatePanel() {
    panel();

    const count = panelQuery("#lnl-count");
    const loaded = panelQuery("#lnl-loaded");
    const details = panelQuery("#lnl-details-text");
    const btn = panelQuery("#lnl-filter-toggle");
    const p = panelElement();

    if (count) {
      count.textContent =
        state.completed
          ? String(
              state.pendingCount
            )
          : "…";
    }

    if (loaded) {
      if (state.apiError) {
        loaded.textContent = `API: ${state.apiError}`;
      } else if (state.completed) {
        loaded.textContent = `${state.totalLoaded} conversas verificadas sem rolagem`;
      } else if (state.totalLoaded > 0) {
        loaded.textContent = `Carregando em segundo plano… ${state.totalLoaded} verificadas`;
      } else {
        loaded.textContent = "Carregando conversas pela API…";
      }
    }

    if (details) {
      details.textContent =
        `Páginas API: ${state.pagesLoaded}` +
        (state.totalHint ? ` • Total informado: ${state.totalHint}` : "") +
        " • Bolinha visual: não usada" +
        " • Filtro: lista completa pela API" +
        " • Abertura: direta pelo React" +
        " • Atualização: instantânea ao responder ou marcar como lida" +
        " • Tema: automático";
    }

    renderOrderSummary();

    if (btn) {
      const compactViewport = isCompactViewport();

      if (compactViewport) {
        btn.textContent =
          state.filterActive
            ? `↩ ${state.pendingCount}`
            : state.completed
            ? `👁 ${state.pendingCount}`
            : "…";
      } else {
        btn.textContent =
          state.filterActive
            ? "↩"
            : state.completed
            ? "👁"
            : "…";
      }

      btn.title =
        state.filterActive
          ? "Voltar para todas as conversas"
          : "Mostrar somente não lidas";

      btn.setAttribute(
        "aria-label",
        btn.title
      );

      btn.disabled =
        !state.completed;
    }

    if (p) {
      p.classList.toggle("compact", state.filterActive);
    }

    syncTheme(true);
    scheduleFloatingPanelPosition();

    if (state.filterActive) {
      renderOverlay();
    }
  }

  function findConversationRows() {
    return [
      ...document.querySelectorAll(
        'div.flex.items-center.border-b.cursor-pointer.overflow-hidden.w-full'
      )
    ].filter(el => {
      const r = el.getBoundingClientRect();

      return (
        r.left < 650 &&
        r.width >= 250 &&
        r.width <= 650 &&
        r.height >= 55 &&
        r.height <= 140 &&
        r.bottom > 100
      );
    });
  }

  function findListContainer() {
    const rows = findConversationRows();

    if (rows.length) {
      let node = rows[0].parentElement;

      for (
        let i = 0;
        i < 8 && node && node !== document.body;
        i++, node = node.parentElement
      ) {
        const r = node.getBoundingClientRect();
        const s = getComputedStyle(node);
        const oy = s.overflowY;

        if (
          r.left < 650 &&
          r.width >= 300 &&
          r.width <= 650 &&
          r.height >= 300 &&
          (
            oy === "auto" ||
            oy === "scroll" ||
            node.scrollHeight > node.clientHeight + 30
          )
        ) {
          return node;
        }
      }
    }

    const candidates = [
      ...document.querySelectorAll("div")
    ].filter(el => {
      const r = el.getBoundingClientRect();

      if (
        r.left > 650 ||
        r.width < 300 ||
        r.width > 650 ||
        r.height < 300
      ) return false;

      const s = getComputedStyle(el);

      return (
        s.overflowY === "auto" ||
        s.overflowY === "scroll" ||
        el.scrollHeight > el.clientHeight + 100
      );
    });

    candidates.sort(
      (a, b) =>
        a.getBoundingClientRect().width -
        b.getBoundingClientRect().width
    );

    return candidates[0] || null;
  }

  function findSidebarShell() {
    const rows = findConversationRows();
    if (!rows.length) return null;

    let node = rows[0];
    let best = null;
    let bestWidth = 0;

    for (
      let i = 0;
      i < 12 && node && node !== document.body;
      i++, node = node.parentElement
    ) {
      const r = node.getBoundingClientRect();

      if (
        r.left < 700 &&
        r.width >= 320 &&
        r.width <= 560 &&
        r.height >= 400
      ) {
        const score = r.width;
        if (score >= bestWidth) {
          best = node;
          bestWidth = score;
        }
      }
    }

    return best;
  }

  function calculateOverlayGeometry() {
    const w = window.innerWidth;
    const h = window.innerHeight;
    const compact = isCompactViewport();
    const mobile = isMobileViewport();

    if (compact) {
      const shell = findSidebarShell() || findListContainer();
      const top = mobile ? 0 : 82;

      if (shell) {
        const r = shell.getBoundingClientRect();
        const left = Math.max(0, Math.round(r.left));
        const width = Math.min(
          Math.max(280, Math.round(r.width)),
          Math.max(280, w - left)
        );

        return {
          left,
          top,
          width,
          height: Math.max(360, h - top)
        };
      }

      const width = mobile
        ? Math.min(360, Math.max(280, Math.round(w * 0.82)))
        : w <= 1100
        ? Math.min(360, Math.max(300, Math.round(w * 0.80)))
        : Math.min(350, Math.max(310, Math.round(w * 0.26)));

      return {
        left: 0,
        top,
        width,
        height: Math.max(360, h - top)
      };
    }

    const container = findListContainer();

    if (!container) {
      const left = 58;
      const top = 125;
      const width = Math.min(440, Math.max(360, Math.round(w * 0.255)));
      const height = Math.max(420, h - top);
      return { left, top, width, height };
    }

    const r = container.getBoundingClientRect();
    const left = Math.max(0, Math.round(r.left));
    const top = Math.max(80, Math.round(r.top));
    const width = Math.min(620, Math.max(360, Math.round(r.width)));
    const maxHeight = Math.max(380, h - top);
    const height = Math.min(maxHeight, Math.max(420, Math.round(r.height)));

    return { left, top, width, height };
  }

  function applyOverlayGeometry() {
    const ov =
      ensureOverlay();

    if (!state.overlayGeometry) {
      state.overlayGeometry =
        calculateOverlayGeometry();

      state.geometryViewportWidth =
        window.innerWidth;

      state.geometryViewportHeight =
        window.innerHeight;
    }

    const g =
      state.overlayGeometry;

    ov.style.left =
      `${g.left}px`;

    ov.style.top =
      `${g.top}px`;

    ov.style.width =
      `${g.width}px`;

    ov.style.height =
      `${g.height}px`;

    // Travamos também min/max para impedir qualquer CSS ou conteúdo
    // interno de alterar visualmente o tamanho do painel.
    ov.style.minWidth =
      `${g.width}px`;

    ov.style.maxWidth =
      `${g.width}px`;

    ov.style.minHeight =
      `${g.height}px`;

    ov.style.maxHeight =
      `${g.height}px`;
  }

  function resetOverlayGeometry() {
    state.overlayGeometry = null;
    state.geometryViewportWidth = null;
    state.geometryViewportHeight = null;
  }

  function positionOverlay(
    force = false
  ) {
    if (force) {
      resetOverlayGeometry();
    }

    applyOverlayGeometry();
  }

  function hideOverlay() {
    const ov = overlayElement();
    if (ov) ov.style.display = "none";
  }

  function renderOverlay() {
    const ov = ensureOverlay();

    ov.style.display = "flex";
    applyOverlayGeometry();
    syncTheme();

    const root = overlayRoot();

    const count = root?.getElementById("lnl-overlay-count");
    if (count) {
      count.textContent =
        state.completed
          ? String(
              state.pending.length
            )
          : "…";
    }

    const sync = root?.getElementById("lnl-overlay-sync");

    if (sync) {
      if (state.refreshing) {
        sync.textContent = "Atualizando conversas…";
      } else if (state.lastUpdatedAt) {
        const ago = formatAgo(state.lastUpdatedAt);

        sync.textContent =
          ago === "agora"
            ? "Atualizado agora • automático"
            : `Atualizado há ${ago} • automático`;
      } else {
        sync.textContent = "Atualização automática ativa";
      }
    }

    const refreshButton =
      root?.getElementById("lnl-overlay-refresh");

    if (refreshButton) {
      refreshButton.classList.toggle(
        "lnl-spinning",
        state.refreshing
      );
    }

    renderOverlayList();
  }

  function renderOverlayList() {
    const root = overlayRoot();
    if (!root) return;

    const list = root.getElementById("lnl-overlay-list");
    const search = root.getElementById("lnl-overlay-search");

    if (!list) return;

    if (!state.completed) {
      list.innerHTML = "";

      const loading =
        document.createElement(
          "div"
        );

      loading.className =
        "lnl-empty";

      loading.textContent =
        "Carregando todas as conversas não lidas…";

      list.appendChild(
        loading
      );

      return;
    }

    const q = normalize(search?.value || "");

    const items =
      state.pending.filter(item => {
        if (!q) return true;

        return (
          normalize(item.name).includes(q) ||
          normalize(item.preview).includes(q) ||
          onlyDigits(item.contactId || item.phone)
            .includes(onlyDigits(q))
        );
      });

    list.innerHTML = "";

    if (!items.length) {
      const empty = document.createElement("div");
      empty.className = "lnl-empty";
      empty.textContent = "Nenhuma conversa não lida encontrada.";
      list.appendChild(empty);
      return;
    }

    for (const item of items) {
      const row = document.createElement("button");

      row.type = "button";
      row.className = "lnl-pending-item";

      if (String(item.id) === String(state.selectedId)) {
        row.classList.add("lnl-selected-item");
      }

      if (String(item.id) === String(state.openingId)) {
        row.classList.add("lnl-opening-item");
      }

      const top = document.createElement("div");
      top.className = "lnl-item-top";

      const nameWrap = document.createElement("div");
      nameWrap.className = "lnl-name-wrap";

      const dot = document.createElement("span");
      dot.className = "lnl-row-dot";

      const name = document.createElement("span");
      name.className = "lnl-item-name";
      name.textContent = item.name || "Conversa";

      const ago = document.createElement("span");
      ago.className = "lnl-item-time";
      ago.textContent = formatAgo(item.lastMessageDate);

      nameWrap.append(dot, name);
      top.append(nameWrap, ago);

      const preview = document.createElement("div");
      preview.className = "lnl-item-preview";
      preview.textContent =
        item.preview ||
        "Conversa aguardando atendimento";

      const meta = document.createElement("div");
      meta.className = "lnl-item-meta";

      const tail = lastDigits(item.contactId || item.phone);

      if (item.instanceName) {
        const instance = document.createElement("span");
        instance.className = "lnl-meta-chip";
        instance.textContent = item.instanceName;
        meta.appendChild(instance);
      }

      if (tail) {
        const phone = document.createElement("span");
        phone.className = "lnl-phone-tail";
        phone.textContent = `final ${tail}`;
        meta.appendChild(phone);
      }

      row.append(top, preview, meta);

      row.addEventListener("click", () => openConversation(item));

      list.appendChild(row);
    }
  }

  // ---------------------------------------------------------
  // Abertura direta / atualização
  // ---------------------------------------------------------

  function openConversation(item) {
    if (!item?.id) {
      status("Essa conversa não possui ID interno.");
      return;
    }

    state.openingId = String(item.id);
    renderOverlayList();

    status(`Abrindo ${item.name || "conversa"} diretamente…`);

    window.postMessage({
      source: SOURCE,
      payload: {
        type: "open-conversation-direct",
        conversationId: String(item.id)
      }
    }, "*");

    // O drawer permanece aberto após selecionar uma conversa.
    // Ele só fecha por ação explícita do usuário (× ou alternância do filtro).
  }

  function messageDateMs(value) {
    const n = Date.parse(value || "");
    return Number.isFinite(n) ? n : 0;
  }

  async function loadAnsweredAt() {
    try {
      const result =
        await chrome.storage.local.get(
          ANSWERED_STORAGE_KEY
        );

      const raw =
        result?.[ANSWERED_STORAGE_KEY];

      const now = Date.now();
      const maxAge =
        30 * 24 * 60 * 60 * 1000;

      state.answeredAt.clear();

      if (
        raw &&
        typeof raw === "object"
      ) {
        for (
          const [id, value]
          of Object.entries(raw)
        ) {
          // V16.16: formato novo { ts, recv }; aceita o número antigo.
          const ts = Number(
            (value && typeof value === "object")
              ? value.ts
              : value
          ) || 0;

          const recv = Number(
            (value && typeof value === "object")
              ? value.recv
              : 0
          ) || 0;

          if (
            id &&
            ts > 0 &&
            now - ts < maxAge
          ) {
            state.answeredAt.set(
              String(id),
              { ts, recv }
            );
          }
        }
      }
    } catch (err) {
      console.warn(
        "[LNL] Falha ao carregar respondidas locais:",
        err
      );
    }
  }

  async function saveAnsweredAt() {
    try {
      const obj =
        Object.fromEntries(
          state.answeredAt.entries()
        );

      await chrome.storage.local.set({
        [ANSWERED_STORAGE_KEY]: obj
      });
    } catch (err) {
      console.warn(
        "[LNL] Falha ao salvar respondidas locais:",
        err
      );
    }
  }

  function rebuildPending() {
    state.pending =
      state.conversations
        .filter(shouldShowAsUnread)
        .sort((a, b) => {
          const da =
            Date.parse(
              a.lastMessageDate || 0
            ) || 0;

          const db =
            Date.parse(
              b.lastMessageDate || 0
            ) || 0;

          return db - da;
        });

    state.pendingCount =
      state.pending.length;

    updatePanel();

    if (state.filterActive) {
      renderOverlayList();
    }
  }

  // V16.16: o cliente respondeu depois da nossa resposta?
  // Compara com a data (do servidor) da última mensagem recebida
  // no momento da resposta, e não com o relógio do computador.
  // Assim um relógio adiantado não esconde respostas do cliente.
  function customerRepliedAfter(entry, item) {
    const received =
      messageDateMs(
        item?.lastReceivedMessageDate
      );

    if (!received) return false;

    if (entry.recv) {
      return received > entry.recv;
    }

    return received > entry.ts;
  }

  function localAnsweredAfterLastReceived(item) {
    const id = String(item?.id || "");
    if (!id) return false;

    const entry =
      state.answeredAt.get(id);

    if (!entry?.ts) return false;

    // O cliente enviou algo depois da nossa resposta:
    // a conversa precisa voltar para as não lidas.
    return !customerRepliedAfter(entry, item);
  }

  function knownLastReceivedMs(id) {
    const item =
      state.conversations.find(
        x => String(x?.id) === String(id)
      );

    return messageDateMs(
      item?.lastReceivedMessageDate
    );
  }

  function markAnsweredLocally(
    id,
    timestamp = Date.now(),
    lastReceivedMs = null
  ) {
    const key = String(id || "");
    if (!key) return;

    const ts =
      Number(timestamp || Date.now()) ||
      Date.now();

    const recv =
      Number(
        lastReceivedMs != null
          ? lastReceivedMs
          : knownLastReceivedMs(key)
      ) || 0;

    const previous =
      state.answeredAt.get(key) || { ts: 0, recv: 0 };

    if (
      ts > previous.ts ||
      recv > previous.recv
    ) {
      state.answeredAt.set(key, {
        ts: Math.max(ts, previous.ts),
        recv: Math.max(recv, previous.recv)
      });
      void saveAnsweredAt();
    }

    // Evita a API recolocar a conversa enquanto ainda
    // não refletiu o envio de texto, áudio, imagem ou anexo.
    suppressPendingTemporarily(
      key,
      60000
    );

    rebuildPending();
  }

  function clearAnsweredLocally(id) {
    const key = String(id || "");

    if (
      !key ||
      !state.answeredAt.has(key)
    ) {
      return;
    }

    state.answeredAt.delete(key);
    void saveAnsweredAt();
    rebuildPending();
  }

  function reconcileAnsweredAtWithApi() {
    let changed = false;

    for (const item of state.conversations) {
      const id = String(item?.id || "");
      if (!id) continue;

      const entry =
        state.answeredAt.get(id);

      if (!entry?.ts) continue;

      if (customerRepliedAfter(entry, item)) {
        state.answeredAt.delete(id);
        changed = true;
      }
    }

    if (changed) {
      void saveAnsweredAt();
    }
  }

  function applyConversationMessagesState(
    conversationId,
    messages
  ) {
    const id = String(conversationId || "");

    if (
      !id ||
      !Array.isArray(messages) ||
      !messages.length
    ) {
      return;
    }

    const directional =
      messages
        .map((msg, index) => ({
          msg,
          index,
          ts: messageDateMs(msg?.createdAt)
        }))
        .filter(
          item =>
            typeof item.msg?.received ===
            "boolean"
        );

    if (!directional.length) return;

    directional.sort((a, b) => {
      if (a.ts && b.ts && a.ts !== b.ts) {
        return a.ts - b.ts;
      }
      if (a.ts && !b.ts) return 1;
      if (!a.ts && b.ts) return -1;
      return a.index - b.index;
    });

    const latest =
      directional[directional.length - 1];

    const ts =
      latest.ts || Date.now();

    const lastReceivedTs =
      directional
        .filter(x => x.msg.received === true && x.ts)
        .reduce((max, x) => Math.max(max, x.ts), 0);

    if (latest.msg.received === false) {
      // Última mensagem real da conversa foi uma saída do
      // atendente. Vale para texto, áudio, imagem e anexo.
      markAnsweredLocally(
        id,
        ts,
        Math.max(lastReceivedTs, knownLastReceivedMs(id))
      );
    } else {
      // O cliente respondeu depois: volta automaticamente.
      clearAnsweredLocally(id);
    }
  }

  function lastMessageWasSentByUs(item) {
    if (!item || item.isPending !== true) return false;

    const sent = messageDateMs(item.lastSendedMessageDate);
    const received = messageDateMs(item.lastReceivedMessageDate);

    if (!sent) return false;
    if (!received) return true;

    return sent >= received;
  }

  function shouldShowAsUnread(item) {
    return Boolean(
      item &&
      item.isPending === true &&
      !lastMessageWasSentByUs(item) &&
      !localAnsweredAfterLastReceived(item)
    );
  }

  function isOptimisticallyRead(id) {
    const key = String(id || "");
    if (!key) return false;

    const until =
      Number(
        state.optimisticReadUntil.get(key) || 0
      );

    if (!until) return false;

    if (Date.now() >= until) {
      state.optimisticReadUntil.delete(key);
      return false;
    }

    return true;
  }

  function suppressPendingTemporarily(id, ms = 20000) {
    const key = String(id || "");
    if (!key) return;

    state.optimisticReadUntil.set(
      key,
      Date.now() + ms
    );
  }

  function requestRefresh(reason = "manual") {
    state.refreshing = true;
    updatePanel();

    window.postMessage({
      source: SOURCE,
      payload: {
        type: "refresh-pending",
        reason
      }
    }, "*");
  }

  function optimisticMarkCurrentRead(explicitId = null) {
    const id = String(explicitId || state.selectedId || "");
    if (!id) return;

    suppressPendingTemporarily(id);

    let changed = false;

    state.conversations =
      state.conversations.map(item => {
        if (
          String(item.id) === id &&
          item.isPending === true
        ) {
          changed = true;
          return {
            ...item,
            isPending: false
          };
        }

        return item;
      });

    if (!changed) return;

    state.pending =
      state.conversations
        .filter(shouldShowAsUnread)
        .sort((a, b) => {
          const da = Date.parse(a.lastMessageDate || 0) || 0;
          const db = Date.parse(b.lastMessageDate || 0) || 0;
          return db - da;
        });

    state.pendingCount = state.pending.length;
    updatePanel();
  }

  document.addEventListener(
    "click",
    e => {
      const target =
        e.target instanceof Element
          ? e.target.closest("button, [role='button']")
          : null;

      if (!target) return;

      const text = normalize(
        target.innerText ||
        target.textContent ||
        target.getAttribute("aria-label") ||
        ""
      );

      if (text.includes("marcar como lida")) {
        setTimeout(() => {
          const id =
            String(state.selectedId || "");

          if (id) {
            markAnsweredLocally(id);
          }

          optimisticMarkCurrentRead();
          requestRefresh("mark-read");
        }, 120);
      }
    },
    true
  );

  function toggleFilter() {
    state.filterActive =
      !state.filterActive;

    if (
      state.filterActive &&
      !state.completed &&
      !state.pending.length
    ) {
      status(
        "Aguarde a API terminar de carregar."
      );
    }

    if (state.filterActive) {
      // Calcula a largura/altura uma única vez ao abrir.
      resetOverlayGeometry();
      renderOverlay();
    } else {
      hideOverlay();
      resetOverlayGeometry();
    }

    updatePanel();
  }

  let statusTimer = null;

  function status(msg) {
    panel();

    const el = panelQuery("#lnl-status");
    if (!el) return;

    el.textContent = msg;

    clearTimeout(statusTimer);

    statusTimer = setTimeout(() => {
      if (el.textContent === msg) {
        el.textContent = "";
      }
    }, 4500);
  }

  // ---------------------------------------------------------
  // Eventos
  // ---------------------------------------------------------

  window.addEventListener(
    "resize",
    () => {
      syncTheme(true);

      updatePanel();

      if (!state.filterActive) {
        return;
      }

      const oldW =
        state.geometryViewportWidth ??
        window.innerWidth;

      const oldH =
        state.geometryViewportHeight ??
        window.innerHeight;

      const changed =
        Math.abs(
          window.innerWidth - oldW
        ) > 24 ||
        Math.abs(
          window.innerHeight - oldH
        ) > 24;

      // Só muda o tamanho quando o próprio navegador realmente mudou
      // de dimensão. Atualizações da API/DOM não mexem mais no painel.
      if (changed) {
        positionOverlay(true);
      }
    },
    { passive: true }
  );

  const themeObserver = new MutationObserver(() => {
    syncTheme();
  });

  const observeTheme = () => {
    if (!document.documentElement) return;

    themeObserver.observe(
      document.documentElement,
      {
        attributes: true,
        attributeFilter: ["class", "style", "data-theme"]
      }
    );

    if (document.body) {
      themeObserver.observe(
        document.body,
        {
          attributes: true,
          attributeFilter: ["class", "style", "data-theme"]
        }
      );
    }
  };

  observeTheme();

  setInterval(() => {
    syncTheme(true);
  }, 1200);

  window.matchMedia?.("(prefers-color-scheme: dark)")
    ?.addEventListener?.("change", () => syncTheme(true));

  function findMessageComposer() {
    const direct = [
      ...document.querySelectorAll(
        'textarea[placeholder*="Mensagem" i], input[placeholder*="Mensagem" i], [contenteditable="true"][data-placeholder*="Mensagem" i], [contenteditable="true"][aria-label*="Mensagem" i]'
      )
    ].filter(el => {
      const r = el.getBoundingClientRect();
      return r.width > 120 && r.height > 20 && r.bottom > 0 && r.top < window.innerHeight;
    });

    if (direct.length) {
      direct.sort((a,b) => b.getBoundingClientRect().top - a.getBoundingClientRect().top);
      return direct[0];
    }

    const fallback = [
      ...document.querySelectorAll('[contenteditable="true"]')
    ].filter(el => {
      const r = el.getBoundingClientRect();
      return r.width > 180 && r.height > 20 && r.top > window.innerHeight * 0.55 && r.bottom <= window.innerHeight + 10;
    });

    fallback.sort((a,b) => b.getBoundingClientRect().top - a.getBoundingClientRect().top);
    return fallback[0] || null;
  }


  window.addEventListener(
    "message",
    e => {
      if (
        e.source !== window ||
        e.data?.source !== SOURCE
      ) return;

      const p = e.data.payload;
      if (!p) return;

      if (p.type === "refresh-start") {
        state.refreshing = true;
        updatePanel();
        return;
      }

      if (p.type === "current-conversation") {
        state.selectedId = String(p.conversationId || "");
        state.orderSummary = null;
        renderOrderSummary();
        renderOverlayList();
        setTimeout(requestOrderSummary, 350);
        return;
      }

      if (p.type === "conversation-messages") {
        applyConversationMessagesState(
          p.conversationId,
          p.messages
        );
        return;
      }

      if (p.type === "outbound-message-sent") {
        const id =
          String(
            p.conversationId ||
            state.selectedId ||
            ""
          );

        if (id) {
          markAnsweredLocally(id);
          optimisticMarkCurrentRead(id);
          status("Respondida • removida das não lidas");

          setTimeout(
            () => requestRefresh("reply-confirm"),
            2500
          );

          setTimeout(
            () => requestRefresh("reply-confirm-late"),
            10000
          );
        }

        return;
      }

      if (p.type === "order-data-summary") {
        if (p.conversationId && state.selectedId && String(p.conversationId) !== String(state.selectedId)) {
          return;
        }
        state.orderSummary = p;
        renderOrderSummary();
        return;
      }

      if (p.type === "panel-status") {
        if (p.message) {
          status(
            String(p.message)
          );
        }
        return;
      }


      if (p.type === "all-conversations") {
        state.conversations =
          (Array.isArray(p.conversations)
            ? p.conversations
            : [])
            .map(item => {
              if (
                item?.isPending === true &&
                isOptimisticallyRead(item?.id)
              ) {
                return {
                  ...item,
                  isPending: false
                };
              }

              return item;
            });

        reconcileAnsweredAtWithApi();

        state.pending =
          state.conversations
            .filter(shouldShowAsUnread)
            .sort((a, b) => {
              const da = Date.parse(a.lastMessageDate || 0) || 0;
              const db = Date.parse(b.lastMessageDate || 0) || 0;
              return db - da;
            });

        state.totalLoaded =
          Number(p.totalLoaded) ||
          state.conversations.length;

        // O contador exibido segue a mesma regra da lista local.
        // O CRM pode manter isPending=true mesmo depois de uma resposta enviada.
        state.pendingCount = state.pending.length;

        if (typeof p.pagesLoaded === "number") {
          state.pagesLoaded = p.pagesLoaded;
        }

        if (p.totalHint != null) {
          state.totalHint = p.totalHint;
        }

        if (p.completed) {
          state.completed = true;
        }

        if (p.refreshedAt) {
          state.lastUpdatedAt = p.refreshedAt;
        }

        state.refreshing = false;
        state.apiError = null;

        updatePanel();
        return;
      }

      if (p.type === "page-progress") {
        state.pagesLoaded =
          Number(p.pages) ||
          state.pagesLoaded;

        if (p.totalHint != null) {
          state.totalHint = p.totalHint;
        }

        state.apiError = null;
        updatePanel();
        return;
      }

      if (p.type === "open-start") {
        state.openingId = String(p.conversationId || "");
        renderOverlayList();
        return;
      }

      if (
        p.type ===
        "mobile-open-invoked"
      ) {
        state.selectedId =
          String(
            p.conversationId ||
            ""
          );

        // No celular o drawer ocupa quase toda a tela.
        // Fecha somente a camada visual após o clique para deixar
        // o chat aparecer. No desktop/tablet o drawer continua
        // persistente entre os cliques.
        state.filterActive = false;
        hideOverlay();
        resetOverlayGeometry();
        updatePanel();

        status(
          "Conversa aberta • toque em 👁 para ver as não lidas novamente."
        );

        return;
      }

      if (p.type === "open-success") {
        state.openingId = null;
        state.selectedId = String(p.conversationId || "");

        // Mantém o drawer aberto em qualquer resolução e apenas
        // atualiza o destaque da conversa que acabou de ser aberta.
        renderOverlayList();

        status("Conversa aberta diretamente, sem usar a pesquisa.");
        return;
      }

      if (p.type === "open-error") {
        state.openingId = null;
        renderOverlayList();

        status(
          `Falha na abertura direta: ${p.message || p.code || "erro desconhecido"}`
        );
        return;
      }

      if (p.type === "api-error") {
        const details = [
          p.message,
          p.status ? `status ${p.status}` : "",
          p.skip != null ? `skip ${p.skip}` : ""
        ]
          .filter(Boolean)
          .join(" • ");

        state.apiError =
          details || "erro desconhecido";

        state.refreshing = false;
        updatePanel();

        status(`Falha API: ${state.apiError}`);
      }
    }
  );

  window.addEventListener(
    "resize",
    () => {
      closeMobileMenu();
      resetOverlayGeometry();
      if (state.filterActive) positionOverlay(true);
      scheduleFloatingPanelPosition();
    },
    { passive: true }
  );

  window.addEventListener(
    "scroll",
    scheduleFloatingPanelPosition,
    { passive: true, capture: true }
  );

  if (window.visualViewport) {
    window.visualViewport.addEventListener(
      "resize",
      () => {
        // Ao abrir/fechar o teclado, recolhe o menu e reposiciona o atalho.
        if (isCompactViewport()) closeMobileMenu();
        scheduleFloatingPanelPosition();
      },
      { passive: true }
    );

    window.visualViewport.addEventListener(
      "scroll",
      scheduleFloatingPanelPosition,
      { passive: true }
    );
  }

  // Inicialização
  panel();
  syncTheme(true);
  updatePanel();

  loadAnsweredAt().then(() => {
    rebuildPending();
    requestRefresh("answered-cache-loaded");
  });
})();
