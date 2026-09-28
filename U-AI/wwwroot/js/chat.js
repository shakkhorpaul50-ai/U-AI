(function () {
  "use strict";

  var shell = document.getElementById("chatShell");
  if (!shell) return;

  var thread = document.getElementById("thread");
  var form = document.getElementById("composer");
  var input = document.getElementById("input");
  var sendBtn = document.getElementById("sendBtn");
  var stopBtn = document.getElementById("stopBtn");
  var strip = document.getElementById("statusStrip");
  var sidebar = document.getElementById("sidebar");
  var modeHidden = document.getElementById("newSessionMode");

  var sessionId = parseInt(shell.dataset.session || "0", 10);
  var busy = false;
  var controller = null;
  var liveRow = null, liveBubble = null, liveFull = "";

  // ---- mobile sidebar -------------------------------------------------
  function scrim() {
    var s = document.getElementById("scrim");
    if (!s) { s = document.createElement("div"); s.id = "scrim"; document.body.appendChild(s); }
    return s;
  }
  function setMenu(open) {
    if (!sidebar) return;
    sidebar.classList.toggle("open", open);
    document.body.classList.toggle("menu-open", open);
    scrim().style.display = open && window.innerWidth <= 860 ? "block" : "none";
  }
  var openBtn = document.getElementById("openSidebar");
  var closeBtn = document.getElementById("closeSidebar");
  if (openBtn) openBtn.addEventListener("click", function () { setMenu(true); });
  if (closeBtn) closeBtn.addEventListener("click", function () { setMenu(false); });
  scrim().addEventListener("click", function () { setMenu(false); });

  // ---- mode switching -------------------------------------------------
  function setMode(mode) {
    var btns = document.querySelectorAll(".mode-btn");
    for (var i = 0; i < btns.length; i++) {
      btns[i].classList.toggle("active", btns[i].dataset.mode === mode);
    }
    if (modeHidden) modeHidden.value = mode;
    input.placeholder = "Message in " + (btns.length ? (document.querySelector('.mode-btn.active') || {}).textContent : mode) + " mode…";
  }
  document.addEventListener("click", function (e) {
    var b = e.target.closest ? e.target.closest(".mode-btn") : null;
    if (b) setMode(b.dataset.mode);
  });

  // ---- composer -------------------------------------------------------
  if (input) {
    input.addEventListener("input", function () {
      input.style.height = "auto";
      input.style.height = Math.min(input.scrollHeight, 160) + "px";
    });
    input.addEventListener("keydown", function (e) {
      if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); form.requestSubmit(); }
    });
  }

  // ---- rendering ------------------------------------------------------
  function scrollDown() { thread.scrollTop = thread.scrollHeight; }

  function addRow(role, text) {
    var wrap = document.createElement("div");
    wrap.className = "row-" + role;
    var b = document.createElement("div");
    b.className = "bubble";
    b.textContent = text;
    wrap.appendChild(b);
    thread.appendChild(wrap);
    scrollDown();
    return b;
  }

  function renderMarkdown(el, text) {
    // Minimal, escape-first markdown: code fences, inline code, paragraphs.
    var esc = text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    var blocks = esc.split(/```/);
    var html = "";
    for (var i = 0; i < blocks.length; i++) {
      if (i % 2 === 1) {
        var code = blocks[i].replace(/^[a-zA-Z0-9+#-]*\n/, "");
        html += "<pre><code>" + code + "</code></pre>";
      } else {
        html += blocks[i]
          .split(/\n{2,}/)
          .filter(function (p) { return p.trim().length; })
          .map(function (p) {
            // Split on inline code first so URLs/bold inside `...` stay literal.
            var parts = p.split(/(`[^`]+`)/g).map(function (seg, idx) {
              if (idx % 2 === 1) return "<code>" + seg.slice(1, -1) + "</code>";
              return seg
                .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
                .replace(/(https?:\/\/[^\s<]+)/g, '<a href="$1" target="_blank" rel="noopener">$1</a>');
            }).join("");
            return "<p>" + parts.replace(/\n/g, "<br/>") + "</p>";
          })
          .join("");
      }
    }
    el.innerHTML = html;
  }

  function status(msg) {
    if (!msg) { strip.hidden = true; return; }
    strip.hidden = false;
    strip.textContent = msg;
  }

  // ---- send -----------------------------------------------------------
  function setBusy(on) {
    busy = on;
    sendBtn.disabled = on;
    stopBtn.hidden = !on;
    input.disabled = on;
  }

  form.addEventListener("submit", function (e) {
    e.preventDefault();
    if (busy) return;

    var text = (input.value || "").trim();
    if (!text) return;
    if (sessionId === 0) {
      status("Start a new chat first — pick a mode on the left.");
      setMenu(true);
      return;
    }

    addRow("user", text);
    input.value = "";
    input.style.height = "auto";

    // The assistant row is created now and STAYS. Streaming appends into it;
    // completion only removes the cursor. Nothing is ever hidden, so the
    // reply is visible without a refresh.
    liveFull = "";
    liveRow = document.createElement("div");
    liveRow.className = "row-assistant";
    liveBubble = document.createElement("div");
    liveBubble.className = "bubble cursor";
    liveRow.appendChild(liveBubble);
    thread.appendChild(liveRow);
    scrollDown();
    setBusy(true);
    status("Waiting for the model…");

    controller = new AbortController();

    fetch("/chat/send", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "RequestVerificationToken": shell.dataset.antiforgery
      },
      body: JSON.stringify({ sessionId: sessionId, message: text }),
      signal: controller.signal
    }).then(function (res) {
      if (!res.ok || !res.body) throw new Error("HTTP " + res.status);
      return readSse(res.body);
    }).then(function () {
      status("");
    }).catch(function (err) {
      if (err.name === "AbortError") {
        status("Stopped.");
      } else {
        status("Failed: " + err.message);
      }
    }).finally(function () {
      // If the stream produced nothing (error/abort before first token),
      // remove the empty row so it doesn't linger.
      if (liveRow && !liveFull) liveRow.remove();
      if (liveBubble) liveBubble.classList.remove("cursor");
      liveRow = null; liveBubble = null;
      setBusy(false);
      controller = null;
    });
  });

  stopBtn.addEventListener("click", function () {
    if (controller) controller.abort();
  });

  // Read an SSE stream: frames are "data: {...}\n\n".
  function readSse(body) {
    var reader = body.getReader();
    var decoder = new TextDecoder();
    var buf = "";

    return new Promise(function (resolve, reject) {
      function pump() {
        reader.read().then(function (r) {
          if (r.done) { flush(); return resolve(); }
          buf += decoder.decode(r.value, { stream: true });

          var idx;
          while ((idx = buf.indexOf("\n\n")) >= 0) {
            var frame = buf.slice(0, idx);
            buf = buf.slice(idx + 2);
            handle(frame);
          }
          pump();
        }).catch(reject);
      }

      function flush() { if (buf.trim()) { handle(buf); buf = ""; } }

      function handle(frame) {
        var line = frame.split("\n").filter(function (l) { return l.indexOf("data:") === 0; })[0];
        if (!line) return;
        var payload = line.slice(5).trim();
        if (!payload) return;

        var ev;
        try { ev = JSON.parse(payload); } catch (err) { return; }

        if (ev.t === "token" && ev.v) {
          liveFull += ev.v;
          if (liveBubble) renderMarkdown(liveBubble, liveFull);
          scrollDown();
          status("Generating…");
        } else if (ev.t === "queued") {
          status(ev.waiting > 1 ? "Queued — one generation at a time on this host." : "Waiting for the model…");
        } else if (ev.t === "error") {
          status("Error: " + (ev.v || "unknown"));
        } else if (ev.t === "limited") {
          status(ev.v || "Limit reached. Try again later.");
          if (liveRow && !liveFull) liveRow.remove();
          if (liveBubble) liveBubble.classList.remove("cursor");
          liveRow = null; liveBubble = null;
        } else if (ev.t === "done") {
          if (liveBubble && liveFull) {
            liveBubble.classList.remove("cursor");
            renderMarkdown(liveBubble, liveFull);
          }
          scrollDown();
        }
      }

      pump();
    });
  }

  // Re-render the server-side turns as markdown on load.
  function renderExisting() {
    var rows = thread.querySelectorAll(".row-assistant .bubble");
    for (var i = 0; i < rows.length; i++) {
      var t = rows[i].textContent;
      if (t.indexOf("```") >= 0 || t.indexOf("`") >= 0) renderMarkdown(rows[i], t);
    }
  }

  renderExisting();
  if (parseInt(shell.dataset.waiting || "0", 10) > 0) {
    status("Another request is generating. Yours will start when it finishes.");
  }
})();
