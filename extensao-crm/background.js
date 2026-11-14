
const GROQ_BASE_URL =
  "https://api.groq.com/openai/v1";

const DEFAULT_GROQ_MODEL =
  "whisper-large-v3-turbo";

const GROQ_VISION_MODEL =
  "qwen/qwen3.8-27b";

const GROQ_SUMMARY_MODEL =
  "openai/gpt-oss-20b";

const GROQ_TRANSCRIPTION_MODELS =
  new Set([
    "whisper-large-v3-turbo",
    "whisper-large-v3"
  ]);

const GROQ_IMAGE_CACHE_VERSION =
  "groq-vision-v1";


function storageGet(keys) {
  return new Promise(resolve => {
    chrome.storage.local.get(
      keys,
      result => resolve(result || {})
    );
  });
}

function storageSet(values) {
  return new Promise((resolve, reject) => {
    chrome.storage.local.set(
      values,
      () => {
        const err =
          chrome.runtime.lastError;

        if (err) {
          reject(
            new Error(err.message)
          );
          return;
        }

        resolve();
      }
    );
  });
}

// V16.7: remove resíduos do Agent Core e a antiga chave OpenAI do navegador.
chrome.storage.local.remove(
  [
    "agent_core_url",
    "agent_core_token",
    "assistant_commercial_responses_v15_51",
    "openai_api_key",
    "vps_transcription_api_key",
    "transcricao_api_key"
  ],
  () => { void chrome.runtime.lastError; }
);

function extensionFromMime(
  mimeType
) {
  const mime =
    String(mimeType || "")
      .toLowerCase();

  if (
    mime.includes("mpeg") ||
    mime.includes("mp3")
  ) {
    return ".mp3";
  }

  if (
    mime.includes("ogg") ||
    mime.includes("opus")
  ) {
    return ".ogg";
  }

  if (mime.includes("wav")) {
    return ".wav";
  }

  if (mime.includes("webm")) {
    return ".webm";
  }

  if (
    mime.includes("mp4") ||
    mime.includes("m4a")
  ) {
    return ".m4a";
  }

  return ".mp3";
}

