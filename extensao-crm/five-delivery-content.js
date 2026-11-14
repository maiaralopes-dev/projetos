(() => {
  if (window.__CRM_FIVE_DELIVERY_V15_34__) return;
  window.__CRM_FIVE_DELIVERY_V15_34__ = true;

  const STORAGE_KEY = "five_delivery_pending_v15_34";
  const STATE_NAMES = {
    AC:"Acre", AL:"Alagoas", AP:"Amapá", AM:"Amazonas", BA:"Bahia",
    CE:"Ceará", DF:"Distrito Federal", ES:"Espírito Santo", GO:"Goiás",
    MA:"Maranhão", MT:"Mato Grosso", MS:"Mato Grosso do Sul",
    MG:"Minas Gerais", PA:"Pará", PB:"Paraíba", PR:"Paraná",
    PE:"Pernambuco", PI:"Piauí", RJ:"Rio de Janeiro",
    RN:"Rio Grande do Norte", RS:"Rio Grande do Sul", RO:"Rondônia",
    RR:"Roraima", SC:"Santa Catarina", SP:"São Paulo", SE:"Sergipe",
    TO:"Tocantins"
  };

  const sleep = ms => new Promise(r => setTimeout(r, ms));

  function norm(v) {
    return String(v || "")
      .normalize("NFD")
      .replace(/[\u0300-\u036f]/g, "")
      .replace(/\s+/g, " ")
      .trim()
      .toLowerCase();
  }

  function visible(el) {
    if (!el) return false;
    const r = el.getBoundingClientRect();
    const cs = getComputedStyle(el);
    return r.width > 0 && r.height > 0 && cs.display !== "none" && cs.visibility !== "hidden";
  }

  function allControls() {
    return [...document.querySelectorAll(
      'input:not([type="hidden"]), textarea, select, [role="combobox"], [contenteditable="true"]'
    )].filter(visible);
  }

  function exactTextNodes(texts) {
    const wanted = new Set(texts.map(norm));
    return [...document.querySelectorAll('label, span, p, div, h1, h2, h3, h4')]
      .filter(visible)
      .filter(el => wanted.has(norm(el.textContent)));
  }

  function controlNearLabel(labels) {
    for (const label of exactTextNodes(labels)) {
      if (label.tagName === 'LABEL' && label.htmlFor) {
        const linked = document.getElementById(label.htmlFor);
        if (linked && visible(linked)) return linked;
      }

      let node = label;
      for (let i = 0; node && i < 5; i++, node = node.parentElement) {
        const candidate = node.querySelector?.(
          'input:not([type="hidden"]), textarea, select, [role="combobox"], [contenteditable="true"]'
        );
        if (candidate && visible(candidate) && candidate !== label) return candidate;
      }
    }
    return null;
  }

  function byPlaceholder(placeholders) {
    const wanted = placeholders.map(norm);
    return allControls().find(el => {
      const ph = norm(el.getAttribute?.('placeholder'));
      return ph && wanted.some(x => ph === x || ph.includes(x));
    }) || null;
  }

  function findControl(labels, placeholders = []) {
    return controlNearLabel(labels) || byPlaceholder(placeholders);
  }

  function controlCurrentText(el) {
    if (!el) return "";

    if (
      el instanceof HTMLInputElement ||
      el instanceof HTMLTextAreaElement ||
      el instanceof HTMLSelectElement
    ) {
      return String(el.value || "").trim();
    }

    return String(el.textContent || "").trim();
  }

  function looksLikePlaceholder(value) {
    const t = norm(value);

    return (
      !t ||
      t === "selecione" ||
      t.startsWith("selecione ") ||
      t.startsWith("digite ") ||
      t === "estado" ||
      t === "cidade" ||
      t === "bairro" ||
      t === "rua"
    );
  }

  function hasFilledValue(el) {
    const value = controlCurrentText(el);
    return Boolean(value) && !looksLikePlaceholder(value);
  }

  function manualSource(data, key) {
    return norm(data?.sources?.[key]) === "correcao manual";
  }

  async function fillTextIfNeeded(
    key,
    value,
    labels,
    placeholders,
    formattedValue = value,
    force = false
  ) {
    if (!value) return {key, ok:false, reason:"empty"};

    const el = findControl(labels, placeholders);
    if (!el) return {key, ok:false, reason:"not-found"};

    // Quando o CEP já preencheu o campo, preserva o dado da Five Delivery.
    // Só substitui se o usuário tiver feito uma correção manual no CRM.
    if (!force && hasFilledValue(el)) {
      return {
        key,
        ok:true,
        reason:"kept-cep",
        kept:controlCurrentText(el)
      };
    }

    const ok = nativeSet(el, formattedValue);
    return {
      key,
      ok,
      reason:ok ? "filled" : "unsupported"
    };
  }

  function nativeSet(el, value) {
    if (!el || value == null || value === "") return false;
    const v = String(value);

    try {
      if (el instanceof HTMLInputElement) {
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
        setter?.call(el, v);
        el.dispatchEvent(new Event('input', {bubbles:true}));
        el.dispatchEvent(new Event('change', {bubbles:true}));
        el.dispatchEvent(new Event('blur', {bubbles:true}));
        return true;
      }
      if (el instanceof HTMLTextAreaElement) {
        const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')?.set;
        setter?.call(el, v);
        el.dispatchEvent(new Event('input', {bubbles:true}));
        el.dispatchEvent(new Event('change', {bubbles:true}));
        return true;
      }
      if (el instanceof HTMLSelectElement) {
        return false;
      }
      if (el.isContentEditable) {
        el.textContent = v;
        el.dispatchEvent(new InputEvent('input', {bubbles:true, inputType:'insertText', data:v}));
        return true;
      }
    } catch {}
    return false;
  }

  function formatCpf(d) {
    d = String(d || '').replace(/\D+/g, '');
    return d.length === 11 ? `${d.slice(0,3)}.${d.slice(3,6)}.${d.slice(6,9)}-${d.slice(9)}` : d;
  }

  function formatZip(d) {
    d = String(d || '').replace(/\D+/g, '');
    return d.length === 8 ? `${d.slice(0,5)}-${d.slice(5)}` : d;
  }

  function normalizeBrazilNationalPhone(value) {
    let d = String(value || '')
      .replace(/\D+/g, '');

    if (
      (d.length === 12 || d.length === 13) &&
      d.startsWith('55')
    ) {
      d = d.slice(2);
    }

    if (
      (d.length === 11 || d.length === 12) &&
      d.startsWith('0')
    ) {
      d = d.slice(1);
    }

    if (d.length === 10) {
      const ddd = d.slice(0, 2);
      const subscriber = d.slice(2);

      if (/^[6-9]/.test(subscriber)) {
        d = ddd + '9' + subscriber;
      }
    }

    return d;
  }

  function formatPhone(d) {
    d = normalizeBrazilNationalPhone(d);

    if (d.length === 11) {
      return `(${d.slice(0,2)}) ${d.slice(2,7)}-${d.slice(7)}`;
    }

    if (d.length === 10) {
      return `(${d.slice(0,2)}) ${d.slice(2,6)}-${d.slice(6)}`;
    }

    return d;
  }

  async function setState(control, uf) {
    if (!control || !uf) return false;
    const full = STATE_NAMES[uf] || uf;

    if (control instanceof HTMLSelectElement) {
      const option = [...control.options].find(opt => {
        const t = norm(opt.textContent);
        const val = norm(opt.value);
        return t === norm(uf) || t === norm(full) || val === norm(uf) || val === norm(full);
      });
      if (!option) return false;
      control.value = option.value;
      control.dispatchEvent(new Event('input', {bubbles:true}));
      control.dispatchEvent(new Event('change', {bubbles:true}));
      return true;
    }

    // Combobox custom (Radix/Headless UI/etc.).
    try {
      control.click();
      await sleep(180);
      const options = [...document.querySelectorAll('[role="option"], [data-radix-collection-item], li, [role="menuitem"]')]
        .filter(visible);
      const option = options.find(el => {
        const t = norm(el.textContent);
        return t === norm(full) || t === norm(uf) || t.includes(norm(full));
      });
      if (option) {
        option.click();
        return true;
      }
    } catch {}

    return false;
  }

  let toastHideTimer = null;

  function toast() {
    let host =
      document.getElementById(
        'crm-five-delivery-status-v15-34'
      );

    if (host) return host;

    host =
      document.createElement(
        'div'
      );

    host.id =
      'crm-five-delivery-status-v15-34';

    Object.assign(
      host.style,
      {
        position:'fixed',
        right:'18px',
        top:'82px',
        bottom:'auto',
        zIndex:'2147483647',
        width:'min(310px, calc(100vw - 36px))',
        padding:'10px 38px 10px 12px',
        borderRadius:'10px',
        background:'rgba(31,35,40,.96)',
        color:'#fff',
        border:'1px solid rgba(255,255,255,.10)',
        boxShadow:'0 8px 24px rgba(0,0,0,.20)',
        fontFamily:'Arial, sans-serif',
        fontSize:'12px',
        lineHeight:'1.35',
        opacity:'0',
        transform:'translateY(-6px)',
        transition:'opacity .18s ease, transform .18s ease',
        pointerEvents:'auto'
      }
    );

    const close =
      document.createElement(
        'button'
      );

    close.type =
      'button';

    close.textContent =
      '×';

    close.setAttribute(
      'aria-label',
      'Fechar aviso'
    );

    Object.assign(
      close.style,
      {
        position:'absolute',
        top:'5px',
        right:'7px',
        width:'26px',
        height:'26px',
        padding:'0',
        border:'0',
        borderRadius:'7px',
        background:'transparent',
        color:'rgba(255,255,255,.72)',
        fontSize:'18px',
        lineHeight:'24px',
        cursor:'pointer'
      }
    );

    close.addEventListener(
      'click',
      () => {
        host.style.opacity =
          '0';

        host.style.transform =
          'translateY(-6px)';

        setTimeout(
          () => {
            host.style.display =
              'none';
          },
          190
        );
      }
    );

    host.appendChild(
      close
    );

    document.documentElement.appendChild(
      host
    );

    return host;
  }

  function showStatus(title, detail='') {
    const el =
      toast();

    if (toastHideTimer) {
      clearTimeout(
        toastHideTimer
      );
    }

    el.style.display =
      'block';

    const close =
      el.querySelector(
        'button'
      );

    [...el.childNodes]
      .filter(
        node =>
          node !== close
      )
      .forEach(
        node =>
          node.remove()
      );

    const content =
      document.createElement(
        'div'
      );

    // V16.16: texto puro (sem innerHTML) para mensagens de erro.
    const titleEl =
      document.createElement('div');
    titleEl.style.fontWeight = '700';
    titleEl.style.marginBottom = detail ? '2px' : '0';
    titleEl.textContent = String(title || '');
    content.appendChild(titleEl);

    if (detail) {
      const detailEl =
        document.createElement('div');
      detailEl.style.opacity = '.76';
      detailEl.style.fontSize = '11px';
      detailEl.textContent = String(detail);
      content.appendChild(detailEl);
    }

    el.insertBefore(
      content,
      close || null
    );

    requestAnimationFrame(
      () => {
        el.style.opacity =
          '1';

        el.style.transform =
          'translateY(0)';
      }
    );

    toastHideTimer =
      setTimeout(
        () => {
          el.style.opacity =
            '0';

          el.style.transform =
            'translateY(-6px)';

          setTimeout(
            () => {
              el.style.display =
                'none';
            },
            190
          );
        },
        5000
      );
  }

  async function payload() {
    const result = await chrome.storage.local.get(STORAGE_KEY);
    return result?.[STORAGE_KEY] || null;
  }

  async function markConsumed(data, result) {
    await chrome.storage.local.set({
      [STORAGE_KEY]: {
        ...data,
        pending:false,
        consumedAt:new Date().toISOString(),
        lastResult:result
      }
    });
  }

  async function fillText(key, value, labels, placeholders, formattedValue=value) {
    if (!value) return {key, ok:false, reason:'empty'};
    const el = findControl(labels, placeholders);
    if (!el) return {key, ok:false, reason:'not-found'};
    const ok = nativeSet(el, formattedValue);
    return {key, ok, reason: ok ? 'filled' : 'unsupported'};
  }

  async function run() {
    if (!location.pathname.startsWith('/pedido/cadastrar')) return;

    const data = await payload();
    if (!data?.pending) return;

    const age = Date.now() - Date.parse(data.createdAt || 0);
    if (!Number.isFinite(age) || age > 30 * 60 * 1000) return;

    showStatus('Dados recebidos do CRM', 'Aguardando o formulário da Five Delivery…');

    // Wait for page/form to mount.
    for (let i=0; i<30; i++) {
      if (findControl(['CPF'], ['000.000.000-00'])) break;
      await sleep(300);
    }

    const results = [];

    // Identity first.
    results.push(await fillText('cpf', data.cpf, ['CPF'], ['000.000.000-00'], formatCpf(data.cpf)));
    results.push(await fillText('name', data.name, ['Nome'], ['Nome do Cliente']));
    results.push(await fillText('email', data.email, ['E-mail','Email'], ['E-mail do Cliente','Email do Cliente']));
    results.push(await fillText('phone', data.phone, ['WhatsApp','Whatsapp'], ['WhatsApp','Whatsapp'], formatPhone(data.phone)));

    // CEP first, because Five may auto-complete address from it.
    const zipResult = await fillText('zip', data.zip, ['CEP'], ['00000-000'], formatZip(data.zip));
    results.push(zipResult);

    if (zipResult.ok) {
      await sleep(1600);
    }

    // REGRA V15.34:
    // Quando existe CEP, os dados retornados pela Five/ViaCEP têm prioridade
    // em Rua, Bairro, Cidade e Estado.
    // Assim, uma leitura imperfeita do CRM (ex.: "Ponto de Referência" como cidade)
    // não sobrescreve um endereço correto obtido pelo CEP.
    //
    // Correções MANUAIS feitas pelo usuário na ficha continuam tendo prioridade.

    results.push(
      await fillTextIfNeeded(
        'street',
        data.street,
        ['Rua'],
        ['Digite a rua'],
        data.street,
        manualSource(data, 'street')
      )
    );

    results.push(
      await fillTextIfNeeded(
        'neighborhood',
        data.neighborhood,
        ['Bairro'],
        ['Digite o Bairro','Digite o bairro'],
        data.neighborhood,
        manualSource(data, 'neighborhood')
      )
    );

    // Número e complemento normalmente não vêm do CEP.
    results.push(
      await fillText(
        'number',
        data.number,
        ['Número','Numero'],
        ['Digite o número','Digite o numero']
      )
    );

    results.push(
      await fillText(
        'complement',
        data.complement,
        ['Complemento'],
        ['Digite o complemento']
      )
    );

    results.push(
      await fillTextIfNeeded(
        'city',
        data.city,
        ['Cidade'],
        ['Digite a cidade'],
        data.city,
        manualSource(data, 'city')
      )
    );

    const stateControl =
      findControl(
        ['Estado','UF'],
        ['Digite o estado']
      );

    let stateOk = false;
    let stateReason = 'empty';

    if (data.state) {
      if (
        stateControl &&
        hasFilledValue(stateControl) &&
        !manualSource(data, 'state')
      ) {
        stateOk = true;
        stateReason = 'kept-cep';
      } else {
        stateOk =
          await setState(
            stateControl,
            data.state
          );

        stateReason =
          stateOk
            ? 'filled'
            : 'not-found';
      }
    }

    results.push({
      key:'state',
      ok:stateOk,
      reason:stateReason
    });

    const available =
      results.filter(
        r => r.reason !== 'empty'
      ).length;

    const filled =
      results.filter(
        r => r.ok
      ).length;

    const missed =
      results
        .filter(
          r =>
            r.reason === 'not-found' ||
            r.reason === 'unsupported'
        )
        .map(r => r.key);

    const keptByCep =
      results
        .filter(
          r => r.reason === 'kept-cep'
        )
        .map(r => r.key);

    const result = {
      filled,
      available,
      missed,
      keptByCep,
      at:new Date().toISOString()
    };

    if (filled >= 4) {
      await markConsumed(data, result);
    }

    if (!available) {
      showStatus('Não encontrei dados para preencher', 'Volte ao CRM e envie a ficha novamente.');
      return;
    }

    if (missed.length) {
      showStatus(
        `${filled}/${available} campos preenchidos`,
        `Confira o formulário. Ainda não consegui mapear automaticamente: ${missed.join(', ')}.`
      );
    } else {
      showStatus(
        `${filled} campos preenchidos`,
        keptByCep.length
          ? `CEP preservado em: ${keptByCep.join(', ')}. Confira os dados e finalize manualmente.`
          : 'Confira os dados, escolha Produto/Tratamento/Modo de Envio e finalize manualmente.'
      );
    }
  }

  run().catch(err => {
    showStatus('Erro ao preencher a Five Delivery', String(err?.message || err));
  });
})();
