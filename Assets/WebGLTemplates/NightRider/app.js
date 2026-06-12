/* ============================================================
   Retro Unity Game Host — control logic + wiring diagram
   ============================================================ */

(function () {
  "use strict";

  /* ---------- persisted state ---------- */
  var DEFAULTS = { scan: null, hsize: false, power: true };
  var state = load();

  function load() {
    try {
      var s = JSON.parse(localStorage.getItem("rcrt-state"));
      if (s && typeof s === "object") {
        return {
          scan: s.scan === "under" || s.scan === "over" ? s.scan : null,
          hsize: !!s.hsize,
          power: s.power !== false
        };
      }
    } catch (e) {}
    return Object.assign({}, DEFAULTS);
  }
  function save() {
    try { localStorage.setItem("rcrt-state", JSON.stringify(state)); } catch (e) {}
  }

  /* ---------- geometry constants ---------- */
  var UNDER = 0.90;     // underscan: shrink, reveals border (9/10)
  var OVER = 1.10;      // overscan: enlarge, edges trimmed by tube (11/10)
  var HSIZE = 1.25;     // 5:4 — NES 320/256 stretch

  var root = document.documentElement;
  var game = document.getElementById("game");

  /* transition presets (transform is shared between resize + power) */
  var TWEEN_OFF = "transform 440ms cubic-bezier(0.62,0.05,0.7,0.16), opacity 280ms ease-in 380ms, filter 220ms ease-out";
  var TWEEN_ON  = "transform 470ms cubic-bezier(0.16,0.9,0.3,1.2), opacity 170ms ease-out, filter 380ms ease-out";
  function resizeTween() { game.style.transition = ""; }   /* fall back to CSS bounce */

  function apply() {
    /* scan + h-size scale */
    var s = state.scan === "under" ? UNDER : state.scan === "over" ? OVER : 1;
    var sx = s * (state.hsize ? HSIZE : 1);
    game.style.setProperty("--sx", sx.toFixed(4));
    game.style.setProperty("--sy", s.toFixed(4));

    /* power: CRT shut-off — collapse height to a line + fade brightness/alpha */
    game.style.setProperty("--py", state.power ? "1" : "0.006");
    game.style.opacity = state.power ? "1" : "0";
    game.style.filter = state.power ? "brightness(1)" : "brightness(1.9)";
    document.getElementById("power-btn").classList.toggle("on", state.power);

    /* scan LEDs (3-state: neither lit = default) */
    document.querySelectorAll("#scan-group .pvm-btn").forEach(function (b) {
      b.classList.toggle("on", b.dataset.scan === state.scan);
    });
    /* h-size LED */
    document.getElementById("hsize-btn").classList.toggle("on", state.hsize);

    save();
  }

  /* ---------- button handlers ---------- */
  document.getElementById("power-btn").addEventListener("click", function () {
    state.power = !state.power;
    game.style.transition = state.power ? TWEEN_ON : TWEEN_OFF;
    apply();
  });
  /* PROJECT = fullscreen the monitor (projects the picture full-screen) */
  var monitor = document.getElementById("monitor");
  var projectBtn = document.getElementById("project-btn");
  projectBtn.addEventListener("click", function () {
    var fsEl = document.fullscreenElement || document.webkitFullscreenElement;
    if (fsEl) {
      (document.exitFullscreen || document.webkitExitFullscreen).call(document);
    } else {
      (monitor.requestFullscreen || monitor.webkitRequestFullscreen).call(monitor);
    }
  });
  function onFsChange() {
    var fsEl = document.fullscreenElement || document.webkitFullscreenElement;
    projectBtn.classList.toggle("on", fsEl === monitor);
  }
  document.addEventListener("fullscreenchange", onFsChange);
  document.addEventListener("webkitfullscreenchange", onFsChange);

  document.getElementById("scan-group").addEventListener("click", function (e) {
    var btn = e.target.closest(".pvm-btn");
    if (!btn) return;
    // press the lit one -> back to default (null)
    resizeTween();
    state.scan = state.scan === btn.dataset.scan ? null : btn.dataset.scan;
    apply();
  });

  document.getElementById("hsize-btn").addEventListener("click", function () {
    resizeTween();
    state.hsize = !state.hsize;
    apply();
  });

  /* ============================================================
     KEYBOARD (full layout, relevant keys highlighted + anchored)
     ============================================================ */
  // map[char] = { id, fn (css var name), sub (shifted glyph shown) }
  var KEYMAP = {
    "A": { id: "k-A", fn: "move" },
    "D": { id: "k-D", fn: "move" },
    ",": { id: "k-Comma", fn: "atkL", sub: "<" },
    ".": { id: "k-Period", fn: "atkR", sub: ">" },
    "L": { id: "k-L", fn: "atkL" },        // attack left
    ";": { id: "k-Semicolon", fn: "atkR" }, // attack right
    "Space": { id: "k-Space", fn: "music" },
    "Enter": { id: "k-Enter", fn: "start" }
  };
  var FN_VAR = {
    move: "--fn-move", atkL: "--fn-atkL", atkR: "--fn-atkR",
    atkR2: "--fn-atkR", music: "--fn-music", start: "--fn-start"
  };

  var ROWS = [
    ["`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=", { l: "⌫", cls: "wide" }],
    [{ l: "Tab", cls: "tab" }, "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]", "\\"],
    [{ l: "Caps", cls: "caps" }, "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'", { l: "Enter", cls: "enter" }],
    [{ l: "Shift", cls: "shift" }, "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/", { l: "Shift", cls: "shift" }],
    [{ l: "Ctrl", cls: "wide" }, { l: "Alt", cls: "wide" }, { l: "Space", cls: "space" }, { l: "Alt", cls: "wide" }, { l: "Ctrl", cls: "wide" }]
  ];
  // arrow cluster handled separately
  var ARROWS = { left: "←", up: "↑", down: "↓", right: "→" };

  function makeKey(spec) {
    var label = typeof spec === "string" ? spec : spec.l;
    var key = document.createElement("div");
    key.className = "key" + (typeof spec === "object" && spec.cls ? " " + spec.cls : "");

    // resolve highlight: 'Enter' & 'Space' matched by label; printable by char
    var mapKey = null;
    if (label === "Enter") mapKey = "Enter";
    else if (label === "Space") mapKey = "Space";
    else if (KEYMAP[label]) mapKey = label;

    var m = mapKey && KEYMAP[mapKey];
    if (m) {
      key.id = m.id;
      key.classList.add("hl");
      if (m.dual) {
        // split highlight: left half = first fn, right half = second fn
        key.classList.add("dual");
        key.style.setProperty("--c2", "var(" + FN_VAR[m.dual[0]] + ")");
        key.style.setProperty("--c", "var(" + FN_VAR[m.dual[1]] + ")");
      } else {
        key.style.setProperty("--c", "var(" + FN_VAR[m.fn] + ")");
      }
      if (m.sub) {
        var sub = document.createElement("span");
        sub.className = "sub";
        sub.textContent = m.sub;
        key.appendChild(sub);
      }
    }
    key.appendChild(document.createTextNode(label));
    return key;
  }

  function buildKeyboard() {
    var kb = document.getElementById("keyboard");
    kb.innerHTML = "";

    var main = document.createElement("div");
    main.className = "kb-main";
    ROWS.forEach(function (row) {
      var r = document.createElement("div");
      r.className = "krow";
      row.forEach(function (spec) { r.appendChild(makeKey(spec)); });
      main.appendChild(r);
    });
    kb.appendChild(main);

    // arrow cluster
    var arrows = document.createElement("div");
    arrows.className = "kb-arrows";
    var up = document.createElement("div");
    up.className = "krow";
    var upKey = makeArrow(ARROWS.up, "k-Up", false);
    up.appendChild(upKey);
    var bottom = document.createElement("div");
    bottom.className = "krow";
    bottom.appendChild(makeArrow(ARROWS.left, "k-Left", true));
    bottom.appendChild(makeArrow(ARROWS.down, "k-Down", false));
    bottom.appendChild(makeArrow(ARROWS.right, "k-Right", true));
    arrows.appendChild(up);
    arrows.appendChild(bottom);
    kb.appendChild(arrows);
  }

  function makeArrow(glyph, id, isMove) {
    var k = document.createElement("div");
    k.className = "key";
    k.id = id;
    if (isMove) {
      k.classList.add("hl");
      k.style.setProperty("--c", "var(--fn-move)");
    }
    k.textContent = glyph;
    return k;
  }

  /* ============================================================
     WIRES — controller → function → keyboard connector lines
     ============================================================ */
  // [fromAnchorId, toElementId, fnColorVar]
  var CONNECTIONS = [
    // controller -> functions
    ["c-dpad", "f-move", "--fn-move"],
    ["c-b", "f-atkL", "--fn-atkL"],
    ["c-a", "f-atkR", "--fn-atkR"],
    ["c-select", "f-music", "--fn-music"],
    ["c-start", "f-start", "--fn-start"],
    // functions -> keyboard
    ["f-move", "k-A", "--fn-move"],
    ["f-move", "k-D", "--fn-move"],
    ["f-move", "k-Left", "--fn-move"],
    ["f-move", "k-Right", "--fn-move"],
    ["f-atkL", "k-Comma", "--fn-atkL"],
    ["f-atkL", "k-L", "--fn-atkL"],
    ["f-atkR", "k-Period", "--fn-atkR"],
    ["f-atkR", "k-Semicolon", "--fn-atkR"],
    ["f-music", "k-Space", "--fn-music"],
    ["f-start", "k-Enter", "--fn-start"]
  ];

  var SVGNS = "http://www.w3.org/2000/svg";

  function centerBottom(el, cont) {
    var r = el.getBoundingClientRect();
    var c = cont.getBoundingClientRect();
    return { x: r.left - c.left + r.width / 2, y: r.bottom - c.top };
  }
  function centerTop(el, cont) {
    var r = el.getBoundingClientRect();
    var c = cont.getBoundingClientRect();
    return { x: r.left - c.left + r.width / 2, y: r.top - c.top };
  }

  function drawWires() {
    var diagram = document.getElementById("diagram");
    var svg = document.getElementById("wires");
    if (!diagram || !svg) return;

    var w = diagram.clientWidth;
    var h = diagram.clientHeight;
    svg.setAttribute("width", w);
    svg.setAttribute("height", h);
    svg.setAttribute("viewBox", "0 0 " + w + " " + h);
    while (svg.firstChild) svg.removeChild(svg.firstChild);

    CONNECTIONS.forEach(function (conn) {
      var from = document.getElementById(conn[0]);
      var to = document.getElementById(conn[1]);
      if (!from || !to) return;

      var a = centerBottom(from, diagram);
      var b = centerTop(to, diagram);
      // skip degenerate (hidden) anchors
      if (b.y <= a.y) { var t = a; a = b; b = t; }

      var dy = Math.max(28, (b.y - a.y) * 0.42);
      var d = "M " + a.x + " " + a.y +
              " C " + a.x + " " + (a.y + dy) +
              " " + b.x + " " + (b.y - dy) +
              " " + b.x + " " + b.y;

      var color = "var(" + conn[2] + ")";

      // soft glow underlay
      var glow = document.createElementNS(SVGNS, "path");
      glow.setAttribute("d", d);
      glow.setAttribute("fill", "none");
      glow.setAttribute("stroke", color);
      glow.setAttribute("stroke-width", "5");
      glow.setAttribute("stroke-linecap", "round");
      glow.setAttribute("opacity", "0.16");
      svg.appendChild(glow);

      // crisp line
      var path = document.createElementNS(SVGNS, "path");
      path.setAttribute("d", d);
      path.setAttribute("fill", "none");
      path.setAttribute("stroke", color);
      path.setAttribute("stroke-width", "1.6");
      path.setAttribute("stroke-linecap", "round");
      path.setAttribute("opacity", "0.85");
      svg.appendChild(path);

      // node dots
      [a, b].forEach(function (p) {
        var dot = document.createElementNS(SVGNS, "circle");
        dot.setAttribute("cx", p.x);
        dot.setAttribute("cy", p.y);
        dot.setAttribute("r", "2.6");
        dot.setAttribute("fill", color);
        svg.appendChild(dot);
      });
    });
  }

  /* ============================================================
     HINTS — placeholder fold-out drawers
     ============================================================ */
  var HINTS = [
    {
      tag: "HINT", title: "Getting started",
      body: "Placeholder hint copy goes here. Describe the opening area, what the player should try first, and the basic loop. Keep it spoiler-free.",
      spoiler: "A blacked-out spoiler line lives here — hover to reveal the actual answer when this section is written."
    },
    {
      tag: "HINT", title: "First boss",
      body: "Placeholder hint copy. Nudge the player toward the mechanic they need to learn without giving the solution away.",
      spoiler: "Hidden spoiler: the exact pattern / weakness goes here once written."
    },
    {
      tag: "SPOILER", title: "Secret area",
      body: "Placeholder copy describing that a secret exists and roughly where to look.",
      spoiler: "Full spoiler: step-by-step route to the secret area goes here."
    },
    {
      tag: "SPOILER", title: "True ending",
      body: "Placeholder copy. Set expectations — there is more than one ending.",
      spoiler: "Full spoiler: the conditions required for the true ending go here."
    }
  ];

  function buildHints() {
    var wrap = document.getElementById("hints");
    HINTS.forEach(function (h) {
      var d = document.createElement("details");
      d.className = "hint";

      var s = document.createElement("summary");
      var tags = document.createElement("span");
      tags.className = "tags";
      var tagH = document.createElement("span");
      tagH.className = "tag";
      tagH.textContent = "HINT";
      tagH.style.background = "var(--fn-atkR)";
      var tagS = document.createElement("span");
      tagS.className = "tag";
      tagS.textContent = "SPOILER";
      tagS.style.background = "var(--fn-atkL)";
      tags.appendChild(tagH);
      tags.appendChild(tagS);
      var title = document.createElement("span");
      title.textContent = h.title;
      var chev = document.createElement("span");
      chev.className = "chev";
      chev.textContent = "▶";
      s.appendChild(tags);
      s.appendChild(title);
      s.appendChild(chev);
      d.appendChild(s);

      var body = document.createElement("div");
      body.className = "hint-body";
      var p1 = document.createElement("p");
      p1.textContent = h.body;
      var p2 = document.createElement("p");
      var sp = document.createElement("span");
      sp.className = "spoiler";
      sp.textContent = h.spoiler;
      p2.appendChild(sp);
      body.appendChild(p1);
      body.appendChild(p2);
      d.appendChild(body);

      wrap.appendChild(d);
    });
  }

  /* ============================================================
     BOOT
     ============================================================ */
  function boot() {
    /* first paint: no tween, so a persisted power-off shows instantly */
    game.style.transition = "none";
    apply();
    // re-enable the bounce transition next frame
    requestAnimationFrame(function () {
      requestAnimationFrame(function () { game.style.transition = ""; });
    });
    buildKeyboard();
    buildHints();
    drawWires();

    // redraw wires whenever layout shifts
    window.addEventListener("resize", debounce(drawWires, 80));
    if (window.ResizeObserver) {
      var ro = new ResizeObserver(debounce(drawWires, 60));
      ro.observe(document.getElementById("diagram"));
    }
    if (document.fonts && document.fonts.ready) {
      document.fonts.ready.then(function () { drawWires(); });
    }
    // a couple of settling redraws for late layout
    setTimeout(drawWires, 120);
    setTimeout(drawWires, 500);
  }

  function debounce(fn, ms) {
    var t;
    return function () { clearTimeout(t); t = setTimeout(fn, ms); };
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", boot);
  } else {
    boot();
  }
})();