function safeFileName(
  fileName,
  mimeType
) {
  let name =
    String(fileName || "")
      .trim()
      .replace(
        /[\\/:*?"<>|]+/g,
        "_"
      );

  const ext =
    extensionFromMime(
      mimeType
    );

  if (!name) {
    name =
      "audio-cliente" +
      ext;
  }

  if (
    !/\.[a-z0-9]{2,5}$/i.test(
      name
    )
  ) {
    name += ext;
  }

  return name;
}

function groqCacheStorageKey(
  cacheKey,
  model = DEFAULT_GROQ_MODEL
) {
  const safeKey =
    String(cacheKey || "")
      .replace(
        /[^a-zA-Z0-9_-]/g,
        "_"
      )
      .slice(0, 180);

  const safeModel =
    String(model || DEFAULT_GROQ_MODEL)
      .replace(
        /[^a-zA-Z0-9_-]/g,
        "_"
      );

  return (
    "audio_transcript_groq__" +
    safeKey +
    "__" +
    safeModel
  );
}

async function getGroqSettings() {
  const stored =
    await storageGet([
      "groq_api_key",
      "groq_transcription_model"
    ]);

  const requestedModel =
    String(
      stored.groq_transcription_model ||
      DEFAULT_GROQ_MODEL
    ).trim();

  const model =
    GROQ_TRANSCRIPTION_MODELS.has(
      requestedModel
    )
      ? requestedModel
      : DEFAULT_GROQ_MODEL;

  return {
    apiKey:
      String(
        stored.groq_api_key ||
        ""
      ).trim(),
    model,
    baseUrl:
      GROQ_BASE_URL
  };
}

function transcriptSummaryStorageKey(cacheKey) {
  const safeKey =
    String(cacheKey || "")
      .replace(/[^a-zA-Z0-9_-]/g, "_")
      .slice(0, 180);

  return (
    "audio_summary_groq__" +
    safeKey +
    "__v1"
  );
}

async function summarizeTranscript(payload) {
  const { apiKey } = await getGroqSettings();

  if (!apiKey) {
    throw new Error(
      "Configure sua chave da Groq nas configurações ⚙."
    );
  }

  const text =
    String(payload?.text || "").trim();

  if (!text) {
    throw new Error(
      "Não há transcrição para resumir."
    );
  }

  const cacheKey =
    String(payload?.cacheKey || "").trim();

  const storageKey =
    cacheKey
      ? transcriptSummaryStorageKey(cacheKey)
      : "";

  if (storageKey) {
    const existing =
      await storageGet([storageKey]);

    if (
      typeof existing[storageKey] === "string" &&
      existing[storageKey].trim()
    ) {
      return {
        summary: existing[storageKey],
        cached: true,
        model: GROQ_SUMMARY_MODEL
      };
    }
  }

  let response;
  try {
    response = await fetch(
      `${GROQ_BASE_URL}/chat/completions`,
      {
        method: "POST",
        headers: {
          "Authorization": `Bearer ${apiKey}`,
          "Content-Type": "application/json"
        },
        body: JSON.stringify({
          model: GROQ_SUMMARY_MODEL,
          temperature: 0.2,
          max_completion_tokens: 260,
          reasoning_effort: "low",
          messages: [
            {
              role: "system",
              content:
                "Você resume áudios de clientes em português do Brasil para atendimento comercial. Seja curto e fiel. Não invente informações. Preserve valores, datas, quantidades, condições de pagamento, objeções, dúvidas e pedidos do cliente. Se houver mais de um ponto importante, use bullets curtos."
            },
            {
              role: "user",
              content:
                `Resuma esta transcrição de cliente de forma prática para o atendente:

${text}`
            }
          ]
        }),
        cache: "no-store"
      }
    );
  } catch {
    throw new Error(
      "Não consegui conectar à Groq para gerar o resumo."
    );
  }

  let body = null;
  try { body = await response.json(); } catch {}

  if (!response.ok) {
    if (response.status === 401) {
      throw new Error(
        "Chave da Groq inválida ou não autorizada."
      );
    }
    if (response.status === 429) {
      throw new Error(
        "Limite de uso da Groq atingido. Tente novamente em instantes."
      );
    }
    throw new Error(
      String(
        body?.error?.message ||
        body?.error ||
        body?.message ||
        `Erro da Groq ao resumir (HTTP ${response.status}).`
      )
    );
  }

  const summary =
    String(
      body?.choices?.[0]?.message?.content || ""
    ).trim();

  if (!summary) {
    throw new Error(
      "A Groq não retornou um resumo."
    );
  }

  if (storageKey) {
    await storageSet({
      [storageKey]: summary
    });
  }

  return {
    summary,
    cached: false,
    model: GROQ_SUMMARY_MODEL
  };
}

async function cachedTranscript(
  cacheKey,
  model = DEFAULT_GROQ_MODEL
) {
  const primaryKey =
    groqCacheStorageKey(
      cacheKey,
      model
    );

  const stored =
    await storageGet([primaryKey]);

  if (
    typeof stored[primaryKey] ===
      "string" &&
    stored[primaryKey].trim()
  ) {
    return {
      provider: "groq",
      text: stored[primaryKey]
    };
  }

  return {
    provider: "groq",
    text: ""
  };
}

async function testGroqConnection(
  providedApiKey = "",
  providedModel = ""
) {
  const current =
    await getGroqSettings();

  const apiKey =
    String(
      providedApiKey ||
      current.apiKey ||
      ""
    ).trim();

  if (!apiKey) {
    throw new Error(
      "Informe sua GROQ_API_KEY."
    );
  }

  const requestedModel =
    String(
      providedModel ||
      current.model ||
      DEFAULT_GROQ_MODEL
    ).trim();

  const model =
    GROQ_TRANSCRIPTION_MODELS.has(
      requestedModel
    )
      ? requestedModel
      : DEFAULT_GROQ_MODEL;

  let response;

  try {
    response = await fetch(
      `${GROQ_BASE_URL}/models`,
      {
        method: "GET",
        headers: {
          "Authorization":
            `Bearer ${apiKey}`,
          "Content-Type":
            "application/json"
        },
        cache: "no-store"
      }
    );
  } catch {
    throw new Error(
      "Não consegui conectar à Groq. Verifique sua internet."
    );
  }

  let body = null;
  try {
    body = await response.json();
  } catch {}

  if (!response.ok) {
    if (response.status === 401) {
      throw new Error(
        "Chave da Groq inválida ou não autorizada."
      );
    }

    throw new Error(
      String(
        body?.error?.message ||
        body?.error ||
        body?.message ||
        `Groq respondeu HTTP ${response.status}.`
      )
    );
  }

  const models =
    Array.isArray(body?.data)
      ? body.data.map(
          item =>
            String(item?.id || "")
        )
      : [];

  return {
    ok: true,
    model,
    modelAvailable:
      models.includes(model),
    visionModel:
      GROQ_VISION_MODEL,
    visionAvailable:
      models.includes(GROQ_VISION_MODEL),
    availableModels:
      models.filter(
        id =>
          GROQ_TRANSCRIPTION_MODELS.has(id) ||
          id === GROQ_VISION_MODEL
      )
  };
}



async function transcribeAudio(
  payload
) {
  const {
    apiKey,
    model
  } = await getGroqSettings();

  if (!apiKey) {
    throw new Error(
      "Configure sua chave da Groq nas configurações ⚙."
    );
  }

  const cacheKey =
    String(
      payload.cacheKey || ""
    );

  if (!cacheKey) {
    throw new Error(
      "Não encontrei o identificador do áudio."
    );
  }

  const cacheKeyStorage =
    groqCacheStorageKey(
      cacheKey,
      model
    );

  const existing =
    await cachedTranscript(
      cacheKey,
      model
    );

  if (
    existing.text &&
    existing.text.trim()
  ) {
    return {
      text:
        existing.text,
      cached: true,
      provider:
        existing.provider,
      model
    };
  }

  const audioUrl =
    String(
      payload.audioUrl ||
      ""
    ).trim();

  if (!/^https:\/\//i.test(audioUrl)) {
    throw new Error(
      "URL do áudio inválida."
    );
  }

  const audioResponse =
    await fetch(
      audioUrl,
      {
        method: "GET",
        credentials: "include",
        cache: "no-store"
      }
    );

  if (!audioResponse.ok) {
    throw new Error(
      `Não consegui baixar o áudio (HTTP ${audioResponse.status}).`
    );
  }

  const blob =
    await audioResponse.blob();

  if (!blob.size) {
    throw new Error(
      "O arquivo de áudio veio vazio."
    );
  }

  // O limite mais conservador atende inclusive o free tier da Groq.
  if (blob.size > 25 * 1024 * 1024) {
    throw new Error(
      "O áudio excede 25 MB, limite usado nesta integração com a Groq."
    );
  }

  const mimeType =
    String(
      payload.mimeType ||
      blob.type ||
      "audio/mpeg"
    );

  const finalBlob =
    blob.type
      ? blob
      : blob.slice(
          0,
          blob.size,
          mimeType
        );

  const form =
    new FormData();

  form.append(
    "file",
    finalBlob,
    safeFileName(
      payload.fileName,
      mimeType
    )
  );
  form.append("model", model);
  form.append("language", "pt");
  form.append("response_format", "json");
  form.append("temperature", "0");

  let response;

  try {
    response = await fetch(
      `${GROQ_BASE_URL}/audio/transcriptions`,
      {
        method: "POST",
        headers: {
          "Authorization":
            `Bearer ${apiKey}`
        },
        body: form,
        cache: "no-store"
      }
    );
  } catch {
    throw new Error(
      "Não consegui conectar à API de transcrição da Groq."
    );
  }

  let body = null;
  try {
    body = await response.json();
  } catch {}

  if (!response.ok) {
    if (response.status === 401) {
      throw new Error(
        "Chave da Groq inválida ou não autorizada."
      );
    }

    if (response.status === 413) {
      throw new Error(
        "O áudio é grande demais para a Groq."
      );
    }

    if (response.status === 429) {
      throw new Error(
        "Limite de uso da Groq atingido. Tente novamente em instantes."
      );
    }

    const message =
      body?.error?.message ||
      body?.error ||
      body?.message ||
      `Erro da Groq (HTTP ${response.status}).`;

    throw new Error(
      String(message)
    );
  }

  const text =
    String(
      body?.text ||
      ""
    ).trim();

  if (!text) {
    throw new Error(
      "A Groq não retornou texto para esse áudio."
    );
  }

  await storageSet({
    [cacheKeyStorage]:
      text
  });

  return {
    text,
    cached: false,
    provider: "groq",
    model,
    language: "pt"
  };
}


function imageCacheStorageKey(cacheKey) {
  return (
    "image_groq_vision__" +
    String(cacheKey || "")
      .replace(
        /[^a-zA-Z0-9_-]/g,
        "_"
      )
      .slice(0, 180) +
    "__" +
    GROQ_IMAGE_CACHE_VERSION
  );
}

function imageMimeType(payloadMime, blobType, url) {
  const candidate =
    String(
      payloadMime ||
      blobType ||
      ""
    ).toLowerCase();

  if (candidate.startsWith("image/")) {
    return candidate.split(";")[0];
  }

  const cleanUrl =
    String(url || "")
      .split("?")[0]
      .toLowerCase();

  if (/\.png$/.test(cleanUrl)) return "image/png";
  if (/\.webp$/.test(cleanUrl)) return "image/webp";
  return "image/jpeg";
}

function bytesToBase64(bytes) {
  let binary = "";
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunk));
  }
  return btoa(binary);
}

