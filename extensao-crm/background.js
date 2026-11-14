
const VPS_BASE_URL =
  "https://trascricao.masterautomacoes.com.br";

const OCR_IMAGE_CACHE_VERSION =
  "vps-ocr-v2";

const LEGACY_OPENAI_TRANSCRIPTION_MODELS =
  [
    "gpt-transcribe",
    "gpt-4o-mini-transcribe"
  ];

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
    "openai_api_key"
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

function vpsCacheStorageKey(
  cacheKey
) {
  return (
    "audio_transcript_vps__" +
    String(cacheKey || "")
      .replace(
        /[^a-zA-Z0-9_-]/g,
        "_"
      )
      .slice(0, 180)
  );
}

function legacyCacheStorageKeys(
  cacheKey
) {
  const safe =
    String(cacheKey || "")
      .replace(
        /[^a-zA-Z0-9_-]/g,
        "_"
      )
      .slice(0, 180);

  return (
    LEGACY_OPENAI_TRANSCRIPTION_MODELS
      .map(
        model =>
          "audio_transcript__" +
          safe +
          "__" +
          model
      )
  );
}

async function getVpsSettings() {
  const stored =
    await storageGet([
      "vps_transcription_api_key",
      "transcricao_api_key"
    ]);

  return {
    apiKey:
      String(
        stored.vps_transcription_api_key ||
        stored.transcricao_api_key ||
        ""
      ).trim(),
    baseUrl:
      VPS_BASE_URL
  };
}

async function cachedTranscript(
  cacheKey
) {
  const primaryKey =
    vpsCacheStorageKey(
      cacheKey
    );

  const legacyKeys =
    legacyCacheStorageKeys(
      cacheKey
    );

  const stored =
    await storageGet([
      primaryKey,
      ...legacyKeys
    ]);

  if (
    typeof stored[primaryKey] ===
      "string" &&
    stored[primaryKey].trim()
  ) {
    return {
      provider: "vps",
      text:
        stored[primaryKey]
    };
  }

  // Reaproveita transcrições já salvas por versões antigas,
  // evitando retranscrever áudios que já foram processados.
  for (const key of legacyKeys) {
    if (
      typeof stored[key] ===
        "string" &&
      stored[key].trim()
    ) {
      await storageSet({
        [primaryKey]:
          stored[key]
      });

      return {
        provider: "cache-legado",
        text:
          stored[key]
      };
    }
  }

  return {
    provider: "vps",
    text: ""
  };
}

async function testVpsConnection(
  providedApiKey = ""
) {
  const current =
    await getVpsSettings();

  const apiKey =
    String(
      providedApiKey ||
      current.apiKey ||
      ""
    ).trim();

  const headers = {};

  if (apiKey) {
    headers.Authorization =
      `Bearer ${apiKey}`;
  }

  let response;

  try {
    response =
      await fetch(
        `${VPS_BASE_URL}/health`,
        {
          method: "GET",
          headers,
          cache: "no-store"
        }
      );
  } catch (err) {
    throw new Error(
      "Não consegui conectar à VPS. Verifique a internet e o domínio."
    );
  }

  let body = null;

  try {
    body =
      await response.json();
  } catch {}

  if (!response.ok) {
    throw new Error(
      body?.detail ||
      body?.error ||
      `VPS respondeu HTTP ${response.status}.`
    );
  }

  return {
    ok: true,
    status:
      String(
        body?.status ||
        "ok"
      ),
    model:
      String(
        body?.model ||
        ""
      ),
    transcribing:
      Boolean(
        body?.transcribing
      ),
    queueWaiting:
      Number(
        body?.queue_waiting ||
        0
      )
  };
}


