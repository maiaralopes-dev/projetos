
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

    return type === "AUDIO" || mime.startsWith("audio/");
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

    return exact || null;
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

    const received =
      message.received === true &&
      status === "RECEIVED" &&
      directType === "received";

    const sent =
      message.received === false ||
      ["SENT", "DELIVERED", "READ", "PLAYED"].includes(status) ||
      directType === "sent";

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

  function inspectNearestReactChain(audio, audioUrl) {
    let node = audio;

    // O React key pode estar no próprio <audio> ou em um ancestral DOM próximo.
    // Em cada DOM node, porém, só subimos pela cadeia "return" do fiber.
    // Nunca varremos children/sibling, que foi a causa dos falsos positivos.
    for (
      let level = 0;
      node &&
      node !== document.body &&
      level < 8;
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

  function ensureAnchor(audio, meta) {
    const audioUrl =
      normalizeUrl(
        audio.currentSrc ||
        audio.getAttribute("src") ||
        meta.audioUrl ||
        ""
      );

    const key =
      makeKey(
        meta,
        audioUrl
      );

    audio.dataset.crmReceivedAudioKey =
      key;

    let player =
      audio.parentElement;

    if (
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
          Number.isFinite(audio.duration)
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
        ""
      );

    if (!audioUrl) return;

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
        continue;
      }

      inspectAudio(audio);
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