async function blobToDataUrl(blob, mimeType) {
  const buffer = await blob.arrayBuffer();
  const base64 = bytesToBase64(new Uint8Array(buffer));
  return `data:${mimeType};base64,${base64}`;
}

function normalizeGroqVisionJson(value) {
  const src = value && typeof value === "object" ? value : {};
  const out = {};
  const fields = [
    "name", "street", "number", "complement", "neighborhood",
    "city", "state", "zip", "cpf", "email"
  ];

  for (const key of fields) {
    const v = String(src[key] ?? "").trim();
    if (v && !/^(null|undefined|nao informado|não informado|n\/a)$/i.test(v)) {
      out[key] = v;
    }
  }
  return out;
}

function groqVisionTextFromFields(fields) {
  const f = normalizeGroqVisionJson(fields);
  return [
    ["NOME", f.name],
    ["RUA", f.street],
    ["NUMERO", f.number],
    ["COMPLEMENTO", f.complement],
    ["BAIRRO", f.neighborhood],
    ["CIDADE", f.city],
    ["ESTADO", f.state],
    ["CEP", f.zip],
    ["CPF", f.cpf],
    ["EMAIL", f.email]
  ]
    .filter(([, v]) => v)
    .map(([k, v]) => `${k}: ${v}`)
    .join("\n");
}

