
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
          font-size: 15px;
          line-height: 1.55;
        }

        .summary-box {
          display: none;
          margin-top: 10px;
          padding: 10px 11px;
          border-radius: 8px;
          background: rgba(17, 24, 30, .42);
          border: 1px solid rgba(142, 193, 229, .25);
        }

        .summary-box.show { display: block; }

        .summary-label {
          color: #8ec1e5;
          font-size: 10px;
          font-weight: 800;
          text-transform: uppercase;
          letter-spacing: .05em;
          margin-bottom: 5px;
        }

        .summary-text {
          white-space: pre-wrap;
          word-break: break-word;
          color: #edf2f5;
          font-size: 13px;
          line-height: 1.5;
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
            font-size: 16px;
            line-height: 1.55;
          }

          .summary-text {
            font-size: 14px;
            line-height: 1.5;
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

          <div class="summary-box">
            <div class="summary-label">Resumo</div>
            <div class="summary-text"></div>
          </div>

          <div class="footer">
            <button class="link copy" type="button">Copiar</button>
            <button class="link summary" type="button">Resumo</button>
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

    const summary =
      shadow.querySelector(
        ".summary"
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

    summary.addEventListener(
      "click",
      async () => {
        const text =
          shadow.querySelector(
            ".text"
          )?.textContent || "";

        if (!text.trim()) {
          setStatus(
            shadow,
            "Transcreva o áudio antes de resumir."
          );
          return;
        }

        summary.disabled = true;
        const original = summary.textContent;
        summary.textContent = "Resumindo…";
        setStatus(
          shadow,
          "Gerando resumo com Groq…"
        );

        try {
          const response =
            await sendRuntime({
              type: "summarize-transcript",
              cacheKey:
                meta.attachmentId ||
                meta.messageId ||
                key,
              text
            });

          if (!response?.ok) {
            throw new Error(
              response?.error ||
              "Falha ao gerar resumo."
            );
          }

          const box =
            shadow.querySelector(
              ".summary-box"
            );
          const summaryText =
            shadow.querySelector(
              ".summary-text"
            );

          if (summaryText) {
            summaryText.textContent =
              escapeText(response.summary || "");
          }
          box?.classList.add("show");
          setStatus(
            shadow,
            response.cached
              ? "Resumo salvo."
              : ""
          );
        } catch (err) {
          setStatus(
            shadow,
            String(
              err?.message ||
              err ||
              "Falha ao gerar resumo."
            )
          );
        } finally {
          summary.disabled = false;
          summary.textContent = original;
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

    shadow.querySelector(
      ".summary-box"
    )?.classList.remove("show");

    const summaryText =
      shadow.querySelector(
        ".summary-text"
      );
    if (summaryText) summaryText.textContent = "";

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
        !settings.hasGroqApiKey
      ) {
        openSettings(
          "Adicione sua chave da Groq para transcrever os áudios."
        );

        return;
      }

      btn.disabled = true;
      btn.textContent =
        "Transcrevendo…";

      setStatus(
        shadow,
        "Transcrevendo com Groq…"
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
          width: min(470px, calc(100vw - 28px));
          max-height: min(90vh, 820px);
          overflow: auto;
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
          line-height: 1.45;
          margin-bottom: 14px;
        }

        .section {
          padding: 12px;
          margin-top: 10px;
          border: 1px solid #e0e5e9;
          border-radius: 10px;
        }

        .section-title {
          font-size: 12px;
          font-weight: 800;
          margin-bottom: 2px;
        }

        .section-note {
          color: #77818a;
          font-size: 10px;
          line-height: 1.35;
          margin-bottom: 8px;
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
        select {
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

        .message {
          min-height: 15px;
          margin-top: 10px;
          color: #66727c;
          font-size: 10px;
          line-height: 1.35;
        }

        .test-row {
          display: flex;
          gap: 8px;
          margin-top: 10px;
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
          flex: 1;
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

          .section {
            border-color: #3b4044;
            background: #222527;
          }

          .sub,
          .message,
          label,
          .section-note {
            color: #aab1b7;
          }

          input,
          select {
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
            Áudio e imagens usam somente a <strong>Groq Cloud</strong>.
            A mesma GROQ_API_KEY é usada para transcrição e leitura visual.
          </div>

          <div class="section">
            <div class="section-title">🎙️ Transcrição • Groq</div>
            <div class="section-note">
              O áudio só é enviado à Groq quando você clicar em “Transcrever”.
            </div>

            <label>GROQ_API_KEY</label>
            <input
              class="groq-api-key"
              type="password"
              autocomplete="off"
              placeholder="gsk_..."
            />

            <label>Modelo de transcrição</label>
            <select class="groq-model">
              <option value="whisper-large-v3-turbo">
                whisper-large-v3-turbo • rápido/econômico
              </option>
              <option value="whisper-large-v3">
                whisper-large-v3 • maior precisão
              </option>
            </select>

            <div class="test-row">
              <button class="test test-groq" type="button">
                Testar Groq
              </button>
            </div>
          </div>


          <div class="section">
            <div class="section-title">📷 Leitura de imagens • Groq</div>
            <div class="section-note">
              As imagens são analisadas somente quando você clicar no botão 📷. Modelo: qwen/qwen3.8-27b.
            </div>
          </div>

          <div class="message"></div>

          <div class="actions">
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

      if (settings?.ok) {
        if (settings.hasGroqApiKey) {
          shadow.querySelector(
            ".groq-api-key"
          ).placeholder =
            "Chave Groq já salva — digite outra apenas para trocar";
        }


        if (settings.groqModel) {
          shadow.querySelector(
            ".groq-model"
          ).value = settings.groqModel;
        }
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
      ".test-groq"
    ).addEventListener(
      "click",
      async () => {
        const btn =
          shadow.querySelector(
            ".test-groq"
          );

        const groqApiKey =
          shadow.querySelector(
            ".groq-api-key"
          ).value.trim();

        const groqModel =
          shadow.querySelector(
            ".groq-model"
          ).value;

        btn.disabled = true;
        message.textContent =
          "Testando conexão com a Groq…";

        try {
          const response =
            await sendRuntime({
              type: "test-groq",
              groqApiKey,
              groqModel
            });

          if (!response?.ok) {
            throw new Error(
              response?.error ||
              "Falha ao testar Groq."
            );
          }

          if (response.modelAvailable === false) {
            message.textContent =
              `Groq conectada, mas o modelo ${response.model} não apareceu na conta.`;
          } else if (response.visionAvailable === false) {
            message.textContent =
              `Groq conectada • áudio disponível. O modelo visual ${response.visionModel} não apareceu na conta.`;
          } else {
            message.textContent =
              `Groq conectada • áudio e leitura de imagens disponíveis.`;
          }
        } catch (err) {
          message.textContent =
            String(
              err?.message ||
              err ||
              "Falha ao testar Groq."
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
        const groqApiKey =
          shadow.querySelector(
            ".groq-api-key"
          ).value.trim();

        const groqModel =
          shadow.querySelector(
            ".groq-model"
          ).value;


        const response =
          await sendRuntime({
            type: "save-settings",
            groqApiKey,
            groqModel
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
