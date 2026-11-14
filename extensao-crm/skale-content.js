(() => {
  if (window.__CRM_SKALE_V16_21__) return;
  window.__CRM_SKALE_V16_21__ = true;

  const STORAGE_KEY = "skale_order_pending_v16";
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

  function textElements() {
    return [...document.querySelectorAll('button,label,span,p,div,h1,h2,h3,h4,li,[role="option"],[role="button"]')].filter(visible);
  }

  function byText(text, exact = true) {
    const wanted = norm(text);
    const candidates = textElements().filter(el => {
      const t = norm(el.textContent);
      return exact ? t === wanted : t.includes(wanted);
    });
    candidates.sort((a,b) => (a.textContent || '').length - (b.textContent || '').length);
    return candidates[0] || null;
  }

  function clickable(el) {
    if (!el) return null;
    return el.closest('button,[role="button"],[role="option"],li,[cmdk-item],[data-radix-collection-item]') || el;
  }

  function clickText(text, exact = true) {
    const el = clickable(byText(text, exact));
    if (!el || !visible(el)) return false;
    try { el.click(); return true; } catch { return false; }
  }

  function nativeSet(el, value) {
    if (!el || value == null) return false;
    const v = String(value);
    try {
      const proto = el instanceof HTMLTextAreaElement
        ? HTMLTextAreaElement.prototype
        : HTMLInputElement.prototype;
      const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
      if (!setter) return false;

      const old = String(el.value || '');
      el.focus?.();
      setter.call(el, v);

      // React mantém um valueTracker interno. Recuar o tracker para o valor
      // anterior faz o onChange enxergar a alteração programática como nova.
      if (el._valueTracker && typeof el._valueTracker.setValue === 'function') {
        try { el._valueTracker.setValue(old); } catch {}
      }

      try {
        el.dispatchEvent(new InputEvent('input', {
          bubbles: true,
          composed: true,
          inputType: 'insertText',
          data: v
        }));
      } catch {
        el.dispatchEvent(new Event('input', {bubbles:true, composed:true}));
      }
      el.dispatchEvent(new Event('change',{bubbles:true, composed:true}));
      el.dispatchEvent(new Event('blur',{bubbles:true, composed:true}));
      return true;
    } catch {}
    return false;
  }

  function allInputs() {
    return [...document.querySelectorAll('input:not([type="hidden"]),textarea,[role="combobox"]')].filter(visible);
  }

  function fieldByPlaceholder(parts) {
    const wanted = parts.map(norm);
    return allInputs().find(el => {
      const p = norm(el.getAttribute?.('placeholder'));
      return p && wanted.some(w => p === w || p.includes(w));
    }) || null;
  }

  function cleanLabelText(value) {
    return norm(value)
      .replace(/\s*\*\s*$/, '')
      .replace(/\s+/g, ' ')
      .trim();
  }

  function fieldNearLabel(labels) {
    const wanted = labels.map(cleanLabelText);
    const labelNodes = [...document.querySelectorAll('label,[data-label],span,p,div')]
      .filter(visible)
      .filter(el => {
        const txt = cleanLabelText(el.textContent);
        return wanted.some(w => txt === w);
      });

    // Preferência 1: associação HTML real label[for].
    for (const label of labelNodes) {
      if (label.tagName === 'LABEL' && label.htmlFor) {
        const linked = document.getElementById(label.htmlFor);
        if (linked && visible(linked)) return linked;
      }
    }

    const inputs = allInputs();
    let best = null;
    let bestScore = Infinity;

    // Preferência 2: input visualmente imediatamente abaixo do rótulo.
    // Isso é mais estável na Skale do que depender da árvore de divs do React.
    for (const label of labelNodes) {
      const lr = label.getBoundingClientRect();
      for (const input of inputs) {
        const ir = input.getBoundingClientRect();
        const verticalGap = ir.top - lr.bottom;
        if (verticalGap < -8 || verticalGap > 105) continue;

        const overlap = Math.max(0, Math.min(lr.right, ir.right) - Math.max(lr.left, ir.left));
        const centerDelta = Math.abs((lr.left + lr.right) / 2 - (ir.left + ir.right) / 2);
        if (overlap <= 0 && centerDelta > Math.max(180, ir.width * 0.75)) continue;

        const score = Math.max(0, verticalGap) * 4 + centerDelta;
        if (score < bestScore) {
          best = input;
          bestScore = score;
        }
      }
    }

    if (best) return best;

    // Preferência 3: ancestral próximo, usado apenas como fallback.
    for (const label of labelNodes) {
      let node = label.parentElement;
      for (let i=0; node && i<3; i++, node=node.parentElement) {
        const input = [...node.querySelectorAll?.('input:not([type="hidden"]),textarea,[role="combobox"]') || []]
          .find(visible);
        if (input) return input;
      }
    }

    return null;
  }

  function findField(labels, placeholders=[]) {
    return fieldNearLabel(labels) || fieldByPlaceholder(placeholders);
  }

  function digits(v){ return String(v||'').replace(/\D+/g,''); }
  function fmtCpf(v){ const d=digits(v); return d.length===11?`${d.slice(0,3)}.${d.slice(3,6)}.${d.slice(6,9)}-${d.slice(9)}`:v; }
  function fmtCep(v){ const d=digits(v); return d.length===8?`${d.slice(0,5)}-${d.slice(5)}`:v; }
  function fmtPhone(v){
    let d=digits(v); if ((d.length===12||d.length===13)&&d.startsWith('55')) d=d.slice(2);
    if (d.length===10 && /^[6-9]/.test(d.slice(2))) d=d.slice(0,2)+'9'+d.slice(2);
    return d.length===11?`(${d.slice(0,2)}) ${d.slice(2,7)}-${d.slice(7)}`:d;
  }

  let hideTimer=null;
  function toast() {
    let el=document.getElementById('crm-skale-status-v16-21');
    if (el) return el;
    el=document.createElement('div'); el.id='crm-skale-status-v16-21';
    Object.assign(el.style,{position:'fixed',right:'18px',top:'82px',zIndex:'2147483647',width:'min(340px,calc(100vw - 36px))',padding:'12px 14px',borderRadius:'12px',background:'rgba(24,28,34,.97)',color:'#fff',border:'1px solid rgba(255,255,255,.12)',boxShadow:'0 12px 34px rgba(0,0,0,.28)',fontFamily:'Arial,sans-serif',fontSize:'12px',lineHeight:'1.4'});
    document.documentElement.appendChild(el); return el;
  }
  function status(title,detail=''){
    const el=toast(); el.textContent=''; const t=document.createElement('strong'); t.textContent=String(title||''); el.appendChild(t); if(detail){ const d=document.createElement('div'); d.style.opacity='.78'; d.style.marginTop='3px'; d.textContent=String(detail); el.appendChild(d); }
    if(hideTimer) clearTimeout(hideTimer); hideTimer=setTimeout(()=>el.remove(),7000);
  }

  async function payload(){ const r=await chrome.storage.local.get(STORAGE_KEY); return r?.[STORAGE_KEY]||null; }
  async function consume(data,result){ await chrome.storage.local.set({[STORAGE_KEY]:{...data,pending:false,consumedAt:new Date().toISOString(),lastResult:result}}); }

  async function waitFor(fn, timeout=8000, step=200){
    const end=Date.now()+timeout;
    while(Date.now()<end){ const v=fn(); if(v) return v; await sleep(step); }
    return null;
  }

  function stepText(n){ return byText(`Passo ${n} de 4`,false); }

  // Não usa apenas o texto "Novo pedido" para detectar o modal, porque
  // esse mesmo texto também existe no botão da tela principal.
  function modalOpen(){
    return Boolean(
      stepText(1) ||
      (byText('Cliente', true) && byText('Pedido', true) && byText('Pagamento', true) && byText('Revisão', true))
    );
  }

  function findNewOrderButton(){
    const candidates = [...document.querySelectorAll('button,[role="button"],a')].filter(visible);
    const scored = candidates
      .map(el => {
        const t = norm(el.innerText || el.textContent);
        let score = 0;
        if (t === '+ novo pedido') score = 100;
        else if (t === 'novo pedido') score = 95;
        else if (t.endsWith('novo pedido')) score = 80;
        else if (t.includes('novo pedido')) score = 60;
        return {el, score, len:t.length};
      })
      .filter(x => x.score > 0)
      .sort((a,b) => b.score - a.score || a.len - b.len);
    return scored[0]?.el || null;
  }

  async function openNewOrder(){
    if (modalOpen()) return true;

    // A Skale é uma SPA em React e o conteúdo pode aparecer depois do
    // document_idle. Aguarda o botão existir de verdade antes de desistir.
    const btn = await waitFor(() => findNewOrderButton(), 20000, 250);
    if (!btn) return false;

    try {
      btn.scrollIntoView?.({block:'center', inline:'center'});
      btn.click();
    } catch {
      return false;
    }

    return Boolean(await waitFor(() => modalOpen(), 12000, 200));
  }

  function fieldValueMatches(el, value, mode='text') {
    if (!el || value == null) return false;
    const current = String(el.value ?? el.textContent ?? '').trim();
    const wanted = String(value ?? '').trim();
    if (!wanted) return true;
    if (mode === 'digits') return digits(current) === digits(wanted);
    return norm(current) === norm(wanted);
  }

  async function fill(el, value, mode='text') {
    if (!value || !el) return false;
    for (let attempt = 0; attempt < 5; attempt++) {
      try { el.scrollIntoView?.({block:'center', inline:'nearest'}); } catch {}
      nativeSet(el, value);
      await sleep(160 + attempt * 90);
      if (fieldValueMatches(el, value, mode)) return true;

      // Alguns inputs mascarados da Skale recriam o nó no React. Tenta uma
      // digitação pelo mecanismo nativo do navegador antes da próxima rodada.
      try {
        el.focus?.();
        el.select?.();
        if (document.execCommand) {
          document.execCommand('insertText', false, String(value));
          el.dispatchEvent(new Event('change',{bubbles:true, composed:true}));
          el.dispatchEvent(new Event('blur',{bubbles:true, composed:true}));
        }
      } catch {}
      await sleep(140);
      if (fieldValueMatches(el, value, mode)) return true;
    }
    return fieldValueMatches(el, value, mode);
  }

  async function waitField(labels, placeholders=[], timeout=10000) {
    return await waitFor(() => findField(labels, placeholders), timeout, 180);
  }

  async function fillField(labels, placeholders, value, mode='text', timeout=9000) {
    if (!value) return {ok:true, skipped:true, el:null};
    for (let attempt=0; attempt<4; attempt++) {
      const el = await waitField(labels, placeholders, timeout);
      if (!el) return {ok:false, el:null};
      const ok = await fill(el, value, mode);
      if (ok) return {ok:true, el};
      await sleep(260);
    }
    const el = findField(labels, placeholders);
    return {ok:Boolean(el && fieldValueMatches(el,value,mode)), el};
  }

  function checkboxNearText(text){
    const label=byText(text,true) || byText(text,false);
    if(!label) return null;
    let node=label;
    for(let i=0;node&&i<4;i++,node=node.parentElement){
      const cb=node.querySelector?.('input[type="checkbox"]'); if(cb&&visible(cb)) return cb;
      const role=node.querySelector?.('[role="checkbox"]'); if(role&&visible(role)) return role;
    }
    return null;
  }

  async function clickCepLookup(input){
    if(!input) return false;
    let node=input.parentElement;
    for(let i=0;node&&i<3;i++,node=node.parentElement){
      const btn=[...node.querySelectorAll('button')].find(visible);
      if(btn){ try{btn.click(); return true;}catch{} }
    }
    return false;
  }

  async function prepareClient(data){
    const receivedCount = [
      data.cpf, data.phone, data.name, data.email, data.zip,
      data.street, data.number, data.neighborhood, data.state,
      data.complement, data.city
    ].filter(v => String(v || '').trim()).length;
    status('Preparando cliente na Skale', `${receivedCount}/11 dados recebidos do CRM.`);

    // 1) Busca inicial pelo CPF.
    clickText('CPF',true);
    const search=await waitFor(()=>fieldByPlaceholder(['000.000.000-00']),7000,180);
    if(!search) throw new Error('Campo de busca por CPF não encontrado.');
    if(!await fill(search,fmtCpf(data.cpf),'digits')) throw new Error('Não consegui preencher o CPF para busca.');

    const searchButton=await waitFor(()=>clickable(byText('Buscar',true)),5000,150);
    if(!searchButton) throw new Error('Botão Buscar não encontrado.');
    searchButton.click();

    await waitFor(() => {
      if (byText('Cliente não encontrado',false)) return 'not-found';
      const next=clickable(byText('Próxima: Pedido',false));
      if(next && !next.disabled && next.getAttribute('aria-disabled')!=='true') return 'existing';
      return null;
    },12000,200);

    const notFound=Boolean(byText('Cliente não encontrado',false));
    if(!notFound){
      const next=clickable(byText('Próxima: Pedido',false));
      if(next && !next.disabled && next.getAttribute('aria-disabled')!=='true'){
        next.click();
        await waitFor(()=>stepText(2),7000,180);
        return {existing:true,advanced:Boolean(stepText(2))};
      }
      status('Cliente encontrado na Skale','Selecione o cadastro encontrado. A extensão continua assim que o Passo 2 aparecer.');
      await waitFor(()=>stepText(2),20000,300);
      return {existing:true,advanced:Boolean(stepText(2))};
    }

    status('Cliente não encontrado','Formulário detectado. Preenchendo dados recebidos do CRM…');

    const formReady=await waitFor(() => {
      const title=byText('Novo perfil',false);
      const name=findField(['Nome completo'],['Como está no documento']);
      const phone=findField(['Telefone (WhatsApp)','Telefone'],['(00) 00000-0000']);
      const zip=findField(['CEP'],['00000-000']);
      return title && name && phone && zip ? true : null;
    },15000,180);
    if(!formReady) throw new Error('O formulário de Novo Perfil apareceu, mas os campos ainda não ficaram disponíveis.');

    try { byText('Novo perfil',false)?.scrollIntoView?.({block:'start'}); } catch {}
    await sleep(300);

    const failed=[];
    const record = async (label, labels, placeholders, value, mode='text') => {
      if (!value) return;
      const result = await fillField(labels, placeholders, value, mode);
      if (!result.ok) failed.push(label);
    };

    // Re-localiza cada campo a cada escrita. Isso evita referências antigas
    // quando o React da Skale recria os inputs durante máscaras/validações.
    await record('CPF',['CPF'],['000.000.000-00'],fmtCpf(data.cpf),'digits');
    await record('Telefone',['Telefone (WhatsApp)','Telefone'],['(00) 00000-0000'],fmtPhone(data.phone),'digits');
    await record('Nome',['Nome completo'],['Como está no documento'],data.name,'text');

    if(data.email){
      await record('E-mail',['E-mail','Email'],['nome@email.com'],data.email,'text');
    } else {
      const cb=await waitFor(()=>checkboxNearText('Não possui e-mail'),5000,180);
      if(cb){
        if(cb instanceof HTMLInputElement){ if(!cb.checked) cb.click(); }
        else if(cb.getAttribute('aria-checked')!=='true') cb.click();
      }
    }

    await record('CEP',['CEP'],['00000-000'],fmtCep(data.zip),'digits');

    // Dispara a busca de CEP usando uma referência nova do input. A consulta pode
    // recriar os campos de endereço, então todos eles serão localizados de novo depois.
    const zipInput = findField(['CEP'],['00000-000']);
    if(zipInput && data.zip){
      await sleep(250);
      await clickCepLookup(zipInput);
      await sleep(1400);
    }

    // Se a Skale/ViaCEP preencheu um campo, preserva. Caso contrário usa o CRM.
    const addressSpecs = [
      ['Rua',['Rua'],[],data.street,'text'],
      ['Número',['Número','Numero'],[],data.number,'text'],
      ['Bairro',['Bairro'],[],data.neighborhood,'text'],
      ['UF',['UF','Estado'],[],data.state,'text'],
      ['Complemento',['Complemento'],['Apto, bloco'],data.complement,'text'],
      ['Cidade',['Cidade'],[],data.city,'text']
    ];

    for (const [label, labels, placeholders, value, mode] of addressSpecs) {
      if (!value) continue;
      let el = findField(labels, placeholders);
      const current = String(el?.value || '').trim();
      if (current) continue;
      const result = await fillField(labels, placeholders, value, mode);
      if (!result.ok) failed.push(label);
    }

    // Verificação final usando referências novas e comparação tolerante.
    const verifySpecs = [
      ['Telefone',['Telefone (WhatsApp)','Telefone'],['(00) 00000-0000'],data.phone,'digits'],
      ['Nome',['Nome completo'],['Como está no documento'],data.name,'text'],
      ['CEP',['CEP'],['00000-000'],data.zip,'digits'],
      ['Número',['Número','Numero'],[],data.number,'text']
    ];

    for (const [label, labels, placeholders, value, mode] of verifySpecs) {
      if (!value) continue;
      const el = findField(labels, placeholders);
      if (!el || !fieldValueMatches(el,value,mode)) {
        if (!failed.includes(label)) failed.push(label);
      }
    }

    if(failed.length){
      status('Dados parcialmente preenchidos',`Recebi ${receivedCount}/11 dados. Revise: ${failed.join(', ')}. Não avancei automaticamente.`);
      return {existing:false,advanced:false,reason:'fields-not-confirmed',failed,receivedCount};
    }

    status('Cliente preenchido','Dados confirmados na Skale. Avançando para Pedido…');
    const next=await waitFor(()=>clickable(byText('Próxima: Pedido',false)),5000,180);
    if(next && !next.disabled && next.getAttribute('aria-disabled')!=='true'){
      next.click();
      await waitFor(()=>stepText(2),8000,180);
    }
    return {existing:false,advanced:Boolean(stepText(2)),receivedCount};
  }

  async function chooseCombo(placeholderText, optionText){
    const control=fieldByPlaceholder([placeholderText]) || byText(placeholderText,true);
    if(!control) return false;
    try{ clickable(control).click(); }catch{return false;}
    await sleep(250);
    const option=byText(optionText,true) || byText(optionText,false);
    if(!option) return false;
    try{ clickable(option).click(); return true;}catch{return false;}
  }

  async function prepareOrder(data){
    if(!stepText(2)) return {advanced:false};
    if(!data.productMonths){
      status('Cliente pronto','Escolha o produto na Skale. Não identifiquei com segurança se é 2, 4 ou 6 meses.');
      return {advanced:false,reason:'product-needed'};
    }

    const product=`Azzure - ${data.productMonths} Meses`;
    const productOk=await chooseCombo('Pesquisar kit',product);
    if(!productOk){
      status('Escolha o produto',`Não consegui selecionar automaticamente “${product}”.`);
      return {advanced:false,reason:'product-not-found'};
    }
    await sleep(500);

    const logisticsOk=await chooseCombo('Selecione a logística',data.logisticsHint||'Five Delivery');
    if(!logisticsOk){
      status('Produto selecionado','Escolha a transportadora e depois continue para Pagamento.');
      return {advanced:false,reason:'logistics-needed'};
    }

    const next=clickable(byText('Próxima: Pagamento',false));
    if(next){ next.click(); await waitFor(()=>stepText(3),7000); }
    return {advanced:Boolean(stepText(3)),product};
  }

  async function preparePayment(data){
    if(!stepText(3)) return {advanced:false};
    const label=data.paymentHint==='after_pay'?'After Pay':data.paymentHint==='antecipada'?'Antecipada':null;
    if(!label){
      status('Pedido e logística preenchidos','Escolha After Pay ou Antecipada conforme a negociação com o cliente.');
      return {advanced:false,reason:'payment-needed'};
    }
    if(!clickText(label,true)){
      status('Escolha a modalidade',`Não consegui selecionar automaticamente “${label}”.`);
      return {advanced:false,reason:'payment-not-found'};
    }
    await sleep(300);
    const next=clickable(byText('Próxima: Revisão',false));
    if(next){ next.click(); await waitFor(()=>stepText(4),7000); }
    return {advanced:Boolean(stepText(4)),payment:label};
  }

  async function run(){
    if(!location.pathname.startsWith('/pedidos')) return;
    const data=await payload();
    if(!data?.pending) return;
    const age=Date.now()-Date.parse(data.createdAt||0);
    if(!Number.isFinite(age)||age>30*60*1000) return;

    status('Dados recebidos do CRM','Abrindo o Novo Pedido da Skale…');
    if(!await openNewOrder()) throw new Error('Não encontrei o botão “+ Novo Pedido”.');

    const client=await prepareClient(data);
    if(!client.advanced) return;
    const order=await prepareOrder(data);
    if(!order.advanced) return;
    const payment=await preparePayment(data);
    if(!payment.advanced) return;

    status('Pedido preparado na Skale','Revise Cliente, Logística, Venda e anexos. A extensão NÃO clica em “Lançar pedido”.');
    await consume(data,{client,order,payment,readyForReview:true,at:new Date().toISOString()});
  }

  run().catch(err=>status('Erro ao preparar pedido na Skale',String(err?.message||err)));
})();