async function callGroqVision(dataUrl, apiKey) {
  let response;
  try {
    response = await fetch(
      `${GROQ_BASE_URL}/chat/completions`,
      {
        method: "POST",
        headers: {
          "Authorization": `Bearer ${apiKey}`,
          "Content-Type": "application/json"
        },
        body: JSON.stringify({
          model: GROQ_VISION_MODEL,
          temperature: 0,
          max_completion_tokens: 900,
          response_format: { type: "json_object" },
          messages: [
            {
              role: "system",
              content:
                "Você extrai dados cadastrais brasileiros de imagens. Nunca invente dados. Use apenas o que estiver claramente visível. Se um campo não estiver visível, retorne string vazia. Não confunda nome de empresa, concessionária, cabeçalho, rótulo de tabela ou título de documento com nome do cliente. Para contas/faturas, priorize Usuário, Cliente ou Titular. Para endereço, extraia rua, número, complemento, bairro, cidade, estado e CEP quando visíveis. CPF só deve ser retornado se os 11 dígitos estiverem visíveis; CPF mascarado deve ficar vazio. Retorne somente JSON válido com as chaves: name, street, number, complement, neighborhood, city, state, zip, cpf, email."
            },
            {
              role: "user",
              content: [
                {
                  type: "text",
                  text:
                    "Leia esta imagem enviada pelo cliente e extraia somente os dados cadastrais explicitamente visíveis. Preserve grafia útil, mas normalize UF para 2 letras quando clara. Não deduza CEP, CPF, nome ou endereço que não estejam visíveis."
                },
                {
                  type: "image_url",
                  image_url: { url: dataUrl }
                }
              ]
            }
          ]
        }),
        cache: "no-store"
      }
    );
  } catch {
    throw new Error(
      "Não consegui conectar à Groq para ler a imagem."
    );
  }

  let body = null;
  try {
    body = await response.json();
  } catch {}

  if (!response.ok) {
    if (response.status === 401) {
      throw new Error(
        "Chave da Groq inválida ou não autorizada."
      );
    }
    if (response.status === 429) {
      throw new Error(
        "Limite de uso da Groq atingido. Tente novamente em instantes."
      );
    }
    throw new Error(
      String(
        body?.error?.message ||
        body?.detail ||
        `Erro da Groq ao ler imagem (HTTP ${response.status}).`
      )
    );
  }

  const content =
    String(
      body?.choices?.[0]?.message?.content ||
      ""
    ).trim();

  if (!content) {
    throw new Error(
      "A Groq não retornou dados para essa imagem."
    );
  }

  let parsed = null;
  try {
    parsed = JSON.parse(content);
  } catch {
    const match = content.match(/\{[\s\S]*\}/);
    if (match) {
      try { parsed = JSON.parse(match[0]); } catch {}
    }
  }

  if (!parsed || typeof parsed !== "object") {
    throw new Error(
      "A Groq respondeu a imagem em um formato inesperado."
    );
  }

  return normalizeGroqVisionJson(parsed);
}