async function transcribeAudio(
  payload
) {
  const {
    apiKey
  } = await getVpsSettings();

  if (!apiKey) {
    throw new Error(
      "Configure a chave da sua VPS nas configurações."
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
    vpsCacheStorageKey(
      cacheKey
    );

  const existing =
    await cachedTranscript(
      cacheKey
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
        existing.provider
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

  let response;

  try {
    response =
      await fetch(
        `${VPS_BASE_URL}/transcribe`,
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
  } catch (err) {
    throw new Error(
      "Não consegui conectar à API de transcrição da VPS."
    );
  }

  let body = null;

  try {
    body =
      await response.json();
  } catch {}

  if (!response.ok) {
    if (response.status === 401) {
      throw new Error(
        "Chave da VPS inválida ou não autorizada."
      );
    }

    const message =
      body?.detail ||
      body?.error ||
      body?.message ||
      `Erro da VPS (HTTP ${response.status}).`;

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
      "A VPS não retornou texto para esse áudio."
    );
  }

  await storageSet({
    [cacheKeyStorage]:
      text
  });

  return {
    text,
    cached:
      Boolean(
        body?.cached
      ),
    provider: "vps",
    model:
      String(
        body?.model ||
        ""
      ),
    language:
      String(
        body?.language ||
        ""
      ),
    duration:
      Number(
        body?.duration ||
        0
      ),
    processingTime:
      Number(
        body?.processing_time ||
        0
      ),
    queueWaitTime:
      Number(
        body?.queue_wait_time ||
        0
      )
  };
}


function imageCacheStorageKey(cacheKey) {
  return (
    "image_ocr_vps__" +
    String(cacheKey || "")
      .replace(
        /[^a-zA-Z0-9_-]/g,
        "_"
      )
      .slice(0, 180) +
    "__" +
    OCR_IMAGE_CACHE_VERSION
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
  if (/\.bmp$/.test(cleanUrl)) return "image/bmp";

  return "image/jpeg";
}

function safeImageFileName(fileName, mimeType) {
  let name =
    String(fileName || "")
      .trim()
      .replace(/[\\/:*?"<>|]+/g, "_");

  const mime = String(mimeType || "").toLowerCase();
  let ext = ".jpg";
  if (mime.includes("png")) ext = ".png";
  else if (mime.includes("webp")) ext = ".webp";
  else if (mime.includes("bmp")) ext = ".bmp";
  else if (mime.includes("jpeg") || mime.includes("jpg")) ext = ".jpg";

  if (!name) name = "imagem-cliente" + ext;
  if (!/\.(?:jpe?g|png|webp|bmp)$/i.test(name)) name += ext;
  return name;
}


function scoreOcrText(text) {
  const raw = String(text || "").trim();
  if (!raw) return 0;

  let score = Math.min(4, Math.floor(raw.length / 45));
  const normalized = raw
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase();

  const signals = [
    /\b(?:cep|cepo)\b/,
    /\bcpf\b/,
    /\b(?:nome|cliente|titular)\b/,
    /\b(?:rua|r\.?|avenida|av\.?|logradouro)\b/,
    /\b(?:numero|número|nº|n°|s\/?n)\b/,
    /\b(?:bairro|cidade|estado|uf)\b/,
    /@/,
    /\b\d{5}[-.\s]?\d{3}\b/,
    /\b\d{3}[.\s]?\d{3}[.\s]?\d{3}[-.\s]?\d{2}\b/
  ];

  for (const re of signals) {
    if (re.test(normalized)) score += 2;
  }

  return score;
}

async function enhanceImageForOcr(blob) {
  try {
    if (
      typeof createImageBitmap !== "function" ||
      typeof OffscreenCanvas === "undefined"
    ) {
      return null;
    }

    const bitmap = await createImageBitmap(blob);
    try {
      const longest = Math.max(bitmap.width, bitmap.height);
      const targetLongest = Math.min(
        2800,
        Math.max(longest, Math.round(longest * 1.45))
      );
      const scale = targetLongest / longest;
      const width = Math.max(1, Math.round(bitmap.width * scale));
      const height = Math.max(1, Math.round(bitmap.height * scale));

      const canvas = new OffscreenCanvas(width, height);
      const ctx = canvas.getContext("2d", { willReadFrequently: true });
      if (!ctx) return null;

      ctx.imageSmoothingEnabled = true;
      ctx.imageSmoothingQuality = "high";
      ctx.drawImage(bitmap, 0, 0, width, height);

      const img = ctx.getImageData(0, 0, width, height);
      const d = img.data;
      const contrast = 1.85;
      const midpoint = 138;

      for (let i = 0; i < d.length; i += 4) {
        const gray =
          0.299 * d[i] +
          0.587 * d[i + 1] +
          0.114 * d[i + 2];

        let value =
          (gray - midpoint) * contrast + midpoint + 8;

        value = Math.max(0, Math.min(255, value));
        d[i] = value;
        d[i + 1] = value;
        d[i + 2] = value;
      }

      ctx.putImageData(img, 0, 0);
      return await canvas.convertToBlob({
        type: "image/png",
        quality: 1
      });
    } finally {
      bitmap.close?.();
    }
  } catch {
    return null;
  }
}

async function callVpsOcr(blob, fileName, apiKey) {
  const form = new FormData();
  form.append("file", blob, fileName);

  let response;
  try {
    response = await fetch(
      `${VPS_BASE_URL}/ocr`,
      {
        method: "POST",
        headers: {
          "Authorization": `Bearer ${apiKey}`
        },
        body: form,
        cache: "no-store"
      }
    );
  } catch {
    throw new Error(
      "Não consegui conectar ao OCR da sua VPS."
    );
  }

  let body = null;
  try {
    body = await response.json();
  } catch {}

  if (!response.ok) {
    if (response.status === 401) {
      throw new Error(
        "Chave da VPS inválida ou não autorizada para o OCR."
      );
    }

    throw new Error(
      String(
        body?.detail ||
        body?.error ||
        body?.message ||
        `Erro do OCR da VPS (HTTP ${response.status}).`
      )
    );
  }

  return {
    text: String(body?.text || "").trim(),
    items: Array.isArray(body?.items) ? body.items : [],
    lines: Number(body?.lines || 0),
    processingTime: Number(body?.processing_time || 0)
  };
}

async function readOrderDataFromImage(payload) {
  const { apiKey } = await getVpsSettings();

  if (!apiKey) {
    throw new Error(
      "Configure a chave da sua VPS nas configurações ⚙. A mesma chave é usada para transcrição e OCR."
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
    String(existing[storageKey].text || "").trim()
  ) {
    return {
      text: String(existing[storageKey].text).trim(),
      cached: true,
      provider: "vps-ocr",
      items: Array.isArray(existing[storageKey].items)
        ? existing[storageKey].items
        : [],
      lines: Number(existing[storageKey].lines || 0),
      processingTime: Number(existing[storageKey].processingTime || 0),
      enhancedPass: Boolean(existing[storageKey].enhancedPass),
      score: Number(existing[storageKey].score || 0)
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

  if (blob.size > 20 * 1024 * 1024) {
    throw new Error(
      "A imagem é grande demais para o OCR (máximo 20 MB)."
    );
  }

  const mimeType =
    imageMimeType(
      payload.mimeType,
      blob.type,
      imageUrl
    );

  if (!/^image\/(?:jpeg|jpg|png|webp|bmp)$/i.test(mimeType)) {
    throw new Error(
      `Formato de imagem não suportado pelo OCR: ${mimeType || "desconhecido"}.`
    );
  }

  const finalBlob =
    blob.type
      ? blob
      : blob.slice(0, blob.size, mimeType);

  const originalName =
    safeImageFileName(
      payload.fileName,
      mimeType
    );

  let best =
    await callVpsOcr(
      finalBlob,
      originalName,
      apiKey
    );

  let bestScore =
    scoreOcrText(best.text);

  let usedEnhancedPass = false;

  // Fotos manuscritas ou com iluminação ruim: se a primeira leitura
  // vier fraca, tenta novamente com escala, cinza e contraste.
  if (bestScore < 12) {
    const enhancedBlob =
      await enhanceImageForOcr(finalBlob);

    if (enhancedBlob?.size) {
      const enhanced =
        await callVpsOcr(
          enhancedBlob,
          "imagem-cliente-contraste.png",
          apiKey
        );

      const enhancedScore =
        scoreOcrText(enhanced.text);

      if (enhancedScore > bestScore) {
        best = enhanced;
        bestScore = enhancedScore;
        usedEnhancedPass = true;
      }
    }
  }

  const text =
    String(best.text || "").trim();

  if (!text) {
    throw new Error(
      "O OCR terminou, mas não encontrou texto na imagem. Fotos manuscritas podem exigir uma imagem mais próxima e nítida."
    );
  }

  const cachedValue = {
    text,
    items: best.items,
    lines: best.lines,
    processingTime: best.processingTime,
    enhancedPass: usedEnhancedPass,
    score: bestScore
  };

  await storageSet({
    [storageKey]: cachedValue
  });

  return {
    ...cachedValue,
    cached: false,
    provider: "vps-ocr"
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
          const vps =
            await getVpsSettings();

          sendResponse({
            ok: true,
            provider: "vps",
            vpsBaseUrl:
              vps.baseUrl,
            hasVpsApiKey:
              Boolean(
                vps.apiKey
              )
          });

          return;
        }


        if (
          message?.type ===
          "save-settings"
        ) {
          const currentVps =
            await getVpsSettings();

          const vpsApiKey =
            String(
              message.vpsApiKey ||
              ""
            ).trim();

          const values = {};

          // Campo vazio mantém o valor atual.
          if (vpsApiKey) {
            values.vps_transcription_api_key =
              vpsApiKey;
          } else if (
            !currentVps.apiKey
          ) {
            throw new Error(
              "Informe a chave da API da VPS."
            );
          }


          await storageSet(
            values
          );

          sendResponse({
            ok: true
          });

          return;
        }

        if (
          message?.type ===
          "test-vps"
        ) {
          const result =
            await testVpsConnection(
              message.vpsApiKey
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
          const cached =
            await cachedTranscript(
              message.cacheKey
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
          "open-five-delivery"
        ) {
          const url =
            String(
              message.url ||
              "https://app.fivedelivery.com.br/pedido/cadastrar"
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
