(() => {
  if (window.__CRM_ORDER_DATA_V15_34__) return;
  window.__CRM_ORDER_DATA_V15_34__ = true;

  const PAGE_SOURCE = "LNL_V15_34_DIRECT";
  const BRIDGE_SOURCE = "CRM_ORDER_DATA_BRIDGE_V15_34";
  const CACHE_PREFIX = "order_data_v15_34__";
  const AUDIO_TRANSCRIPT_BRIDGE = "CRM_AUDIO_TRANSCRIPT_BRIDGE_V15_34";

  const FIELDS = [
    ["name", "Nome completo", true],
    ["street", "Rua", true],
    ["number", "Número", true],
    ["complement", "Complemento", false],
    ["neighborhood", "Bairro", true],
    ["city", "Cidade", true],
    ["state", "Estado", true],
    ["zip", "CEP", true],
    ["cpf", "CPF", true],
    ["email", "E-mail", false],
    ["phone", "Telefone", true]
  ];

  const REQUIRED_FIELDS =
    FIELDS.filter(([, , required]) => required !== false);

  const state = {
    currentConversationId: null,
    currentConversation: null,
    messagesByConversation: new Map(),
    host: null,
    values: {},
    sources: {},
    edited: new Set(),
    imageValues: {},
    imageAttachmentId: null,
    confirmCardUrl: null,
    confirmCardBlob: null,
    cepAudit: null,
    clientConfirmation: null,
    lastExtractedAt: null
  };

  function normalize(value) {
    return String(value || "")
      .normalize("NFD")
      .replace(/[\u0300-\u036f]/g, "")
      .replace(/\s+/g, " ")
      .trim()
      .toLowerCase();
  }

  function compact(value) {
    return String(value || "")
      .replace(/\s+/g, " ")
      .trim();
  }

  function digits(value) {
    return String(value || "")
      .replace(/\D+/g, "");
  }


  function normalizeBrazilNationalPhone(value) {
    let d = digits(value);

    // Remove código do Brasil quando vier junto do número.
    if (
      (d.length === 12 || d.length === 13) &&
      d.startsWith("55")
    ) {
      d = d.slice(2);
    }

    // Remove zero de operadora/tronco quando vier no início:
    // 0 + DDD + telefone.
    if (
      (d.length === 11 || d.length === 12) &&
      d.startsWith("0")
    ) {
      d = d.slice(1);
    }

    // Celular brasileiro antigo: DDD (2) + número (8).
    // Telefones fixos normalmente começam em 2, 3, 4 ou 5.
    // Celulares antigos começam em 6, 7, 8 ou 9 e precisam
    // receber o nono dígito "9" logo após o DDD.
    if (d.length === 10) {
      const ddd = d.slice(0, 2);
      const subscriber = d.slice(2);

      if (/^[6-9]/.test(subscriber)) {
        d = ddd + "9" + subscriber;
      }
    }

    return d;
  }

  function normalizeBrazilStoredPhone(value) {
    const original = digits(value);

    const hadBrazilCode =
      (original.length === 12 || original.length === 13) &&
      original.startsWith("55");

    const national =
      normalizeBrazilNationalPhone(original);

    if (
      hadBrazilCode &&
      (national.length === 10 || national.length === 11)
    ) {
      return "55" + national;
    }

    return national;
  }


  function titleCase(value) {
    const keepLower = new Set(["da","de","do","das","dos","e"]);
    return compact(value)
      .split(" ")
      .map((part, index) => {
        const low = part.toLowerCase();
        if (index > 0 && keepLower.has(low)) return low;
        if (/^[A-Z]{2}$/.test(part)) return part;
        return low ? low[0].toUpperCase() + low.slice(1) : low;
      })
      .join(" ");
  }

  function formatZip(value) {
    const d = digits(value);
    return d.length === 8
      ? `${d.slice(0,5)}-${d.slice(5)}`
      : compact(value);
  }

  function formatCpf(value) {
    const d = digits(value);
    return d.length === 11
      ? `${d.slice(0,3)}.${d.slice(3,6)}.${d.slice(6,9)}-${d.slice(9)}`
      : compact(value);
  }

  function formatPhone(value) {
    const d = normalizeBrazilNationalPhone(value);
    if (d.length === 11) {
      return `(${d.slice(0,2)}) ${d.slice(2,7)}-${d.slice(7)}`;
    }

    if (d.length === 10) {
      return `(${d.slice(0,2)}) ${d.slice(2,6)}-${d.slice(6)}`;
    }

    return compact(value);
  }

  function cpfValid(value) {
    const d = digits(value);
    if (d.length !== 11 || /^(\d)\1{10}$/.test(d)) return false;

    function check(pos) {
      let sum = 0;
      const end = pos - 1;
      for (let i = 0; i < end; i++) {
        sum += Number(d[i]) * (pos - i);
      }
      let r = (sum * 10) % 11;
      if (r === 10) r = 0;
      return r === Number(d[end]);
    }

    return check(10) && check(11);
  }

  function setIfEmpty(target, sources, key, value, source) {
    value = compact(value);
    if (!value) return;
    if (!target[key]) {
      target[key] = value;
      sources[key] = source;
    }
  }

  function extractLabel(text, labels) {
    const escaped = labels
      .map(x => x.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"))
      .join("|");

    const re = new RegExp(
      `(?:^|\\n|;|,)\\s*(?:${escaped})\\s*[:=-]?\\s*([^\\n;,]+)`,
      "i"
    );

    const m = String(text || "").match(re);
    return m ? compact(m[1]) : "";
  }

  function cleanStreet(value) {
    return compact(value)
      .replace(/^(?:rua|r\.?(?=\s)|avenida|av\.?(?=\s)|estrada|rodovia|travessa|tv\.?(?=\s)|alameda|pra[cç]a|loteamento|sitio|s[ií]tio)\s+/i, match => match.trim() + " ")
      .replace(/\s+bairro\s+.*$/i, "")
      .trim();
  }


  function trimPunctuation(value) {
    return compact(value)
      .replace(/^[,;:.!?…\s]+|[,;:.!?…\s]+$/g, "")
      .trim();
  }

  function canonicalStreet(value) {
    let raw = trimPunctuation(value);
    if (!raw) return "";

    raw = raw
      .replace(/^(?:rua|logradouro|endereco|endereço)\s*[:=-]\s*/i, "")
      .replace(/^av(?:enida)?\.?\s*[:=-]?\s*/i, "Av. ")
      .replace(/^r(?:ua)?\.?\s*[:=-]?\s*/i, "Rua ")
      .replace(/^estrada\s*[:=-]?\s*/i, "Estrada ")
      .replace(/^rodovia\s*[:=-]?\s*/i, "Rodovia ")
      .replace(/^travessa\s*[:=-]?\s*/i, "Travessa ")
      .replace(/^tv\.?(?=\s)\s*[:=-]?\s*/i, "Travessa ")
      .replace(/^alameda\s*[:=-]?\s*/i, "Alameda ")
      .replace(/^pra[cç]a\s*[:=-]?\s*/i, "Praça ")
      .replace(/\s+/g, " ")
      .trim();

    let formatted = titleCase(raw)
      .replace(/^Av\.\s*/i, "Av. ")
      .replace(/^Rua\s+/i, "Rua ")
      .replace(/^Estrada\s+/i, "Estrada ")
      .replace(/^Rodovia\s+/i, "Rodovia ")
      .replace(/^Travessa\s+/i, "Travessa ")
      .replace(/^Alameda\s+/i, "Alameda ")
      .replace(/^Praça\s+/i, "Praça ");

    // Cliente às vezes escreve "rua travessa X".
    // Mantém apenas o tipo específico do logradouro.
    formatted = formatted
      .replace(/^Rua\s+Travessa\s+/i, "Travessa ")
      .replace(/^Rua\s+Avenida\s+/i, "Avenida ")
      .replace(/^Rua\s+Av\.\s+/i, "Av. ")
      .replace(/^Rua\s+Estrada\s+/i, "Estrada ")
      .replace(/^Rua\s+Rodovia\s+/i, "Rodovia ")
      .replace(/^Rua\s+Alameda\s+/i, "Alameda ")
      .replace(/^Rua\s+Praça\s+/i, "Praça ");

    return formatted;
  }

  function cleanNeighborhoodValue(value) {
    return titleCase(
      compact(value)
        .replace(/^bairro\s*[:.=-]?\s*/i, "")
        .replace(/^b(?=[.:\s])\.?\s*[:.=-]?\s*/i, "")
        .trim()
    );
  }

  function cleanSimpleField(value, labels = []) {
    let raw = compact(value);

    for (const label of labels) {
      const escaped = label.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
      raw = raw.replace(
        new RegExp(`^${escaped}\\s*[:=-]?\\s*`, "i"),
        ""
      );
    }

    return trimPunctuation(raw);
  }

  function normalizeComplement(value) {
    let raw = compact(value);
    if (!raw) return "";

    const parts = [];

    const apt = raw.match(
      /\b(?:apt|apto|apartamento)\.?\s*[:=-]?\s*([A-Za-z0-9/-]+)/i
    )?.[1];

    const tower = raw.match(
      /\b(?:torre)\s*[:=-]?\s*([A-Za-z0-9/-]+)/i
    )?.[1];

    const block = raw.match(
      /\b(?:bloco)\s*[:=-]?\s*([A-Za-z0-9/-]+)/i
    )?.[1];

    const house = raw.match(
      /\b(?:casa)\s*[:=-]?\s*([A-Za-z0-9/-]+)/i
    )?.[1];

    const room = raw.match(
      /\b(?:sala)\s*[:=-]?\s*([A-Za-z0-9/-]+)/i
    )?.[1];

    if (apt) parts.push(`Apt ${apt}`);
    if (tower) parts.push(`Torre ${tower}`);
    if (block) parts.push(`Bloco ${block}`);
    if (house) parts.push(`Casa ${house}`);
    if (room) parts.push(`Sala ${room}`);

    return parts.join(", ");
  }

  function parseNumberValue(value) {
    const raw = compact(value);

    const number =
      raw.match(/(?:^|\s)(\d+[A-Za-z0-9/-]*)(?=\s|$)/)?.[1] ||
      "";

    const complement =
      normalizeComplement(raw);

    return {
      number,
      complement
    };
  }

  function cleanNameCandidate(value) {
    return trimPunctuation(value)
      .replace(/\s+/g, " ")
      .trim();
  }

  function nameSourceScore(source) {
    const s = normalize(source || "");
    if (!s) return 0;
    if (s.includes("nome informado")) return 110;
    if (s.includes("nome antes da linha cpf") || s.includes("nome antes do cpf")) return 105;
    if (s.includes("nome apos cpf") || s.includes("nome após cpf")) return 102;
    if (s.includes("resposta estruturada")) return 95;
    if (s.includes("nome antes do e-mail") || s.includes("nome antes do email")) return 92;
    if (s.includes("audio")) return 82;
    if (s.includes("mensagem")) return 80;
    if (s.includes("nome preservado")) return 88;
    if (s.includes("perfil do crm")) return 50;
    return 60;
  }

  function preferName(target, sources, candidate, source) {
    const next = cleanNameCandidate(candidate);
    if (!looksLikeHumanName(next) || isGenericConfirmationPhrase(next) || isPaymentDecisionPhrase(next)) return;

    const current = cleanNameCandidate(target.name || "");

    if (!current) {
      target.name = titleCase(next);
      sources.name = source;
      return;
    }

    const a = normalize(current);
    const b = normalize(next);

    if (a === b) return;

    const aWords = a.split(/\s+/).filter(Boolean);
    const bWords = b.split(/\s+/).filter(Boolean);
    const currentScore = nameSourceScore(sources.name);
    const nextScore = nameSourceScore(source);

    const nextIsFullerVersion =
      b.startsWith(a + " ") ||
      b.endsWith(" " + a) ||
      (
        bWords.length > aWords.length &&
        aWords.every(word => bWords.includes(word))
      );

    // V15.47: um nome confiável não pode ser substituído por uma frase solta
    // só porque apareceu mais tarde. Fontes explícitas ganham de heurísticas,
    // e uma versão mais completa só substitui quando a fonte é ao menos tão forte.
    if (
      nextScore > currentScore ||
      (nextIsFullerVersion && nextScore >= currentScore) ||
      (sources.name === "perfil do CRM" && nextScore >= currentScore)
    ) {
      target.name = titleCase(next);
      sources.name = source;
    }
  }

  function standaloneNameCandidate(text) {
    const raw = String(text || "")
      .replace(/\r/g, "\n")
      .trim();

    const lines = raw
      .split(/\n+/)
      .map(cleanNameCandidate)
      .filter(Boolean);

    for (const line of lines.slice(0, 2)) {
      if (looksLikeHumanName(line)) {
        return line;
      }
    }

    const compacted = compact(raw);

    const firstSentence = compacted.match(
      /^([A-Za-zÀ-ÖØ-öø-ÿ'’ -]{5,90})[.,;]\s+(?=(?:r\.?(?=\s)|rua|av\.?(?=\s)|avenida|estrada|bairro|b\.?(?=\s))\b)/i
    )?.[1];

    if (
      firstSentence &&
      looksLikeHumanName(firstSentence)
    ) {
      return firstSentence;
    }

    return "";
  }

  function repairFragmentEmail(text, email) {
    const e = compact(email).toLowerCase();
    const local = e.split("@")[0] || "";

    if (!/^\d+$/.test(local)) {
      return e;
    }

    const escaped =
      e.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

    const match = String(text || "").match(
      new RegExp(
        `([A-Za-zÀ-ÖØ-öø-ÿ'’]+(?:\\s+[A-Za-zÀ-ÖØ-öø-ÿ'’]+){1,3})\\s+(${escaped})`,
        "i"
      )
    );

    if (!match?.[1]) return e;

    const prefix = cleanNameCandidate(match[1]);

    if (
      !looksLikeHumanName(prefix) ||
      /\b(?:meu|email|e-mail|contato|whatsapp)\b/i.test(prefix)
    ) {
      return e;
    }

    const joined =
      normalize(prefix)
        .replace(/[^a-z0-9]+/g, "") +
      e;

    return joined;
  }

  function contentWords(value) {
    const ignored =
      new Set([
        "rua","r","avenida","av","estrada","rodovia",
        "travessa","tv","alameda","praca","de","da","do","das","dos"
      ]);

    return normalize(value)
      .replace(/[^a-z0-9 ]+/g, " ")
      .split(/\s+/)
      .filter(Boolean)
      .filter(word => !ignored.has(word));
  }

  function overlapRatio(a, b) {
    const aa = new Set(contentWords(a));
    const bb = new Set(contentWords(b));

    if (!aa.size || !bb.size) return 0;

    let common = 0;

    for (const word of aa) {
      if (bb.has(word)) common += 1;
    }

    return common /
      Math.max(
        1,
        Math.min(aa.size, bb.size)
      );
  }

  function hasStreetType(value) {
    return /^(?:rua|r\.?(?=\s)|avenida|av\.?(?=\s)|estrada|rodovia|travessa|tv\.?(?=\s)|alameda|pra[cç]a)\b/i.test(
      compact(value)
    );
  }

  function shouldReplaceStreetWithCep(current, cepStreet) {
    const cur = compact(current);
    const cep = compact(cepStreet);

    if (!cep) return false;
    if (!cur) return true;

    if (
      /\b(?:cpf|cep|bairro|cidade|estado)\b/i.test(cur)
    ) {
      return true;
    }

    const ratio =
      overlapRatio(cur, cep);

    if (ratio < 0.45) {
      return true;
    }

    if (
      cur.length >
      cep.length * 1.45
    ) {
      return true;
    }

    // Se o CEP tem o tipo oficial do logradouro e o valor atual não,
    // só substitui quando o texto do CEP parece limpo.
    if (
      !hasStreetType(cur) &&
      hasStreetType(cep) &&
      !/\s+-\s+de\s+/i.test(cep) &&
      cep.length < 90
    ) {
      return true;
    }

    return false;
  }



  function isGenericConfirmationPhrase(value) {
    const n = normalize(value);
    if (!n) return false;

    return /^(?:tudo\s+correto|tudo\s+certo|esta\s+tudo\s+correto|est[aá]\s+tudo\s+certo|dados\s+corretos|dados\s+certos|pode\s+confirmar|pode\s+finalizar|pode\s+mandar|pode\s+seguir|confirmo|confirmado|sim|ok|okay|certo|correto|perfeito|beleza|fechado|combinado)(?:[.!?\s]+.*)?$/i.test(n);
  }

  function isPaymentDecisionPhrase(value) {
    const n = normalize(value);
    if (!n) return false;

    return /(?:^|\b)(?:vou\s+pagar|pagar\s+com|pagamento|forma\s+de\s+pagamento|pix|cartao|cartão|boleto|dinheiro|parcela|parcelado|parcelar|a\s+vista|à\s+vista)(?:\b|$)/i.test(n) ||
      /^(?:quero|vou\s+querer|pode\s+ser|prefiro|escolho)\b.*\b(?:pix|cartao|cartão|boleto|dinheiro|parcela|pagamento)\b/i.test(n);
  }

  function looksLikeHumanName(value) {
    const raw = compact(value);
    if (!raw || raw.length < 5 || raw.length > 90) return false;
    if (/\d|@|https?:\/\//i.test(raw)) return false;

    const normalized = normalize(raw);

    // Cabeçalhos de documentos, empresas e comprovantes não são nomes de pessoa.
    if (
      /\b(?:comprovante|transferencia|transferência|fatura|conta\s+de|nota\s+fiscal|documento\s+auxiliar|danf|danf3e|energia|equatorial|companhia|saneamento|corsan|cnpj|total\s+a\s+pagar|mes\/ano|mês\/ano|grupo\s+e\s+subgrupo|classificacao|classificação|tarifa|codigo\s+do\s+imovel|código\s+do\s+imóvel|localizacao|localização|convenio|convênio)\b/i.test(normalized)
    ) {
      return false;
    }

    // Respostas de confirmação nunca podem virar nome do cliente.
    if (isGenericConfirmationPhrase(raw) || isPaymentDecisionPhrase(raw)) {
      return false;
    }

    // Perguntas e frases de atendimento nunca podem virar nome.
    // Ex.: "Quantos dia leva para chegar".
    if (
      /^(?:quanto|quantos|quantas|qual|quais|quando|onde|como|porque|por que|pra que|para que)\b/i.test(normalized) ||
      /\b(?:leva|demora|chega|chegar|entrega|prazo|frete|valor|preco|preço|custa)\b/i.test(normalized) ||
      /^(?:eu gostaria|gostaria|quero saber|queria saber|me fala|me diga|pode me dizer)\b/i.test(normalized)
    ) {
      return false;
    }

    // Frases locacionais/conversacionais também não são nomes.
    // Ex.: "E em Piúma Espírito Santo".
    if (/^(?:e\s+)?em\s+[A-Za-zÀ-ÖØ-öø-ÿ'’ .-]+$/i.test(raw)) {
      return false;
    }

    // Nome exato de estado brasileiro não pode virar nome de pessoa.
    // Ex.: "Rio de Janeiro" em uma resposta separada é estado, não nome.
    if (stateToUf(raw)) {
      return false;
    }

    // Endereço/logradouro nunca deve virar nome do cliente.
    // Ex.: "rua travessa João de Freitas Assumpção".
    if (
      /^(?:rua|r\.?|avenida|av\.?|travessa|tv\.?|estrada|rodovia|alameda|pra[cç]a|logradouro|bairro|cidade|estado|cep|cpf|numero|número)\b/i.test(raw)
    ) {
      return false;
    }

    // Evita frases conversacionais/metalinguísticas sendo interpretadas como nome.
    // Ex.: "Esse aí é o meu nome de CPF", "este é meu CPF", "qual o seu nome".
    if (
      /^(?:ok|okay|ta ok|ta bom|ta certo|ta beleza|correto|certo|perfeito|beleza|entendi|combinado|sim|nao|não|obg|vlw|obrigad[oa]|valeu|fechou)(?:\s+|$)/i.test(normalized) ||
      /^(?:bom dia|boa tarde|boa noite|tenho interesse|pode ser|quero sim|em relacionamento|sem nome|novo contato|obg pela|obrigad[oa] pela)(?:\s+|$)/i.test(normalized) ||
      /^(?:aqui\s+)?n[aã]o\s+tem\s+(?:rua|n[uú]mero|numero|cep|bairro|complemento)(?:\s+|$)/i.test(normalized) ||
      /^(?:aqui|ali|lá|la)\b.*\b(?:rua|bairro|cidade|cep|endereco|endereço)\b/i.test(normalized) ||
      /\b(?:meu|seu|esse|este|isso|aqui|ai|aí)\b.*\b(?:nome|cpf|cep|cidade|estado|email|e-mail)\b/i.test(normalized) ||
      /\b(?:nome|cpf|cep|cidade|estado|email|e-mail)\b.*\b(?:meu|seu|esse|este|isso)\b/i.test(normalized) ||
      /^(?:qual|como|esse|este|isso)\b/i.test(normalized)
    ) {
      return false;
    }

    const stop = new Set([
      "sim","nao","não","ok","okay","ta ok","ta bom","ta certo","correto","certo","perfeito","beleza",
      "ola","olá","oi","obg","vlw","obrigado","obrigada","valeu","entendi","combinado","fechou",
      "bom dia","boa tarde","boa noite","4 meses","2 meses","3 meses",
      "quero","quero sim","tenho interesse","pode ser",
      "em relacionamento","sem nome","cliente","contato","desconhecido",
      "conversa atual","lead","novo contato"
    ]);
    if (stop.has(normalized)) return false;

    const parts = raw.split(/\s+/).filter(Boolean);
    if (parts.length < 2 || parts.length > 8) return false;

    return parts.every(part =>
      /^[A-Za-zÀ-ÖØ-öø-ÿ'’-]+$/.test(part)
    );
  }

  function visible(el) {
    if (!(el instanceof Element)) return false;
    const r = el.getBoundingClientRect();
    const cs = getComputedStyle(el);
    return r.width > 0 && r.height > 0 && cs.display !== "none" && cs.visibility !== "hidden";
  }

  function exactLabelNodes(names) {
    const wanted = new Set(names.map(normalize));
    return [...document.querySelectorAll("label,span,div,p")]
      .filter(visible)
      .filter(el => wanted.has(normalize(el.textContent)));
  }

  function textNearLabel(labelNames) {
    for (const label of exactLabelNodes(labelNames)) {
      const row = label.parentElement;
      if (!row) continue;

      const values = [...row.querySelectorAll("span,div,p,a,input")]
        .filter(visible)
        .map(el => {
          if (el instanceof HTMLInputElement) return compact(el.value);
          return compact(el.textContent);
        })
        .filter(Boolean)
        .filter(v => !labelNames.map(normalize).includes(normalize(v)))
        .filter(v => normalize(v) !== normalize(label.textContent));

      for (const value of values) {
        if (value.length <= 140) return value;
      }
    }
    return "";
  }

  function domProfileData() {
    const out = {};

    const name = textNearLabel(["Nome"]);
    if (name && normalize(name) !== "nome") out.name = name;

    const email = textNearLabel(["E-mail","Email"]);
    if (email && /@/.test(email)) out.email = email;

    const phone = textNearLabel(["Telefone","Celular"]);
    if (phone && digits(phone).length >= 10) out.phone = phone;

    if (!out.name) {
      const headerCandidates = [
        ...document.querySelectorAll(
          'header h1, header h2, header h3, [class*="header" i] h1, [class*="header" i] h2'
        )
      ].filter(visible);

      for (const el of headerCandidates) {
        const value = compact(el.textContent);
        if (looksLikeHumanName(value)) {
          out.name = value;
          break;
        }
      }
    }

    return out;
  }

  const STATE_NAME_TO_UF = new Map([
    ["acre","AC"],
    ["alagoas","AL"],
    ["amapa","AP"],
    ["amazonas","AM"],
    ["bahia","BA"],
    ["ceara","CE"],
    ["distrito federal","DF"],
    ["espirito santo","ES"],
    ["espirito santos","ES"],
    ["goias","GO"],
    ["maranhao","MA"],
    ["mato grosso","MT"],
    ["mato grosso do sul","MS"],
    ["minas gerais","MG"],
    ["para","PA"],
    ["paraiba","PB"],
    ["parana","PR"],
    ["pernambuco","PE"],
    ["piaui","PI"],
    ["rio de janeiro","RJ"],
    ["rio grande do norte","RN"],
    ["rio grande do sul","RS"],
    ["rondonia","RO"],
    ["roraima","RR"],
    ["santa catarina","SC"],
    ["sao paulo","SP"],
    ["sergipe","SE"],
    ["tocantins","TO"]
  ]);

  function splitCityStateTail(value) {
    const raw = trimPunctuation(value);
    if (!raw) return { city: "", state: "" };

    const normalized = normalize(raw);
    const entries = [...STATE_NAME_TO_UF.entries()]
      .sort((a, b) => b[0].length - a[0].length);

    for (const [stateName, uf] of entries) {
      if (normalized === stateName) {
        return { city: "", state: uf };
      }

      if (normalized.endsWith(` ${stateName}`)) {
        const wordsToRemove = stateName.split(/\s+/).length;
        const words = raw.split(/\s+/);
        const city = trimPunctuation(words.slice(0, -wordsToRemove).join(" "));
        if (city) return { city: titleCase(city), state: uf };
      }
    }

    const ufTail = raw.match(/^(.*?)\s+(AC|AL|AP|AM|BA|CE|DF|ES|GO|MA|MT|MS|MG|PA|PB|PR|PE|PI|RJ|RN|RS|RO|RR|SC|SP|SE|TO)[.!?,;:]*$/i);
    if (ufTail?.[1]) {
      return {
        city: titleCase(trimPunctuation(ufTail[1])),
        state: ufTail[2].toUpperCase()
      };
    }

    return { city: raw, state: "" };
  }

  function stateToUf(value) {
    const raw = compact(value);
    if (!raw) return "";

    const validUfs = new Set([
      "AC","AL","AP","AM","BA","CE","DF","ES","GO","MA","MT","MS","MG",
      "PA","PB","PR","PE","PI","RJ","RN","RS","RO","RR","SC","SP","SE","TO"
    ]);

    // UF pontuada, muito comum em respostas:
    // S.P -> SP, R.J -> RJ, R.S -> RS.
    const dotted = raw.match(
      /(?:^|[^A-Za-z])([A-Za-z])\s*\.\s*([A-Za-z])\.?(?:$|[^A-Za-z])/i
    );
    if (dotted) {
      const uf = (dotted[1] + dotted[2]).toUpperCase();
      if (validUfs.has(uf)) return uf;
    }

    const direct = raw.match(
      /\b(AC|AL|AP|AM|BA|CE|DF|ES|GO|MA|MT|MS|MG|PA|PB|PR|PE|PI|RJ|RN|RS|RO|RR|SC|SP|SE|TO)\b/i
    );
    if (direct) return direct[1].toUpperCase();

    const normalized = normalize(raw);

    if (normalized === "espirito santos") {
      return "ES";
    }

    // "para" sem acento é uma preposição extremamente comum.
    // Só aceitamos Pará por nome quando o texto original contém acento
    // ou quando vem explicitamente rotulado como estado.
    if (normalized === "para") {
      if (
        /pará/i.test(raw) ||
        /^(?:estado|uf)\s*[:.=-]?\s*para$/i.test(raw)
      ) {
        return "PA";
      }
      return "";
    }

    return STATE_NAME_TO_UF.get(normalized) || "";
  }

  function isCpfOnly(value) {
    return /^(?:\d{3}[.\s]?\d{3}[.\s]?\d{3}[-\s]?\d{2})$/.test(compact(value));
  }

  function isZipOnly(value) {
    return /^(?:\d{5}[-.\s]?\d{3})$/.test(compact(value));
  }

  function isEmailOnly(value) {
    return /^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}$/i.test(compact(value));
  }

  function isOrderDataRequest(text) {
    const n = normalize(text);

    let score = 0;
    if (n.includes("nome completo")) score += 1;
    if (n.includes("rua")) score += 1;
    if (n.includes("bairro")) score += 1;
    if (n.includes("cidade")) score += 1;
    if (n.includes("cep")) score += 1;
    if (n.includes("cpf")) score += 1;
    if (n.includes("e-mail") || n.includes("email")) score += 1;

    return score >= 4;
  }

  function allConversationMessages() {
    const id = state.currentConversationId;
    const map = id ? state.messagesByConversation.get(String(id)) : null;
    if (!map) return [];

    return [...map.values()]
      .filter(
        m =>
          compact(m.text) ||
          (Array.isArray(m.attachments) && m.attachments.length)
      )
      .sort((a,b) => {
        const ta = Date.parse(a.createdAt || "") || 0;
        const tb = Date.parse(b.createdAt || "") || 0;
        if (ta !== tb) return ta - tb;
        return String(a.id || "").localeCompare(String(b.id || ""));
      });
  }

  function relevantReceivedMessages() {
    const all = allConversationMessages();
    if (!all.length) return [];

    // Usa somente respostas recebidas depois do ÚLTIMO pedido de dados.
    // Isso evita que "ok correto", conversas antigas, preços etc. contaminem a ficha.
    let boundaryIndex = -1;

    for (let i = 0; i < all.length; i++) {
      const m = all[i];
      if (m.received === false && isOrderDataRequest(m.text)) {
        boundaryIndex = i;
      }
    }

    return all
      .slice(boundaryIndex + 1)
      .filter(m => m.received === true)
      .filter(
        m =>
          compact(m.text) ||
          (Array.isArray(m.attachments) && m.attachments.length)
      );
  }

  function isImageAttachment(att) {
    if (!att || typeof att !== "object") return false;

    const type =
      normalize(att.type || "");

    const mime =
      String(att.mimeType || "")
        .toLowerCase();

    const file =
      String(att.fileName || att.url || "")
        .split("?")[0]
        .toLowerCase();

    return (
      type === "image" ||
      type === "imagem" ||
      mime.startsWith("image/") ||
      /\.(?:png|jpe?g|webp|gif|bmp)$/i.test(file)
    );
  }

  function customerImageAttachments() {
    const found = [];
    const seen = new Set();

    for (const message of relevantReceivedMessages()) {
      for (const att of message?.attachments || []) {
        if (!isImageAttachment(att)) continue;

        const url =
          compact(att.url);

        if (!url) continue;

        const key =
          String(att.id || url);

        if (seen.has(key)) continue;
        seen.add(key);

        found.push({
          ...att,
          id: key,
          messageId:
            String(message.id || ""),
          createdAt:
            message.createdAt || null
        });
      }
    }

    return found;
  }

  function parseConversationalTranscript(text, out, sources, isAudioTranscript = false) {
    const raw = compact(text);
    if (!raw) return;

    const source = isAudioTranscript
      ? "áudio transcrito"
      : "mensagem";

    // Ex.: "O meu nome é Adelson José Viana." / "Meu nome é ..."
    const nameMatch = raw.match(
      /\b(?:o\s+meu\s+nome\s+e|o\s+meu\s+nome\s+é|meu\s+nome\s+e|meu\s+nome\s+é|o\s+nome\s+e|o\s+nome\s+é)\s+([A-Za-zÀ-ÖØ-öø-ÿ'’ -]{5,90})(?:[.!?]|$)/i
    );

    if (nameMatch?.[1]) {
      const spokenName =
        trimPunctuation(
          nameMatch[1]
        );

      if (
        looksLikeHumanName(
          spokenName
        )
      ) {
        out.name =
          titleCase(
            spokenName
          );

        sources.name =
          source;
      }
    }

    // Ex.: "O nome da cidade é Venda Nova do Imigrante."
    const cityMatch = raw.match(
      /\b(?:o\s+nome\s+da\s+cidade\s+(?:e|é)|a\s+cidade\s+(?:e|é)|cidade\s+(?:e|é))\s+([A-Za-zÀ-ÖØ-öø-ÿ'’ -]{2,90})(?:[.!?]|$)/i
    );

    if (cityMatch?.[1]) {
      const city = stripTrailingState(
        trimPunctuation(cityMatch[1])
      );

      if (city) {
        out.city = titleCase(city);
        sources.city = source;
      }
    }

    // Estado citado naturalmente no áudio, por exemplo:
    // "Eu moro aqui no Espírito Santo".
    const stateInfo = stateMention(raw);
    if (stateInfo?.uf && !out.state) {
      out.state = stateInfo.uf;
      sources.state = source;
    }

    // Alguns áudios são transcritos apenas com o nome, sem frase introdutória.
    if (
      isAudioTranscript &&
      looksLikeHumanName(raw)
    ) {
      out.name =
        titleCase(
          trimPunctuation(raw)
        );

      sources.name =
        source;
    }
  }

  function parseFormStyleReply(text, out, sources) {
    const originalLines =
      String(text || "")
        .replace(/\r/g, "\n")
        .split(/\n+/)
        .map(compact)
        .filter(Boolean);

    if (!originalLines.length) return;

    // 1) Formulário com rótulos explícitos.
    let explicitFields = 0;

    for (const line of originalLines) {
      let m = null;

      m = line.match(
        /^(?:seu\s+)?nome\s+completo\s*[:=-]\s*(.+)$/i
      );
      if (m?.[1]) {
        preferName(
          out,
          sources,
          m[1],
          "resposta estruturada"
        );
        explicitFields += 1;
        continue;
      }

      m = line.match(
        /^(?:rua|logradouro|endereco|endereço)\s*[:=-]\s*(.+)$/i
      );
      if (m?.[1]) {
        let streetRaw =
          m[1]
            .split(
              /\s+(?=(?:n[uú]mero|numero|nº|n°|bairro|cidade|estado|uf|cep|cpf)\b)/i
            )[0];

        // Ex.: "Rua Pastor Manoel de Brito, 381. Bairro Bocaina, ..."
        // O número após vírgula não faz parte do logradouro.
        const commaNumber = streetRaw.match(
          /^(.+?),\s*(\d{1,6}[A-Za-z]?)\b/
        );

        if (commaNumber) {
          streetRaw = commaNumber[1];

          setIfEmpty(
            out,
            sources,
            "number",
            commaNumber[2],
            "resposta estruturada"
          );
        }

        setIfEmpty(
          out,
          sources,
          "street",
          canonicalStreet(streetRaw),
          "resposta estruturada"
        );

        explicitFields += 1;
        continue;
      }

      m = line.match(
        /^(?:n[uú]mero|numero|nº|n°)\s*[:=-]\s*(.+)$/i
      );
      if (m?.[1]) {
        const parsed =
          parseNumberValue(m[1]);

        if (parsed.number) {
          setIfEmpty(
            out,
            sources,
            "number",
            parsed.number,
            "resposta estruturada"
          );
        }

        if (parsed.complement) {
          setIfEmpty(
            out,
            sources,
            "complement",
            parsed.complement,
            "resposta estruturada"
          );
        }

        explicitFields += 1;
        continue;
      }

      m = line.match(
        /^(?:complemento)\s*[:=-]\s*(.+)$/i
      );
      if (m?.[1]) {
        setIfEmpty(
          out,
          sources,
          "complement",
          trimPunctuation(m[1]),
          "resposta estruturada"
        );
        explicitFields += 1;
        continue;
      }

      m = line.match(
        /^(?:bairro|b(?=[.:\s])\.?)\s*[:.=-]?\s*(.+)$/i
      );
      if (m?.[1]) {
        setIfEmpty(
          out,
          sources,
          "neighborhood",
          cleanNeighborhoodValue(
            m[1]
          ),
          "resposta estruturada"
        );
        explicitFields += 1;
        continue;
      }

      m = line.match(
        /^cidade\s*[:=-]\s*(.+)$/i
      );
      if (m?.[1]) {
        const cityValue =
          stripTrailingState(
            cleanSimpleField(
              m[1],
              ["cidade"]
            )
          );

        if (cityValue) {
          setIfEmpty(
            out,
            sources,
            "city",
            titleCase(cityValue),
            "resposta estruturada"
          );
        }

        explicitFields += 1;
        continue;
      }

      m = line.match(
        /^(?:estado|uf)\s*[:=-]\s*(.+)$/i
      );
      if (m?.[1]) {
        const uf =
          stateToUf(
            cleanSimpleField(
              m[1],
              ["estado","uf"]
            )
          );

        if (uf) {
          setIfEmpty(
            out,
            sources,
            "state",
            uf,
            "resposta estruturada"
          );
        }

        explicitFields += 1;
        continue;
      }

      m = line.match(
        /^cep(?:\s+da\s+rua)?\s*[:=-]\s*(.+)$/i
      );
      if (m?.[1]) {
        const zip = findZip(m[1]);

        if (zip) {
          setIfEmpty(
            out,
            sources,
            "zip",
            formatZip(zip),
            "resposta estruturada"
          );
        }

        explicitFields += 1;
        continue;
      }

      m = line.match(
        /^cpf(?:\s*\([^)]*\))?\s*[:=-]\s*(.*)$/i
      );
      if (m) {
        const cpf = findCpf(m[1]);

        if (cpf) {
          setIfEmpty(
            out,
            sources,
            "cpf",
            formatCpf(cpf),
            "resposta estruturada"
          );
        }

        explicitFields += 1;
        continue;
      }

      m = line.match(
        /^(?:e-?mail)\s*[:=-]\s*(.+)$/i
      );
      if (m?.[1]) {
        const email =
          m[1].match(
            /[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/i
          )?.[0];

        if (email) {
          setIfEmpty(
            out,
            sources,
            "email",
            repairFragmentEmail(
              m[1],
              email
            ),
            "resposta estruturada"
          );
        }

        explicitFields += 1;
      }
    }

    // Se o cliente respondeu praticamente todo o formulário com rótulos,
    // não usamos a heurística posicional abaixo.
    if (explicitFields >= 4) {
      return;
    }

    // 2) Formato em linhas sem rótulos:
    // Nome
    // Rua ...
    // Número
    // Bairro
    // Cidade
    // Estado
    // CEP
    // E-mail
    const firstName =
      cleanNameCandidate(
        originalLines[0]
      );

    if (
      firstName &&
      looksLikeHumanName(firstName)
    ) {
      preferName(
        out,
        sources,
        firstName,
        "resposta estruturada"
      );
    }

    let streetIndex = -1;
    let parsedNumberFromStreet = "";

    for (
      let i = 0;
      i < originalLines.length;
      i++
    ) {
      const line =
        trimPunctuation(
          originalLines[i]
        );

      const streetMatch =
        line.match(
          /^(rua|r\.?(?=\s)|avenida|av\.?(?=\s)|estrada|rodovia|travessa|tv\.?(?=\s)|alameda|pra[cç]a)\s*[.:=-]?\s+(.+)$/i
        );

      if (!streetMatch?.[2]) continue;

      let streetValue =
        compact(
          `${streetMatch[1]} ${streetMatch[2]}`
        );

      const numbered =
        streetValue.match(
          /^(.*?\D)\s+(\d+[A-Za-z0-9/-]*)$/
        );

      if (numbered) {
        streetValue =
          numbered[1];

        parsedNumberFromStreet =
          numbered[2];
      }

      setIfEmpty(
        out,
        sources,
        "street",
        canonicalStreet(
          streetValue
        ),
        "resposta estruturada"
      );

      if (parsedNumberFromStreet) {
        setIfEmpty(
          out,
          sources,
          "number",
          parsedNumberFromStreet,
          "resposta estruturada"
        );
      }

      streetIndex = i;
      break;
    }

    if (streetIndex < 0) return;

    const after =
      originalLines
        .slice(streetIndex + 1)
        .map(trimPunctuation)
        .filter(Boolean);

    let cursor = 0;

    if (
      !out.number &&
      after[cursor] &&
      /^\d+[A-Za-z0-9/-]*[.]?$/.test(
        after[cursor]
      )
    ) {
      setIfEmpty(
        out,
        sources,
        "number",
        trimPunctuation(after[cursor]),
        "resposta estruturada"
      );

      cursor += 1;
    }

    const addressParts =
      after
        .slice(cursor)
        .filter(line =>
          !isCpfOnly(line) &&
          !isZipOnly(line) &&
          !isEmailOnly(line) &&
          !/^(?:ce|cep|cp)\.?\s*[:=-]?\s*\d{4,5}[-.\s]?\d{3}$/i.test(compact(line))
        )
        .map(line => {
          let value = compact(line);

          if (
            /^(?:bairro|b(?=[.:\s])\.?)/i.test(value)
          ) {
            value = cleanNeighborhoodValue(value);
          }

          value = value
            .replace(
              /^(?:cidade)\s*[:.=-]?\s*/i,
              ""
            )
            .trim();

          return value;
        })
        .filter(Boolean);

    let stateIndex = -1;
    let uf = "";

    for (
      let i = 0;
      i < addressParts.length;
      i++
    ) {
      const candidate =
        stateToUf(
          addressParts[i]
        );

      if (candidate) {
        stateIndex = i;
        uf = candidate;
        break;
      }
    }

    if (stateIndex >= 0) {
      setIfEmpty(
        out,
        sources,
        "state",
        uf,
        "resposta estruturada"
      );

      if (
        stateIndex >= 1
      ) {
        setIfEmpty(
          out,
          sources,
          "city",
          titleCase(
            stripTrailingState(
              addressParts[
                stateIndex - 1
              ]
            )
          ),
          "resposta estruturada"
        );
      }

      if (
        stateIndex >= 2
      ) {
        setIfEmpty(
          out,
          sources,
          "neighborhood",
          titleCase(
            addressParts[
              stateIndex - 2
            ]
          ),
          "resposta estruturada"
        );
      }
    } else {
      if (addressParts[0]) {
        setIfEmpty(
          out,
          sources,
          "neighborhood",
          titleCase(
            addressParts[0]
          ),
          "resposta estruturada"
        );
      }

      if (addressParts[1]) {
        setIfEmpty(
          out,
          sources,
          "city",
          titleCase(
            stripTrailingState(
              addressParts[1]
            )
          ),
          "resposta estruturada"
        );
      }
    }
  }

  function cpfCandidatesFromText(text) {
    const raw = String(text || "");
    const values = [];
    const seen = new Set();

    const addCandidate = value => {
      const d = digits(value);
      if (d.length !== 11 || seen.has(d)) return;
      seen.add(d);
      values.push(value);
    };

    // 1) CPF explicitamente rotulado, inclusive quando existe observação
    // entre o rótulo e o número: "CPF: (para nota fiscal) 88186334220".
    const explicitRe =
      /\b(?:meu\s+)?cpf\b(?:\s*\([^)]{0,80}\))?\s*[:.=-]?\s*((?:\d[\s.\-]*){10}\d)/ig;

    let match;
    while ((match = explicitRe.exec(raw))) {
      addCandidate(match[1]);
      if (match.index === explicitRe.lastIndex) explicitRe.lastIndex += 1;
    }

    // 2) Linhas/tokens independentes com 11 dígitos.
    // Esse padrão cobre respostas em formulário onde o cliente envia o CPF
    // sem escrever a palavra CPF, por exemplo "83870849991" em uma linha.
    for (const line of raw.replace(/\r/g, "\n").split(/\n+/)) {
      const trimmed = line.trim();
      if (/^[\d\s.\-]+$/.test(trimmed) && digits(trimmed).length === 11) {
        addCandidate(trimmed);
      }
    }

    // 3) Token de 11 dígitos no meio de uma mensagem maior.
    // Só aceita quando o número passa na validação real de CPF, reduzindo
    // o risco de confundir telefone com CPF.
    const looseTokenRe = /(?:^|[^0-9])(\d{11})(?!\d)/g;
    while ((match = looseTokenRe.exec(raw))) {
      if (cpfValid(match[1])) addCandidate(match[1]);
      if (match.index === looseTokenRe.lastIndex) looseTokenRe.lastIndex += 1;
    }

    // 4) Token único com 11 dígitos e pontuação.
    // Ex.: "253148400.06" no fim de uma linha de endereço.
    const tokenRe = /(?:^|[^0-9])(\d[\d.\-]{9,24})(?!\d)/g;
    while ((match = tokenRe.exec(raw))) {
      const candidate = match[1];
      if (/[.\-]/.test(candidate) && digits(candidate).length === 11) {
        addCandidate(candidate);
      }
      if (match.index === tokenRe.lastIndex) tokenRe.lastIndex += 1;
    }

    // 5) Mensagem inteira contendo apenas CPF/separadores.
    const whole = raw.trim();
    if (/^[\d\s.\-]+$/.test(whole) && digits(whole).length === 11) {
      addCandidate(whole);
    }

    return values;
  }

  function findCpf(text) {
    return cpfCandidatesFromText(text)[0] || "";
  }

  function selectBestCpfFromMessages(messages) {
    const found = [];

    for (let i = 0; i < messages.length; i++) {
      const text = String(messages[i]?.text || "");
      const candidates = cpfCandidatesFromText(text);

      for (let j = 0; j < candidates.length; j++) {
        const raw = candidates[j];

        found.push({
          raw,
          formatted: formatCpf(raw),
          valid: cpfValid(raw),
          messageIndex: i,
          candidateIndex: j
        });
      }
    }

    if (!found.length) return null;

    // Prioriza o CPF válido mais recente.
    // Se nenhum for válido, usa o mais recente e mantém o alerta visual.
    const valid = found.filter(item => item.valid);
    const pool = valid.length ? valid : found;

    return pool[pool.length - 1];
  }

  function nameAfterCpfLabel(text) {
    const raw = compact(text);

    const match = raw.match(
      /\b(?:meu\s+)?cpf\b\s*[:.=-]?\s*((?:\d[\s.\-]*){10}\d)\s+([A-Za-zÀ-ÖØ-öø-ÿ'’ -]{5,100})$/i
    );

    if (!match?.[2]) return "";

    const candidate =
      trimPunctuation(match[2]);

    return looksLikeHumanName(candidate)
      ? candidate
      : "";
  }


  function findZip(text) {
    const raw = String(text || "");

    // V15.44: aceita CEP digitado com separadores fora do padrão quando há
    // rótulo explícito. Também tolera o erro comum "CEO" no lugar de "CEP".
    // Ex.: "CEO 72.541 613" -> 72541-613.
    const explicitFlexible = raw.match(
      /\bce[po]\s*[:.=-]?\s*((?:\d[\s.\-]*){8})(?!\d)/i
    );

    if (explicitFlexible?.[1]) {
      const d = digits(explicitFlexible[1]);
      if (d.length === 8) return d;
    }

    const exact = raw.match(
      /(?:^|[^\d])(?:cep\s*[:=-]?\s*)?(\d{5}[-.\s]?\d{3})(?!\d)/i
    );

    if (exact?.[1]) {
      return exact[1];
    }

    // Recupera apenas um erro bem específico e seguro:
    // "CEP 26 185 00" -> 26185-000.
    // Só faz isso quando há rótulo CEP explícito e o último grupo é "00".
    const spacedSeven = raw.match(
      /\bcep\s*[:.=-]?\s*(\d{2})[\s.\-]+(\d{3})[\s.\-]+(00)\b/i
    );

    if (spacedSeven) {
      return `${spacedSeven[1]}${spacedSeven[2]}${spacedSeven[3]}0`;
    }

    // Alguns clientes enviam CEP com 7 dígitos e o zero final omitido.
    // Ex.: "CEP 8982000" -> 89820-000. Só corrige quando existe
    // o rótulo CEP explícito e o valor termina em 000, reduzindo falsos positivos.
    const explicitSeven = raw.match(
      /\bcep\s*[:.=-]?\s*(\d{7})\b/i
    );

    if (explicitSeven?.[1] && /000$/.test(explicitSeven[1])) {
      return `${explicitSeven[1]}0`;
    }

    return "";
  }


  function stripCpfAndZip(text) {
    let raw = String(text || "");

    raw = raw.replace(
      /(?:^|[^\d])cpf\s*[:=-]?\s*\d{3}[.\s]?\d{3}[.\s]?\d{3}[-\s]?\d{2}(?!\d)/ig,
      " "
    );

    raw = raw.replace(
      /(?:^|[^\d])cep\s*[:=-]?\s*\d{5}[-.\s]?\d{3}(?!\d)/ig,
      " "
    );

    raw = raw.replace(
      /\bcep\s*[:.=-]?\s*\d{2}[\s.\-]+\d{3}[\s.\-]+00\b/ig,
      " "
    );

    return compact(raw);
  }

  function stripTrailingState(value) {
    let raw = compact(value);
    if (!raw) return "";

    // UF no final: "Niquelândia GO" -> "Niquelândia"
    raw = raw.replace(
      /\s+(AC|AL|AP|AM|BA|CE|DF|ES|GO|MA|MT|MS|MG|PA|PB|PR|PE|PI|RJ|RN|RS|RO|RR|SC|SP|SE|TO)$/i,
      ""
    ).trim();

    const normalized = normalize(raw);

    const stateNames = [
      "rio grande do sul",
      "rio grande do norte",
      "mato grosso do sul",
      "rio de janeiro",
      "espirito santo",
      "santa catarina",
      "sao paulo",
      "minas gerais",
      "mato grosso",
      "distrito federal",
      "pernambuco",
      "maranhao",
      "rondonia",
      "tocantins",
      "alagoas",
      "amazonas",
      "paraiba",
      "sergipe",
      "roraima",
      "goias",
      "bahia",
      "ceara",
      "parana",
      "para",
      "piaui",
      "amapa",
      "acre"
    ];

    for (const stateName of stateNames) {
      if (
        normalized === stateName ||
        normalized.endsWith(` ${stateName}`)
      ) {
        const removeWords = stateName.split(/\s+/).length;
        const words = raw.split(/\s+/);

        if (words.length > removeWords) {
          return words
            .slice(0, -removeWords)
            .join(" ")
            .trim();
        }

        return "";
      }
    }

    return raw;
  }

  function stateMention(text) {
    const raw = String(text || "");

    // Remove trechos que parecem CEP antes de procurar UF.
    const stateRaw = raw.replace(
      /\b(?:cep|ce|cp)\.?\s*[:=-]?\s*\d[\d.\-\s]{5,12}\b/ig,
      " "
    );

    const validUfs = new Set([
      "AC","AL","AP","AM","BA","CE","DF","ES","GO","MA","MT","MS","MG",
      "PA","PB","PR","PE","PI","RJ","RN","RS","RO","RR","SC","SP","SE","TO"
    ]);

    // UFs pontuadas: S.P, R.J, R.S, S.C etc.
    const dottedMatches = [...stateRaw.matchAll(
      /(?:^|[^A-Za-z])([A-Za-z])\s*\.\s*([A-Za-z])\.?(?=$|[^A-Za-z])/g
    )];

    for (let i = dottedMatches.length - 1; i >= 0; i--) {
      const m = dottedMatches[i];
      const uf = (m[1] + m[2]).toUpperCase();

      if (validUfs.has(uf)) {
        return {
          uf,
          name: m[0].trim(),
          rawIndex: m.index
        };
      }
    }

    const normalized = normalize(stateRaw);

    const stateNames = [
      ["rio grande do sul","RS"],
      ["rio grande do norte","RN"],
      ["mato grosso do sul","MS"],
      ["rio de janeiro","RJ"],
      ["espirito santo","ES"],
      ["santa catarina","SC"],
      ["sao paulo","SP"],
      ["minas gerais","MG"],
      ["mato grosso","MT"],
      ["distrito federal","DF"],
      ["pernambuco","PE"],
      ["maranhao","MA"],
      ["rondonia","RO"],
      ["tocantins","TO"],
      ["alagoas","AL"],
      ["amazonas","AM"],
      ["paraiba","PB"],
      ["sergipe","SE"],
      ["roraima","RR"],
      ["goias","GO"],
      ["bahia","BA"],
      ["ceara","CE"],
      ["parana","PR"],
      ["piaui","PI"],
      ["amapa","AP"],
      ["acre","AC"]
    ];

    for (const [name, uf] of stateNames) {
      const escaped =
        name.replace(
          /[.*+?^${}()|[\]\\]/g,
          "\\$&"
        );

      const re =
        new RegExp(
          `(?:^|\\s)${escaped}(?=$|\\s|[,.;])`,
          "ig"
        );

      const matches =
        [...normalized.matchAll(re)];

      if (matches.length) {
        const m =
          matches[
            matches.length - 1
          ];

        return {
          uf,
          name,
          normalizedIndex: m.index
        };
      }
    }

    // Pará é especial porque "para" é preposição comum.
    const paraAccent =
      [...stateRaw.matchAll(/\bPará\b/g)];

    if (paraAccent.length) {
      const m =
        paraAccent[
          paraAccent.length - 1
        ];

      return {
        uf: "PA",
        name: "Pará",
        rawIndex: m.index
      };
    }

    // Estado/UF explicitamente rotulado aceita qualquer caixa.
    const explicitUf = stateRaw.match(
      /\b(?:estado|uf)\s*[:.=-]?\s*(AC|AL|AP|AM|BA|CE|DF|ES|GO|MA|MT|MS|MG|PA|PB|PR|PE|PI|RJ|RN|RS|RO|RR|SC|SP|SE|TO)\b/i
    );

    if (explicitUf?.[1]) {
      return {
        uf: explicitUf[1].toUpperCase(),
        name: explicitUf[1],
        rawIndex: explicitUf.index
      };
    }

    // Sigla em MAIÚSCULAS é segura.
    // Isso impede palavras como "se" de virarem Sergipe.
    const upperMatches = [...stateRaw.matchAll(
      /\b(AC|AL|AP|AM|BA|CE|DF|ES|GO|MA|MT|MS|MG|PA|PB|PR|PE|PI|RJ|RN|RS|RO|RR|SC|SP|SE|TO)\b/g
    )];

    if (upperMatches.length) {
      const m =
        upperMatches[
          upperMatches.length - 1
        ];

      return {
        uf: m[1],
        name: m[1],
        rawIndex: m.index
      };
    }

    // Sigla minúscula só é aceita em contexto claro de endereço.
    // PA e SE ficam de fora por colisão forte com palavras comuns.
    const hasAddressContext =
      /\b(?:rua|avenida|av\.?|travessa|estrada|rodovia|bairro|cidade|cep|brasil)\b/i.test(stateRaw) ||
      /\d{5}[-.\s]?\d{3}/.test(stateRaw);

    if (hasAddressContext) {
      const lowerMatches = [...stateRaw.matchAll(
        /\b(ac|al|ap|am|ba|ce|df|es|go|ma|mt|ms|mg|pb|pr|pe|pi|rj|rn|rs|ro|rr|sc|sp|to)\b/g
      )];

      if (lowerMatches.length) {
        const m =
          lowerMatches[
            lowerMatches.length - 1
          ];

        return {
          uf: m[1].toUpperCase(),
          name: m[1],
          rawIndex: m.index
        };
      }
    }

    return null;
  }


  function takeUntilLabels(value, labels) {
    let result = compact(value);
    if (!result) return "";

    const escaped = labels.map(x =>
      x.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")
    );

    const re = new RegExp(
      `\\s+(?=${escaped.join("|")})\\b`,
      "i"
    );

    return compact(result.split(re)[0]);
  }

  function normalizedWords(value) {
    return normalize(value)
      .replace(/[^a-z0-9 ]+/g, " ")
      .split(/\s+/)
      .filter(Boolean);
  }

  function significantStreetWords(value) {
    const ignore = new Set([
      "rua","r","avenida","av","travessa","tv","estrada","rodovia",
      "alameda","praca","de","da","do","das","dos"
    ]);
    return normalizedWords(value)
      .filter(word => !ignore.has(word))
      .filter(word => word.length >= 3);
  }

  function lineHasStreetWords(line, street) {
    const hay = new Set(normalizedWords(line));
    const words = significantStreetWords(street);
    if (!words.length) return false;
    let hits = 0;
    for (const word of words) if (hay.has(word)) hits += 1;
    return hits >= Math.min(2, words.length);
  }

  function inferNumberAndComplementFromMessages(messages, street) {
    if (!street) return {};
    const streetWords = significantStreetWords(street);
    if (!streetWords.length) return {};

    for (const m of messages) {
      const lines = String(m?.text || "")
        .replace(/\r/g, "\n")
        .split(/\n+/)
        .map(x => compact(x))
        .filter(Boolean);

      for (const originalLine of lines) {
        if (!lineHasStreetWords(originalLine, street)) continue;

        const line = originalLine
          .replace(/[;,]+/g, " ")
          .replace(/\.(?=\s|$)/g, " ")
          .replace(/\s+/g, " ")
          .trim();

        const normalizedLine = normalize(line);
        let lastEnd = -1;
        for (const word of streetWords) {
          const idx = normalizedLine.lastIndexOf(word);
          if (idx >= 0) lastEnd = Math.max(lastEnd, idx + word.length);
        }

        const tail = lastEnd >= 0 ? line.slice(lastEnd).trim() : line;
        const numberMatch = tail.match(/(?:^|\s)(\d{1,6}[A-Za-z]?)(?=\s|$)/);
        if (!numberMatch?.[1]) continue;

        const result = { number: numberMatch[1] };
        const afterNumber = tail.slice((numberMatch.index || 0) + numberMatch[0].length).trim();
        const complement = afterNumber.match(/^([A-Za-z]{1,3}|\d{1,4})$/)?.[1];
        if (complement) result.complement = complement;
        return result;
      }
    }
    return {};
  }

  function oneEditOrContained(a, b) {
    const aa = normalize(a);
    const bb = normalize(b);
    if (!aa || !bb) return false;
    if (aa === bb || aa.includes(bb) || bb.includes(aa)) return true;
    if (Math.abs(aa.length - bb.length) > 1) return false;
    let i=0,j=0,edits=0;
    while (i<aa.length && j<bb.length) {
      if (aa[i]===bb[j]) { i++; j++; continue; }
      edits++;
      if (edits>1) return false;
      if (aa.length>bb.length) i++;
      else if (bb.length>aa.length) j++;
      else { i++; j++; }
    }
    if (i<aa.length || j<bb.length) edits++;
    return edits<=1;
  }

  function explicitNameAfterLabel(text) {
    const raw = compact(text);
    const m = raw.match(/\bnome(?:\s+completo)?\s*[:=-]?\s+([A-Za-zÀ-ÖØ-öø-ÿ'’ -]{5,100})$/i);
    return cleanNameCandidate(m?.[1] || "");
  }

  function parseCommaSeparatedAddress(text, out, sources) {
    const raw = compact(text);
    if (!raw) return false;

    // Ex.:
    // "Rua Pastor Manoel de Brito, 381. Bairro Bocaina, Conceição de Macabu, RJ"
    // Pode estar no meio de uma mensagem maior.
    const m = raw.match(
      /\b((?:rua|avenida|av\.?|estrada|rodovia|travessa|alameda|pra[cç]a)\s+[^,]+),\s*(\d{1,6}[A-Za-z]?)\.?\s*(?:bairro\s+)?([^,]+),\s*([^,]+),\s*(AC|AL|AP|AM|BA|CE|DF|ES|GO|MA|MT|MS|MG|PA|PB|PR|PE|PI|RJ|RN|RS|RO|RR|SC|SP|SE|TO)\b/i
    );

    if (!m) return false;

    const street = canonicalStreet(m[1]);
    const number = trimPunctuation(m[2]);
    const neighborhood = titleCase(trimPunctuation(m[3]));
    const city = titleCase(trimPunctuation(m[4]));
    const state = m[5].toUpperCase();

    // Este padrão é muito mais confiável que as heurísticas posicionais.
    // Portanto substitui valores ruins já extraídos anteriormente.
    if (street) {
      out.street = street;
      sources.street = "endereço explícito";
    }

    if (number) {
      out.number = number;
      sources.number = "endereço explícito";
    }

    if (neighborhood) {
      out.neighborhood = neighborhood;
      sources.neighborhood = "endereço explícito";
    }

    if (city) {
      out.city = city;
      sources.city = "endereço explícito";
    }

    out.state = state;
    sources.state = "endereço explícito";

    return true;
  }

  function cleanEmailTypos(text) {
    return String(text || "")
      .replace(/\s*,\s*@/g, "@")
      .replace(/\s*@\s*/g, "@")
      .replace(/(@[A-Z0-9.-]+)\s*\.\s*([A-Z]{2,})\b/ig, "$1.$2")
      // Ex.: "gmail.co m" -> "gmail.com"
      .replace(/(@[A-Z0-9.-]+\.[A-Z]{2})\s+([A-Z])\b/ig, "$1$2")
      // Ex.: "gmail.c om" -> "gmail.com"
      .replace(/(@[A-Z0-9.-]+\.[A-Z])\s+([A-Z]{2})\b/ig, "$1$2");
  }

  function messageContainsStreet(messages, street) {
    const words = significantStreetWords(street);
    if (!words.length) return false;

    return messages.some(m => {
      const hay = new Set(normalizedWords(m?.text || ""));
      const hits = words.filter(word => hay.has(word)).length;
      return hits >= Math.min(2, words.length);
    });
  }

  function messageContainsNumber(messages, number) {
    const n = compact(number);
    if (!n || n === "SN") return true;

    const escaped = n.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const re = new RegExp(`(?:^|[^0-9A-Za-z])${escaped}(?:$|[^0-9A-Za-z])`, "i");
    return messages.some(m => re.test(String(m?.text || "")));
  }

  function messageHasStrongStreetEvidence(messages, street) {
    const candidate = compact(street);
    const words = significantStreetWords(candidate);

    if (!candidate || !words.length) return false;

    const escapedCandidate =
      normalize(candidate)
        .replace(/[.*+?^${}()|[\]\\]/g, "\\$&")
        .replace(/\s+/g, "\\s+");

    return messages.some(m => {
      const text = compact(m?.text || "");
      if (!text) return false;

      const normalizedText = normalize(text);

      // 1) Rua/logradouro explicitamente rotulado com o candidato.
      // Ex.: "Rua Esperança", "Rua: Esperança", "Travessa Leoberto Leal".
      const exactStreetLabel =
        new RegExp(
          `\\b(?:rua|r\\.?|avenida|av\\.?|travessa|tv\\.?|estrada|rodovia|logradouro|endereco|endereço)\\s*[:.=-]?\\s*${escapedCandidate}\\b`,
          "i"
        ).test(normalizedText);

      if (exactStreetLabel) return true;

      // 2) Se o candidato já contém o tipo do logradouro, aceita a frase inteira.
      const fullCandidate =
        normalize(candidate);

      if (
        hasStreetType(candidate) &&
        normalizedText.includes(fullCandidate)
      ) {
        return true;
      }

      // 3) Rua com 2+ palavras significativas pode ser confirmada por
      // correspondência forte no mesmo texto.
      if (words.length >= 2) {
        const hay = new Set(normalizedWords(text));
        const hits = words.filter(word => hay.has(word)).length;

        if (
          hits >= Math.max(2, Math.ceil(words.length * 0.75)) &&
          /\b(?:rua|r\.?|avenida|av\.?|travessa|tv\.?|estrada|rodovia|logradouro|endereco|endereço)\b/i.test(text)
        ) {
          return true;
        }
      }

      // IMPORTANTE: uma rua de uma única palavra não é aceita só porque
      // aquela palavra apareceu em outro contexto.
      return false;
    });
  }

  function messageHasStrongNumberEvidence(messages, number, street) {
    const n = compact(number);
    if (!n || n === "SN") return true;

    const escaped =
      n.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

    return messages.some(m => {
      const text = compact(m?.text || "");
      if (!text) return false;

      const explicitlyLabeled =
        new RegExp(
          `\\b(?:n[uú]mero|numero|nº|n°)\\s*[:.=-]?\\s*${escaped}(?:$|[^0-9A-Za-z])`,
          "i"
        ).test(text);

      if (explicitlyLabeled) return true;

      if (!street) return false;

      // Número precisa aparecer próximo ao próprio logradouro.
      // Isso impede qualquer "55" perdido na conversa de virar número da casa.
      const streetWords = significantStreetWords(street);

      if (!streetWords.length) return false;

      const normalized = normalize(text);
      const allStreetWordsPresent =
        streetWords.every(word =>
          normalized.includes(word)
        );

      if (!allStreetWordsPresent) return false;

      const numberRe =
        new RegExp(
          `(?:^|[^0-9A-Za-z])${escaped}(?:$|[^0-9A-Za-z])`,
          "i"
        );

      if (!numberRe.test(text)) return false;

      // Rua e número precisam estar no mesmo trecho curto.
      const firstWord =
        streetWords[0];

      const streetPos =
        normalized.indexOf(firstWord);

      const numberPos =
        normalized.search(numberRe);

      if (
        streetPos < 0 ||
        numberPos < 0
      ) {
        return false;
      }

      return Math.abs(numberPos - streetPos) <= 140;
    });
  }

  function applyMultilineCustomerDataFix(values, sources, messages) {
    for (const message of messages || []) {
      if (message?.audioTranscript) continue;

      const lines = String(message?.text || "")
        .replace(/\r/g, "\n")
        .split(/\n+/)
        .map(line => compact(line))
        .filter(Boolean);

      if (!lines.length) continue;

      // Nome + CPF na mesma linha, por exemplo:
      // "Luiz Afonso dos Santos 289.356.402-04"
      for (const line of lines) {
        const cpf = findCpf(line);
        if (!cpf) continue;

        const cpfPos = line.indexOf(cpf);
        const beforeCpf = cpfPos >= 0
          ? trimPunctuation(line.slice(0, cpfPos))
          : "";

        if (beforeCpf && looksLikeHumanName(beforeCpf)) {
          values.name = titleCase(beforeCpf);
          sources.name = "mensagem · nome antes do CPF";
        }
      }

      // Nome imediatamente antes de uma linha de CPF é evidência forte.
      // Ex.: "Fernando Figueiredo Ferreira" seguido de "CPF 84124237715".
      for (let i = 1; i < lines.length; i++) {
        if (!/\bcpf\b/i.test(lines[i])) continue;
        const previous = trimPunctuation(lines[i - 1]);
        if (previous && looksLikeHumanName(previous)) {
          preferName(values, sources, previous, "mensagem · nome antes da linha CPF");
        }
      }

      // Rua com número entre parênteses.
      // Ex.: "Rua Maria Gonçalves Maravilha (172)".
      for (const line of lines) {
        const m = line.match(
          /^((?:rua|r\.?|avenida|av\.?|estrada|rodovia|travessa|tv\.?|alameda|pra[cç]a)\s+.+?)\s*\(\s*(\d{1,6}[A-Za-z]?)\s*\)\s*$/i
        );
        if (!m?.[1] || !m?.[2]) continue;
        values.street = canonicalStreet(m[1]);
        sources.street = "mensagem · rua+número entre parênteses";
        values.number = m[2];
        sources.number = "mensagem · número entre parênteses";
      }

      // Linha curta de número: "N81", "N 81", "Nº81".
      for (const line of lines) {
        const m = line.match(/^n(?:[º°.]|úmero|umero)?\s*[:.=-]?\s*(\d{1,6}[A-Za-z]?)$/i);
        if (!m?.[1]) continue;
        values.number = m[1];
        sources.number = "mensagem · número em linha própria";
      }

      // Cidade seguida pelo nome completo do estado em linha própria.
      // Ex.: "Piuma Espírito santo" -> cidade Piúma, UF ES.
      for (const line of lines) {
        const normalizedLine = normalize(line);
        const states = [
          ["acre","AC"],["alagoas","AL"],["amapa","AP"],["amazonas","AM"],["bahia","BA"],
          ["ceara","CE"],["distrito federal","DF"],["espirito santo","ES"],["goias","GO"],
          ["maranhao","MA"],["mato grosso do sul","MS"],["mato grosso","MT"],["minas gerais","MG"],
          ["para","PA"],["paraiba","PB"],["parana","PR"],["pernambuco","PE"],["piaui","PI"],
          ["rio de janeiro","RJ"],["rio grande do norte","RN"],["rio grande do sul","RS"],
          ["rondonia","RO"],["roraima","RR"],["santa catarina","SC"],["sao paulo","SP"],
          ["sergipe","SE"],["tocantins","TO"]
        ];
        for (const [stateName, uf] of states) {
          if (!normalizedLine.endsWith(" " + stateName)) continue;
          const cityLen = normalizedLine.length - stateName.length;
          const cityRaw = trimPunctuation(line.slice(0, cityLen));
          if (!cityRaw || /^(?:bairro|rua|avenida|estrada|proximo|próximo)$/i.test(cityRaw)) continue;
          values.city = titleCase(cityRaw);
          sources.city = "mensagem · cidade + estado";
          values.state = uf;
          sources.state = "mensagem · cidade + estado";
          break;
        }
      }

      // Rua com indicação explícita de sem número.
      // Ex.: "Rua Marquês de Tamandaré s/N Fórum do Comarca"
      for (const line of lines) {
        const sn = line.match(
          /^((?:rua|r\.?|avenida|av\.?|estrada|rodovia|travessa|tv\.?|alameda|pra[cç]a)\s+.+?)\s+(?:s\s*\/?\s*n|s\.?\s*n\.?|sem\s+n[uú]mero)\b\s*(.*)$/i
        );

        if (!sn?.[1]) continue;

        const street = canonicalStreet(sn[1]);
        if (street) {
          values.street = street;
          sources.street = "mensagem · sem número";
        }

        values.number = "SN";
        sources.number = "sem número informado";

        const complement = trimPunctuation(sn[2] || "");
        if (complement && !/^(?:bairro|cep|cpf|cidade|estado|uf)\b/i.test(complement)) {
          values.complement = titleCase(complement);
          sources.complement = "mensagem · após s/n";
        }
      }

      // Bairro em linha própria tem prioridade sobre heurísticas soltas.
      for (const line of lines) {
        const m = line.match(/^bairro\s*[:=-]?\s*(.+)$/i);
        if (!m?.[1]) continue;

        let neighborhood = trimPunctuation(m[1]);
        neighborhood = neighborhood
          .replace(/\s+(?:cep|cpf)\b.*$/i, "")
          .trim();

        if (neighborhood) {
          values.neighborhood = titleCase(neighborhood);
          sources.neighborhood = "mensagem · bairro explícito";
        }
      }

      // Cidade + UF em linha própria, por exemplo "Uruara-PA".
      // Esse padrão é forte e corrige casos em que o bairro foi confundido com cidade.
      for (const line of lines) {
        const m = line.match(
          /^([A-Za-zÀ-ÖØ-öø-ÿ'’ .-]{2,80}?)\s*[-–—/]\s*(AC|AL|AP|AM|BA|CE|DF|ES|GO|MA|MT|MS|MG|PA|PB|PR|PE|PI|RJ|RN|RS|RO|RR|SC|SP|SE|TO)$/i
        );

        if (!m?.[1] || !m?.[2]) continue;

        const city = trimPunctuation(m[1]);
        if (city && !/^(?:bairro|rua|avenida|estrada)$/i.test(city)) {
          values.city = titleCase(city);
          sources.city = "mensagem · cidade-UF";
          values.state = m[2].toUpperCase();
          sources.state = "mensagem · cidade-UF";
        }
      }
    }
  }

  function sanitizeAddressEvidence(values, sources, messages) {
    const originalStreet = values.street;
    const originalStreetSource = sources.street;

    if (
      values.street &&
      sources.street === "mensagem" &&
      !messageHasStrongStreetEvidence(
        messages,
        values.street
      )
    ) {
      delete values.street;
      delete sources.street;
    }

    // Se a rua "mensagem" foi descartada, o número inferido a partir dela
    // também é descartado, a menos que o cliente tenha escrito Número: X.
    if (
      originalStreet &&
      originalStreetSource === "mensagem" &&
      !values.street &&
      values.number &&
      sources.number === "mensagem"
    ) {
      const n =
        compact(values.number);

      const escaped =
        n.replace(
          /[.*+?^${}()|[\]\\]/g,
          "\\$&"
        );

      const explicit =
        messages.some(m =>
          new RegExp(
            `\\b(?:n[uú]mero|numero|nº|n°)\\s*[:.=-]?\\s*${escaped}(?:$|[^0-9A-Za-z])`,
            "i"
          ).test(
            compact(
              m?.text || ""
            )
          )
        );

      if (!explicit) {
        delete values.number;
        delete sources.number;
      }
    }

    if (
      values.number &&
      sources.number === "mensagem" &&
      !messageHasStrongNumberEvidence(
        messages,
        values.number,
        values.street
      )
    ) {
      delete values.number;
      delete sources.number;
    }

    if (
      !values.number &&
      values.street &&
      /\b(?:lote|quadra|qd\.?|lt\.?)\b/i.test(
        values.complement || ""
      )
    ) {
      values.number = "SN";
      sources.number = "sem número informado";
    }
  }

  function parsePackedStreetNumber(text, out, sources) {
    const raw = compact(text);
    if (!raw) return false;

    // Ex.:
    // "Rua Albano 281 parque Afonso..."
    // "Rua das andorinha 07 bairro alto arroio..."
    // "R Estefano José Vanoli 1279."
    const match = raw.match(
      /\b((?:rua|r\.?(?=\s)|avenida|av\.?(?=\s)|estrada|rodovia|travessa|tv\.?(?=\s)|alameda|pra[cç]a)\s+[A-Za-zÀ-ÖØ-öø-ÿ'’.-]+(?:\s+[A-Za-zÀ-ÖØ-öø-ÿ'’.-]+){0,8}?)\s*[,.]?\s+(\d{1,6}[A-Za-z]?)(?=\s|[,.;]|$)/i
    );

    if (!match?.[1] || !match?.[2]) {
      return false;
    }

    const street =
      canonicalStreet(
        match[1]
          .replace(/\s+(?:n|nº|n°|numero|número)\.?$/i, "")
      );

    if (!street) return false;

    out.street = street;
    sources.street =
      "mensagem · rua+número";

    out.number =
      match[2];

    sources.number =
      "mensagem · rua+número";

    return true;
  }

  function parseDfBlockAddress(text, out, sources) {
    const raw = compact(text);
    if (!raw) return false;

    // Endereços do DF frequentemente não têm "Rua".
    // Ex.: "Qd 416 conjunto M casa 22 Santa Maria Distrito Federal".
    // Nesse formato, Quadra vira logradouro, Conjunto vira complemento e
    // Casa vira número. Bairro/localidade é preservado até o ViaCEP enriquecer.
    const m = raw.match(
      /\b(?:qd\.?|quadra)\s*[:.=-]?\s*([A-Za-z0-9/-]+)\s+(?:conj(?:unto)?\.?\s*[:.=-]?\s*)?([A-Za-z0-9/-]+)?\s+(?:casa|n[uú]mero|numero|n[º°])\s*[:.=-]?\s*(\d{1,6}[A-Za-z]?)\s+(.+?)(?=\s+(?:distrito\s+federal|df)\b|$)/i
    );

    if (!m) return false;

    const quadra = trimPunctuation(m[1] || "");
    const conjunto = trimPunctuation(m[2] || "");
    const numero = trimPunctuation(m[3] || "");
    let localidade = trimPunctuation(m[4] || "");

    if (quadra) {
      out.street = `Quadra ${quadra}`;
      sources.street = "mensagem · endereço DF";
    }
    if (numero) {
      out.number = numero;
      sources.number = "mensagem · casa";
    }
    if (conjunto) {
      out.complement = `Conjunto ${conjunto}`;
      sources.complement = "mensagem · conjunto";
    }
    if (localidade) {
      localidade = localidade.replace(/\b(?:distrito\s+federal|df)\b.*$/i, "").trim();
      if (localidade) {
        out.neighborhood = titleCase(localidade);
        sources.neighborhood = "mensagem · localidade DF";
      }
    }
    out.state = "DF";
    sources.state = "mensagem · Distrito Federal";
    return true;
  }

  function parseInlineOrderMessage(text, out, sources) {
    const original = compact(text);
    if (!original) return;

    parseDfBlockAddress(original, out, sources);
    parseCommaSeparatedAddress(original, out, sources);

    const explicitNumber = original.match(
      /\b(?:n[uú]mero|numero|nº|n°)\s*[:=-]?\s*(\d{1,6}[A-Za-z]?)/i
    )?.[1];

    if (explicitNumber) {
      setIfEmpty(out, sources, "number", explicitNumber, "mensagem");
    }

    const cpf = findCpf(original);
    if (cpf) {
      setIfEmpty(
        out,
        sources,
        "cpf",
        formatCpf(cpf),
        "mensagem"
      );
    }

    const zip = findZip(original);
    if (zip) {
      setIfEmpty(
        out,
        sources,
        "zip",
        formatZip(zip),
        "mensagem"
      );
    }

    parsePackedStreetNumber(
      original,
      out,
      sources
    );

    const explicitName = explicitNameAfterLabel(original);
    if (explicitName && looksLikeHumanName(explicitName)) {
      out.name = titleCase(explicitName);
      sources.name = "nome informado";
    }

    const cpfTrailingName =
      nameAfterCpfLabel(
        original
      );

    if (cpfTrailingName) {
      out.name =
        titleCase(
          cpfTrailingName
        );

      sources.name =
        "mensagem · nome após CPF";
    }

    const stateInfo = stateMention(original);
    if (stateInfo?.uf) {
      setIfEmpty(
        out,
        sources,
        "state",
        stateInfo.uf,
        "mensagem"
      );
    }

    let raw = stripCpfAndZip(original)
      .replace(/\bnome(?:\s+completo)?\s*[:=-]?\s+[A-Za-zÀ-ÖØ-öø-ÿ'’ -]{5,100}$/i, " ")
      .replace(/\bbrasil\b/ig, " ")
      .replace(/\s+/g, " ")
      .trim();

    // "Barro" é um erro de digitação comum para "Bairro".
    raw = raw.replace(/\bbarro\b/ig, "bairro");

    // Nome no início: "Valdomiro Pereira Damas cidade Niquelândia..."
    const namePrefix = raw.match(
      /^(.+?)\s+(?=cidade\b|rua\b|avenida\b|estrada\b|rodovia\b|bairro\b|cep\b|cpf\b)/i
    )?.[1];

    if (
      namePrefix &&
      !stateMention(namePrefix) &&
      looksLikeHumanName(namePrefix)
    ) {
      setIfEmpty(
        out,
        sources,
        "name",
        titleCase(namePrefix),
        "mensagem"
      );
    }

    // "Bairro Cidade Nova" deve virar Bairro = Cidade Nova,
    // e não Bairro = Cidade / Cidade = Nova.
    const leadingNeighborhood =
      raw.match(
        /^bairro\s*[:=-]?\s*(.+)$/i
      )?.[1];

    if (leadingNeighborhood) {
      setIfEmpty(
        out,
        sources,
        "neighborhood",
        titleCase(
          trimPunctuation(
            leadingNeighborhood
          )
        ),
        "mensagem"
      );
    }

    // Cidade explicitamente marcada.
    const cityMatch =
      leadingNeighborhood
        ? null
        : raw.match(
            /\bcidade\s*[:=-]?\s*(.+?)(?=\s+(?:rua|avenida|estrada|rodovia|bairro|estado|uf|cpf|cep)\b|$)/i
          );

    if (cityMatch?.[1]) {
      // Remove estado/UF no final sem perder acentos da cidade.
      // Ex.: "Niquelândia Goiás" -> "Niquelândia"
      //      "Porto Alegre Rio Grande do Sul" -> "Porto Alegre"
      let cityRaw = stripTrailingState(cityMatch[1]);

      if (cityRaw) {
        setIfEmpty(
          out,
          sources,
          "city",
          titleCase(cityRaw),
          "mensagem"
        );
      }
    }

    // Rua em texto corrido. Para antes de bairro/quadra/lote/cidade/estado.
    const streetMatch = raw.match(
      /\b(?:rua|r\.?(?=\s)|avenida|av\.?(?=\s)|estrada|rodovia|travessa|tv\.?(?=\s)|alameda|pra[cç]a)\s*[:=-]?\s*(.+?)(?=\s+(?:n[uú]mero|numero|nº|n°|bairro|quadra|qd\.?|lote|lt\.?|cidade|estado|uf|cpf|cep)\b|$)/i
    );

    if (streetMatch?.[1]) {
      let streetValue = compact(streetMatch[1]);

      // Só considera número residencial quando ele está no fim do trecho
      // da própria rua, nunca CPF/quadra/lote.
      const numbered = streetValue.match(
        /^(.*?\D)\s+(\d+[A-Za-z0-9/-]*)$/
      );

      if (numbered) {
        setIfEmpty(
          out,
          sources,
          "street",
          titleCase(cleanStreet(numbered[1])),
          "mensagem"
        );
        setIfEmpty(
          out,
          sources,
          "number",
          numbered[2],
          "mensagem"
        );
      } else {
        setIfEmpty(
          out,
          sources,
          "street",
          titleCase(cleanStreet(streetValue)),
          "mensagem"
        );
      }
    }

    // Bairro explicitamente informado.
    const neighborhoodMatch = raw.match(
      /\bbairro\s*[:=-]?\s*(.+?)(?=\s+(?:cidade|estado|uf|cep|cpf|rua|avenida|estrada|quadra|lote)\b|$)/i
    );

    if (neighborhoodMatch?.[1]) {
      let value = compact(neighborhoodMatch[1]);

      // Corta CPF, e-mail, CEP e UF que vierem grudados depois do bairro/cidade.
      value = value
        .replace(/\s+cpf\s*[:=-]?\s*\d.*$/i, " ")
        .replace(/\s+[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}.*$/i, " ")
        .replace(/\s+\d{5}[-.\s]?\d{3}.*$/i, " ")
        .replace(/\s+\d{8}.*$/i, " ")
        .replace(/\s+(AC|AL|AP|AM|BA|CE|DF|ES|GO|MA|MT|MS|MG|PA|PB|PR|PE|PI|RJ|RN|RS|RO|RR|SC|SP|SE|TO)\.?\s*$/i, " ")
        .trim();

      if (
        out.city &&
        normalize(value).endsWith(
          normalize(out.city)
        )
      ) {
        value = compact(
          value.slice(
            0,
            Math.max(
              0,
              value.length - out.city.length
            )
          )
        );
      }

      // Caso frequente: "bairro centro morro da fumaça SC ..."
      // Centro é o bairro; o restante é a cidade.
      const centerCity = value.match(/^centro\s+(.{3,80})$/i);
      if (centerCity?.[1] && !out.city) {
        setIfEmpty(
          out,
          sources,
          "city",
          titleCase(trimPunctuation(centerCity[1])),
          "mensagem"
        );

        value = "Centro";
      }

      if (value) {
        setIfEmpty(
          out,
          sources,
          "neighborhood",
          titleCase(value),
          "mensagem"
        );
      }
    }

    // Quadra/Lote são complemento, não número residencial.
    const quadra = raw.match(
      /\b(?:quadra|qd\.?)\s*[:=-]?\s*([A-Za-z0-9/-]+)/i
    )?.[1];

    const lote = raw.match(
      /\b(?:lote|lt\.?)\s*[:=-]?\s*([A-Za-z0-9/-]+)/i
    )?.[1];

    if (quadra || lote) {
      const parts = [];
      if (quadra) parts.push(`Quadra ${quadra}`);
      if (lote) parts.push(`Lote ${lote}`);

      setIfEmpty(
        out,
        sources,
        "complement",
        parts.join(", "),
        "mensagem"
      );
    }

    // Se há rua + quadra/lote, mas nenhum número residencial explícito,
    // o cadastro deve usar "SN" em vez de inventar 3, 5 ou o CPF.
    if (
      out.street &&
      !out.number &&
      (quadra || lote)
    ) {
      setIfEmpty(
        out,
        sources,
        "number",
        "SN",
        "sem número informado"
      );
    }
  }

  function parseLooseAddress(text, out, sources) {
    let raw = compact(text);
    if (!raw || raw.length < 8) return;

    if (
      /@/.test(raw) ||
      isCpfOnly(raw)
    ) return;

    const cpf = findCpf(raw);
    if (cpf) {
      raw = raw.replace(
        /(?:^|[^\d])(?:cpf\s*[:=-]?\s*)?\d{3}[.\s]?\d{3}[.\s]?\d{3}[-\s]?\d{2}(?!\d)/i,
        " "
      );
    }

    const zip = findZip(raw);
    if (zip) {
      setIfEmpty(
        out,
        sources,
        "zip",
        formatZip(zip),
        "mensagem"
      );

      raw = raw.replace(
        /(?:^|[^\d])(?:cep\s*[:=-]?\s*)?\d{5}[-.\s]?\d{3}(?!\d)/i,
        " "
      );
    }

    raw = compact(
      raw
        .replace(/\bbrasil\b/ig, " ")
        .replace(/\bbarro\b/ig, "bairro")
    );

    const looseState =
      stateMention(
        raw
      );

    const hasAddressHint =
      /\b(?:rua|r\.?(?=\s)|avenida|av\.?(?=\s)|estrada|rodovia|travessa|tv\.?(?=\s)|alameda|pra[cç]a|bairro|cidade|estado|cep|brasil)\b/i.test(raw) ||
      Boolean(looseState);

    if (!hasAddressHint) return;

    // Mensagens com quadra/lote são tratadas pelo parser contextual acima.
    // Isso evita usar "3" ou "5" como número da casa.
    if (/\b(?:quadra|qd\.?|lote|lt\.?)\b/i.test(raw)) {
      return;
    }

    if (looseState?.uf) {
      setIfEmpty(
        out,
        sources,
        "state",
        looseState.uf,
        "mensagem"
      );
    }

    // Só tenta número dentro de um trecho iniciado por tipo de logradouro.
    const streetWithNumber = raw.match(
      /\b((?:rua|r\.?(?=\s)|avenida|av\.?(?=\s)|estrada|rodovia|travessa|tv\.?(?=\s)|alameda|pra[cç]a)\s+.+?)\s+(\d+[A-Za-z0-9/-]*)(?=\s+(?:bairro|cidade|estado|uf)\b|,|$)/i
    );

    if (streetWithNumber) {
      setIfEmpty(
        out,
        sources,
        "street",
        titleCase(
          cleanStreet(
            streetWithNumber[1]
          )
        ),
        "mensagem"
      );

      setIfEmpty(
        out,
        sources,
        "number",
        streetWithNumber[2],
        "mensagem"
      );
    }
  }

  function addressComparable(value) {
    return normalize(value)
      .replace(/\b(?:rua|r|avenida|av|estrada|rodovia|travessa|tv|alameda|praca|praça)\b/g, " ")
      .replace(/[^a-z0-9 ]+/g, " ")
      .replace(/\s+/g, " ")
      .trim();
  }

  function similarAddressPart(a, b, kind = "text") {
    const aa = addressComparable(a);
    const bb = addressComparable(b);
    if (!aa || !bb) return true;
    if (aa === bb || aa.includes(bb) || bb.includes(aa)) return true;
    if (kind !== "street") return false;

    const stop = new Set(["de","da","do","das","dos","e"]);
    const ta = aa.split(" ").filter(x => x.length > 1 && !stop.has(x));
    const tb = bb.split(" ").filter(x => x.length > 1 && !stop.has(x));
    if (!ta.length || !tb.length) return true;
    const sb = new Set(tb);
    const common = ta.filter(x => sb.has(x)).length;
    return common / Math.max(ta.length, tb.length) >= 0.45;
  }

  function detectClientConfirmation() {
    const all = allConversationMessages();
    if (!all.length) return { confirmed: false, text: "" };

    const asksConfirmation = text => {
      const n = normalize(text);
      return /\b(confirma|confirmar|confirmacao|confirmação)\b/.test(n) ||
        /\b(dados|endereco|pedido)\b.*\b(corret|certo)\b/.test(n) ||
        /\bsim\s+ou\s+nao\b/.test(n);
    };

    const strongPositive = text => {
      const n = normalize(text).replace(/[.!?]+$/g, "").trim();
      return /\b(pode confirmar|pode sim|confirmo|confirmado|esta certo|ta certo|tudo certo|dados corretos|correto sim|sim pode)\b/.test(n);
    };

    const shortPositive = text => {
      const n = normalize(text).replace(/[.!?]+$/g, "").trim();
      return /^(sim|ok|certo|correto|pode|confirmo)$/.test(n);
    };

    let lastAsk = -1;
    for (let i = 0; i < all.length; i++) {
      const m = all[i];
      if (m.received === false && asksConfirmation(m.text || "")) lastAsk = i;
    }

    for (let i = all.length - 1; i >= 0; i--) {
      const m = all[i];
      if (m.received !== true || !compact(m.text)) continue;
      if (strongPositive(m.text)) return { confirmed: true, text: compact(m.text) };
      if (lastAsk >= 0 && i > lastAsk && shortPositive(m.text)) {
        return { confirmed: true, text: compact(m.text) };
      }
    }
    return { confirmed: false, text: "" };
  }

  async function lookupCep(zip) {
    const d = digits(zip);
    if (d.length !== 8) return null;

    try {
      const result = await chrome.runtime.sendMessage({
        type: "lookup-cep",
        zip: d
      });

      if (!result?.ok || !result.data) return null;
      return result.data;
    } catch {
      return null;
    }
  }

  async function enrichByCep(values, sources, messages = []) {
    state.cepAudit = null;
    if (!values.zip) return;

    const data = await lookupCep(values.zip);
    if (!data) return;

    // V15.40: quando o CEP é válido e o ViaCEP retorna um campo,
    // o dado oficial do CEP tem prioridade sobre qualquer tentativa
    // de interpretação da conversa. A conversa continua responsável
    // por nome, número, complemento, CPF, e-mail e telefone.
    const officialStreet = canonicalStreet(data.street);
    const officialNeighborhood = titleCase(compact(data.neighborhood));
    const officialCity = titleCase(compact(data.city));
    const officialState = compact(data.state).toUpperCase();

    const prior = {
      street: compact(values.street),
      neighborhood: compact(values.neighborhood),
      city: compact(values.city),
      state: compact(values.state).toUpperCase()
    };
    const divergences = [];
    if (prior.street && officialStreet && !similarAddressPart(prior.street, officialStreet, "street")) divergences.push("Rua");
    if (prior.neighborhood && officialNeighborhood && !similarAddressPart(prior.neighborhood, officialNeighborhood)) divergences.push("Bairro");
    if (prior.city && officialCity && !similarAddressPart(prior.city, officialCity)) divergences.push("Cidade");
    if (prior.state && officialState && prior.state !== officialState) divergences.push("Estado");
    state.cepAudit = {
      checked: true,
      zip: formatZip(digits(values.zip)),
      divergences,
      official: { street: officialStreet, neighborhood: officialNeighborhood, city: officialCity, state: officialState },
      prior
    };

    if (officialStreet) {
      values.street = officialStreet;
      sources.street = "CEP · ViaCEP";
    }

    if (officialNeighborhood) {
      values.neighborhood = officialNeighborhood;
      sources.neighborhood = "CEP · ViaCEP";
    }

    if (officialCity) {
      values.city = officialCity;
      sources.city = "CEP · ViaCEP";
    }

    if (officialState) {
      values.state = officialState;
      sources.state = "CEP · ViaCEP";
    }

    // Número e complemento nunca vêm do ViaCEP. Se ainda não foram
    // encontrados, tentamos extraí-los das mensagens do cliente usando
    // o logradouro oficial como referência.
    const inferred = inferNumberAndComplementFromMessages(
      messages,
      officialStreet || values.street
    );

    if (!values.number && inferred.number) {
      values.number = inferred.number;
      sources.number = "mensagem";
    }

    if (!values.complement && inferred.complement) {
      values.complement = inferred.complement;
      sources.complement = "mensagem";
    }
  }

  function parseCombinedLine(text, out, sources) {
    const raw = compact(text);
    if (!raw) return;

    const type = "(?:rua|r\\.?|avenida|av\\.?|estrada|rodovia|travessa|tv\\.?|alameda|pra[cç]a|loteamento|sitio|s[ií]tio)";

    // Ex.: Jusepe Maciel da Cunha, estrada campo novo 459 bairro Ipanema, Porto Alegre rs
    const full = raw.match(
      new RegExp(
        `^([^,]{4,100}),\\s*(${type}\\s+.+?)\\s+(\\d+[A-Za-z0-9/-]*)\\s+(?:bairro\\s+)?([^,]{2,80}),\\s*([^,]{2,80}?)\\s+([A-Za-z]{2})$`,
        "i"
      )
    );

    if (full) {
      setIfEmpty(out, sources, "name", titleCase(full[1]), "mensagem");
      setIfEmpty(out, sources, "street", titleCase(cleanStreet(full[2])), "mensagem");
      setIfEmpty(out, sources, "number", full[3], "mensagem");
      setIfEmpty(out, sources, "neighborhood", titleCase(full[4]), "mensagem");
      setIfEmpty(out, sources, "city", titleCase(full[5]), "mensagem");
      setIfEmpty(out, sources, "state", full[6].toUpperCase(), "mensagem");
      return;
    }

    const addr = raw.match(
      new RegExp(
        `(${type}\\s+.+?)\\s+(\\d+[A-Za-z0-9/-]*)\\s+(?:bairro\\s+)?([^,]{2,80}),\\s*([^,]{2,80}?)\\s+([A-Za-z]{2})(?:\\b|$)`,
        "i"
      )
    );

    if (addr) {
      setIfEmpty(out, sources, "street", titleCase(cleanStreet(addr[1])), "mensagem");
      setIfEmpty(out, sources, "number", addr[2], "mensagem");
      setIfEmpty(out, sources, "neighborhood", titleCase(addr[3]), "mensagem");
      setIfEmpty(out, sources, "city", titleCase(addr[4]), "mensagem");
      setIfEmpty(out, sources, "state", addr[5].toUpperCase(), "mensagem");
    }
  }

  function splitZipFromMessages(messages) {
    for (
      let i = 0;
      i < messages.length;
      i++
    ) {
      const current =
        compact(
          messages[i]?.text
        );

      const first =
        current.match(
          /\b(?:cep|cp)\.?\s*[:=-]?\s*(\d{5})\b/i
        )?.[1];

      if (!first) continue;

      const same =
        current.match(
          /\b(?:cep|cp)\.?\s*[:=-]?\s*\d{5}\D+(\d{3})\b/i
        )?.[1];

      if (same) {
        return first + same;
      }

      const next =
        compact(
          messages[i + 1]?.text
        );

      const tail =
        next.match(
          /^\.?\s*(\d{3})\s*$/
        )?.[1];

      if (tail) {
        return first + tail;
      }
    }

    return "";
  }

  function receivedMessages() {
    return relevantReceivedMessages();
  }

  function nameBeforeMalformedEmail(text) {
    const raw = String(text || "");
    const m = raw.match(
      /(?:^|[\n.;])\s*([A-Za-zÀ-ÖØ-öø-ÿ'’ -]{5,90})\s*@\s*(?:gmail|hotmail|outlook|yahoo)\s*\.\s*(?:com|com\.br)\b/i
    );
    if (!m?.[1]) return "";
    const candidate = trimPunctuation(m[1]);
    return looksLikeHumanName(candidate) ? candidate : "";
  }

  function extractData() {
    const out = {};
    const sources = {};
    const messages = receivedMessages();

    for (const m of messages) {
      const text = String(m.text || "").replace(/\r/g, "\n");

      // Ordem de leitura:
      // 0) linguagem natural, inclusive transcrição de áudio;
      // 1) formato em linhas;
      // 2) mensagem corrida com rótulos/contexto;
      // 3) regras genéricas.
      const isAudioTranscript =
        Boolean(m.audioTranscript);

      parseConversationalTranscript(
        text,
        out,
        sources,
        isAudioTranscript
      );

      // Áudio transcrito usa parser dedicado de linguagem natural.
      // Não passa pelas heurísticas de "formulário em linhas", porque uma
      // frase como "Esse aí é o meu nome de CPF" não é um nome.
      if (!isAudioTranscript) {
        parseFormStyleReply(
          text,
          out,
          sources
        );

        parseInlineOrderMessage(
          text,
          out,
          sources
        );

        // Reaplica o padrão forte após o parser estruturado,
        // para corrigir casos em que uma linha inteira foi engolida como "Rua".
        parsePackedStreetNumber(
          text,
          out,
          sources
        );
      }

      const malformedEmailName = nameBeforeMalformedEmail(text);
      if (malformedEmailName) {
        preferName(
          out,
          sources,
          malformedEmailName,
          "mensagem · nome antes do e-mail"
        );
      }

      const emailText = cleanEmailTypos(text);
      const email =
        emailText.match(
          /[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/i
        )?.[0];

      if (email) {
        setIfEmpty(
          out,
          sources,
          "email",
          repairFragmentEmail(
            emailText,
            email
          ),
          "mensagem"
        );
      }

      const cpf = findCpf(text);
      if (cpf) setIfEmpty(out, sources, "cpf", formatCpf(cpf), "mensagem");

      const zip = findZip(text);
      if (zip) setIfEmpty(out, sources, "zip", formatZip(zip), "mensagem");

      if (!isAudioTranscript) {
        const name =
          extractLabel(
            text,
            [
              "nome completo",
              "seu nome completo",
              "nome"
            ]
          );

        if (
          name &&
          looksLikeHumanName(name) &&
          !/^(rua|avenida|estrada|cpf|cep|email|e-mail)\b/i.test(name)
        ) {
          setIfEmpty(
            out,
            sources,
            "name",
            titleCase(name),
            "mensagem"
          );
        }
      }

      if (!isAudioTranscript) {
      let street =
        extractLabel(
          text,
          [
            "rua",
            "logradouro",
            "endereco",
            "endereço"
          ]
        );

      if (street) {
        // Para antes de número/quadra/lote/bairro/cidade/estado/CPF/CEP.
        street =
          street
            .split(
              /\s+(?=(?:n[uú]mero|numero|nº|n°|quadra|qd\.?|lote|lt\.?|bairro|barro|cidade|estado|uf|cpf|cep)\b)/i
            )[0];

        const numbered =
          street.match(
            /^(.*?)(?:\s*,?\s+)(\d+[A-Za-z0-9/-]*)$/
          );

        if (numbered) {
          setIfEmpty(
            out,
            sources,
            "street",
            canonicalStreet(
              numbered[1]
            ),
            "mensagem"
          );

          setIfEmpty(
            out,
            sources,
            "number",
            numbered[2],
            "mensagem"
          );
        } else {
          setIfEmpty(
            out,
            sources,
            "street",
            canonicalStreet(
              street
            ),
            "mensagem"
          );
        }
      }

      const number =
        extractLabel(
          text,
          [
            "numero",
            "número",
            "nº",
            "n°"
          ]
        );

      if (number) {
        const parsed =
          parseNumberValue(
            number
          );

        if (parsed.number) {
          setIfEmpty(
            out,
            sources,
            "number",
            parsed.number,
            "mensagem"
          );
        }

        if (parsed.complement) {
          setIfEmpty(
            out,
            sources,
            "complement",
            parsed.complement,
            "mensagem"
          );
        }
      }

      const neighborhood =
        extractLabel(
          text,
          ["bairro"]
        );

      if (neighborhood) {
        setIfEmpty(
          out,
          sources,
          "neighborhood",
          cleanNeighborhoodValue(
            neighborhood
          ),
          "mensagem"
        );
      }

      const city =
        extractLabel(
          text,
          ["cidade"]
        );

      if (city) {
        const cityValue =
          stripTrailingState(
            cleanSimpleField(
              city,
              ["cidade"]
            )
          );

        if (cityValue) {
          setIfEmpty(
            out,
            sources,
            "city",
            titleCase(
              cityValue
            ),
            "mensagem"
          );
        }
      }

      const uf = extractLabel(text, ["estado", "uf"]);
      if (uf) {
        const normalizedUf = stateToUf(uf);
        setIfEmpty(
          out,
          sources,
          "state",
          normalizedUf || titleCase(uf),
          "mensagem"
        );
      }

      parseCombinedLine(text, out, sources);
      parseLooseAddress(text, out, sources);

      const punctuated =
        compact(text)
          .replace(/\s+/g, " ")
          .match(/^(?:rua\.?\s*)?(travessa|rua|avenida|av\.?|estrada|rodovia)\.?\s+(.+?)\.?\s+(\d{1,6}[A-Za-z]?)\.?\s*([A-Za-z]{1,3})?\.?$/i);

      if (punctuated) {
        const kind = punctuated[1];
        const body = punctuated[2];
        const normalizedKind = /^av/i.test(kind) ? "Av." : titleCase(kind);

        setIfEmpty(out, sources, "street", canonicalStreet(`${normalizedKind} ${body}`), "mensagem");
        setIfEmpty(out, sources, "number", punctuated[3], "mensagem");

        if (punctuated[4]) {
          setIfEmpty(out, sources, "complement", punctuated[4].toUpperCase(), "mensagem");
        }
      }
      }

    }

    // Endereço explícito com vírgulas tem prioridade final sobre
    // as heurísticas do formulário/linhas.
    for (const m of messages) {
      if (m?.audioTranscript) continue;

      parseCommaSeparatedAddress(
        m?.text || "",
        out,
        sources
      );
    }

    // CEP dividido em duas mensagens, por exemplo:
    // "Cp.28470" + ".000".
    if (!out.zip) {
      const splitZip =
        splitZipFromMessages(
          messages
        );

      if (splitZip) {
        out.zip =
          formatZip(
            splitZip
          );

        sources.zip =
          "mensagens";
      }
    }

    // Endereço corrido sem "Rua", mas com CEP na mesma mensagem.
    // Ex.: "Zélia Maria Abichequer 321 101 Lajeado RS Brasil CEP 95900-708".
    for (const m of messages) {
      const raw =
        compact(
          m?.text
        );

      if (
        !findZip(raw) ||
        /\b(?:rua|r\.?(?=\s)|avenida|av\.?(?=\s)|estrada|rodovia)\b/i.test(
          raw
        ) ||
        findCpf(raw)
      ) {
        continue;
      }

      const withoutZip =
        raw
          .replace(
            /\b(?:cep\s*)?\d{5}[-.\s]?\d{3}\b/ig,
            " "
          )
          .replace(
            /\bbrasil\b/ig,
            " "
          );

      const stateInfo =
        stateMention(
          withoutZip
        );

      let core =
        compact(
          withoutZip
        );

      if (stateInfo?.uf) {
        core =
          core.replace(
            new RegExp(
              `\\b${stateInfo.uf}\\b`,
              "i"
            ),
            " "
          );
      }

      const loose =
        compact(core).match(
          /^(.+?\D)\s+(\d{1,6})(?:\s+(\d{1,5}))?\s+([A-Za-zÀ-ÖØ-öø-ÿ'’ -]{2,80})$/
        );

      if (!loose) continue;

      const streetCandidate =
        trimPunctuation(
          loose[1]
        );

      const numberCandidate =
        loose[2];

      const secondNumber =
        loose[3];

      const cityCandidate =
        stripTrailingState(
          loose[4]
        );

      if (
        streetCandidate &&
        streetCandidate.split(/\s+/).length >= 2
      ) {
        setIfEmpty(
          out,
          sources,
          "street",
          titleCase(
            streetCandidate
          ),
          "mensagem"
        );
      }

      if (
        numberCandidate &&
        !out.number
      ) {
        out.number =
          numberCandidate;

        sources.number =
          "mensagem";
      }

      if (
        secondNumber &&
        !out.complement
      ) {
        out.complement =
          `Complemento ${secondNumber}`;

        sources.complement =
          "mensagem";
      }

      if (
        cityCandidate &&
        !out.city
      ) {
        out.city =
          titleCase(
            cityCandidate
          );

        sources.city =
          "mensagem";
      }
    }

    // CPF pode ser corrigido pelo cliente depois da primeira resposta.
    // Ex.: envia um CPF inválido, o atendente avisa, e o cliente manda outro.
    // O CPF válido mais recente tem prioridade sobre o primeiro encontrado.
    const bestCpf =
      selectBestCpfFromMessages(
        messages
      );

    if (bestCpf) {
      out.cpf =
        bestCpf.formatted;

      sources.cpf =
        bestCpf.valid
          ? "mensagem mais recente · CPF válido"
          : "mensagem mais recente";
    }

    // UF pontuada enviada pelo cliente (S.P, R.J, R.S...) tem prioridade
    // sobre inferências anteriores, inclusive falsos "PA" vindos da palavra "para".
    let latestDottedUf = "";
    for (const m of messages) {
      if (m?.audioTranscript) continue;

      const rawText = String(m?.text || "");
      const dotted = [...rawText.matchAll(
        /(?:^|[^A-Za-z])([A-Za-z])\s*\.\s*([A-Za-z])\.?(?=$|[^A-Za-z])/ig
      )];

      if (dotted.length) {
        const last = dotted[dotted.length - 1];
        const candidate = (last[1] + last[2]).toUpperCase();

        if (
          /^(AC|AL|AP|AM|BA|CE|DF|ES|GO|MA|MT|MS|MG|PA|PB|PR|PE|PI|RJ|RN|RS|RO|RR|SC|SP|SE|TO)$/.test(candidate)
        ) {
          latestDottedUf = candidate;
        }
      }
    }

    if (latestDottedUf) {
      out.state = latestDottedUf;
      sources.state = "mensagem · UF pontuada";
    }

    // Nome enviado logo após o CPF tem prioridade final.
    for (const m of messages) {
      if (m?.audioTranscript) continue;

      const trailingName =
        nameAfterCpfLabel(
          m?.text || ""
        );

      if (trailingName) {
        out.name =
          titleCase(
            trailingName
          );

        sources.name =
          "mensagem · nome após CPF";
      }
    }

    // Limpeza final do bairro para casos como "B:sarandi".
    if (out.neighborhood) {
      out.neighborhood =
        cleanNeighborhoodValue(
          out.neighborhood
        );
    }

    // Nome prefixado em mensagem corrida (mensagens de texto).
    // Transcrições de áudio já foram tratadas pelo parser conversacional.
    for (const m of messages) {
      if (m?.audioTranscript) continue;

      const raw =
        compact(
          m?.text
        );

      const prefix =
        raw.match(
          /^(.+?)\s+(?=cidade\b|rua\b|avenida\b|estrada\b|bairro\b|cpf\b|cep\b)/i
        )?.[1];

      if (
        prefix &&
        looksLikeHumanName(
          prefix
        )
      ) {
        preferName(
          out,
          sources,
          prefix,
          "mensagem"
        );
      }

      const standalone =
        standaloneNameCandidate(
          m?.text
        );

      if (standalone) {
        preferName(
          out,
          sources,
          standalone,
          "mensagem"
        );
      }
    }

    // Correção forte para respostas em várias linhas (nome+CPF, rua s/n,
    // bairro em linha própria e cidade-UF).
    applyMultilineCustomerDataFix(
      out,
      sources,
      messages
    );

    // V15.47: alguns clientes enviam cada dado em uma mensagem separada.
    // Se o ViaCEP não trouxer bairro, preservamos "Centro" quando ele foi
    // informado isoladamente pelo cliente.
    if (!out.neighborhood) {
      const centroMsg = messages.find(m =>
        m?.received !== false && normalize(compact(m?.text || "")) === "centro"
      );
      if (centroMsg) {
        out.neighborhood = "Centro";
        sources.neighborhood = "mensagem · bairro informado";
      }
    }

    // Segurança final: nenhuma extração anterior pode deixar um endereço
    // ocupando o campo de nome.
    if (
      out.name &&
      (!looksLikeHumanName(out.name) || isGenericConfirmationPhrase(out.name) || isPaymentDecisionPhrase(out.name))
    ) {
      delete out.name;
      delete sources.name;
    }

    const meta =
      state.currentConversation ||
      {};

    // Perfil é fallback; uma resposta mais completa do cliente pode substituí-lo.
    if (
      meta.name &&
      looksLikeHumanName(
        meta.name
      )
    ) {
      if (!out.name) {
        out.name =
          titleCase(
            meta.name
          );

        sources.name =
          "perfil do CRM";
      } else {
        preferName(
          out,
          sources,
          meta.name,
          "perfil do CRM"
        );
      }
    }

    const dom =
      domProfileData();

    if (
      dom.name &&
      looksLikeHumanName(
        dom.name
      )
    ) {
      if (!out.name) {
        out.name =
          titleCase(
            dom.name
          );

        sources.name =
          "perfil do CRM";
      }
    }

    // V15.48: limpeza final de pontuação e separação cidade + estado.
    // Ex.: "Porecatu Paraná!" -> cidade Porecatu, estado PR.
    for (const key of ["name", "street", "neighborhood", "city", "state"]) {
      if (out[key]) out[key] = trimPunctuation(out[key]);
    }

    if (out.city) {
      const cityState = splitCityStateTail(out.city);
      if (cityState.city) out.city = cityState.city;
      if (!out.state && cityState.state) {
        out.state = cityState.state;
        sources.state = "mensagem · cidade + estado";
      }
    }

    // Fallback adicional para CEP explicitamente rotulado, mesmo quando
    // vier em texto corrido com outros campos logo depois.
    if (!out.zip) {
      for (const m of [...messages].reverse()) {
        const raw = String(m?.text || "");
        const match = raw.match(/\bce[po]\b\s*[:.=-]?\s*((?:\d[\s.\-]*){8})(?!\d)/i);
        if (!match?.[1]) continue;
        const d = digits(match[1]);
        if (d.length === 8) {
          out.zip = formatZip(d);
          sources.zip = "mensagem · CEP explícito";
          break;
        }
      }
    }

    const phoneCandidate =
      compact(meta.phone || "") ||
      (
        digits(meta.contactId || "").length >= 10
          ? compact(meta.contactId)
          : ""
      );

    if (phoneCandidate) {
      const normalizedPhone =
        normalizeBrazilStoredPhone(
          phoneCandidate
        );

      setIfEmpty(
        out,
        sources,
        "phone",
        normalizedPhone || phoneCandidate,
        normalizedPhone !== digits(phoneCandidate)
          ? "perfil do CRM · 9 ajustado"
          : "perfil do CRM"
      );
    }

    if (dom.email) {
      setIfEmpty(out, sources, "email", dom.email.toLowerCase(), "perfil do CRM");
    }
    if (dom.phone) {
      const normalizedDomPhone =
        normalizeBrazilStoredPhone(
          dom.phone
        );

      setIfEmpty(
        out,
        sources,
        "phone",
        normalizedDomPhone || dom.phone,
        normalizedDomPhone !== digits(dom.phone)
          ? "perfil do CRM · 9 ajustado"
          : "perfil do CRM"
      );
    }

    state.lastExtractedAt = new Date().toISOString();
    return { values: out, sources, messageCount: messages.length };
  }

  function cacheKey() {
    return CACHE_PREFIX + String(state.currentConversationId || "none");
  }

  async function loadSaved() {
    if (!state.currentConversationId) return {};
    try {
      const result = await chrome.storage.local.get(cacheKey());
      return result?.[cacheKey()] || {};
    } catch {
      return {};
    }
  }

  async function saveCurrent() {
    if (!state.currentConversationId) return;

    // Salva somente correções feitas manualmente.
    // Dados capturados automaticamente são relidos toda vez para não
    // congelar um erro antigo como aconteceu na V15.9.
    const manualValues = {};

    for (const key of state.edited) {
      const value = compact(state.values[key]);
      if (value) manualValues[key] = value;
    }

    try {
      await chrome.storage.local.set({
        [cacheKey()]: {
          manualValues,
          imageValues: {
            ...(state.imageValues || {})
          },
          imageAttachmentId:
            state.imageAttachmentId || null,
          // V15.48: preserva somente um nome automático confiável.
          // Os demais campos continuam sendo relidos para não congelar erros.
          trustedName:
            (state.values?.name && looksLikeHumanName(state.values.name))
              ? titleCase(cleanNameCandidate(state.values.name))
              : null,
          savedAt: new Date().toISOString()
        }
      });
    } catch {}
  }

  function mergeMessages(conversationId, messages) {
    const id = String(conversationId || "");
    if (!id || !Array.isArray(messages)) return;

    let map = state.messagesByConversation.get(id);
    if (!map) {
      map = new Map();
      state.messagesByConversation.set(id, map);
    }

    for (const m of messages) {
      if (!m?.id) continue;
      map.set(String(m.id), m);
    }

    if (id === String(state.currentConversationId || "") && state.host) {
      refreshExtraction(false);
    }
  }

  function mergeAudioTranscript(payload) {
    const text = compact(payload?.text);
    if (!text) return;

    const id = String(
      payload?.conversationId ||
      state.currentConversationId ||
      ""
    );

    if (!id) return;

    let map = state.messagesByConversation.get(id);
    if (!map) {
      map = new Map();
      state.messagesByConversation.set(id, map);
    }

    const baseId =
      payload?.messageId ||
      payload?.attachmentId ||
      ("anon_" + normalize(text).slice(0, 60));

    const pseudoId =
      "audio-transcript:" +
      String(baseId);

    const createdAt =
      payload?.createdAt ||
      new Date().toISOString();

    map.set(
      pseudoId,
      {
        id: pseudoId,
        conversationId: id,
        received: true,
        status: "RECEIVED",
        text,
        createdAt,
        audioTranscript: true,
        sourceAttachmentId:
          payload?.attachmentId || null,
        sourceMessageId:
          payload?.messageId || null
      }
    );

    if (
      id === String(state.currentConversationId || "") &&
      state.host
    ) {
      refreshExtraction(false);
    }
  }

  function fieldInput(key) {
    return state.host?.shadowRoot?.querySelector(`[data-field="${key}"]`) || null;
  }

  function escapeHtml(value) {
    return String(value || "")
      .replace(/&/g,"&amp;")
      .replace(/</g,"&lt;")
      .replace(/>/g,"&gt;")
      .replace(/"/g,"&quot;");
  }

  function darkMode() {
    try {
      const c = getComputedStyle(document.body).backgroundColor;
      const m = c.match(/rgba?\((\d+)[,\s]+(\d+)[,\s]+(\d+)/i);
      if (m) {
        return (Number(m[1]) + Number(m[2]) + Number(m[3])) / 3 < 150;
      }
    } catch {}
    return window.matchMedia?.("(prefers-color-scheme: dark)")?.matches ?? true;
  }

  function buildPanel() {
    closePanel();

    const host = document.createElement("div");
    host.id = "crm-order-data-v15-34";
    Object.assign(host.style, {
      position:"fixed",
      inset:"0",
      zIndex:"2147483647",
      pointerEvents:"auto"
    });

    const shadow = host.attachShadow({mode:"open"});
    const dark = darkMode();
    const vars = dark ? {
      bg:"#1d1f21", bg2:"#25282b", bg3:"#2b2f33", text:"#f3f5f7",
      muted:"#aab2b8", border:"#3c4247", blue:"#62aef2", good:"#77c995",
      warn:"#e3b864", backdrop:"rgba(0,0,0,.55)"
    } : {
      bg:"#ffffff", bg2:"#f6f8fa", bg3:"#ffffff", text:"#1d2730",
      muted:"#66717b", border:"#dce2e7", blue:"#3c93e6", good:"#2d8a55",
      warn:"#a56b00", backdrop:"rgba(0,0,0,.32)"
    };

    shadow.innerHTML = `
      <style>
        *{box-sizing:border-box}
        .backdrop{position:fixed;inset:0;background:${vars.backdrop};display:flex;align-items:center;justify-content:center;padding:14px;font-family:Arial,Helvetica,sans-serif;color:${vars.text}}
        .card{width:min(600px,100%);max-height:min(760px,calc(100vh - 28px));overflow:hidden;display:flex;flex-direction:column;background:${vars.bg};border:1px solid ${vars.border};border-radius:14px;box-shadow:0 16px 48px rgba(0,0,0,.34)}
        .head{display:flex;align-items:flex-start;justify-content:space-between;gap:12px;padding:14px 15px 12px;border-bottom:1px solid ${vars.border};background:${vars.bg2}}
        .title{font-size:16px;font-weight:700}.sub{margin-top:4px;font-size:10px;color:${vars.muted};line-height:1.35}
        .close{width:32px;height:32px;border:1px solid ${vars.border};border-radius:9px;background:${vars.bg3};color:${vars.text};font-size:18px;cursor:pointer}
        .body{overflow:auto;padding:13px 15px 15px}
        .summary{display:flex;align-items:center;justify-content:space-between;gap:10px;padding:8px 10px;border:1px solid ${vars.border};background:${vars.bg2};border-radius:10px;margin-bottom:12px;font-size:10px;color:${vars.muted}}
        .summary strong{color:${vars.text};font-size:12px}.grid{display:grid;grid-template-columns:1fr 1fr;gap:10px}
        .field{display:flex;flex-direction:column;gap:4px}.field label{display:flex;align-items:center;justify-content:space-between;gap:8px;font-size:10px;color:${vars.muted};font-weight:700;text-transform:uppercase;letter-spacing:.03em}
        .source{font-size:9px;text-transform:none;font-weight:500;letter-spacing:0;color:${vars.muted}}
        input{width:100%;height:38px;border:1px solid ${vars.border};background:${vars.bg3};color:${vars.text};border-radius:9px;padding:0 10px;font-size:12.5px;outline:none}
        input:focus{border-color:${vars.blue};box-shadow:0 0 0 2px color-mix(in srgb, ${vars.blue} 22%, transparent)}
        input.missing{border-style:dashed;color:${vars.muted}}
        input.invalid{border-color:${vars.warn}}
        .validation{min-height:12px;font-size:9px;color:${vars.muted}}
        .validation.good{color:${vars.good}}.validation.warn{color:${vars.warn}}
        .actions{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:8px;margin-top:14px}
        button.action{min-height:42px;border:0;border-radius:10px;padding:0;font-size:18px;font-weight:700;cursor:pointer;background:${vars.blue};color:#0e1820;display:flex;align-items:center;justify-content:center;line-height:1}
        button.action:hover{filter:brightness(1.03)}
        button.secondary{background:${vars.bg2};color:${vars.text};border:1px solid ${vars.border}}
        button.skale{background:${vars.blue};color:#0e1820;border:0}
        .status{min-height:16px;margin-top:10px;color:${vars.muted};font-size:10px;line-height:1.4}
        .privacy{margin-top:12px;padding-top:10px;border-top:1px solid ${vars.border};color:${vars.muted};font-size:9px;line-height:1.45}
        .preview-backdrop{position:absolute;inset:0;background:rgba(0,0,0,.58);display:none;align-items:center;justify-content:center;padding:16px;z-index:4}
        .preview-backdrop.open{display:flex}
        .preview-sheet{width:min(520px,100%);max-height:min(92vh,820px);display:flex;flex-direction:column;background:${vars.bg};border:1px solid ${vars.border};border-radius:16px;overflow:hidden;box-shadow:0 18px 52px rgba(0,0,0,.36)}
        .preview-head{display:flex;align-items:flex-start;justify-content:space-between;gap:12px;padding:14px 15px;border-bottom:1px solid ${vars.border};background:${vars.bg2}}
        .preview-title{font-size:14px;font-weight:700}.preview-sub{margin-top:4px;font-size:10px;color:${vars.muted};line-height:1.4}
        .preview-body{padding:12px 14px 14px;overflow:auto}
        .preview-img-wrap{padding:8px;border-radius:14px;border:1px solid ${vars.border};background:${vars.bg2}}
        .preview-img-wrap img{display:block;width:100%;height:auto;border-radius:10px;background:#fff}
        .preview-note{margin-top:10px;font-size:10px;line-height:1.45;color:${vars.muted}}
        .preview-actions{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:8px;margin-top:12px}
        .preview-actions button{min-height:40px;border-radius:10px;border:1px solid ${vars.border};background:${vars.bg3};color:${vars.text};font-size:17px;font-weight:700;cursor:pointer}
        .preview-actions button.primary{background:${vars.blue};border-color:transparent;color:#0e1820}

        @media(max-width:768px){.backdrop{padding:0;align-items:flex-end}.card{width:100%;max-height:94vh;border-radius:16px 16px 0 0;border-left:0;border-right:0;border-bottom:0}.head{padding:13px 14px 11px}.body{padding:12px 14px 16px}.grid{grid-template-columns:1fr;gap:9px}.actions{grid-template-columns:repeat(4,minmax(0,1fr));gap:6px}.summary{align-items:flex-start;flex-direction:column}.close{width:34px;min-width:34px}}
      </style>
      <div class="backdrop" id="backdrop">
        <div class="card" role="dialog" aria-modal="true" aria-label="Dados do pedido">
          <div class="head">
            <div>
              <div class="title">📋 Dados do pedido</div>
              <div class="sub" id="subtitle">Lendo as mensagens recebidas do cliente…</div>
            </div>
            <button class="close" id="close" type="button">×</button>
          </div>
          <div class="body">
            <div class="summary"><strong id="found">0/10 encontrados</strong><span id="messages">0 mensagens recebidas analisadas</span></div>
            <div class="grid">
              ${FIELDS.map(([key,label]) => `
                <div class="field">
                  <label>${label}<span class="source" id="src-${key}"></span></label>
                  <input data-field="${key}" autocomplete="off" placeholder="Não encontrado">
                  <div class="validation" id="val-${key}"></div>
                </div>`).join("")}
            </div>
            <div class="actions">
              <button class="action" id="copy" type="button" title="Copiar dados" aria-label="Copiar dados">📋</button>
              <button class="action secondary" id="missing" type="button" title="Copiar o que está faltando" aria-label="Copiar o que está faltando">❓</button>
              <button class="action secondary" id="fill" type="button" title="Preencher CRM" aria-label="Preencher CRM">📝</button>
              <button class="action secondary" id="refresh" type="button" title="Atualizar leitura" aria-label="Atualizar leitura">↻</button>
              <button class="action secondary" id="image" type="button" title="Ler imagem do cliente com Groq" aria-label="Ler imagem do cliente">📷</button>
              <button class="action skale" id="skale" type="button" title="Preparar pedido na Skale" aria-label="Preparar pedido na Skale">🚚</button>
              <button class="action secondary" id="card" type="button" title="Gerar card de confirmação" aria-label="Gerar card de confirmação">🪪</button>
            </div>
            <div class="status" id="status"></div>
            <div class="privacy">A leitura principal é feita localmente. A transcrição de áudio e a leitura manual de imagens usam a Groq Cloud somente quando você clicar nas respectivas ações. O CEP continua sendo conferido pelo ViaCEP. O resultado da leitura de imagem fica em cache local para evitar reprocessamento. Ao usar “Preparar pedido na Skale”, os dados do cliente são preenchidos e as próximas etapas são automatizadas somente quando houver informação segura; a extensão nunca clica em “Lançar pedido”.</div>
          </div>
          <div class="preview-backdrop" id="confirm-modal" aria-hidden="true">
            <div class="preview-sheet">
              <div class="preview-head">
                <div>
                  <div class="preview-title">🪪 Card de confirmação</div>
                  <div class="preview-sub">Use este card no lugar do print: ele sai inteiro no celular e pode ser compartilhado com o cliente.</div>
                </div>
                <button class="close" id="confirm-close-top" type="button">×</button>
              </div>
              <div class="preview-body">
                <div class="preview-img-wrap">
                  <img id="confirm-preview-img" alt="Prévia do card">
                </div>
                <div class="preview-note">Dica: no celular, use o botão de compartilhar para enviar direto ao WhatsApp quando o navegador suportar. Caso contrário, baixe a imagem e anexe normalmente.</div>
                <div class="preview-actions">
                  <button class="primary" id="confirm-share" type="button" title="Compartilhar">📲</button>
                  <button id="confirm-download" type="button" title="Baixar imagem">⬇</button>
                  <button id="confirm-copytext" type="button" title="Copiar texto">📋</button>
                  <button id="confirm-close" type="button" title="Fechar">✕</button>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>`;

    document.documentElement.appendChild(host);
    state.host = host;

    shadow.getElementById("close").addEventListener("click", closePanel);
    shadow.getElementById("backdrop").addEventListener("click", e => {
      if (e.target?.id === "backdrop") closePanel();
    });

    for (const [key] of FIELDS) {
      const input = fieldInput(key);
      input.addEventListener("input", () => {
        state.values[key] = compact(input.value);
        state.sources[key] = "editado";
        state.edited.add(key);
        // Qualquer correção manual invalida o PNG anterior para o card
        // sempre refletir exatamente os valores que estão no painel.
        revokeConfirmationCardUrl();
        renderFields();
        saveCurrent();
      });
    }

    shadow.getElementById("copy").addEventListener("click", copyData);
    shadow.getElementById("missing").addEventListener("click", copyMissing);
    shadow.getElementById("refresh").addEventListener("click", () => refreshExtraction(true));
    shadow.getElementById("image").addEventListener("click", () => readCustomerImage(true));
    shadow.getElementById("fill").addEventListener("click", fillCRM);
    shadow.getElementById("skale").addEventListener("click", () => sendToSkale(false));
    shadow.getElementById("card").addEventListener("click", openConfirmationCardPreview);
    shadow.getElementById("confirm-close-top").addEventListener("click", closeConfirmationCardPreview);
    shadow.getElementById("confirm-close").addEventListener("click", closeConfirmationCardPreview);
    shadow.getElementById("confirm-download").addEventListener("click", downloadConfirmationCard);
    shadow.getElementById("confirm-copytext").addEventListener("click", copyData);
    shadow.getElementById("confirm-share").addEventListener("click", shareConfirmationCard);
    shadow.getElementById("confirm-modal").addEventListener("click", e => {
      if (e.target?.id === "confirm-modal") closeConfirmationCardPreview();
    });
  }

  function closePanel() {
    revokeConfirmationCardUrl();
    state.host?.remove();
    state.host = null;
  }

  function setStatus(text) {
    const el = state.host?.shadowRoot?.getElementById("status");
    if (el) el.textContent = text || "";
  }

  function postPanelStatus(message) {
    window.postMessage({
      source: PAGE_SOURCE,
      payload: {
        type: "panel-status",
        message: String(message || "")
      }
    }, "*");
  }

  function renderFields(messageCount = null) {
    if (!state.host) return;
    let found = 0;

    for (const [key, , required] of FIELDS) {
      const input = fieldInput(key);
      if (!input) continue;
      const value = compact(state.values[key]);
      if (document.activeElement !== input && input.value !== value) input.value = value;
      input.classList.toggle("missing", !value);
      input.classList.remove("invalid");
      if (value && required !== false) found += 1;

      const src = state.host.shadowRoot.getElementById(`src-${key}`);
      if (src) src.textContent = value ? (state.sources[key] || "") : "faltando";

      const val = state.host.shadowRoot.getElementById(`val-${key}`);
      if (val) {
        val.textContent = "";
        val.className = "validation";
      }
    }

    const cpf = compact(state.values.cpf);
    if (cpf) {
      const ok = cpfValid(cpf);
      const input = fieldInput("cpf");
      const val = state.host.shadowRoot.getElementById("val-cpf");
      if (val) {
        val.textContent = ok ? "CPF válido" : "Confira este CPF";
        val.className = `validation ${ok ? "good" : "warn"}`;
      }
      if (!ok) input?.classList.add("invalid");
    }

    const extraChecks = [
      ["zip", compact(state.values.zip), value => digits(value).length === 8, "CEP válido", "Confira este CEP"],
      ["email", compact(state.values.email), emailValid, "E-mail válido", "Confira este e-mail"],
      ["phone", compact(state.values.phone), phoneValid, "Telefone válido", "Confira este telefone"]
    ];

    for (const [key, value, validator, goodText, warnText] of extraChecks) {
      if (!value) continue;
      const ok = validator(value);
      const input = fieldInput(key);
      const val = state.host.shadowRoot.getElementById(`val-${key}`);
      if (val) {
        val.textContent = ok ? goodText : warnText;
        val.className = `validation ${ok ? "good" : "warn"}`;
      }
      if (!ok) input?.classList.add("invalid");
    }

    const zipValidation = state.host.shadowRoot.getElementById("val-zip");
    if (zipValidation && state.cepAudit?.checked) {
      const divs = state.cepAudit.divergences || [];
      zipValidation.textContent = divs.length
        ? `⚠️ CEP confirmado, mas confira: ${divs.join(", ")}`
        : "✓ CEP confirmado pelo ViaCEP";
      zipValidation.className = `validation ${divs.length ? "warn" : "good"}`;
    }

    const foundEl = state.host.shadowRoot.getElementById("found");
    if (foundEl) {
      const summary = orderValidation();
      foundEl.textContent = summary.ready
        ? `✅ ${summary.found}/${summary.total} essenciais • pronto para Skale`
        : `${summary.found}/${summary.total} essenciais encontrados`;
    }

    if (messageCount != null) {
      const msgEl = state.host.shadowRoot.getElementById("messages");
      if (msgEl) msgEl.textContent = `${messageCount} mensagens recebidas analisadas`;
    }

    const subtitle = state.host.shadowRoot.getElementById("subtitle");
    if (subtitle) {
      subtitle.textContent = state.currentConversation?.name
        ? `Cliente: ${state.currentConversation.name}`
        : "Conversa atual";
    }
  }

  function normalizeImageExtractedData(data) {
    const out = {};

    const name =
      compact(data?.name);

    if (name && looksLikeHumanName(name)) {
      out.name =
        titleCase(name);
    }

    const street =
      compact(data?.street);

    if (street) {
      out.street =
        canonicalStreet(street);
    }

    const number =
      compact(data?.number);

    if (number) {
      out.number =
        number;
    }

    const complement =
      compact(data?.complement);

    if (complement) {
      out.complement =
        complement;
    }

    const neighborhood =
      compact(data?.neighborhood);

    if (neighborhood) {
      out.neighborhood =
        cleanNeighborhoodValue(
          neighborhood
        );
    }

    const city =
      compact(data?.city);

    if (city) {
      out.city =
        titleCase(city);
    }

    const uf =
      stateToUf(
        data?.state
      );

    if (uf) {
      out.state = uf;
    }

    const zip =
      compact(data?.zip);

    if (digits(zip).length === 8) {
      out.zip =
        formatZip(zip);
    }

    const cpf =
      compact(data?.cpf);

    if (digits(cpf).length === 11 && cpfValid(cpf)) {
      out.cpf =
        formatCpf(cpf);
    }

    const email =
      compact(data?.email)
        .toLowerCase();

    if (
      /^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}$/i.test(email)
    ) {
      out.email = email;
    }

    return out;
  }

  function looksLikeBrazilianUtilityDocument(rawText) {
    const n = normalize(String(rawText || ""));
    if (!n) return false;

    let score = 0;
    if (/\b(?:companhia|saneamento|energia|agua|água|esgoto|fatura)\b/i.test(n)) score += 1;
    if (/\bcnpj\b/i.test(n)) score += 1;
    if (/\b(?:codigo do imovel|código do imóvel|convenio|convênio|localizacao|localização)\b/i.test(n)) score += 1;
    if (/\b(?:usuario|usuário|titular|cliente)\b/i.test(n)) score += 1;
    if (/\bcep\b/i.test(n)) score += 1;
    return score >= 2;
  }

  function ocrDigitsNearLabel(value) {
    return String(value || "")
      .toUpperCase()
      .replace(/[OQ]/g, "0")
      .replace(/[IL|]/g, "1")
      .replace(/S/g, "5")
      .replace(/B/g, "8")
      .replace(/[^0-9]/g, "");
  }

  function parseBrazilianDocumentOcr(rawText, out, sources) {
    const raw = String(rawText || "").replace(/\r/g, "\n");
    const lines = raw
      .split(/\n+/)
      .map(compact)
      .filter(Boolean);

    if (!lines.length) return;

    const source = "imagem OCR · documento";
    const utilityMode = looksLikeBrazilianUtilityDocument(rawText);

    // Em contas de água/luz, o nome do titular normalmente aparece depois
    // de Usuário/Titular/Cliente. Isso tem prioridade sobre cabeçalhos da empresa.
    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      const sameLine = line.match(/\b(?:usu[aá]rio|titular|cliente)\b\s*[:.-]?\s*([A-Za-zÀ-ÖØ-öø-ÿ'’ -]{5,90})$/i)?.[1];
      const nextLine = !sameLine && /^(?:usu[aá]rio|titular|cliente)\s*[:.-]?$/i.test(line)
        ? lines[i + 1]
        : "";
      const candidate = trimPunctuation(sameLine || nextLine || "");
      if (
        candidate &&
        looksLikeHumanName(candidate) &&
        !/\b(?:companhia|saneamento|corsan|energia|cnpj|codigo|código|localizacao|localização|convenio|convênio)\b/i.test(candidate)
      ) {
        preferName(out, sources, candidate, source);
        break;
      }
    }

    // 1) CEP explícito em documento. Tolera espaços, pontos, hífen e
    // alguns erros comuns do OCR (O/Q=0, I/l=1) somente perto do rótulo CEP.
    let zipLineIndex = -1;
    let zipDigits = "";

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      let d = "";

      const strict = line.match(/\bce[po]\s*[:.=-]?\s*((?:\d[\s.\-]*){8})(?!\d)/i);
      if (strict?.[1]) d = digits(strict[1]);

      if (d.length !== 8 && /\bcep\b/i.test(line)) {
        const tail = line.replace(/^.*?\bcep\b\s*[:.=-]?/i, "");
        const rawCandidate = tail.match(/[0-9OQIL|SB][0-9OQIL|SB\s.\-]{6,16}/i)?.[0] || "";
        const corrected = ocrDigitsNearLabel(rawCandidate);
        if (corrected.length >= 8) d = corrected.slice(0, 8);
      }

      if (d.length === 8) {
        zipLineIndex = i;
        zipDigits = d;
        break;
      }
    }

    if (zipDigits) {
      setIfEmpty(out, sources, "zip", formatZip(zipDigits), source);

      // Em contas é comum: "CEP: 72906258 SANTO ANTONIO DO DESCOBERTO GO".
      const zipLine = lines[zipLineIndex];
      let tail = zipLine
        .replace(/.*?\bce[po]\s*[:.=-]?\s*(?:\d[\s.\-]*){8}/i, " ")
        .replace(/^[,;:\-\s]+/, "")
        .trim();

      const ufMatch = tail.match(/\b(AC|AL|AP|AM|BA|CE|DF|ES|GO|MA|MT|MS|MG|PA|PB|PR|PE|PI|RJ|RN|RS|RO|RR|SC|SP|SE|TO)\b\s*$/i);
      if (ufMatch) {
        const uf = ufMatch[1].toUpperCase();
        setIfEmpty(out, sources, "state", uf, source);
        tail = tail.slice(0, ufMatch.index).trim();
      }

      if (tail && /^[A-Za-zÀ-ÖØ-öø-ÿ'’ .-]{3,80}$/.test(tail)) {
        setIfEmpty(out, sources, "city", titleCase(tail), source);
      }
    }

    // Em contas/faturas, a linha do endereço costuma conter logradouro, número e CEP.
    // Ex.: "RUA VINTE DE SETEMBRO-M.VALE II-JOBIM - 68 - CEP:94065250".
    if (utilityMode) {
      for (const line of lines) {
        if (!/\b(?:rua|avenida|av\.?|travessa|estrada|rodovia|alameda|pra[cç]a)\b/i.test(line)) continue;

        let beforeCep = line.split(/\bcep\b/i)[0].trim();
        beforeCep = beforeCep.replace(/[;|]+$/g, "").trim();

        const numberMatch = beforeCep.match(/(?:\s[-–—,]\s*|\s+)(\d{1,6}[A-Za-z]?)\s*[-–—,]?\s*$/);
        if (numberMatch?.[1]) {
          setIfEmpty(out, sources, "number", numberMatch[1], source);
          beforeCep = beforeCep.slice(0, numberMatch.index).replace(/[\s,;:-]+$/g, "").trim();
        } else if (/\bS\s*\/\s*N\b|\bSN\b/i.test(beforeCep)) {
          setIfEmpty(out, sources, "number", "SN", source);
          beforeCep = beforeCep.replace(/\b(?:S\s*\/\s*N|SN)\b.*$/i, "").trim();
        }

        if (beforeCep) {
          const street = beforeCep.match(/\b((?:rua|avenida|av\.?|travessa|estrada|rodovia|alameda|pra[cç]a)\s+.+)$/i)?.[1];
          if (street) setIfEmpty(out, sources, "street", canonicalStreet(street), source);
        }

        const quadra = line.match(/\b(?:quadra|qd?\.?|q\.)\s*[:.-]?\s*([A-Za-z0-9/-]+)/i)?.[1];
        const lote = line.match(/\b(?:lote|lt?\.?|l\.)\s*[:.-]?\s*([A-Za-z0-9/-]+)/i)?.[1];
        const complementParts = [];
        if (quadra) complementParts.push(`Quadra ${quadra}`);
        if (lote) complementParts.push(`Lote ${lote}`);
        if (complementParts.length) setIfEmpty(out, sources, "complement", complementParts.join(", "), source);
      }
    }

    // 2) Procura a linha de endereço imediatamente antes do CEP ou próxima dele.
    const addressCandidates = [];
    if (zipLineIndex >= 0) {
      for (let i = Math.max(0, zipLineIndex - 3); i < zipLineIndex; i++) {
        addressCandidates.push({ line: lines[i], index: i });
      }
    }
    lines.forEach((line, index) => {
      if (/\b(?:rua|r\.?(?=\s)|avenida|av\.?(?=\s)|travessa|estrada|rodovia|alameda|pra[cç]a)\b/i.test(line)) {
        addressCandidates.push({ line, index });
      }
    });

    const seen = new Set();
    for (const item of addressCandidates) {
      const line = compact(item.line);
      if (!line || seen.has(line)) continue;
      seen.add(line);

      const streetMatch = line.match(/\b((?:rua|r\.?(?=\s)|avenida|av\.?(?=\s)|travessa|estrada|rodovia|alameda|pra[cç]a)\s+[^,;]+?)(?=\s*,\s*(?:q(?:d|uadra)?\.?|l(?:t|ote)?\.?|s\/?n|n(?:[º°o]|umero|úmero)?\b|bairro\b)|$)/i);
      if (streetMatch?.[1]) {
        setIfEmpty(out, sources, "street", canonicalStreet(streetMatch[1]), source);
      }

      // Número residencial. Em documentos pode aparecer como S/N.
      if (/\bS\s*\/\s*N\b|\bSN\b/i.test(line)) {
        setIfEmpty(out, sources, "number", "SN", source);
      } else {
        const numberMatch = line.match(/(?:^|,|\s)\s*(?:n(?:[º°o]|umero|úmero)?\.?\s*[:.-]?\s*)?(\d+[A-Za-z0-9/-]*)\b/i);
        // Só usa número solto quando não parece ser quadra/lote.
        if (numberMatch?.[1] && !new RegExp(`(?:q(?:d|uadra)?|l(?:t|ote)?)\\.?\\s*${numberMatch[1]}\\b`, "i").test(line)) {
          setIfEmpty(out, sources, "number", numberMatch[1], source);
        }
      }

      const quadra = line.match(/\b(?:quadra|qd?\.?|q\.)\s*[:.-]?\s*([A-Za-z0-9/-]+)/i)?.[1];
      const lote = line.match(/\b(?:lote|lt?\.?|l\.)\s*[:.-]?\s*([A-Za-z0-9/-]+)/i)?.[1];
      const complementParts = [];
      if (quadra) complementParts.push(`Quadra ${quadra}`);
      if (lote) complementParts.push(`Lote ${lote}`);
      if (complementParts.length) {
        setIfEmpty(out, sources, "complement", complementParts.join(", "), source);
      }

      // Texto depois de S/N costuma ser bairro/localidade.
      const afterSn = line.match(/\b(?:S\s*\/\s*N|SN)\b\s*[,;-]?\s*([A-Za-zÀ-ÖØ-öø-ÿ'’ .-]{3,70})$/i)?.[1];
      if (afterSn && !/^(?:go|df|sp|rj|mg|pr|sc|rs|ba|pa|pe|ce|es|mt|ms|ma|pb|rn|al|se|pi|ro|rr|am|ac|ap|to)$/i.test(afterSn.trim())) {
        setIfEmpty(out, sources, "neighborhood", cleanNeighborhoodValue(afterSn), source);
      }

      const bairro = line.match(/\bbairro\s*[:.=-]?\s*([A-Za-zÀ-ÖØ-öø-ÿ'’ .-]{3,70})(?=\s+(?:cidade|cep|estado|uf)\b|$)/i)?.[1];
      if (bairro) {
        setIfEmpty(out, sources, "neighborhood", cleanNeighborhoodValue(bairro), source);
      }
    }

    // 3) Nome de titular em linha isolada e forte, próxima ao endereço.
    // É mantido como dado de baixa prioridade; applyImageValues não substitui
    // um nome já obtido da conversa/CRM.
    const addrIndex = addressCandidates.length ? addressCandidates[0].index : -1;
    if (addrIndex > 0) {
      for (let i = addrIndex - 1; i >= Math.max(0, addrIndex - 4); i--) {
        const candidate = trimPunctuation(lines[i]);
        if (
          candidate &&
          looksLikeHumanName(candidate) &&
          candidate.split(/\s+/).length >= 2 &&
          !/\b(?:residencial|convencional|tensao|tensão|tarifa|grupo|classificacao|classificação)\b/i.test(candidate)
        ) {
          preferName(out, sources, candidate, source);
          break;
        }
      }
    }
  }

  function extractDataFromOcrText(rawText) {
    const fullText =
      String(rawText || "")
        .replace(/\r/g, "\n")
        .trim();

    if (!fullText) return {};

    const out = {};
    const sources = {};

    // Primeiro tenta estrutura típica de documento/fatura.
    // Em contas de consumo evitamos o parser conversacional, pois cabeçalhos
    // como "Código do Imóvel" e "Localização" não são dados cadastrais.
    parseBrazilianDocumentOcr(fullText, out, sources);

    if (looksLikeBrazilianUtilityDocument(fullText)) {
      const emailText = cleanEmailTypos(fullText);
      const email = emailText.match(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/i)?.[0];
      if (email) {
        setIfEmpty(out, sources, "email", repairFragmentEmail(emailText, email), "imagem OCR · documento");
      }

      const cpfCandidates = cpfCandidatesFromText(fullText).filter(cpfValid);
      if (cpfCandidates.length) {
        setIfEmpty(out, sources, "cpf", formatCpf(cpfCandidates[0]), "imagem OCR · documento");
      }

      return normalizeImageExtractedData(out);
    }

    const chunks = [
      fullText,
      ...fullText
        .split(/\n+/)
        .map(compact)
        .filter(line => line.length >= 2)
    ];

    for (const text of chunks) {
      parseConversationalTranscript(
        text,
        out,
        sources,
        false
      );

      parseFormStyleReply(
        text,
        out,
        sources
      );

      parseInlineOrderMessage(
        text,
        out,
        sources
      );

      parsePackedStreetNumber(
        text,
        out,
        sources
      );

      const malformedEmailName =
        nameBeforeMalformedEmail(text);

      if (malformedEmailName) {
        preferName(
          out,
          sources,
          malformedEmailName,
          "imagem OCR · nome antes do e-mail"
        );
      }

      const emailText = cleanEmailTypos(text);
      const email =
        emailText.match(
          /[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/i
        )?.[0];

      if (email) {
        setIfEmpty(
          out,
          sources,
          "email",
          repairFragmentEmail(emailText, email),
          "imagem OCR"
        );
      }

      const cpf = findCpf(text);
      if (cpf) {
        setIfEmpty(
          out,
          sources,
          "cpf",
          formatCpf(cpf),
          "imagem OCR"
        );
      }

      const zip = findZip(text);
      if (zip) {
        setIfEmpty(
          out,
          sources,
          "zip",
          formatZip(zip),
          "imagem OCR"
        );
      }

      const name =
        extractLabel(
          text,
          [
            "nome completo",
            "seu nome completo",
            "nome"
          ]
        );

      if (
        name &&
        looksLikeHumanName(name) &&
        !/^(rua|avenida|estrada|cpf|cep|email|e-mail)\b/i.test(name)
      ) {
        preferName(
          out,
          sources,
          name,
          "imagem OCR · nome"
        );
      }

      let street =
        extractLabel(
          text,
          [
            "rua",
            "logradouro",
            "endereco",
            "endereço"
          ]
        );

      if (street) {
        street =
          street
            .split(
              /\s+(?=(?:n[uú]mero|numero|nº|n°|quadra|qd\.?|lote|lt\.?|bairro|barro|cidade|estado|uf|cpf|cep)\b)/i
            )[0];

        const numbered =
          street.match(
            /^(.*?)(?:\s*,?\s+)(\d+[A-Za-z0-9/-]*)$/
          );

        if (numbered) {
          setIfEmpty(
            out,
            sources,
            "street",
            canonicalStreet(numbered[1]),
            "imagem OCR"
          );
          setIfEmpty(
            out,
            sources,
            "number",
            numbered[2],
            "imagem OCR"
          );
        } else {
          setIfEmpty(
            out,
            sources,
            "street",
            canonicalStreet(street),
            "imagem OCR"
          );
        }
      }

      const number =
        extractLabel(
          text,
          ["numero", "número", "nº", "n°"]
        );

      if (number) {
        const parsed = parseNumberValue(number);
        if (parsed.number) {
          setIfEmpty(
            out,
            sources,
            "number",
            parsed.number,
            "imagem OCR"
          );
        }
        if (parsed.complement) {
          setIfEmpty(
            out,
            sources,
            "complement",
            parsed.complement,
            "imagem OCR"
          );
        }
      }

      const neighborhood =
        extractLabel(text, ["bairro"]);

      if (neighborhood) {
        setIfEmpty(
          out,
          sources,
          "neighborhood",
          cleanNeighborhoodValue(neighborhood),
          "imagem OCR"
        );
      }

      const city =
        extractLabel(text, ["cidade"]);

      if (city) {
        const cityValue =
          stripTrailingState(
            cleanSimpleField(city, ["cidade"])
          );

        if (cityValue) {
          setIfEmpty(
            out,
            sources,
            "city",
            titleCase(cityValue),
            "imagem OCR"
          );
        }
      }

      const uf =
        extractLabel(text, ["estado", "uf"]);

      if (uf) {
        const normalizedUf = stateToUf(uf);
        setIfEmpty(
          out,
          sources,
          "state",
          normalizedUf || titleCase(uf),
          "imagem OCR"
        );
      }

      parseCombinedLine(
        text,
        out,
        sources
      );

      parseLooseAddress(
        text,
        out,
        sources
      );
    }

    parseCommaSeparatedAddress(
      fullText,
      out,
      sources
    );

    return normalizeImageExtractedData(out);
  }

  function applyImageValues(target, sources, imageValues) {
    // V16.11: a imagem/OCR é fallback. Quando o cliente já digitou um dado
    // na conversa, preservamos o texto explícito. O CEP válido é resolvido
    // depois pelo ViaCEP, que passa a ser a fonte principal de endereço.
    for (const [key, rawValue] of Object.entries(imageValues || {})) {
      const value = compact(rawValue);
      if (!value) continue;

      if (state.edited.has(key)) continue;

      if (
        key === "cpf" &&
        target.cpf &&
        cpfValid(target.cpf) &&
        !cpfValid(value)
      ) {
        continue;
      }

      if (key === "name" && !looksLikeHumanName(value)) {
        continue;
      }

      if (key === "zip") {
        const currentZip = digits(target.zip);
        const imageZip = digits(value);

        // Um CEP válido já encontrado na conversa tem prioridade sobre OCR.
        if (currentZip.length === 8) continue;
        if (imageZip.length !== 8) continue;

        target.zip = formatZip(imageZip);
        sources.zip = "imagem do cliente";
        continue;
      }

      // Rua, número, complemento, bairro, cidade e estado da imagem só entram
      // quando a conversa ainda não forneceu o respectivo campo.
      if (!compact(target[key])) {
        target[key] = value;
        sources[key] = "imagem do cliente · fallback";
      }
    }
  }

  async function loadSavedImageValues(saved = null) {
    const data =
      saved ||
      await loadSaved();

    state.imageValues =
      normalizeImageExtractedData(
        data?.imageValues || {}
      );

    state.imageAttachmentId =
      data?.imageAttachmentId || null;

    return state.imageValues;
  }

  async function readCustomerImage(showInPanel = false) {
    const images =
      customerImageAttachments();

    if (!images.length) {
      const message =
        "Não encontrei imagem recebida do cliente após o último pedido de dados. Abra/role a conversa e tente novamente.";

      if (showInPanel) setStatus(message);
      postPanelStatus(message);
      return false;
    }

    // Prioriza a imagem mais recente da etapa atual.
    const image =
      images[images.length - 1];

    const messageLoading =
      "Lendo imagem com Groq…";

    if (showInPanel) setStatus(messageLoading);
    postPanelStatus(messageLoading);

    try {
      const response =
        await chrome.runtime.sendMessage({
          type: "read-order-image",
          imageUrl: image.url,
          mimeType: image.mimeType || "",
          fileName: image.fileName || "",
          cacheKey:
            image.id || image.url
        });

      if (!response?.ok) {
        throw new Error(
          response?.error ||
          "Não consegui ler a imagem."
        );
      }

      const normalized =
        response.fields && typeof response.fields === "object"
          ? normalizeImageExtractedData(response.fields)
          : extractDataFromOcrText(response.text || "");

      const useful =
        Object.values(normalized)
          .filter(Boolean)
          .length;

      if (!useful) {
        const message =
          "A imagem foi lida, mas não encontrei dados de cadastro nela.";

        if (showInPanel) setStatus(message);
        postPanelStatus(message);
        return false;
      }

      state.imageValues =
        normalized;

      state.imageAttachmentId =
        image.id || image.url;

      if (!Object.keys(state.values || {}).length) {
        const extracted =
          extractData();

        state.values =
          {...extracted.values};

        state.sources =
          {...extracted.sources};
      }

      applyImageValues(
        state.values,
        state.sources,
        normalized
      );

      const currentMessages =
        receivedMessages();

      await enrichByCep(
        state.values,
        state.sources,
        currentMessages
      );

      sanitizeAddressEvidence(
        state.values,
        state.sources,
        currentMessages
      );

      if (state.host) {
        renderFields(
          currentMessages.length
        );
      }

      await saveCurrent();

      const fields =
        Object.keys(normalized)
          .filter(
            key => compact(normalized[key])
          )
          .length;

      const message =
        response.cached
          ? `Imagem já processada • ${fields} dados recuperados do cache Groq.`
          : `Groq concluiu a leitura • ${fields} dados encontrados.`;

      if (showInPanel) setStatus(message);
      postPanelStatus(message);
      return true;
    } catch (err) {
      const message =
        `Erro ao ler imagem pela Groq: ${String(err?.message || err)}`;

      if (showInPanel) setStatus(message);
      postPanelStatus(message);
      return false;
    }
  }

  async function refreshExtraction(force = false) {
    if (!state.host) return;
    const extracted = extractData();
    const currentMessages = receivedMessages();
    let saved = {};

    if (!force && state.currentConversationId) {
      saved = await loadSaved();
    }

    const next = {...extracted.values};
    const nextSources = {...extracted.sources};

    await loadSavedImageValues(saved);

    // V16.11: primeiro juntamos conversa + OCR. Só depois consultamos o CEP.
    // Assim qualquer CEP válido, inclusive vindo de imagem, passa a comandar
    // rua/bairro/cidade/estado sem ser sobrescrito pelo OCR posteriormente.
    applyImageValues(
      next,
      nextSources,
      state.imageValues
    );

    await enrichByCep(
      next,
      nextSources,
      currentMessages
    );

    sanitizeAddressEvidence(
      next,
      nextSources,
      currentMessages
    );

    // V15.48: um nome válido já confirmado em leitura anterior pode voltar
    // como fallback quando a janela atual do histórico não contém a mensagem
    // onde o cliente escreveu o nome completo.
    if (saved?.trustedName && looksLikeHumanName(saved.trustedName)) {
      preferName(next, nextSources, saved.trustedName, "nome preservado");
    }

    if (!next.name) {
      const profileFallback = cleanCustomerName(
        state.currentConversation?.name || domProfileData()?.name || ""
      );
      if (profileFallback && looksLikeHumanName(profileFallback)) {
        next.name = titleCase(profileFallback);
        nextSources.name = "perfil do CRM · fallback";
      }
    }

    const manualValues = saved?.manualValues || {};

    for (const [key,value] of Object.entries(manualValues)) {
      if (compact(value)) {
        next[key] = compact(value);
        nextSources[key] = "correção manual";
        state.edited.add(key);
      }
    }

    if (force) {
      for (const key of state.edited) {
        const current = compact(state.values[key]);
        if (current) {
          next[key] = current;
          nextSources[key] = "correção manual";
        }
      }
    }

    state.values = next;
    state.sources = nextSources;
    renderFields(extracted.messageCount);
    postOrderSummary();
    saveCurrent();

    if (!extracted.messageCount) {
      setStatus("Ainda não encontrei mensagens recebidas carregadas para esta conversa. Abra/role a conversa e tente Atualizar leitura.");
    } else {
      setStatus("Mensagens e áudios transcritos foram analisados. Áudios e imagens usam a Groq; se o cliente enviou dados em uma imagem, use o botão 📷 para fazer a leitura.");
    }
  }

  function cleanCustomerName(value) {
    let text = compact(value);
    if (!text) return "";

    text = text
      .replace(/^(?:meu\s+nome(?:\s+completo)?\s*(?:é|e|:|-)?\s*)/i, "")
      .replace(/^(?:nome\s+completo\s*(?:é|e|:|-)?\s*)/i, "")
      .replace(/^(?:nome\s*(?:é|e|:|-)\s*)/i, "")
      .replace(/^(?:eu\s+sou\s+)/i, "")
      .trim();

    return text;
  }

  function cardFieldValue(key) {
    let value = compact(state.values?.[key]);

    if (!value) return "Não informado";

    if (key === "name") {
      value = cleanCustomerName(value);
      return value || "Não informado";
    }

    if (key === "cpf") return formatCpf(value) || "Não informado";
    if (key === "zip") return formatZip(value) || "Não informado";
    if (key === "phone") return formatPhone(value) || value;
    if (key === "email") return value.toLowerCase();
    if (key === "state") return value.toUpperCase();

    return value;
  }

  function confirmationCardFilename() {
    const rawName = compact(state.values?.name || state.currentConversation?.name || "pedido");
    const base = rawName
      .normalize("NFD")
      .replace(/[̀-ͯ]/g, "")
      .replace(/[^a-zA-Z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "")
      .toLowerCase() || "pedido";

    return `confirmacao-${base}.png`;
  }

  function wrapCanvasLines(ctx, text, maxWidth) {
    const content = String(text || "").replace(/\s+/g, " ").trim();
    if (!content) return [""];

    const words = content.split(" ");
    const lines = [];
    let line = "";

    for (const word of words) {
      const test = line ? `${line} ${word}` : word;
      if (ctx.measureText(test).width <= maxWidth) {
        line = test;
        continue;
      }

      if (line) lines.push(line);

      if (ctx.measureText(word).width <= maxWidth) {
        line = word;
        continue;
      }

      let chunk = "";
      for (const ch of word) {
        const testChunk = chunk + ch;
        if (ctx.measureText(testChunk).width <= maxWidth) {
          chunk = testChunk;
        } else {
          if (chunk) lines.push(chunk);
          chunk = ch;
        }
      }
      line = chunk;
    }

    if (line) lines.push(line);
    return lines.length ? lines : [""];
  }

  function drawRoundRect(ctx, x, y, width, height, radius, fill, stroke = null) {
    const r = Math.min(radius, width / 2, height / 2);
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.arcTo(x + width, y, x + width, y + height, r);
    ctx.arcTo(x + width, y + height, x, y + height, r);
    ctx.arcTo(x, y + height, x, y, r);
    ctx.arcTo(x, y, x + width, y, r);
    ctx.closePath();
    if (fill) {
      ctx.fillStyle = fill;
      ctx.fill();
    }
    if (stroke) {
      ctx.strokeStyle = stroke;
      ctx.stroke();
    }
  }

  function confirmationCardSections() {
    return [
      {
        title: "Dados do cliente",
        fields: [
          ["Nome", cardFieldValue("name")],
          ["CPF", cardFieldValue("cpf")],
          ["Telefone", cardFieldValue("phone")],
          ["E-mail", cardFieldValue("email")]
        ]
      },
      {
        title: "Endereço",
        fields: [
          ["CEP", cardFieldValue("zip")],
          ["Rua", cardFieldValue("street")],
          ["Número", cardFieldValue("number")],
          ["Complemento", cardFieldValue("complement")],
          ["Bairro", cardFieldValue("neighborhood")],
          ["Cidade", cardFieldValue("city")],
          ["Estado", cardFieldValue("state")]
        ]
      }
    ];
  }

  function buildConfirmationCardCanvas() {
    const width = 1080;
    const outer = 48;
    const cardX = 64;
    const cardW = width - cardX * 2;
    const pad = 38;
    const gap = 18;
    const innerW = cardW - pad * 2;
    const colW = (innerW - gap) / 2;
    const canvas = document.createElement("canvas");
    const ctx = canvas.getContext("2d");

    const name = cardFieldValue("name");
    const headerName =
      name !== "Não informado"
        ? name
        : cleanCustomerName(state.currentConversation?.name || "") || "Cliente";

    const fields = {
      name,
      cpf: cardFieldValue("cpf"),
      phone: cardFieldValue("phone"),
      email: cardFieldValue("email"),
      zip: cardFieldValue("zip"),
      street: cardFieldValue("street"),
      number: cardFieldValue("number"),
      complement: cardFieldValue("complement"),
      neighborhood: cardFieldValue("neighborhood"),
      city: cardFieldValue("city"),
      state: cardFieldValue("state")
    };

    const rows = [
      { section: "Dados do cliente" },
      { cells: [["Nome", fields.name, 2]] },
      { cells: [["CPF", fields.cpf, 1], ["Telefone", fields.phone, 1]] },
      { cells: [["E-mail", fields.email, 2]] },
      { section: "Endereço" },
      { cells: [["CEP", fields.zip, 1], ["Estado", fields.state, 1]] },
      { cells: [["Rua", fields.street, 2]] },
      { cells: [["Número", fields.number, 1], ["Complemento", fields.complement, 1]] },
      { cells: [["Bairro", fields.neighborhood, 1], ["Cidade", fields.city, 1]] }
    ];

    ctx.font = "600 32px Arial, Helvetica, sans-serif";
    const measured = [];
    let bodyHeight = 0;

    for (const row of rows) {
      if (row.section) {
        measured.push({ ...row, height: 58 });
        bodyHeight += 58;
        continue;
      }

      let maxLines = 1;
      const cells = row.cells.map(([label, value, span]) => {
        const available = span === 2 ? innerW - 44 : colW - 44;
        const lines = wrapCanvasLines(ctx, value, available);
        maxLines = Math.max(maxLines, lines.length);
        return { label, value, span, lines };
      });

      const height = 44 + maxLines * 40 + 30;
      measured.push({ cells, height });
      bodyHeight += height + 14;
    }

    const headerH = 205;
    const introH = 118;
    const footerH = 36;
    const totalHeight = 28 + headerH + introH + bodyHeight + footerH;

    canvas.width = width;
    canvas.height = totalHeight;

    ctx.fillStyle = "#eef2f6";
    ctx.fillRect(0, 0, width, totalHeight);

    drawRoundRect(ctx, outer, 22, width - outer * 2, totalHeight - 44, 38, "#ffffff");

    // Cabeçalho
    drawRoundRect(ctx, outer, 22, width - outer * 2, headerH, 38, "#5e9de6");
    ctx.fillStyle = "#ffffff";
    ctx.font = "700 50px Arial, Helvetica, sans-serif";
    ctx.fillText("Confirmação do pedido", cardX + 24, 88);
    ctx.font = "600 28px Arial, Helvetica, sans-serif";
    const headerLines = wrapCanvasLines(ctx, headerName, cardW - 80);
    let hy = 132;
    for (const line of headerLines.slice(0, 2)) {
      ctx.fillText(line, cardX + 24, hy);
      hy += 34;
    }

    // Introdução
    let y = 22 + headerH + 48;
    ctx.fillStyle = "#29496b";
    ctx.font = "600 27px Arial, Helvetica, sans-serif";
    const intro = "Confira abaixo os dados do pedido e me confirme se está tudo correto.";
    for (const line of wrapCanvasLines(ctx, intro, innerW)) {
      ctx.fillText(line, cardX + pad, y);
      y += 34;
    }
    y += 22;

    // Corpo compacto em duas colunas
    for (const row of measured) {
      if (row.section) {
        ctx.fillStyle = "#1c2733";
        ctx.font = "700 31px Arial, Helvetica, sans-serif";
        ctx.fillText(row.section, cardX + pad, y + 34);
        y += row.height;
        continue;
      }

      let x = cardX + pad;
      for (const cell of row.cells) {
        const cellW = cell.span === 2 ? innerW : colW;
        const missing = cell.value === "Não informado";

        drawRoundRect(
          ctx,
          x,
          y,
          cellW,
          row.height,
          22,
          missing ? "#fff8ed" : "#f6f8fb",
          missing ? "#efd29a" : "#e1e7ee"
        );

        ctx.fillStyle = "#74808d";
        ctx.font = "700 19px Arial, Helvetica, sans-serif";
        ctx.fillText(cell.label.toUpperCase(), x + 22, y + 30);

        ctx.fillStyle = missing ? "#9b6805" : "#1f2d3a";
        ctx.font = "600 32px Arial, Helvetica, sans-serif";
        let vy = y + 70;
        for (const line of cell.lines) {
          ctx.fillText(line, x + 22, vy);
          vy += 40;
        }

        x += cellW + gap;
      }

      y += row.height + 14;
    }

    return canvas;
  }

  function revokeConfirmationCardUrl() {
    if (state.confirmCardUrl) {
      try { URL.revokeObjectURL(state.confirmCardUrl); } catch {}
      state.confirmCardUrl = null;
    }
    state.confirmCardBlob = null;
  }

  function setCardModalOpen(open) {
    const modal = state.host?.shadowRoot?.getElementById("confirm-modal");
    if (!modal) return;
    modal.classList.toggle("open", !!open);
    modal.setAttribute("aria-hidden", open ? "false" : "true");
  }

  async function ensureConfirmationCardBlob() {
    if (state.confirmCardBlob && state.confirmCardUrl) {
      return { blob: state.confirmCardBlob, url: state.confirmCardUrl };
    }

    const canvas = buildConfirmationCardCanvas();

    const blob = await new Promise((resolve, reject) => {
      canvas.toBlob(result => {
        if (result) resolve(result);
        else reject(new Error("Não consegui gerar a imagem do card."));
      }, "image/png");
    });

    revokeConfirmationCardUrl();
    state.confirmCardBlob = blob;
    state.confirmCardUrl = URL.createObjectURL(blob);

    return { blob, url: state.confirmCardUrl };
  }

  async function openConfirmationCardPreview() {
    if (!state.host) return;

    try {
      setStatus("Gerando card de confirmação…");
      // Sempre reconstrói a imagem ao abrir para evitar card desatualizado
      // depois de editar CEP, número ou qualquer outro campo.
      revokeConfirmationCardUrl();
      const { url } = await ensureConfirmationCardBlob();
      const shadow = state.host.shadowRoot;
      const img = shadow.getElementById("confirm-preview-img");
      img.src = url;
      img.alt = "Card de confirmação do pedido";
      setCardModalOpen(true);
      setStatus("Card pronto para conferência/compartilhamento.");
    } catch (err) {
      setStatus(`Erro ao gerar card: ${String(err?.message || err)}`);
    }
  }

  function closeConfirmationCardPreview() {
    setCardModalOpen(false);
  }

  async function downloadConfirmationCard() {
    try {
      const { url } = await ensureConfirmationCardBlob();
      const a = document.createElement("a");
      a.href = url;
      a.download = confirmationCardFilename();
      document.body.appendChild(a);
      a.click();
      a.remove();
      setStatus("Card baixado.");
    } catch (err) {
      setStatus(`Erro ao baixar card: ${String(err?.message || err)}`);
    }
  }

  async function shareConfirmationCard() {
    try {
      const { blob } = await ensureConfirmationCardBlob();
      const file = new File([blob], confirmationCardFilename(), { type: "image/png" });

      if (navigator.canShare && navigator.canShare({ files: [file] }) && navigator.share) {
        await navigator.share({
          files: [file],
          title: "Confirmação do pedido",
          text: "Confira os dados do pedido e confirme se está tudo correto."
        });
        setStatus("Card compartilhado.");
        return;
      }

      await downloadConfirmationCard();
      setStatus("Seu navegador não suporta compartilhamento direto. Baixei o card para você enviar.");
    } catch (err) {
      if (String(err?.name || "") === "AbortError") return;
      setStatus(`Erro ao compartilhar card: ${String(err?.message || err)}`);
    }
  }

  function emailValid(value) {
    return /^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}$/i.test(compact(value));
  }

  function phoneValid(value) {
    const d = normalizeBrazilNationalPhone(value);
    return d.length === 10 || d.length === 11;
  }

  function orderValidation() {
    const v = state.values || {};
    const missing = [];
    let found = 0;

    for (const [key, label] of REQUIRED_FIELDS) {
      if (compact(v[key])) found += 1;
      else missing.push(label);
    }

    const issues = [];
    if (compact(v.cpf) && !cpfValid(v.cpf)) issues.push("CPF");
    if (compact(v.zip) && digits(v.zip).length !== 8) issues.push("CEP");
    if (compact(v.phone) && !phoneValid(v.phone)) issues.push("Telefone");

    const optionalMissing = [];
    if (!compact(v.email)) optionalMissing.push("E-mail");
    if (!compact(v.complement)) optionalMissing.push("Complemento");

    const optionalIssues = [];
    if (compact(v.email) && !emailValid(v.email)) optionalIssues.push("E-mail");

    const confirmation = detectClientConfirmation();
    state.clientConfirmation = confirmation;
    const cepDivergences = Array.isArray(state.cepAudit?.divergences)
      ? state.cepAudit.divergences
      : [];

    return {
      found,
      total: REQUIRED_FIELDS.length,
      missing,
      issues,
      optionalMissing,
      optionalIssues,
      cepConfirmed: Boolean(state.cepAudit?.checked),
      cepDivergences,
      confirmedByClient: Boolean(confirmation?.confirmed),
      confirmationText: confirmation?.text || "",
      ready: missing.length === 0 && issues.length === 0
    };
  }

  function postOrderSummary() {
    const summary = orderValidation();
    window.postMessage({
      source: PAGE_SOURCE,
      payload: {
        type: "order-data-summary",
        conversationId: state.currentConversationId || "",
        ...summary
      }
    }, "*");
    return summary;
  }

  function missingRequestText() {
    const summary = orderValidation();
    const parts = [];

    if (summary.missing.length) {
      parts.push(`faltou me enviar: ${summary.missing.join(", ")}`);
    }

    if (summary.issues.length) {
      parts.push(`preciso que confira: ${summary.issues.join(", ")}`);
    }

    if (!parts.length) {
      return "Seus dados essenciais estão completos. Pode confirmar se está tudo correto, por favor?";
    }

    return `Só mais uma coisinha, por gentileza: ${parts.join(". Também ")}.`;
  }

  function formattedText() {
    const v = state.values;
    return [
      `Nome completo: ${v.name || ""}`,
      `Rua: ${v.street || ""}`,
      `Número: ${v.number || ""}`,
      `Complemento: ${v.complement || ""}`,
      `Bairro: ${v.neighborhood || ""}`,
      `Cidade: ${v.city || ""}`,
      `Estado: ${v.state || ""}`,
      `CEP: ${v.zip || ""}`,
      `CPF: ${v.cpf || ""}`,
      `E-mail: ${v.email || ""}`,
      `Telefone: ${v.phone || ""}`
    ].join("\n");
  }

  async function copyText(text, success) {
    try {
      await navigator.clipboard.writeText(text);
      setStatus(success);
    } catch {
      const ta = document.createElement("textarea");
      ta.value = text;
      ta.style.position = "fixed";
      ta.style.opacity = "0";
      document.body.appendChild(ta);
      ta.select();
      document.execCommand("copy");
      ta.remove();
      setStatus(success);
    }
  }

  function copyData() {
    copyText(formattedText(), "Dados copiados.");
  }

  function copyMissing() {
    const summary = orderValidation();
    copyText(
      missingRequestText(),
      summary.ready
        ? "Mensagem de confirmação copiada."
        : "Mensagem pedindo somente o que falta foi copiada."
    );
  }
  function editableNearLabel(labelNames) {
    for (const label of exactLabelNodes(labelNames)) {
      if (label.tagName === "LABEL" && label.htmlFor) {
        const linked = document.getElementById(label.htmlFor);
        if (linked && /^(INPUT|TEXTAREA)$/.test(linked.tagName)) return linked;
      }

      let node = label;
      for (let i=0; node && i<4; i++, node=node.parentElement) {
        const candidate = node.querySelector?.('input:not([type="hidden"]), textarea, [contenteditable="true"]');
        if (candidate && candidate !== label) return candidate;
      }
    }
    return null;
  }

  function setNativeValue(el, value) {
    if (!el || !value) return false;
    try {
      if (el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement) {
        const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
        const setter = Object.getOwnPropertyDescriptor(proto, "value")?.set;
        setter?.call(el, value);
        el.dispatchEvent(new Event("input", {bubbles:true}));
        el.dispatchEvent(new Event("change", {bubbles:true}));
        return true;
      }
      if (el.isContentEditable) {
        el.textContent = value;
        el.dispatchEvent(new InputEvent("input", {bubbles:true,inputType:"insertText",data:value}));
        return true;
      }
    } catch {}
    return false;
  }

  async function fillCRM() {
    const addressHeader = exactLabelNodes(["Endereço","Endereco"])[0];
    if (addressHeader) {
      try { addressHeader.click(); } catch {}
      await new Promise(r => setTimeout(r, 220));
    }

    const mapping = [
      ["name", ["Nome","Nome completo"]],
      ["email", ["E-mail","Email"]],
      ["phone", ["Telefone","Celular"]],
      ["cpf", ["Documento","CPF"]],
      ["street", ["Rua","Logradouro","Endereço","Endereco"]],
      ["number", ["Número","Numero"]],
      ["complement", ["Complemento"]],
      ["neighborhood", ["Bairro"]],
      ["city", ["Cidade"]],
      ["state", ["Estado","UF"]],
      ["zip", ["CEP"]]
    ];

    let filled = 0;
    for (const [key,labels] of mapping) {
      const value = compact(state.values[key]);
      if (!value) continue;
      const input = editableNearLabel(labels);
      if (input && setNativeValue(input, value)) filled += 1;
    }

    if (filled) {
      setStatus(`${filled} campo(s) editável(is) do CRM preenchido(s). Confira e salve no CRM.`);
    } else {
      setStatus("Nesta tela o CRM não expôs campos editáveis diretamente. Use “Copiar dados”; a captura automática continua funcionando.");
    }
  }

  function nationalBrazilPhone(value) {
    // A Skale espera telefone brasileiro; mantemos DDD + número normalizado, sem +55 no payload.
    // A normalização também adiciona o nono dígito quando o CRM
    // ainda possui um celular brasileiro no formato antigo de 8 dígitos.
    return normalizeBrazilNationalPhone(value);
  }

  async function refreshValuesForDirectAction() {
    const extracted =
      extractData();

    const currentMessages =
      receivedMessages();

    let saved = {};

    if (state.currentConversationId) {
      saved =
        await loadSaved();
    }

    const next =
      {...extracted.values};

    const nextSources =
      {...extracted.sources};

    await loadSavedImageValues(saved);

    applyImageValues(
      next,
      nextSources,
      state.imageValues
    );

    // Mesma regra do painel: CEP válido é resolvido por último entre as fontes
    // automáticas, garantindo ViaCEP como autoridade de endereço.
    await enrichByCep(
      next,
      nextSources,
      currentMessages
    );

    sanitizeAddressEvidence(
      next,
      nextSources,
      currentMessages
    );

    const manualValues =
      saved?.manualValues || {};

    for (const [key, value] of Object.entries(manualValues)) {
      if (compact(value)) {
        next[key] =
          compact(value);

        nextSources[key] =
          "correção manual";
      }
    }

    state.values =
      next;

    state.sources =
      nextSources;

    postOrderSummary();
    return extracted.messageCount;
  }

  function detectSkaleProductMonths() {
    const texts = allConversationMessages()
      .slice(-40)
      .map(m => normalize(m?.text || ""))
      .filter(Boolean);

    const found = new Set();
    for (const text of texts) {
      if (/\b(?:6|seis)\s*(?:mes|meses)\b/.test(text) || /azzure\s*[-–]?\s*6\s*meses/.test(text)) found.add(6);
      if (/\b(?:4|quatro)\s*(?:mes|meses)\b/.test(text) || /azzure\s*[-–]?\s*4\s*meses/.test(text)) found.add(4);
      if (/\b(?:2|dois)\s*(?:mes|meses)\b/.test(text) || /azzure\s*[-–]?\s*2\s*meses/.test(text)) found.add(2);
    }

    return found.size === 1 ? [...found][0] : null;
  }

  function detectSkalePaymentHint() {
    const texts = allConversationMessages()
      .slice(-30)
      .map(m => normalize(m?.text || ""))
      .filter(Boolean);

    let after = false;
    let prepaid = false;

    for (const text of texts) {
      if (/\b(?:na entrega|quando chegar|depois de receber|apos receber|após receber|after pay)\b/.test(text)) after = true;
      if (/\b(?:pix|boleto|antecipad[oa]|pagar agora|pagamento antecipado)\b/.test(text)) prepaid = true;
    }

    if (after && !prepaid) return "after_pay";
    if (prepaid && !after) return "antecipada";
    return null;
  }

  async function sendToSkale(notifyPanel = false) {
    const v = state.values || {};

    const payload = {
      name: compact(v.name),
      cpf: digits(v.cpf),
      email: compact(v.email).toLowerCase(),
      phone: nationalBrazilPhone(v.phone),
      zip: digits(v.zip),
      street: compact(v.street),
      number: compact(v.number),
      complement: compact(v.complement),
      neighborhood: compact(v.neighborhood),
      city: compact(v.city),
      state: compact(v.state).toUpperCase(),
      sources: {
        street: compact(state.sources?.street),
        number: compact(state.sources?.number),
        complement: compact(state.sources?.complement),
        neighborhood: compact(state.sources?.neighborhood),
        city: compact(state.sources?.city),
        state: compact(state.sources?.state)
      },
      conversationId: state.currentConversationId || "",
      conversationName: state.currentConversation?.name || "",
      createdAt: new Date().toISOString(),
      pending: true,
      productMonths: detectSkaleProductMonths(),
      paymentHint: detectSkalePaymentHint(),
      logisticsHint: "Five Delivery"
    };

    const meaningful = [
      payload.name,
      payload.cpf,
      payload.phone,
      payload.zip,
      payload.street,
      payload.city
    ].filter(Boolean).length;

    if (!meaningful) {
      const message =
        "Ainda não há dados suficientes para preparar o pedido na Skale.";

      setStatus(message);

      if (notifyPanel) {
        postPanelStatus(message);
      }

      return;
    }

    try {
      await chrome.storage.local.set({
        skale_order_pending_v16: payload
      });

      const response = await chrome.runtime.sendMessage({
        type: "open-skale",
        url: "https://skaletracking.com.br/pedidos"
      });

      if (!response?.ok) {
        throw new Error(response?.error || "Não consegui abrir a Skale.");
      }

      const missing = REQUIRED_FIELDS
        .filter(([key]) => !compact(v[key]))
        .map(([, label]) => label);

      if (missing.length) {
        const message =
          `Skale aberta • faltam: ${missing.join(", ")}`;

        setStatus(message);

        if (notifyPanel) {
          postPanelStatus(message);
        }
      } else {
        const message =
          "Skale aberta • dados enviados para preparação do pedido";

        setStatus(message);

        if (notifyPanel) {
          postPanelStatus(message);
        }
      }
    } catch (err) {
      const message =
        `Não consegui preparar a Skale: ${String(err?.message || err)}`;

      setStatus(message);

      if (notifyPanel) {
        postPanelStatus(message);
      }
    }
  }

  async function publishQuickSummary() {
    try {
      await refreshValuesForDirectAction();
      postOrderSummary();
    } catch (err) {
      postPanelStatus(`Não consegui validar os dados: ${String(err?.message || err)}`);
    }
  }

  async function copyMissingDirect() {
    try {
      await refreshValuesForDirectAction();
      const summary = orderValidation();
      const text = missingRequestText();

      try {
        await navigator.clipboard.writeText(text);
      } catch {
        const ta = document.createElement("textarea");
        ta.value = text;
        ta.style.position = "fixed";
        ta.style.opacity = "0";
        document.body.appendChild(ta);
        ta.select();
        document.execCommand("copy");
        ta.remove();
      }

      postPanelStatus(
        summary.ready
          ? "Dados completos • mensagem de confirmação copiada"
          : "Mensagem pedindo somente o que falta foi copiada"
      );
      postOrderSummary();
    } catch (err) {
      postPanelStatus(`Erro ao preparar mensagem: ${String(err?.message || err)}`);
    }
  }

  async function sendDirectToSkale() {
    try {
      postPanelStatus(
        "Lendo dados da conversa…"
      );

      await refreshValuesForDirectAction();

      await sendToSkale(true);
    } catch (err) {
      postPanelStatus(
        `Erro ao preparar Skale: ${String(err?.message || err)}`
      );
    }
  }

  async function openCardDirect() {
    try {
      buildPanel();
      state.edited.clear();
      await refreshExtraction(false);
      await openConfirmationCardPreview();
    } catch (err) {
      postPanelStatus(
        `Erro ao gerar card: ${String(err?.message || err)}`
      );
    }
  }

  function focusFirstProblem() {
    if (!state.host?.shadowRoot) return;
    const summary = orderValidation();
    const labelToKey = Object.fromEntries(FIELDS.map(([key, label]) => [label, key]));
    let key = summary.missing.length ? labelToKey[summary.missing[0]] : null;
    if (!key && summary.issues.length) key = labelToKey[summary.issues[0]] || summary.issues[0].toLowerCase();
    if (!key && summary.cepDivergences?.length) key = "zip";
    if (!key) key = summary.optionalIssues?.length ? "email" : null;
    const input = key ? fieldInput(key) : null;
    if (input) {
      input.scrollIntoView({ behavior: "smooth", block: "center" });
      setTimeout(() => input.focus(), 280);
    }
  }

  async function openPanel(focusProblems = false) {
    buildPanel();
    state.edited.clear();
    await refreshExtraction(false);
    if (focusProblems) focusFirstProblem();
  }


  window.addEventListener("message", e => {
    if (e.source !== window) return;

    if (e.data?.source === PAGE_SOURCE) {
      const p = e.data.payload;
      if (!p) return;

      if (p.type === "current-conversation") {
        const id = String(p.conversationId || "");
        if (id && id !== state.currentConversationId) {
          state.currentConversationId = id;
          state.currentConversation = p.conversation || null;
          state.edited.clear();
          state.imageValues = {};
          state.imageAttachmentId = null;
        } else if (p.conversation) {
          state.currentConversation = p.conversation;
        }
      }

      if (p.type === "conversation-messages") {
        mergeMessages(p.conversationId, p.messages);
      }
    }

    if (
      e.data?.source === AUDIO_TRANSCRIPT_BRIDGE &&
      e.data?.payload?.type === "audio-transcript-ready"
    ) {
      mergeAudioTranscript(
        e.data.payload
      );
    }

    if (
      e.data?.source === BRIDGE_SOURCE &&
      e.data?.payload?.type === "open-panel"
    ) {
      openPanel();
    }

    if (
      e.data?.source === BRIDGE_SOURCE &&
      e.data?.payload?.type === "open-review-direct"
    ) {
      openPanel(true);
    }

    if (
      e.data?.source === BRIDGE_SOURCE &&
      e.data?.payload?.type === "send-skale-direct"
    ) {
      sendDirectToSkale();
    }

    if (
      e.data?.source === BRIDGE_SOURCE &&
      e.data?.payload?.type === "open-card-direct"
    ) {
      openCardDirect();
    }

    if (
      e.data?.source === BRIDGE_SOURCE &&
      e.data?.payload?.type === "quick-summary-request"
    ) {
      publishQuickSummary();
    }

    if (
      e.data?.source === BRIDGE_SOURCE &&
      e.data?.payload?.type === "copy-missing-direct"
    ) {
      copyMissingDirect();
    }

    if (
      e.data?.source === BRIDGE_SOURCE &&
      e.data?.payload?.type === "read-image-direct"
    ) {
      readCustomerImage(false);
    }

  });
})();