async function readOrderDataFromImage(payload) {
  const { apiKey } = await getGroqSettings();

  if (!apiKey) {
    throw new Error(
      "Configure sua chave da Groq nas configurações ⚙."
    );
  }

  const imageUrl =
    String(payload.imageUrl || "").trim();

  if (!/^https:\/\//i.test(imageUrl)) {
    throw new Error("URL da imagem inválida.");
  }

  const cacheKey =
    String(payload.cacheKey || imageUrl);

  const storageKey =
    imageCacheStorageKey(cacheKey);

  const existing =
    await storageGet([storageKey]);

  if (
    existing[storageKey] &&
    typeof existing[storageKey] === "object" &&
    existing[storageKey].fields
  ) {
    const fields = normalizeGroqVisionJson(existing[storageKey].fields);
    return {
      text: groqVisionTextFromFields(fields),
      fields,
      cached: true,
      provider: "groq-vision",
      model: GROQ_VISION_MODEL
    };
  }

  const imageResponse =
    await fetch(
      imageUrl,
      {
        method: "GET",
        credentials: "include",
        cache: "no-store"
      }
    );

  if (!imageResponse.ok) {
    throw new Error(
      `Não consegui baixar a imagem (HTTP ${imageResponse.status}).`
    );
  }

  const blob = await imageResponse.blob();

  if (!blob.size) {
    throw new Error("A imagem veio vazia.");
  }

  // Groq Vision aceita imagens de até 20 MB. Mantemos uma margem para base64/JSON.
  if (blob.size > 14 * 1024 * 1024) {
    throw new Error(
      "A imagem é grande demais para leitura direta pela Groq (limite seguro desta integração: 14 MB)."
    );
  }

  const mimeType =
    imageMimeType(
      payload.mimeType,
      blob.type,
      imageUrl
    );

  if (!/^image\/(?:jpeg|jpg|png|webp)$/i.test(mimeType)) {
    throw new Error(
      `Formato de imagem não suportado pela Groq: ${mimeType || "desconhecido"}.`
    );
  }

  const dataUrl =
    await blobToDataUrl(blob, mimeType);

  const fields =
    await callGroqVision(
      dataUrl,
      apiKey
    );

  const text =
    groqVisionTextFromFields(fields);

  if (!text) {
    throw new Error(
      "A imagem foi analisada pela Groq, mas nenhum dado cadastral confiável foi encontrado."
    );
  }

  await storageSet({
    [storageKey]: {
      fields,
      model: GROQ_VISION_MODEL,
      savedAt: Date.now()
    }
  });

  return {
    text,
    fields,
    cached: false,
    provider: "groq-vision",
    model: GROQ_VISION_MODEL
  };
}


