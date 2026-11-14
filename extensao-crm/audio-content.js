
(() => {
  if (window.__CRM_AUDIO_TRANSCRIBE_V15_18_CONTENT__) return;
  window.__CRM_AUDIO_TRANSCRIBE_V15_18_CONTENT__ = true;

  const SOURCE = "CRM_AUDIO_RECEIVED_V15";
  const TRANSCRIPT_BRIDGE = "CRM_AUDIO_TRANSCRIPT_BRIDGE_V15_34";
  const items = new Map();

  function publishTranscript(meta, text) {
    const transcript = String(text || "").trim();
    if (!transcript || !meta) return;

    try {
      window.postMessage({
        source: TRANSCRIPT_BRIDGE,
        payload: {
          type: "audio-transcript-ready",
          conversationId: meta.conversationId ?? null,
          messageId: meta.messageId ?? null,
          attachmentId: meta.attachmentId ?? null,
          createdAt: meta.createdAt ?? null,
          text: transcript
        }
      }, "*");
    } catch {}
  }

  function sendRuntime(message) {
    return new Promise((resolve, reject) => {
      chrome.runtime.sendMessage(
        message,
        response => {
          const err =
            chrome.runtime.lastError;

          if (err) {
            reject(
              new Error(err.message)
            );
            return;
          }

          resolve(response);
        }
      );
    });
  }

  function escapeText(s) {
    return String(s ?? "");
  }

  function anchorForKey(key) {
    return document.querySelector(
      `[data-crm-transcribe-anchor="${CSS.escape(key)}"]`
    );
  }

  function createAudioUI(key, meta) {
    const anchor =
      anchorForKey(key);

    if (!anchor) return null;

    if (anchor.shadowRoot) {
      return anchor.shadowRoot;
    }

    const shadow =
      anchor.attachShadow({
        mode: "open"
      });

    shadow.innerHTML = `
      <style>
        :host { all: initial; }

        * { box-sizing: border-box; }

        .box {
          width: 100%;
          font-family: Arial, Helvetica, sans-serif;
          color: #e8edf1;
        }

        .actions {
          display: flex;
          align-items: center;
          gap: 6px;
        }

        .btn {
          appearance: none;
          border: 1px solid #31404c;
          border-radius: 6px;
          padding: 6px 10px;
          background: #202a33;
          color: #71b7f0;
          font: 600 11px/1.2 Arial, Helvetica, sans-serif;
          cursor: pointer;
          transition: background .12s ease, border-color .12s ease;
        }

        .btn:hover {
          background: #243441;
          border-color: #40586a;
        }

        .btn:disabled {
          opacity: .58;
          cursor: wait;
        }

        .status {
          min-height: 0;
          margin-top: 5px;
          color: #8e989f;
          font-size: 10px;
          line-height: 1.3;
        }

        .status:empty { display: none; }

        .result {
          display: none;
          position: relative;
          margin-top: 7px;
          padding: 10px 11px 9px 13px;
          border-radius: 9px;
          background: #2b353d;
          border: 1px solid #3e5260;
          overflow: hidden;
        }

        .result::before {
          content: "";
          position: absolute;
          left: 0;
          top: 0;
          bottom: 0;
          width: 3px;
          background: #55a8e8;
          opacity: .85;
        }

        .result.show { display: block; }

        .head {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 8px;
          margin-bottom: 5px;
        }

        .label {
          color: #8ec1e5;
          font-size: 10px;
          font-weight: 700;
          text-transform: uppercase;
          letter-spacing: .055em;
        }

        .badge {
          color: #75828c;
          font: 600 9px/1 Arial, Helvetica, sans-serif;
          white-space: nowrap;
        }

        .text-wrap {
          padding: 0;
          background: transparent;
          border: 0;
        }

        .text {
          white-space: pre-wrap;
          word-break: break-word;
          color: #e4e8eb;
          font-size: 12px;
          line-height: 1.48;
        }

        .footer {
          display: flex;
          align-items: center;
          gap: 10px;
          margin-top: 7px;
        }

        .link {
          appearance: none;
          border: 0;
          background: transparent;
          padding: 0;
          color: #8eabc1;
          font: 600 10px/1.2 Arial, Helvetica, sans-serif;
          cursor: pointer;
        }

        .link:hover {
          color: #b7d5ea;
          text-decoration: underline;
        }

        @media (max-width: 768px) {
          .btn {
            min-height: 34px;
            padding: 6px 10px;
            border-radius: 7px;
          }

          .status {
            max-width: 210px;
            font-size: 9px;
            line-height: 1.25;
          }

          .result {
            padding: 9px 9px 8px 12px;
            border-radius: 8px;
          }

          .text {
            font-size: 12px;
            line-height: 1.45;
          }

          .footer {
            gap: 12px;
            margin-top: 6px;
          }
        }
      </style>

      <div class="box">
        <div class="actions">
          <button class="btn transcribe" type="button">Transcrever</button>
        </div>

        <div class="status"></div>

        <div class="result">
          <div class="head">
            <div class="label">Transcrição do cliente</div>
            <div class="badge">salva</div>
          </div>

          <div class="text-wrap">
            <div class="text"></div>
          </div>

          <div class="footer">
            <button class="link copy" type="button">Copiar</button>
            <button class="link hide" type="button">Ocultar</button>
          </div>
        </div>
      </div>
    `;

    const btn =
      shadow.querySelector(
        ".transcribe"
      );


    const copy =
      shadow.querySelector(
        ".copy"
      );

    const hide =
      shadow.querySelector(
        ".hide"
      );

    btn.addEventListener(
      "click",
      () =>
        transcribe(
          key,
          meta,
          shadow
        )
    );


    copy.addEventListener(
      "click",
      async () => {
        const text =
          shadow.querySelector(
            ".text"
          )?.textContent || "";

        if (!text) return;

        try {
          await navigator.clipboard.writeText(
            text
          );

          setStatus(
            shadow,
            "Copiado."
          );
        } catch {
          setStatus(
            shadow,
            "Não foi possível copiar."
          );
        }
      }
    );

    hide.addEventListener(
      "click",
      () => {
        shadow.querySelector(
          ".result"
        )?.classList.remove(
          "show"
        );

        btn.style.display = "";
      }
    );

    // Tenta restaurar transcrição em cache sem custo.
    sendRuntime({
      type: "get-cached-transcript",
      cacheKey:
        meta.attachmentId ||
        meta.messageId ||
        key
    })
      .then(response => {
        if (
          response?.ok &&
          response.text
        ) {
          showTranscript(
            shadow,
            response.text,
            true,
            meta
          );
        }
      })
      .catch(() => {});

    return shadow;
  }

  function setStatus(
    shadow,
    text
  ) {
    const el =
      shadow?.querySelector(
        ".status"
      );

    if (el) {
      el.textContent =
        escapeText(text);
    }
  }

  function showTranscript(
    shadow,
    text,
    cached = false,
    meta = null
  ) {
    const result =
      shadow.querySelector(
        ".result"
      );

    const textEl =
      shadow.querySelector(
        ".text"
      );

    const badge =
      shadow.querySelector(
        ".badge"
      );

    const btn =
      shadow.querySelector(
        ".transcribe"
      );

    if (textEl) {
      textEl.textContent =
        escapeText(text);
    }

    if (badge) {
      badge.textContent =
        cached
          ? "salva"
          : "nova";
    }

    result?.classList.add(
      "show"
    );

    if (btn) {
      btn.style.display =
        "none";
    }

    setStatus(
      shadow,
      cached
        ? "Transcrição salva."
        : ""
    );

    publishTranscript(
      meta,
      text
    );
  }

  async function transcribe(
    key,
    meta,
    shadow
  ) {
    const btn =
      shadow.querySelector(
        ".transcribe"
      );

    try {
      const settings =
        await sendRuntime({
          type: "get-settings"
        });

      if (
        !settings?.ok ||
        !settings.hasVpsApiKey
      ) {
        openSettings(
          "Adicione a chave da sua VPS para transcrever os áudios."
        );

        return;
      }

      btn.disabled = true;
      btn.textContent =
        "Transcrevendo…";

      setStatus(
        shadow,
        "Transcrevendo na VPS…"
      );

      const response =
        await sendRuntime({
          type: "transcribe-audio",
          cacheKey:
            meta.attachmentId ||
            meta.messageId ||
            key,
          audioUrl:
            meta.audioUrl,
          fileName:
            meta.fileName,
          mimeType:
            meta.mimeType
        });

      if (!response?.ok) {
        throw new Error(
          response?.error ||
          "Falha na transcrição."
        );
      }

      showTranscript(
        shadow,
        response.text,
        Boolean(
          response.cached
        ),
        meta
      );

    } catch (err) {
      setStatus(
        shadow,
        String(
          err?.message ||
          err ||
          "Falha na transcrição."
        )
      );

    } finally {
      btn.disabled = false;
      btn.textContent =
        "Transcrever";
    }
  }

  // -------------------------------------------------------
  // Configuração de chave/modelo
  // -------------------------------------------------------

  let settingsHost = null;

  async function openSettings(
    initialMessage = ""
  ) {
    if (settingsHost) {
      settingsHost.remove();
      settingsHost = null;
    }

    settingsHost =
      document.createElement(
        "div"
      );

    settingsHost.style.position =
      "fixed";
    settingsHost.style.inset = "0";
    settingsHost.style.zIndex =
      "2147483647";
    settingsHost.style.pointerEvents =
      "none";

    const shadow =
      settingsHost.attachShadow({
        mode: "open"
      });

    shadow.innerHTML = `
      <style>
        * { box-sizing: border-box; }

        .backdrop {
          position: fixed;
          inset: 0;
          display: flex;
          align-items: center;
          justify-content: center;
          background: rgba(0,0,0,.42);
          font-family: Arial, Helvetica, sans-serif;
          pointer-events: auto;
        }

        .card {
          width: min(430px, calc(100vw - 32px));
          padding: 18px;
          border-radius: 14px;
          background: #ffffff;
          color: #17212b;
          box-shadow: 0 20px 55px rgba(0,0,0,.30);
        }

        h3 {
          margin: 0 0 6px;
          font-size: 16px;
        }

        .sub {
          color: #68737d;
          font-size: 11px;
          line-height: 1.4;
          margin-bottom: 14px;
        }

        label {
          display: block;
          margin-top: 10px;
          margin-bottom: 5px;
          color: #4e5963;
          font-size: 10px;
          font-weight: 700;
        }

        input,
        select,
        textarea {
          width: 100%;
          height: 38px;
          border: 1px solid #d4dbe1;
          border-radius: 8px;
          padding: 0 10px;
          background: #fff;
          color: #17212b;
          outline: none;
          font-size: 12px;
        }

        textarea {
          min-height: 58px;
          height: 58px;
          padding: 8px 10px;
          resize: vertical;
          line-height: 1.35;
        }

        .card {
          max-height: min(88vh, 780px);
          overflow: auto;
        }


        .message {
          min-height: 15px;
          margin-top: 8px;
          color: #66727c;
          font-size: 10px;
        }

        .actions {
          display: flex;
          justify-content: flex-end;
          gap: 8px;
          margin-top: 14px;
        }

        button {
          appearance: none;
          border: 0;
          border-radius: 8px;
          padding: 9px 12px;
          cursor: pointer;
          font-weight: 700;
          font-size: 11px;
        }

        .test {
          margin-right: auto;
          background: #e6f2ff;
          color: #2e6fa8;
          border: 1px solid #c8e0f5;
        }

        .cancel {
          background: #edf1f4;
          color: #46515b;
        }

        .save {
          background: #4c9fe8;
          color: #0c1720;
        }

        @media (prefers-color-scheme: dark) {
          .card {
            background: #25282b;
            color: #f2f5f7;
            border: 1px solid #3b4044;
          }

          .sub,
          .message,
          label {
            color: #aab1b7;
          }

          input,
          select,
          textarea {
            background: #1d1f21;
            border-color: #43484d;
            color: #f2f5f7;
          }

          .test {
            background: #243545;
            color: #8fc7f7;
            border-color: #3b5870;
          }

          .cancel {
            background: #363a3e;
            color: #d5dade;
          }
        }
      </style>

      <div class="backdrop">
        <div class="card">
          <h3>Configurações da extensão</h3>

          <div class="sub">
            Áudios e imagens: enviados somente para
            <strong>trascricao.masterautomacoes.com.br</strong>
            quando você clicar em "Transcrever" ou no botão 📷.<br><br>
            A mesma chave da VPS é usada para transcrição e OCR.
          </div>

          <label>Chave da API da VPS</label>
          <input
            class="vps-api-key"
            type="password"
            autocomplete="off"
            placeholder="Bearer token da sua VPS"
          />

          <label>Servidor de transcrição e OCR</label>
          <input
            class="vps-url"
            type="text"
            value="https://trascricao.masterautomacoes.com.br"
            readonly
          />


          <div class="message"></div>

          <div class="actions">
            <button class="test" type="button">
              Testar VPS
            </button>


            <button class="cancel" type="button">
              Cancelar
            </button>

            <button class="save" type="button">
              Salvar
            </button>
          </div>
        </div>
      </div>
    `;

    document.documentElement.appendChild(
      settingsHost
    );

    const message =
      shadow.querySelector(
        ".message"
      );

    if (initialMessage) {
      message.textContent =
        initialMessage;
    }

    try {
      const settings =
        await sendRuntime({
          type: "get-settings"
        });

      if (
        settings?.ok &&
        settings.hasVpsApiKey
      ) {
        shadow.querySelector(
          ".vps-api-key"
        ).placeholder =
          "Chave da VPS já salva — digite outra apenas para trocar";
      }



    } catch {}

    const close = () => {
      settingsHost?.remove();
      settingsHost = null;
    };

    shadow.querySelector(
      ".cancel"
    ).addEventListener(
      "click",
      close
    );

    shadow.querySelector(
      ".backdrop"
    ).addEventListener(
      "click",
      e => {
        if (
          e.target.classList.contains(
            "backdrop"
          )
        ) {
          close();
        }
      }
    );

    shadow.querySelector(
      ".test"
    ).addEventListener(
      "click",
      async () => {
        const btn =
          shadow.querySelector(
            ".test"
          );

        const vpsApiKey =
          shadow.querySelector(
            ".vps-api-key"
          ).value.trim();

        btn.disabled = true;
        message.textContent =
          "Testando conexão com a VPS…";

        try {
          const response =
            await sendRuntime({
              type: "test-vps",
              vpsApiKey
            });

          if (!response?.ok) {
            throw new Error(
              response?.error ||
              "Falha ao testar VPS."
            );
          }

          const modelText =
            response.model
              ? ` • modelo ${response.model}`
              : "";

          const queueText =
            Number.isFinite(
              response.queueWaiting
            )
              ? ` • fila ${response.queueWaiting}`
              : "";

          message.textContent =
            `VPS online${modelText}${queueText}.`;
        } catch (err) {
          message.textContent =
            String(
              err?.message ||
              err ||
              "Falha ao testar VPS."
            );
        } finally {
          btn.disabled = false;
        }
      }
    );


    shadow.querySelector(
      ".save"
    ).addEventListener(
      "click",
      async () => {
        const vpsApiKey =
          shadow.querySelector(
            ".vps-api-key"
          ).value.trim();


        const response =
          await sendRuntime({
            type: "save-settings",
            vpsApiKey
          });

        if (!response?.ok) {
          message.textContent =
            response?.error ||
            "Não foi possível salvar.";

          return;
        }

        message.textContent =
          "Configuração salva.";

        setTimeout(
          close,
          450
        );
      }
    );
  }

  // -------------------------------------------------------
  // Eventos vindos do MAIN world
  // -------------------------------------------------------

  window.addEventListener(
    "message",
    e => {
      if (
        e.source !== window ||
        e.data?.source !==
          "CRM_AUDIO_SETTINGS_BRIDGE_V15_4"
      ) {
        return;
      }

      if (
        e.data?.payload?.type ===
        "open-settings"
      ) {
        openSettings();
      }
    }
  );

  window.addEventListener(
    "message",
    e => {
      if (
        e.source !== window ||
        e.data?.source !== SOURCE
      ) {
        return;
      }

      const p =
        e.data.payload;

      if (
        !p ||
        p.type !==
          "received-audio-found"
      ) {
        return;
      }

      items.set(
        p.key,
        p.meta
      );

      createAudioUI(
        p.key,
        p.meta
      );
    }
  );

  // Se o hook marcou antes do listener inicializar, pede um rescan
  // indiretamente ao tocar o DOM.
  setTimeout(() => {
    document.body?.setAttribute(
      "data-crm-audio-ui-ready",
      String(Date.now())
    );
  }, 250);
})();
