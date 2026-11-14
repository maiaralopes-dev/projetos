
(() => {
  if (window.__LNL_V15_34_DIRECT_HOOK__) return;
  window.__LNL_V15_34_DIRECT_HOOK__ = true;

  const SOURCE = "LNL_V15_34_DIRECT";
  const nativeFetch = window.fetch.bind(window);

  const state = {
    template: null,
    currentKey: null,
    fetchingAll: false,
    rawConversations: new Map(),
    normalizedConversations: new Map(),
    pageSize: 20,
    openingId: null,
    openingTimer: null,
    seedUrl: null,
    refreshQueued: false
  };

  function post(payload) {
    try {
      window.postMessage({
        source: SOURCE,
        payload: {
          ...payload,
          ts: new Date().toISOString()
        }
      }, "*");
    } catch {}
  }

  function isConversationListUrl(rawUrl) {
    try {
      const u = new URL(rawUrl, location.href);

      return (
        u.hostname === "messaging.g1.datacrazy.io" &&
        u.pathname === "/api/messaging/conversations"
      );
    } catch {
      return false;
    }
  }

  function conversationIdFromMessagesUrl(rawUrl) {
    try {
      const u = new URL(rawUrl, location.href);

      const m = u.pathname.match(
        /\/api\/messaging\/conversations\/([^/]+)\/messages(?:\/|$)/
      );

      return m
        ? decodeURIComponent(m[1])
        : null;
    } catch {
      return null;
    }
  }

  function messageTextFromObject(obj) {
    if (!obj || typeof obj !== "object") return "";

    const direct = [
      obj.text,
      obj.body,
      obj.message,
      obj.caption,
      obj.interpretedContent,
      obj.transcription,
      typeof obj.content === "string" ? obj.content : null,
      obj.content?.text,
      obj.content?.body,
      obj.content?.message,
      obj.payload?.text,
      obj.data?.text
    ];

    for (const value of direct) {
      if (
        typeof value === "string" &&
        value.trim()
      ) {
        return value.trim();
      }
    }

    return "";
  }

  function normalizeMessageAttachments(value) {
    const raw =
      Array.isArray(value?.attachments)
        ? value.attachments
        : [];

    const out = [];
    const seen = new Set();

    for (const item of raw.slice(0, 20)) {
      if (!item || typeof item !== "object") continue;

      const url = String(
        item.url ||
        item.src ||
        item.downloadUrl ||
        item.mediaUrl ||
        ""
      ).trim();

      if (!url) continue;

      const id = String(
        item.id ||
        item.attachmentId ||
        item.fileId ||
        url
      );

      if (seen.has(id)) continue;
      seen.add(id);

      out.push({
        id,
        url,
        fileName: String(item.fileName || item.filename || item.name || ""),
        mimeType: String(item.mimeType || item.mimetype || item.contentType || ""),
        type: String(item.type || item.mediaType || item.kind || "")
      });
    }

    return out;
  }

  function collectNormalizedMessages(root, conversationId) {
    const found = [];
    const ids = new Set();
    const seen = new WeakSet();

    function walk(value, depth = 0) {
      if (
        value == null ||
        depth > 7
      ) return;

      if (Array.isArray(value)) {
        for (const item of value.slice(0, 500)) {
          walk(item, depth + 1);
        }
        return;
      }

      if (typeof value !== "object") return;

      try {
        if (seen.has(value)) return;
        seen.add(value);
      } catch {}

      const text =
        messageTextFromObject(value);

      const status =
        String(value.status || "")
          .toUpperCase();

      const type =
        String(value.type || value.direction || "")
          .toLowerCase();

      const looksLikeMessage =
        Boolean(value.id || value.messageId) &&
        Boolean(
          text ||
          typeof value.received === "boolean" ||
          status ||
          value.createdAt ||
          value.sentAt
        );

      if (looksLikeMessage) {
        const id = String(
          value.id ||
          value.messageId ||
          `${conversationId}-${found.length}`
        );

        if (!ids.has(id)) {
          ids.add(id);

          let received = null;

          if (typeof value.received === "boolean") {
            received = value.received;
          } else if (status === "RECEIVED") {
            received = true;
          } else if (
            ["received", "incoming", "inbound"].includes(type)
          ) {
            received = true;
          } else if (
            ["sent", "outgoing", "outbound"].includes(type) ||
            ["SENT", "DELIVERED", "READ"].includes(status)
          ) {
            received = false;
          }

          found.push({
            id,
            conversationId:
              String(
                value.conversationId ||
                conversationId ||
                ""
              ),
            received,
            status,
            type,
            text,
            senderName:
              String(value.senderName || value.sender?.name || ""),
            createdAt:
              value.createdAt ||
              value.sentAt ||
              value.timestamp ||
              value.date ||
              null,
            attachments:
              normalizeMessageAttachments(value)
          });
        }
      }

      let entries = [];

      try {
        entries = Object.entries(value)
          .slice(0, 140);
      } catch {
        return;
      }

      for (const [key, child] of entries) {
        if (
          key === "attachments" ||
          key === "quotedMessage" ||
          key === "replyTo"
        ) {
          continue;
        }

        if (
          child &&
          (typeof child === "object")
        ) {
          walk(child, depth + 1);
        }
      }
    }

    walk(root, 0);

    return found.slice(-500);
  }

  function requestKey(rawUrl) {
    try {
      const u = new URL(rawUrl, location.href);

      u.searchParams.delete("skip");
      u.searchParams.delete("take");

      return u.toString();
    } catch {
      return String(rawUrl || "");
    }
  }

  function extractPreview(item) {
    const lm = item?.lastMessage;

    if (!lm || typeof lm !== "object") {
      return "";
    }

    const candidates = [
      lm.content,
      lm.text,
      lm.message,
      lm.caption,
      lm.body,
      lm.interpretedContent,
      lm.transcription
    ];

    for (const v of candidates) {
      if (
        typeof v === "string" &&
        v.trim()
      ) {
        return v.trim().slice(0, 180);
      }
    }

    if (
      lm.mediaType ||
      lm.type
    ) {
      return "Mensagem com anexo";
    }

    return "";
  }

  function normalizeConversation(item) {
    if (
      !item ||
      typeof item !== "object" ||
      typeof item.isPending !== "boolean"
    ) {
      return null;
    }

    const contact =
      item.contact &&
      typeof item.contact === "object"
        ? item.contact
        : null;

    return {
      id: item.id ?? null,
      name: item.name ?? "",
      isPending: item.isPending,

      contactId:
        contact?.contactId ??
        contact?.phone ??
        contact?.id ??
        null,

      phone:
        contact?.phone ??
        item.phone ??
        null,

      lastMessageDate:
        item.lastMessageDate ??
        null,

      lastReceivedMessageDate:
        item.lastReceivedMessageDate ??
        null,

      lastSendedMessageDate:
        item.lastSendedMessageDate ??
        null,

      preview:
        extractPreview(item),

      instanceName:
        item.instance?.name ??
        item.instanceName ??
        ""
    };
  }

  function merge(
    list,
    publishNow = true
  ) {
    let changed = false;

    for (const raw of list || []) {
      const item =
        normalizeConversation(raw);

      if (!item?.id) continue;

      const id =
        String(item.id);

      const prev =
        state.normalizedConversations.get(id);

      state.rawConversations.set(
        id,
        raw
      );

      state.normalizedConversations.set(
        id,
        item
      );

      if (
        !prev ||
        prev.isPending !== item.isPending ||
        prev.lastMessageDate !== item.lastMessageDate ||
        prev.name !== item.name ||
        prev.contactId !== item.contactId ||
        prev.preview !== item.preview
      ) {
        changed = true;
      }
    }

    if (
      changed &&
      publishNow
    ) {
      publish();
    }
  }

  function publish(extra = {}) {
    const conversations = [
      ...state.normalizedConversations.values()
    ];

    const pendingCount =
      conversations.filter(
        x => x.isPending === true
      ).length;

    post({
      type: "all-conversations",
      conversations,
      totalLoaded:
        conversations.length,
      pendingCount,
      ...extra
    });
  }

  async function captureTemplate(
    input,
    init
  ) {
    try {
      const req =
        new Request(
          input,
          init || {}
        );

      const headers = {};

      req.headers.forEach(
        (value, key) => {
          headers[key] = value;
        }
      );

      state.template = {
        headers,
        credentials:
          req.credentials ||
          "include",
        referrer:
          req.referrer || "",
        referrerPolicy:
          req.referrerPolicy || ""
      };
    } catch {
      const headers = {};

      try {
        new Headers(
          init?.headers || {}
        ).forEach(
          (value, key) => {
            headers[key] = value;
          }
        );
      } catch {}

      state.template = {
        headers,
        credentials:
          init?.credentials ||
          "include",
        referrer: "",
        referrerPolicy: ""
      };
    }
  }

  function buildInit() {
    const t =
      state.template || {};

    const init = {
      method: "GET",
      headers: {
        ...(t.headers || {})
      },
      credentials:
        t.credentials ||
        "include",
      cache: "no-store"
    };

    delete init.headers[
      "content-length"
    ];

    delete init.headers[
      "content-type"
    ];

    if (
      t.referrer &&
      t.referrer !==
        "about:client"
    ) {
      init.referrer =
        t.referrer;
    }

    if (t.referrerPolicy) {
      init.referrerPolicy =
        t.referrerPolicy;
    }

    return init;
  }

  async function fetchAllPages(
    seedUrl,
    reason = "background"
  ) {
    if (!state.template) return;

    if (
      !isConversationListUrl(
        seedUrl
      )
    ) return;

    state.seedUrl =
      String(seedUrl || "");

    if (state.fetchingAll) {
      state.refreshQueued = true;
      return;
    }

    const key =
      requestKey(seedUrl);

    state.currentKey = key;
    state.fetchingAll = true;

    const nextRaw =
      new Map();

    const nextNormalized =
      new Map();

    try {
      const seed =
        new URL(
          seedUrl,
          location.href
        );

      const seedTake =
        Number(
          seed.searchParams.get(
            "take"
          )
        );

      if (
        Number.isFinite(
          seedTake
        ) &&
        seedTake > 0 &&
        seedTake <= 100
      ) {
        state.pageSize =
          seedTake;
      }

      const take =
        state.pageSize || 20;

      let skip = 0;
      let pages = 0;
      let totalHint = null;

      post({
        type: "refresh-start",
        reason
      });

      while (pages < 300) {
        const u =
          new URL(
            seed.toString()
          );

        u.searchParams.set(
          "take",
          String(take)
        );

        u.searchParams.set(
          "skip",
          String(skip)
        );

        let resp;

        try {
          resp =
            await nativeFetch(
              u.toString(),
              buildInit()
            );
        } catch (err) {
          post({
            type: "api-error",
            skip,
            message:
              String(
                err?.message ||
                err ||
                "Falha de rede"
              )
          });

          break;
        }

        if (!resp.ok) {
          post({
            type: "api-error",
            skip,
            status:
              resp.status,
            message:
              `HTTP ${resp.status} ${resp.statusText || ""}`.trim()
          });

          break;
        }

        const data =
          await resp.json();

        if (pages === 0) {
          const hints = [
            data?.total,
            data?.totalCount,
            data?.count,
            data?.pagination?.total,
            data?.meta?.total
          ]
            .map(Number)
            .filter(
              Number.isFinite
            );

          if (hints.length) {
            totalHint =
              hints[0];
          }
        }

        const list =
          Array.isArray(
            data?.data
          )
            ? data.data
            : [];

        for (const raw of list) {
          const item =
            normalizeConversation(
              raw
            );

          if (!item?.id) continue;

          const id =
            String(item.id);

          nextRaw.set(
            id,
            raw
          );

          nextNormalized.set(
            id,
            item
          );
        }

        pages += 1;

        post({
          type: "page-progress",
          pages,
          skip,
          pageItems:
            list.length,
          loaded:
            nextNormalized.size,
          totalHint
        });

        if (!list.length) break;
        if (list.length < take) break;

        skip += take;

        if (
          Number.isFinite(
            totalHint
          ) &&
          skip >= totalHint
        ) {
          break;
        }

        await new Promise(
          r =>
            setTimeout(
              r,
              reason === "mark-read"
                ? 35
                : 85
            )
        );
      }

      // Snapshot completo: remove também itens que deixaram de existir
      // no resultado atual ou mudaram de status.
      if (
        nextNormalized.size > 0 ||
        pages > 0
      ) {
        state.rawConversations =
          nextRaw;

        state.normalizedConversations =
          nextNormalized;
      }

      publish({
        completed: true,
        pagesLoaded: pages,
        totalHint,
        reason,
        refreshedAt:
          new Date().toISOString()
      });

    } catch (err) {
      post({
        type: "api-error",
        message:
          String(
            err?.message ||
            err ||
            "Erro desconhecido"
          )
      });

    } finally {
      state.fetchingAll =
        false;

      if (
        state.refreshQueued &&
        state.seedUrl
      ) {
        state.refreshQueued =
          false;

        setTimeout(
          () =>
            fetchAllPages(
              state.seedUrl,
              "queued"
            ),
          180
        );
      }
    }
  }

  function getReactFiber(el) {
    if (
      !(el instanceof Element)
    ) return null;

    try {
      for (
        const key of
        Object.getOwnPropertyNames(
          el
        )
      ) {
        if (
          key.startsWith(
            "__reactFiber$"
          ) ||
          key.startsWith(
            "__reactInternalInstance$"
          )
        ) {
          return el[key];
        }
      }
    } catch {}

    return null;
  }

  function contextCandidate(
    value
  ) {
    return (
      value &&
      typeof value === "object" &&
      (
        typeof value.selectConversation ===
          "function" ||
        typeof value.openByConversation ===
          "function"
      )
    );
  }

  function contextFromFiber(
    fiber
  ) {
    let node = fiber;

    for (
      let i = 0;
      i < 24 && node;
      i++, node = node.return
    ) {
      const candidates = [];

      try {
        candidates.push(
          node.pendingProps?.value
        );
      } catch {}

      try {
        candidates.push(
          node.memoizedProps?.value
        );
      } catch {}

      try {
        candidates.push(
          node.dependencies
            ?.firstContext
            ?.memoizedValue
        );
      } catch {}

      for (
        const candidate of
        candidates
      ) {
        if (
          contextCandidate(
            candidate
          )
        ) {
          return candidate;
        }
      }
    }

    return null;
  }

  function collectContextsFromFiber(
    fiber,
    out,
    seen
  ) {
    let node = fiber;

    for (
      let i = 0;
      i < 36 && node;
      i++, node = node.return
    ) {
      const candidates = [];

      try {
        candidates.push(
          node.pendingProps?.value
        );
      } catch {}

      try {
        candidates.push(
          node.memoizedProps?.value
        );
      } catch {}

      try {
        candidates.push(
          node.dependencies
            ?.firstContext
            ?.memoizedValue
        );
      } catch {}

      for (const candidate of candidates) {
        if (
          contextCandidate(candidate) &&
          !seen.has(candidate)
        ) {
          seen.add(candidate);
          out.push(candidate);
        }
      }
    }
  }

  function getReactContainerFiber(el) {
    if (!(el instanceof Element)) return null;

    try {
      for (
        const key of
        Object.getOwnPropertyNames(el)
      ) {
        if (
          key.startsWith("__reactContainer$")
        ) {
          return el[key];
        }
      }
    } catch {}

    return null;
  }

  function scanReactTreeForContexts(
    rootFiber,
    out,
    seen
  ) {
    if (!rootFiber) return;

    const queue = [rootFiber];
    const visited =
      new Set();

    let count = 0;

    while (
      queue.length &&
      count < 18000
    ) {
      const node =
        queue.shift();

      if (
        !node ||
        visited.has(node)
      ) continue;

      visited.add(node);
      count += 1;

      const candidates = [];

      try {
        candidates.push(
          node.pendingProps?.value
        );
      } catch {}

      try {
        candidates.push(
          node.memoizedProps?.value
        );
      } catch {}

      try {
        candidates.push(
          node.dependencies
            ?.firstContext
            ?.memoizedValue
        );
      } catch {}

      for (const candidate of candidates) {
        if (
          contextCandidate(candidate) &&
          !seen.has(candidate)
        ) {
          seen.add(candidate);
          out.push(candidate);
        }
      }

      if (node.child) {
        queue.push(node.child);
      }

      if (node.sibling) {
        queue.push(node.sibling);
      }
    }
  }

  function contextScore(ctx) {
    let score = 0;

    if (
      typeof ctx?.selectConversation ===
      "function"
    ) {
      score += 5;
    }

    if (
      typeof ctx?.openByConversation ===
      "function"
    ) {
      score += 6;
    }

    try {
      if (ctx?.currentConversation) {
        score += 3;
      }

      if (ctx?.currentConversation?.id) {
        score += 2;
      }
    } catch {}

    return score;
  }

  function findAllConversationContexts() {
    const found = [];
    const seen =
      new Set();

    const exactRows = [
      ...document.querySelectorAll(
        'div.flex.items-center.border-b.cursor-pointer.overflow-hidden.w-full'
      )
    ];

    for (
      const row of
      exactRows.slice(0, 80)
    ) {
      collectContextsFromFiber(
        getReactFiber(row),
        found,
        seen
      );
    }

    const anchors = [
      ...document.querySelectorAll(
        'audio, textarea, input, [contenteditable="true"], button, [role="button"]'
      )
    ];

    for (
      const el of
      anchors.slice(0, 500)
    ) {
      collectContextsFromFiber(
        getReactFiber(el),
        found,
        seen
      );
    }

    // Fallback mais forte para mobile:
    // procura o Provider diretamente a partir da raiz React.
    const rootCandidates = [
      document.getElementById("__next"),
      document.getElementById("root"),
      document.querySelector('[id*="root" i]'),
      document.body?.firstElementChild,
      document.body
    ].filter(Boolean);

    for (const el of rootCandidates) {
      const rootFiber =
        getReactContainerFiber(el);

      if (rootFiber) {
        scanReactTreeForContexts(
          rootFiber,
          found,
          seen
        );
      }
    }

    // Último fallback: alguns builds guardam __reactContainer$
    // em um nó intermediário.
    if (!found.length) {
      const all =
        [...document.querySelectorAll("*")];

      for (
        const el of
        all.slice(0, 2500)
      ) {
        const rootFiber =
          getReactContainerFiber(el);

        if (!rootFiber) continue;

        scanReactTreeForContexts(
          rootFiber,
          found,
          seen
        );

        if (found.length) break;
      }
    }

    found.sort(
      (a, b) =>
        contextScore(b) -
        contextScore(a)
    );

    return found;
  }

  function findConversationContext() {
    return (
      findAllConversationContexts()[0] ||
      null
    );
  }

  function currentConversationId(
    ctx
  ) {
    try {
      return String(
        ctx?.currentConversation?.id ||
        ""
      );
    } catch {
      return "";
    }
  }

  async function waitForCurrent(
    targetId,
    timeoutMs = 2200
  ) {
    const started =
      Date.now();

    while (
      Date.now() - started <
      timeoutMs
    ) {
      const ctx =
        findConversationContext();

      if (
        currentConversationId(
          ctx
        ) === String(targetId)
      ) {
        return true;
      }

      await new Promise(
        r =>
          setTimeout(
            r,
            100
          )
      );
    }

    return false;
  }

  function objectContainsConversationId(
    value,
    targetId,
    depth = 0,
    seen = new WeakSet()
  ) {
    if (
      !value ||
      typeof value !== "object" ||
      depth > 5
    ) {
      return false;
    }

    try {
      if (seen.has(value)) {
        return false;
      }

      seen.add(value);
    } catch {}

    try {
      if (
        String(value.id || "") ===
        String(targetId)
      ) {
        return true;
      }

      if (
        String(
          value.conversationId || ""
        ) ===
        String(targetId)
      ) {
        return true;
      }
    } catch {}

    let entries = [];

    try {
      entries =
        Object.entries(value)
          .slice(0, 80);
    } catch {
      return false;
    }

    for (const [, child] of entries) {
      if (
        child &&
        typeof child === "object" &&
        objectContainsConversationId(
          child,
          targetId,
          depth + 1,
          seen
        )
      ) {
        return true;
      }
    }

    return false;
  }

  function clickNativeConversationRow(
    targetId
  ) {
    const rows = [
      ...document.querySelectorAll(
        'div.flex.items-center.border-b.cursor-pointer.overflow-hidden.w-full'
      )
    ];

    for (const row of rows) {
      const fiber =
        getReactFiber(row);

      if (!fiber) continue;

      const candidates = [];

      try {
        candidates.push(
          fiber.pendingProps
        );
      } catch {}

      try {
        candidates.push(
          fiber.memoizedProps
        );
      } catch {}

      try {
        candidates.push(
          row[
            Object.getOwnPropertyNames(row)
              .find(k =>
                k.startsWith("__reactProps$")
              )
          ]
        );
      } catch {}

      const match =
        candidates.some(value =>
          objectContainsConversationId(
            value,
            targetId
          )
        );

      if (!match) continue;

      try {
        row.click();
        return true;
      } catch {}

      try {
        row.dispatchEvent(
          new MouseEvent(
            "click",
            {
              bubbles: true,
              cancelable: true,
              view: window
            }
          )
        );

        return true;
      } catch {}
    }

    return false;
  }


  function directReactProps(el) {
    if (!(el instanceof Element)) return null;

    try {
      const key =
        Object.getOwnPropertyNames(el)
          .find(k =>
            k.startsWith("__reactProps$")
          );

      return key ? el[key] : null;
    } catch {
      return null;
    }
  }

  function fiberHasKey(
    fiber,
    targetId,
    maxDepth = 8
  ) {
    let node = fiber;

    for (
      let i = 0;
      node && i < maxDepth;
      i++, node = node.return
    ) {
      try {
        if (
          String(node.key || "") ===
          String(targetId)
        ) {
          return true;
        }
      } catch {}
    }

    return false;
  }

  function invokeExactNativeRowClick(
    targetId
  ) {
    const rows = [
      ...document.querySelectorAll(
        'div.flex.items-center.border-b.cursor-pointer.overflow-hidden.w-full'
      )
    ];

    for (const row of rows) {
      const fiber =
        getReactFiber(row);

      const props =
        directReactProps(row);

      const matches =
        fiberHasKey(
          fiber,
          targetId,
          5
        ) ||
        objectContainsConversationId(
          props,
          targetId,
          5
        );

      if (!matches) continue;

      if (
        typeof props?.onClick ===
        "function"
      ) {
        try {
          props.onClick();
          return true;
        } catch {}
      }

      try {
        row.click();
        return true;
      } catch {}
    }

    return false;
  }

  function allContextValuesFromFiber(
    fiber,
    maxDepth = 45
  ) {
    const values = [];
    const seen =
      new Set();

    let node = fiber;

    for (
      let depth = 0;
      node && depth < maxDepth;
      depth++, node = node.return
    ) {
      const candidates = [];

      try {
        candidates.push(
          node.pendingProps?.value
        );
      } catch {}

      try {
        candidates.push(
          node.memoizedProps?.value
        );
      } catch {}

      try {
        let dep =
          node.dependencies?.firstContext;

        let depCount = 0;

        while (
          dep &&
          depCount < 12
        ) {
          candidates.push(
            dep.memoizedValue
          );

          dep =
            dep.next;

          depCount += 1;
        }
      } catch {}

      for (const candidate of candidates) {
        if (
          candidate &&
          typeof candidate ===
            "object" &&
          !seen.has(candidate)
        ) {
          seen.add(candidate);
          values.push({
            value: candidate,
            fiber: node,
            depth
          });
        }
      }
    }

    return values;
  }

  function rootFibers() {
    const roots = [];
    const seen =
      new Set();

    const elements = [
      document.getElementById("__next"),
      document.getElementById("root"),
      document.querySelector(
        '[id*="root" i]'
      ),
      document.body?.firstElementChild,
      document.body
    ].filter(Boolean);

    for (const el of elements) {
      const f =
        getReactContainerFiber(el);

      if (
        f &&
        !seen.has(f)
      ) {
        seen.add(f);
        roots.push(f);
      }
    }

    if (!roots.length) {
      const all =
        [
          ...document.querySelectorAll(
            "*"
          )
        ];

      for (
        const el of
        all.slice(0, 2600)
      ) {
        const f =
          getReactContainerFiber(el);

        if (
          f &&
          !seen.has(f)
        ) {
          seen.add(f);
          roots.push(f);

          if (
            roots.length >= 4
          ) {
            break;
          }
        }
      }
    }

    return roots;
  }

  function scanTreeForValue(
    predicate,
    limit = 22000
  ) {
    for (
      const root of
      rootFibers()
    ) {
      const queue = [root];
      const visited =
        new Set();

      let count = 0;

      while (
        queue.length &&
        count < limit
      ) {
        const node =
          queue.shift();

        if (
          !node ||
          visited.has(node)
        ) continue;

        visited.add(node);
        count += 1;

        const candidates = [];

        try {
          candidates.push(
            node.pendingProps?.value
          );
        } catch {}

        try {
          candidates.push(
            node.memoizedProps?.value
          );
        } catch {}

        try {
          let dep =
            node.dependencies?.firstContext;

          let depCount = 0;

          while (
            dep &&
            depCount < 10
          ) {
            candidates.push(
              dep.memoizedValue
            );

            dep =
              dep.next;

            depCount += 1;
          }
        } catch {}

        for (
          const value of
          candidates
        ) {
          try {
            if (
              value &&
              typeof value ===
                "object" &&
              predicate(
                value,
                node
              )
            ) {
              return value;
            }
          } catch {}
        }

        if (node.child) {
          queue.push(
            node.child
          );
        }

        if (node.sibling) {
          queue.push(
            node.sibling
          );
        }
      }
    }

    return null;
  }

  function findConversationListContext() {
    const rows = [
      ...document.querySelectorAll(
        'div.flex.items-center.border-b.cursor-pointer.overflow-hidden.w-full'
      )
    ];

    for (
      const row of
      rows.slice(0, 20)
    ) {
      const values =
        allContextValuesFromFiber(
          getReactFiber(row)
        );

      for (
        const { value } of
        values
      ) {
        if (
          typeof value
            ?.changeConversation ===
            "function"
        ) {
          return value;
        }
      }
    }

    return scanTreeForValue(
      value =>
        typeof value
          ?.changeConversation ===
          "function"
    );
  }

  function looksLikeNextRouter(value) {
    try {
      return (
        typeof value?.refresh ===
          "function" ||
        typeof value?.prefetch ===
          "function" ||
        typeof value?.back ===
          "function" &&
        typeof value?.forward ===
          "function"
      );
    } catch {
      return false;
    }
  }

  function looksLikeStackNavigator(
    value
  ) {
    if (
      !value ||
      typeof value !==
        "object" ||
      typeof value.push !==
        "function"
    ) {
      return false;
    }

    if (
      looksLikeNextRouter(
        value
      )
    ) {
      return false;
    }

    const keys =
      Object.keys(value);

    const stackHints =
      [
        "pop",
        "goBack",
        "replace",
        "reset",
        "currentRoute",
        "route",
        "routes",
        "canGoBack"
      ]
        .filter(
          k =>
            k in value
        )
        .length;

    return (
      stackHints >= 1 ||
      keys.length <= 18
    );
  }

  function findMobileStackNavigator() {
    const rows = [
      ...document.querySelectorAll(
        'div.flex.items-center.border-b.cursor-pointer.overflow-hidden.w-full'
      )
    ];

    for (
      const row of
      rows.slice(0, 20)
    ) {
      const fiber =
        getReactFiber(row);

      const values =
        allContextValuesFromFiber(
          fiber
        );

      for (
        const { value } of
        values
      ) {
        if (
          looksLikeStackNavigator(
            value
          )
        ) {
          return value;
        }
      }

      // O diagnóstico mostrou um Provider imediatamente abaixo
      // do componente que recebe initialRoute/screens.
      let node = fiber;

      for (
        let depth = 0;
        node && depth < 35;
        depth++, node = node.return
      ) {
        try {
          const parentProps =
            node.return?.pendingProps ||
            node.return?.memoizedProps;

          const isStackProvider =
            node.tag === 10 &&
            parentProps &&
            "initialRoute" in
              parentProps &&
            "screens" in
              parentProps;

          if (isStackProvider) {
            const candidate =
              node.pendingProps
                ?.value ||
              node.memoizedProps
                ?.value;

            if (
              looksLikeStackNavigator(
                candidate
              )
            ) {
              return candidate;
            }
          }
        } catch {}
      }
    }

    return scanTreeForValue(
      value =>
        looksLikeStackNavigator(
          value
        )
    );
  }

  function forceShowMobileChatStack() {
    try {
      const stacks = [
        ...document.querySelectorAll(
          '[id^="stack-"]'
        )
      ];

      if (
        stacks.length < 2
      ) {
        return false;
      }

      let listStack =
        stacks.find(el =>
          el.querySelector(
            'input[placeholder*="Pesquise seus contatos" i], input[placeholder*="Pesquise" i]'
          )
        );

      let chatStack =
        stacks.find(el =>
          el !== listStack &&
          (
            el.querySelector(
              'textarea[placeholder*="Mensagem" i], input[placeholder*="Mensagem" i], [contenteditable="true"]'
            ) ||
            /Finalizar|Marcar como lida/i.test(
              String(
                el.innerText ||
                ""
              )
            )
          )
        );

      if (!listStack) {
        listStack =
          document.getElementById(
            "stack-0"
          );
      }

      if (!chatStack) {
        chatStack =
          stacks.find(
            el =>
              el !== listStack
          );
      }

      if (!chatStack) {
        return false;
      }

      const normalizeClass =
        (el, show) => {
          const tokens =
            String(
              el.className || ""
            )
              .split(/\s+/)
              .filter(Boolean)
              .filter(
                token =>
                  !/^(-?translate-x-|translate-x-\[)/.test(
                    token
                  )
              );

          if (show) {
            tokens.push(
              "translate-x-0"
            );
          } else {
            tokens.push(
              "-translate-x-full"
            );
          }

          el.className =
            [...new Set(tokens)]
              .join(" ");
        };

      if (listStack) {
        normalizeClass(
          listStack,
          false
        );
      }

      normalizeClass(
        chatStack,
        true
      );

      return true;
    } catch {
      return false;
    }
  }

  async function invokeMobileNativeFlow(
    raw,
    id
  ) {
    // 1) Se a conversa está carregada na lista nativa,
    // executa exatamente o onClick original capturado no diagnóstico.
    if (
      invokeExactNativeRowClick(
        id
      )
    ) {
      return {
        method:
          "native-row-onclick"
      };
    }

    // 2) Para conversas que não estão renderizadas,
    // reproduz o mesmo fluxo descoberto:
    // changeConversation(g) -> 200 ms -> stack.push("chat").
    const listCtx =
      findConversationListContext();

    const msgCtx =
      findConversationContext();

    const navigator =
      findMobileStackNavigator();

    let selected = false;

    if (
      typeof listCtx
        ?.changeConversation ===
        "function"
    ) {
      await Promise.resolve(
        listCtx.changeConversation(
          raw
        )
      );

      selected = true;
    } else if (
      typeof msgCtx
        ?.selectConversation ===
        "function"
    ) {
      await Promise.resolve(
        msgCtx.selectConversation(
          raw
        )
      );

      selected = true;
    } else if (
      typeof msgCtx
        ?.openByConversation ===
        "function"
    ) {
      await Promise.resolve(
        msgCtx.openByConversation(
          raw
        )
      );

      selected = true;
    }

    if (!selected) {
      throw new Error(
        "Não encontrei a função interna que seleciona a conversa."
      );
    }

    await new Promise(
      r =>
        setTimeout(
          r,
          220
        )
    );

    let navigated = false;

    if (
      typeof navigator
        ?.push ===
        "function"
    ) {
      try {
        await Promise.resolve(
          navigator.push(
            "chat"
          )
        );

        navigated = true;
      } catch {}
    }

    if (!navigated) {
      navigated =
        forceShowMobileChatStack();
    }

    return {
      method:
        navigated
          ? "changeConversation+stack-chat"
          : "changeConversation"
    };
  }

  async function openConversationDirect(
    conversationId
  ) {
    const id =
      String(
        conversationId || ""
      );

    if (!id) {
      post({
        type: "open-error",
        code: "missing-id",
        message:
          "ID da conversa não informado."
      });
      return;
    }

    const raw =
      state.rawConversations.get(id);

    if (!raw) {
      post({
        type: "open-error",
        conversationId: id,
        code:
          "conversation-not-cached",
        message:
          "A conversa ainda não está no cache da API."
      });
      return;
    }

    const ctx =
      findConversationContext();

    if (!ctx) {
      post({
        type: "open-error",
        conversationId: id,
        code:
          "react-context-not-found",
        message:
          "Não encontrei o contexto interno de conversa do CRM."
      });
      return;
    }

    state.openingId = id;

    post({
      type: "open-start",
      conversationId: id
    });

    try {
      const mobile =
        window.matchMedia?.(
          "(max-width: 768px)"
        )?.matches ||
        window.innerWidth <= 768;

      let mobileMethod = null;

      if (mobile) {
        const result =
          await invokeMobileNativeFlow(
            raw,
            id
          );

        mobileMethod =
          result?.method ||
          "mobile-native-flow";

        // Fecha a lista customizada depois de disparar o MESMO fluxo
        // do clique nativo do CRM.
        post({
          type:
            "mobile-open-invoked",
          conversationId: id,
          method:
            mobileMethod
        });

      } else if (
        typeof ctx.selectConversation ===
          "function"
      ) {
        await Promise.resolve(
          ctx.selectConversation(
            raw
          )
        );
      } else if (
        typeof ctx.openByConversation ===
          "function"
      ) {
        await Promise.resolve(
          ctx.openByConversation(
            raw
          )
        );
      } else {
        throw new Error(
          "Função interna de seleção não disponível."
        );
      }

      let confirmed =
        await waitForCurrent(
          id,
          mobile ? 2400 : 1700
        );

      if (
        !confirmed &&
        mobile
      ) {
        // Se a lista nativa ainda existir no DOM, clicar nela é o fallback
        // mais fiel possível ao clique manual do usuário.
        const nativeClicked =
          clickNativeConversationRow(
            id
          );

        if (nativeClicked) {
          confirmed =
            await waitForCurrent(
              id,
              1800
            );
        }
      }

      // Fallback interno, ainda sem campo de busca.
      if (
        !confirmed &&
        typeof ctx.openByConversation ===
          "function"
      ) {
        await Promise.resolve(
          ctx.openByConversation(
            raw
          )
        );

        confirmed =
          await waitForCurrent(
            id,
            1800
          );
      }

      if (confirmed) {
        state.openingId = null;

        post({
          type: "open-success",
          conversationId: id,
          method:
            mobile
              ? (
                  mobileMethod ||
                  "mobile-native-flow"
                )
              : "react-context"
        });

        return;
      }

      // Mesmo que currentConversation não esteja exposto naquele frame,
      // a chamada pode ter sido aceita. A confirmação pela requisição de
      // mensagens ocorrerá no wrapper de fetch.
      if (state.openingTimer) {
        clearTimeout(
          state.openingTimer
        );
      }

      state.openingTimer =
        setTimeout(() => {
          if (
            state.openingId === id
          ) {
            state.openingId = null;

            post({
              type: "open-error",
              conversationId: id,
              code:
                "not-confirmed",
              message:
                "A função interna foi chamada, mas o CRM não confirmou a seleção."
            });
          }
        }, 1800);

    } catch (err) {
      state.openingId = null;

      post({
        type: "open-error",
        conversationId: id,
        code:
          "react-call-failed",
        message:
          String(
            err?.message ||
            err ||
            "Falha ao abrir conversa."
          )
      });
    }
  }

  window.addEventListener(
    "message",
    e => {
      if (
        e.source !== window ||
        e.data?.source !== SOURCE
      ) return;

      const p =
        e.data.payload;

      if (!p) return;

      if (
        p.type ===
        "open-conversation-direct"
      ) {
        openConversationDirect(
          p.conversationId
        );
      }

      if (
        p.type ===
        "refresh-pending"
      ) {
        if (
          state.seedUrl &&
          state.template
        ) {
          fetchAllPages(
            state.seedUrl,
            p.reason || "manual"
          );
        }
      }
    }
  );

  window.fetch =
    async function(...args) {
      const input = args[0];
      const init =
        args[1] || {};

      const url =
        typeof input === "string"
          ? input
          : (input?.url || "");

      const requestMethod =
        String(
          init?.method ||
          input?.method ||
          "GET"
        ).toUpperCase();

      const response =
        await nativeFetch(
          ...args
        );

      const messageConversationId =
        conversationIdFromMessagesUrl(
          url
        );

      if (
        messageConversationId &&
        ["POST", "PUT", "PATCH"].includes(requestMethod) &&
        response?.ok
      ) {
        post({
          type: "outbound-message-sent",
          conversationId:
            String(
              messageConversationId
            )
        });
      }

      if (
        messageConversationId
      ) {
        const conversationMeta =
          state.normalizedConversations.get(
            String(messageConversationId)
          ) || null;

        post({
          type:
            "current-conversation",
          conversationId:
            String(
              messageConversationId
            ),
          conversation:
            conversationMeta
        });

        try {
          const messagePayload =
            await response
              .clone()
              .json();

          const normalizedMessages =
            collectNormalizedMessages(
              messagePayload,
              messageConversationId
            );

          if (
            normalizedMessages.length
          ) {
            post({
              type:
                "conversation-messages",
              conversationId:
                String(
                  messageConversationId
                ),
              messages:
                normalizedMessages
            });
          }
        } catch {}
      }

      if (
        messageConversationId &&
        state.openingId &&
        String(
          messageConversationId
        ) ===
          String(
            state.openingId
          )
      ) {
        if (state.openingTimer) {
          clearTimeout(
            state.openingTimer
          );
        }

        const confirmedId =
          state.openingId;

        state.openingId = null;

        post({
          type: "open-success",
          conversationId:
            confirmedId,
          method:
            "messages-request"
        });
      }

      if (
        isConversationListUrl(
          url
        )
      ) {
        state.seedUrl =
          String(url || "");

        await captureTemplate(
          input,
          init
        );

        try {
          const data =
            await response
              .clone()
              .json();

          if (
            Array.isArray(
              data?.data
            )
          ) {
            merge(
              data.data,
              false
            );
          }
        } catch {}

        setTimeout(
          () =>
            fetchAllPages(
              url,
              "initial"
            ),
          0
        );
      }

      return response;
    };

  setInterval(() => {
    if (
      state.seedUrl &&
      state.template &&
      !state.fetchingAll
    ) {
      fetchAllPages(
        state.seedUrl,
        "timer"
      );
    }
  }, 60000);

  post({
    type: "hook-ready"
  });
})();