async function lookupCep(zip) {
  const d =
    String(zip || "")
      .replace(/\D+/g, "");

  if (d.length !== 8) {
    throw new Error(
      "CEP inválido."
    );
  }

  const response =
    await fetch(
      `https://viacep.com.br/ws/${d}/json/`,
      {
        method: "GET",
        cache: "no-store"
      }
    );

  if (!response.ok) {
    throw new Error(
      `Não consegui consultar o CEP (HTTP ${response.status}).`
    );
  }

  const body =
    await response.json();

  if (
    !body ||
    body.erro
  ) {
    throw new Error(
      "CEP não encontrado."
    );
  }

  return {
    zip:
      String(body.cep || ""),
    street:
      String(body.logradouro || ""),
    neighborhood:
      String(body.bairro || ""),
    city:
      String(body.localidade || ""),
    state:
      String(body.uf || "")
  };
}

chrome.runtime.onMessage.addListener(
  (message, sender, sendResponse) => {
    (async () => {
      try {
        if (
          message?.type ===
          "lookup-cep"
        ) {
          const data =
            await lookupCep(
              message.zip
            );

          sendResponse({
            ok: true,
            data
          });

          return;
        }

        if (
          message?.type ===
          "get-settings"
        ) {
          const groq =
            await getGroqSettings();

          sendResponse({
            ok: true,
            provider: "groq",
            groqModel: groq.model,
            groqVisionModel: GROQ_VISION_MODEL,
            groqSummaryModel: GROQ_SUMMARY_MODEL,
            hasGroqApiKey:
              Boolean(groq.apiKey)
          });

          return;
        }


        if (
          message?.type ===
          "save-settings"
        ) {
          const currentGroq =
            await getGroqSettings();

          const groqApiKey =
            String(
              message.groqApiKey ||
              ""
            ).trim();

          const requestedModel =
            String(
              message.groqModel ||
              currentGroq.model ||
              DEFAULT_GROQ_MODEL
            ).trim();

          const groqModel =
            GROQ_TRANSCRIPTION_MODELS.has(
              requestedModel
            )
              ? requestedModel
              : DEFAULT_GROQ_MODEL;


          const values = {
            groq_transcription_model:
              groqModel
          };

          // Campos de chave vazios mantêm o valor já salvo.
          if (groqApiKey) {
            values.groq_api_key =
              groqApiKey;
          }


          await storageSet(values);

          sendResponse({
            ok: true
          });

          return;
        }

        if (
          message?.type ===
          "test-groq"
        ) {
          const result =
            await testGroqConnection(
              message.groqApiKey,
              message.groqModel
            );

          sendResponse({
            ok: true,
            ...result
          });

          return;
        }




        if (
          message?.type ===
          "get-cached-transcript"
        ) {
          const groq =
            await getGroqSettings();

          const cached =
            await cachedTranscript(
              message.cacheKey,
              groq.model
            );

          sendResponse({
            ok: true,
            text:
              cached.text,
            provider:
              cached.provider
          });

          return;
        }

        if (
          message?.type ===
          "transcribe-audio"
        ) {
          const result =
            await transcribeAudio(
              message
            );

          sendResponse({
            ok: true,
            ...result
          });

          return;
        }


        if (
          message?.type ===
          "summarize-transcript"
        ) {
          const result =
            await summarizeTranscript(
              message
            );

          sendResponse({
            ok: true,
            ...result
          });

          return;
        }


        if (
          message?.type ===
          "read-order-image"
        ) {
          const result =
            await readOrderDataFromImage(
              message
            );

          sendResponse({
            ok: true,
            ...result
          });

          return;
        }

        if (
          message?.type ===
          "open-skale"
        ) {
          const url =
            String(
              message.url ||
              "https://skaletracking.com.br/pedidos"
            );

          await chrome.tabs.create({
            url,
            active: true
          });

          sendResponse({
            ok: true
          });

          return;
        }

        sendResponse({
          ok: false,
          error:
            "Ação desconhecida."
        });

      } catch (err) {
        sendResponse({
          ok: false,
          error:
            String(
              err?.message ||
              err ||
              "Erro desconhecido."
            )
        });
      }
    })();

    return true;
  }
);
