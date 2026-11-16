
(() => {
  if (window.__CRM_AUDIO_RECEIVED_V15_1__) return;
  window.__CRM_AUDIO_RECEIVED_V15_1__ = true;

  const SOURCE = "CRM_AUDIO_RECEIVED_V15";
  const marked = new WeakMap();

  function post(payload) {
    try {
      window.postMessage({
        source: SOURCE,
        payload
      }, "*");
    } catch {}
  }

  function getReactFiber(el) {
    if (!(el instanceof Element)) return null;

    try {
      const key = Object.getOwnPropertyNames(el).find(k =>
        k.startsWith("__reactFiber$") ||
        k.startsWith("__reactInternalInstance$")
      );

      return key ? el[key] : null;
    } catch {
      return null;
    }
  }

  function getReactProps(el) {
    if (!(el instanceof Element)) return null;

    try {
      const key = Object.getOwnPropertyNames(el).find(k =>
        k.startsWith("__reactProps$")
      );

      return key ? el[key] : null;
    } catch {
      return null;
    }
  }

  function normalizeUrl(value) {
    return String(value || "").trim();
  }

  function isAudioAttachment(a) {
    if (!a || typeof a !== "object") return false;

    const type = String(a.type || "").toUpperCase();
    const mime = String(a.mimeType || a.mimetype || "").toLowerCase();
    const file = String(a.fileName || a.filename || a.url || "")
      .split("?")[0]
      .toLowerCase();

    return (
      ["AUDIO", "PTT", "VOICE"].includes(type) ||
      mime.startsWith("audio/") ||
      /\.(?:ogg|oga|opus|mp3|m4a|wav)$/.test(file)
    );
  }

  function urlPath(value) {
    try {
      const u = new URL(String(value || ""), location.href);
      return u.origin + u.pathname;
    } catch {
      return "";
    }
  }

  function attachmentForExactAudio(message, audioUrl) {
    if (!message || typeof message !== "object") return null;

    const attachments = Array.isArray(message.attachments)
      ? message.attachments.filter(isAudioAttachment)
      : [];

    if (!attachments.length) return null;

    const exact = attachments.find(a =>
      normalizeUrl(a.url) === audioUrl
    );

    if (exact) return exact;

    // V16.27: no celular o player pode usar outro endereço para o mesmo
    // áudio (blob:, link assinado) ou nem ter <audio> com src. Aceita o
    // mesmo caminho sem os parâmetros e, por fim, o único áudio da mensagem.
    const wantedPath = audioUrl ? urlPath(audioUrl) : "";

    if (wantedPath && !/^blob:/i.test(audioUrl)) {
      const samePath = attachments.find(a =>
        urlPath(a.url) === wantedPath
      );

      if (samePath) return samePath;
    }

    return attachments.length === 1
      ? attachments[0]
      : null;
  }

  function classifyMessageProps(props, audioUrl) {
    if (!props || typeof props !== "object") return null;

    const message =
      props.message && typeof props.message === "object"
        ? props.message
        : null;

    if (!message) return null;

    // Vincula o <audio> à mensagem EXATA.
    // Não olha children/sibling/repliedMessage nem percorre objetos vizinhos.
    const attachment =
      attachmentForExactAudio(
        message,
        audioUrl
      );

    if (!attachment) return null;

    const directType =
      String(
        props.type ||
        ""
      ).toLowerCase();

    const status =
      String(
        message.status ||
        ""
      ).toUpperCase();

    // V16.27: o campo "received" da própria mensagem decide. Antes eram
    // exigidos também status RECEIVED e type "received", que nem sempre
    // vêm no layout do celular — e o botão Transcrever não aparecia.
    let received;

    if (typeof message.received === "boolean") {
      received = message.received;
    } else {
      received =
        status === "RECEIVED" ||
        directType === "received";
    }

    const sent = !received;

    return {
      received,
      sent,
      message,
      attachment,
      directType,
      status
    };
  }

  function inspectFiberChainFromElement(el, audioUrl) {
    let fiber =
      getReactFiber(el);

    if (!fiber) return null;

    for (
      let depth = 0;
      fiber && depth < 26;
      depth++, fiber = fiber.return
    ) {
      const candidates = [
        fiber.pendingProps,
        fiber.memoizedProps
      ];

      for (const props of candidates) {
        const classified =
          classifyMessageProps(
            props,
            audioUrl
          );

        if (classified) {
          return classified;
        }
      }
    }

    return null;
  }

  function inspectNearestReactChain(audio, audioUrl, maxLevels = 8) {
    let node = audio;

    // O React key pode estar no próprio <audio> ou em um ancestral DOM próximo.
    // Em cada DOM node, porém, só subimos pela cadeia "return" do fiber.
    // Nunca varremos children/sibling, que foi a causa dos falsos positivos.
    for (
      let level = 0;
      node &&
      node !== document.body &&
      level < maxLevels;
      level++, node = node.parentElement
    ) {
      const result =
        inspectFiberChainFromElement(
          node,
          audioUrl
        );

      if (result) return result;

      // Alguns nós expõem props diretamente, sem fiber útil.
      const props =
        getReactProps(node);

      const direct =
        classifyMessageProps(
          props,
          audioUrl
        );

      if (direct) return direct;
    }

    return null;
  }

  function makeKey(meta, audioUrl) {
    return String(
      meta.attachmentId ||
      meta.messageId ||
      audioUrl
    )
      .replace(/[^a-zA-Z0-9_-]/g, "_")
      .slice(0, 180);
  }

  function removeOldAnchorForAudio(audio) {
    const oldKey =
      audio.dataset.crmReceivedAudioKey;

    if (!oldKey) return;

    const old =
      document.querySelector(
        `[data-crm-transcribe-anchor="${CSS.escape(oldKey)}"]`
      );

    old?.remove();

    delete audio.dataset.crmReceivedAudioKey;
  }

  function ensureAnchor(audio, meta, playerOverride = null) {
    const audioUrl =
      normalizeUrl(
        meta.audioUrl ||
        audio?.currentSrc ||
        audio?.getAttribute?.("src") ||
        ""
      );

    const key =
      makeKey(
        meta,
        audioUrl
      );

    if (audio) {
      audio.dataset.crmReceivedAudioKey =
        key;
    }

    // V16.27: se o botão deste áudio já existe na tela, só reavisa.
    const existing =
      document.querySelector(
        `[data-crm-transcribe-anchor="${CSS.escape(key)}"]`
      );

    if (existing?.isConnected) {
      post({
        type: "received-audio-found",
        key,
        meta: { ...meta, audioUrl, duration: null }
      });
      return;
    }

    let player =
      playerOverride ||
      audio.parentElement;

    if (
      !playerOverride &&
      player?.parentElement &&
      player.parentElement.getBoundingClientRect().height < 130
    ) {
      const parentText =
        String(
          player.parentElement.innerText ||
          ""
        );

      if (
        /\d{1,2}:\d{2}/.test(parentText) ||
        /1x|1\.5x|2x/i.test(parentText)
      ) {
        player =
          player.parentElement;
      }
    }

    if (!player?.parentElement) return;

    let anchor =
      player.parentElement.querySelector(
        `:scope > [data-crm-transcribe-anchor="${CSS.escape(key)}"]`
      );

    if (!anchor) {
      anchor =
        document.createElement(
          "div"
        );

      anchor.setAttribute(
        "data-crm-transcribe-anchor",
        key
      );

      anchor.style.width =
        "100%";
      anchor.style.display =
        "block";
      anchor.style.marginTop =
        "4px";

      player.insertAdjacentElement(
        "afterend",
        anchor
      );
    }

    post({
      type: "received-audio-found",
      key,
      meta: {
        ...meta,
        audioUrl,
        duration:
          Number.isFinite(audio?.duration)
            ? audio.duration
            : null
      }
    });
  }

  function inspectAudio(audio) {
    if (!(audio instanceof HTMLAudioElement)) return;

    const audioUrl =
      normalizeUrl(
        audio.currentSrc ||
        audio.getAttribute("src") ||
        audio.querySelector("source")?.getAttribute("src") ||
        ""
      );

    const classified =
      inspectNearestReactChain(
        audio,
        audioUrl
      );

    // Se encontramos a mensagem exata e ela é enviada, garantimos que
    // qualquer botão antigo desse player seja removido.
    if (
      classified &&
      !classified.received
    ) {
      removeOldAnchorForAudio(
        audio
      );

      marked.set(
        audio,
        {
          url: audioUrl,
          mode: "sent"
        }
      );

      return;
    }

    if (
      !classified ||
      !classified.received
    ) {
      marked.set(
        audio,
        {
          url: audioUrl,
          mode: "unknown"
        }
      );

      return;
    }

    const {
      message,
      attachment
    } = classified;

    const meta = {
      messageId:
        message.id ?? null,
      conversationId:
        message.conversationId ?? null,
      createdAt:
        message.createdAt ??
        message.created_at ??
        message.timestamp ??
        null,
      received: true,
      status:
        message.status ?? null,
      audioUrl:
        attachment.url || audioUrl,
      attachmentId:
        attachment.id ?? null,
      fileName:
        attachment.fileName ||
        attachment.filename ||
        "audio-cliente",
      mimeType:
        attachment.mimeType ||
        attachment.mimetype ||
        "audio/mpeg"
    };

    marked.set(
      audio,
      {
        url: audioUrl,
        mode: "received",
        messageId: meta.messageId,
        attachmentId: meta.attachmentId
      }
    );

    ensureAnchor(
      audio,
      meta
    );
  }

  function scan() {
    const audios =
      [...document.querySelectorAll("audio")];

    for (const audio of audios) {
      const currentUrl =
        normalizeUrl(
          audio.currentSrc ||
          audio.getAttribute("src") ||
          ""
        );

      const previous =
        marked.get(audio);

      // Revalida quando o React reutiliza o mesmo <audio> para outra mensagem.
      if (
        previous &&
        previous.url === currentUrl &&
        previous.mode !== "unknown"
      ) {
        // V16.27: áudio recebido já marcado, mas cujo botão não foi
        // montado (aviso enviado antes de a interface estar pronta, ou
        // o React apagou o botão): avisa de novo.
        if (previous.mode === "received") {
          const key = audio.dataset.crmReceivedAudioKey;
          const anchor = key
            ? document.querySelector(
                `[data-crm-transcribe-anchor="${CSS.escape(key)}"]`
              )
            : null;

          if (!anchor || !anchor.shadowRoot) {
            inspectAudio(audio);
          }
        }

        continue;
      }

      inspectAudio(audio);
    }

    scanPlayersWithoutAudioTag();
  }

  // V16.27: players sem <audio> no DOM (comum no celular). Parte do
  // botão de velocidade ("1x") e sobe até a mensagem dona do player.
  const markedPlayers = new WeakMap();

  function playerContainerFor(speedEl) {
    let node = speedEl.parentElement;

    for (let level = 0; node && node !== document.body && level < 6; level++, node = node.parentElement) {
      const rect = node.getBoundingClientRect();
      if (rect.height > 150) return null;

      if (
        rect.width > 140 &&
        /\d{1,2}:\d{2}/.test(String(node.innerText || ""))
      ) {
        return node;
      }
    }

    return null;
  }

  function scanPlayersWithoutAudioTag() {
    const speedEls =
      [...document.querySelectorAll("button, [role='button'], span, div")]
        .filter(el =>
          el.childElementCount === 0 &&
          /^(?:1|1[.,]5|2)x$/i.test(String(el.textContent || "").trim())
        );

    for (const speedEl of speedEls) {
      const previous = markedPlayers.get(speedEl);

      if (previous === "sent") continue;

      if (previous && previous !== "unknown") {
        const anchor =
          document.querySelector(
            `[data-crm-transcribe-anchor="${CSS.escape(previous)}"]`
          );

        if (anchor?.shadowRoot) continue;
      }

      const player = playerContainerFor(speedEl);
      if (!player) continue;

      // O caminho normal (<audio> no DOM) já cuida deste player.
      if (
        player.querySelector("audio") ||
        player.parentElement?.querySelector(":scope > audio")
      ) {
        continue;
      }

      const classified =
        inspectNearestReactChain(speedEl, "", 14);

      if (!classified) {
        markedPlayers.set(speedEl, "unknown");
        continue;
      }

      if (!classified.received) {
        markedPlayers.set(speedEl, "sent");
        continue;
      }

      const { message, attachment } = classified;

      const meta = {
        messageId: message.id ?? null,
        conversationId: message.conversationId ?? null,
        createdAt:
          message.createdAt ??
          message.created_at ??
          message.timestamp ??
          null,
        received: true,
        status: message.status ?? null,
        audioUrl: normalizeUrl(attachment.url),
        attachmentId: attachment.id ?? null,
        fileName:
          attachment.fileName ||
          attachment.filename ||
          "audio-cliente",
        mimeType:
          attachment.mimeType ||
          attachment.mimetype ||
          "audio/mpeg"
      };

      if (!meta.audioUrl) continue;

      markedPlayers.set(
        speedEl,
        makeKey(meta, meta.audioUrl)
      );

      ensureAnchor(null, meta, player);
    }
  }

  const observer =
    new MutationObserver(() => {
      queueMicrotask(scan);
    });

  function start() {
    if (!document.body) {
      setTimeout(start, 250);
      return;
    }

    observer.observe(
      document.body,
      {
        childList: true,
        subtree: true,
        attributes: true,
        attributeFilter: ["src"]
      }
    );

    scan();

    // Revalidação periódica leve para componentes React reciclados.
    setInterval(
      scan,
      1500
    );
  }

  start();

  post({
    type: "audio-hook-ready"
  });
})();
