/*
 * Default Secret Santa Reveal — behaviour.
 * Ported 1:1 from the approved "Default Secret Santa Reveal.html" design (snowy sky, falling snow, tap the envelope,
 * brush the snow off the card, confetti). Only the data source changed: the recipient now comes from the server
 * when the participant taps the envelope, instead of from the page URL.
 */
(function () {
  'use strict';
  var root = document.querySelector('[data-theme-root]');
  if (!root) return;

  var kit = ThemeKit.create(root);
  var getName = ThemeKit.createRevealer(root);
  var R = kit.refs;
  var reduced = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  var cfg = {
    year: root.dataset.year || '',
    groupTitle: root.dataset.groupTitle || '',
    participant: (root.dataset.participantName || '').trim().split(/\s+/)[0] || '',
    snowAmount: reduced ? 0 : 140,
    autoMusic: true,
    bubbleText: "Psst! Something's buried here. Tap the snow!"
  };

  var state = { open: false, revealed: false, clearing: false, playing: false, loading: false, error: '', name: '', wishes: [] };
  var parts = [], flakes = [], stars = [], shoot = null, nextShoot = performance.now() + 3000;
  var muted = false, drag = false, last = null, scratchReady = false, scratchN = 0, raf = 0;

  /* ---------- decorative SVG generated exactly as in the design (seeded, so identical every time) ---------- */
  function esc(v) { return String(v).replace(/&/g, '&amp;').replace(/"/g, '&quot;'); }
  function tag(name, attrs, children) {
    var s = '<' + name;
    for (var k in attrs) s += ' ' + k.replace(/[A-Z]/g, function (m) { return '-' + m.toLowerCase(); }) + '="' + esc(attrs[k]) + '"';
    return s + '>' + (children || '') + '</' + name + '>';
  }
  function seeded(seed) { return function () { seed = (seed * 16807) % 2147483647; return seed / 2147483647; }; }
  function f1(v) { return v.toFixed(1); }

  function buildDeco() {
    var kids = [], rnd = seeded(7), cx = 150, N = 9, tiers = [], i, k;
    for (i = 0; i < N; i++) { var yb0 = 70 + i * (295 / (N - 1)); tiers.push({ yb: yb0, yt: yb0 - (46 + i * 6), w: 30 + i * 15 }); }
    for (i = N - 1; i >= 0; i--) {
      var yb = tiers[i].yb, yt = tiers[i].yt, w = tiers[i].w, t, x, y;
      var d = 'M' + cx + ' ' + f1(yt);
      for (k = 1; k <= 4; k++) { t = k / 5; x = cx - w * t; y = yt + (yb - yt) * t; d += ' L' + f1(x - 5 - rnd() * 4) + ' ' + f1(y + 3) + ' L' + f1(x + 3) + ' ' + f1(y + 1); }
      var m = 6 + i, sw = (w * 2) / m;
      d += ' L' + f1(cx - w) + ' ' + f1(yb + 4);
      for (k = 0; k < m; k++) { var x0 = cx - w + k * sw; d += ' L' + f1(x0 + sw * 0.5) + ' ' + f1(yb - 5 - rnd() * 5) + ' L' + f1(x0 + sw) + ' ' + f1(yb + 3 + rnd() * 7); }
      for (k = 4; k >= 1; k--) { t = k / 5; x = cx + w * t; y = yt + (yb - yt) * t; d += ' L' + f1(x - 3) + ' ' + f1(y + 1) + ' L' + f1(x + 5 + rnd() * 4) + ' ' + f1(y + 3); }
      d += ' Z';
      kids.push(tag('path', { d: d, fill: 'url(#needle)' }));
      kids.push(tag('path', { d: d, fill: 'url(#needleIn)' }));
      var nd = '';
      for (k = 0; k < 14 + i * 5; k++) { t = 0.3 + rnd() * 0.7; y = yt + (yb - yt) * t; x = cx + (rnd() * 2 - 1) * w * t * 0.9; nd += 'M' + f1(x) + ' ' + f1(y) + ' l' + f1((x < cx ? -1 : 1) * (4 + rnd() * 5)) + ' ' + f1(3 + rnd() * 3) + ' '; }
      kids.push(tag('path', { d: nd, stroke: '#3c9a68', strokeWidth: 1.2, strokeLinecap: 'round', opacity: 0.55, fill: 'none' }));
      var clumps = '', cy0 = yb - 12, half = w * ((cy0 - yt) / (yb - yt)) * 0.78;
      for (x = cx - half; x < cx + half; x += 7 + rnd() * 9) {
        if (rnd() < 0.3) continue;
        var room = half - Math.abs(x - cx), rx = Math.min(4 + rnd() * 6, Math.max(room, 0));
        if (rx < 2.5) continue;
        var edge = Math.abs(x - cx) / half;
        clumps += tag('ellipse', { cx: f1(x), cy: f1(cy0 - edge * 3 - rnd() * 3), rx: f1(rx), ry: f1(2 + rnd() * 2), fill: 'url(#snowCap)' });
      }
      kids.push(tag('g', {}, clumps));
    }
    kids.push(tag('path', { d: 'M150 24 L158 40 Q150 44 142 40 Z', fill: '#ffffff', opacity: 0.9 }));
    var orn = ['#ec3013', '#f4c430', '#c0c7d6', '#ec3013', '#1e5fbf', '#f4c430', '#ec3013'];
    for (k = 0; k < 16; k++) {
      var ti = 1 + Math.floor(rnd() * (N - 1)), tr = tiers[ti], oy = tr.yb - 14 - rnd() * 10, ox = cx + (rnd() * 2 - 1) * tr.w * 0.7, c = orn[k % orn.length], r = 5 + rnd() * 3;
      kids.push(tag('g', {},
        tag('line', { x1: f1(ox), y1: f1(oy - r - 6), x2: f1(ox), y2: f1(oy - r), stroke: '#d9b54a', strokeWidth: 0.8 }) +
        tag('rect', { x: f1(ox - 2), y: f1(oy - r - 3), width: 4, height: 3, fill: '#d9b54a' }) +
        tag('circle', { cx: f1(ox), cy: f1(oy), r: f1(r), fill: c }) +
        tag('circle', { cx: f1(ox + r * 0.25), cy: f1(oy + r * 0.3), r: f1(r * 0.8), fill: '#000', opacity: 0.25 }) +
        tag('circle', { cx: f1(ox - r * 0.35), cy: f1(oy - r * 0.35), r: f1(r * 0.3), fill: '#fff', opacity: 0.75 })));
    }
    var colors = ['#ff4a2b', '#ffd54a', '#6fd3ff', '#fff6d8', '#ff8fb1', '#8dff9e'], li = 0;
    for (var row = 0; row < 8; row++) {
      var wy = 82 + row * 37, ww = (10 + (wy - 30) / 335 * 140) * 0.88, n = 3 + row, sag = 14;
      var p0 = [cx - ww, wy - 8], p1 = [cx, wy + sag], p2 = [cx + ww, wy - 8];
      kids.push(tag('path', { d: 'M' + f1(p0[0]) + ' ' + f1(p0[1]) + ' Q' + f1(p1[0]) + ' ' + f1(p1[1]) + ' ' + f1(p2[0]) + ' ' + f1(p2[1]), stroke: '#0a2a1c', strokeWidth: 1, fill: 'none', opacity: 0.8 }));
      for (k = 0; k < n; k++) {
        var u = (k + 0.5) / n, bx = (1 - u) * (1 - u) * p0[0] + 2 * (1 - u) * u * p1[0] + u * u * p2[0], by = (1 - u) * (1 - u) * p0[1] + 2 * (1 - u) * u * p1[1] + u * u * p2[1];
        var lc = colors[li % colors.length], dur = (1 + (li % 5) * 0.3).toFixed(2), delay = ((li * 0.41) % 2).toFixed(2);
        kids.push(tag('g', { style: 'animation:blink ' + dur + 's ease-in-out ' + delay + 's infinite' },
          tag('circle', { cx: f1(bx), cy: f1(by + 2), r: 5.5, fill: lc, opacity: 0.3 }) +
          tag('ellipse', { cx: f1(bx), cy: f1(by + 2), rx: 2.6, ry: 3.4, fill: lc })));
        li++;
      }
    }
    return kids.join('');
  }

  function buildPines() {
    var rnd = seeded(11), out = '';
    for (var k = 0; k < 26; k++) {
      var x = rnd() * 1440, ht = 30 + rnd() * 50, w = ht * 0.32, b = 150 + rnd() * 10;
      out += tag('path', { d: 'M' + x + ' ' + (b - ht) + ' L' + (x - w * 0.6) + ' ' + (b - ht * 0.55) + ' L' + (x - w * 0.3) + ' ' + (b - ht * 0.55) + ' L' + (x - w) + ' ' + b + ' L' + (x + w) + ' ' + b + ' L' + (x + w * 0.3) + ' ' + (b - ht * 0.55) + ' L' + (x + w * 0.6) + ' ' + (b - ht * 0.55) + ' Z', opacity: 0.6 + rnd() * 0.4 });
    }
    return out;
  }

  function buildGlints() {
    var rnd = seeded(5), out = '';
    for (var k = 0; k < 40; k++)
      out += tag('circle', { cx: (rnd() * 1440).toFixed(0), cy: (212 + rnd() * 85).toFixed(0), r: (0.8 + rnd() * 1.4).toFixed(1), fill: '#ffffff', style: 'animation:blink ' + (1.5 + rnd() * 2).toFixed(2) + 's ease-in-out ' + (rnd() * 3).toFixed(2) + 's infinite' });
    return out;
  }

  var svgSlots = { treeDeco: buildDeco, pines: buildPines, glints: buildGlints };
  root.querySelectorAll('[data-svg]').forEach(function (g) { var f = svgSlots[g.dataset.svg]; if (f) g.innerHTML = f(); });

  /* ---------- canvases ---------- */
  function resize() {
    var dpr = Math.min(window.devicePixelRatio || 1, 2);
    [R.skyRef, R.fxRef].forEach(function (c) {
      if (!c) return;
      c.width = innerWidth * dpr; c.height = innerHeight * dpr;
      c.getContext('2d').setTransform(dpr, 0, 0, dpr, 0, 0);
    });
    stars = Array.from({ length: Math.round(innerWidth * innerHeight / 5000) }, function () {
      return { x: Math.random() * innerWidth, y: Math.random() * innerHeight * 0.72, r: Math.random() * 1.3 + 0.3, p: Math.random() * 6.28, s: 0.6 + Math.random() * 2, big: Math.random() < 0.06 };
    });
    if (state.open && !state.revealed) { scratchReady = false; initScratch(); }
    if (state.open) fitName();
  }

  function draw(t) {
    var W = innerWidth, H = innerHeight, sec = t / 1000;
    var sc = R.skyRef, fc = R.fxRef;
    if (!sc || !fc) return;
    var s = sc.getContext('2d');
    s.clearRect(0, 0, W, H);
    stars.forEach(function (st) {
      var a = reduced ? 0.7 : 0.25 + 0.75 * Math.abs(Math.sin(sec * st.s + st.p));
      s.globalAlpha = a; s.fillStyle = '#fff';
      s.beginPath(); s.arc(st.x, st.y, st.r, 0, 6.28); s.fill();
      if (st.big) { s.globalAlpha = a * 0.7; s.fillRect(st.x - 5 * a, st.y - 0.5, 10 * a, 1); s.fillRect(st.x - 0.5, st.y - 5 * a, 1, 10 * a); }
    });
    if (!reduced) {
      if (!shoot && t > nextShoot) shoot = { x: Math.random() * W * 0.7 + W * 0.2, y: Math.random() * H * 0.25, life: 0 };
      if (shoot) {
        shoot.life += 1; var k = shoot.life / 50;
        var x = shoot.x - k * 260, y = shoot.y + k * 120;
        var gr = s.createLinearGradient(x, y, x + 90, y - 42);
        gr.addColorStop(0, 'rgba(255,255,255,' + (1 - k) + ')'); gr.addColorStop(1, 'rgba(255,255,255,0)');
        s.globalAlpha = 1; s.strokeStyle = gr; s.lineWidth = 2; s.beginPath(); s.moveTo(x, y); s.lineTo(x + 90, y - 42); s.stroke();
        if (shoot.life > 50) { shoot = null; nextShoot = t + 5000 + Math.random() * 6000; }
      }
    }
    s.globalAlpha = 1;

    var f = fc.getContext('2d');
    f.clearRect(0, 0, W, H);
    var want = cfg.snowAmount;
    while (flakes.length < want) flakes.push({ x: Math.random() * W, y: Math.random() * H - H, r: Math.random() * 2.6 + 0.8, v: Math.random() * 0.8 + 0.35, p: Math.random() * 6.28 });
    if (flakes.length > want) flakes.length = want;
    f.fillStyle = '#fff';
    flakes.forEach(function (fl) {
      fl.y += fl.v * (fl.r / 2); fl.x += Math.sin(sec * 0.8 + fl.p) * 0.35;
      if (fl.y > H + 5) { fl.y = -5; fl.x = Math.random() * W; }
      f.globalAlpha = 0.45 + fl.r / 6; f.beginPath(); f.arc(fl.x, fl.y, fl.r, 0, 6.28); f.fill();
    });
    parts = parts.filter(function (p) { return p.life > 0; });
    parts.forEach(function (p) {
      p.vy += p.g; p.vx *= 0.99; p.x += p.vx; p.y += p.vy; p.rot += p.vr; p.life -= 1;
      f.globalAlpha = Math.min(1, p.life / 30);
      f.fillStyle = p.c;
      if (p.kind === 'puff') { f.beginPath(); f.arc(p.x, p.y, p.r, 0, 6.28); f.fill(); }
      else { f.save(); f.translate(p.x, p.y); f.rotate(p.rot); f.scale(1, Math.cos(p.rot * 2)); f.fillRect(-p.w / 2, -p.h / 2, p.w, p.h); f.restore(); }
    });
    f.globalAlpha = 1;
  }

  /* ---------- scratch card ---------- */
  function initScratch() {
    var c = R.scratchRef; if (!c) return;
    var r = c.getBoundingClientRect(); if (!r.width) return;
    var dpr = Math.min(window.devicePixelRatio || 1, 2);
    c.width = r.width * dpr; c.height = r.height * dpr;
    var x = c.getContext('2d', { willReadFrequently: true });
    x.setTransform(dpr, 0, 0, dpr, 0, 0);
    var g = x.createLinearGradient(0, 0, 0, r.height);
    g.addColorStop(0, '#ffffff'); g.addColorStop(0.6, '#eef2fa'); g.addColorStop(1, '#d4def0');
    x.fillStyle = g; x.fillRect(0, 0, r.width, r.height);
    var i, bx, by, br, rg;
    for (i = 0; i < 26; i++) {
      bx = Math.random() * r.width; by = Math.random() * r.height; br = 20 + Math.random() * 50;
      rg = x.createRadialGradient(bx, by, 0, bx, by, br);
      rg.addColorStop(0, 'rgba(176,192,222,.28)'); rg.addColorStop(1, 'rgba(176,192,222,0)');
      x.fillStyle = rg; x.fillRect(bx - br, by - br, br * 2, br * 2);
    }
    for (i = 0; i < 18; i++) {
      bx = Math.random() * r.width; by = Math.random() * r.height; br = 14 + Math.random() * 30;
      rg = x.createRadialGradient(bx - br * 0.3, by - br * 0.3, 0, bx, by, br);
      rg.addColorStop(0, 'rgba(255,255,255,.9)'); rg.addColorStop(1, 'rgba(255,255,255,0)');
      x.fillStyle = rg; x.fillRect(bx - br, by - br, br * 2, br * 2);
    }
    for (i = 0; i < 260; i++) {
      x.globalAlpha = Math.random() * 0.6 + 0.2;
      x.fillStyle = Math.random() < 0.6 ? '#b9c7e2' : '#ffffff';
      x.beginPath(); x.arc(Math.random() * r.width, Math.random() * r.height, Math.random() * 1.6 + 0.3, 0, 6.28); x.fill();
    }
    x.globalAlpha = 1; x.fillStyle = '#6f80a3'; x.font = '700 13px Archivo, sans-serif'; x.textBaseline = 'alphabetic';
    x.fillText('BRUSH HERE', 24, r.height - 28);
    scratchReady = true; scratchN = 0;
  }

  function pt(e) { var r = R.scratchRef.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top, cx: e.clientX, cy: e.clientY }; }

  function erase(a, b) {
    var c = R.scratchRef; if (!c) return;
    var x = c.getContext('2d');
    x.globalCompositeOperation = 'destination-out'; x.lineCap = 'round'; x.lineJoin = 'round'; x.lineWidth = 44;
    x.beginPath(); x.moveTo(a.x, a.y); x.lineTo(b.x + 0.1, b.y); x.stroke();
    x.globalCompositeOperation = 'source-over';
    if (!reduced) for (var i = 0; i < 3; i++) parts.push({ kind: 'puff', x: b.cx + (Math.random() - 0.5) * 30, y: b.cy + (Math.random() - 0.5) * 20, vx: (Math.random() - 0.5) * 3, vy: -Math.random() * 3 - 0.5, g: 0.12, r: Math.random() * 2.5 + 1, c: '#ffffff', life: 40, rot: 0, vr: 0 });
    if (++scratchN % 4 === 0) check();
  }

  function check() {
    var c = R.scratchRef; if (!c) return;
    var d = c.getContext('2d').getImageData(0, 0, c.width, c.height).data, clear = 0, tot = 0;
    for (var i = 3; i < d.length; i += 4 * 7) { tot++; if (d[i] < 128) clear++; }
    if (clear / tot >= 0.9) reveal();
  }

  function reveal() {
    if (state.revealed) return;
    setState({ revealed: true, clearing: true });
    setTimeout(function () { setState({ clearing: false }); }, 800);
    var card = R.cardRef, r = card ? card.getBoundingClientRect() : { left: innerWidth / 2, top: innerHeight / 2, width: 0, height: 0 };
    if (reduced) return;
    var colors = ['#ec3013', '#ffd54a', '#ffffff', '#1f8a5b', '#f7a6a0'];
    function burst(x, y, n, spread) {
      for (var i = 0; i < n; i++) {
        var a = -Math.PI / 2 + (Math.random() - 0.5) * spread, sp = Math.random() * 10 + 6;
        parts.push({ kind: 'conf', x: x, y: y, vx: Math.cos(a) * sp, vy: Math.sin(a) * sp, g: 0.22, w: Math.random() * 6 + 6, h: Math.random() * 4 + 4, rot: Math.random() * 6, vr: (Math.random() - 0.5) * 0.3, c: colors[i % colors.length], life: 140 + Math.random() * 60 });
      }
    }
    burst(r.left + r.width / 2, r.top + r.height * 0.35, 170, Math.PI * 1.1);
    setTimeout(function () { burst(r.left, r.top + r.height * 0.3, 70, 1.2); }, 250);
    setTimeout(function () { burst(r.left + r.width, r.top + r.height * 0.3, 70, 1.2); }, 450);
  }

  function playMusic() { var a = R.audioRef; if (a) { var p = a.play(); if (p && p.catch) p.catch(function () {}); } }

  /* ---------- state -> view ---------- */
  function setState(patch) { Object.assign(state, patch); render(); }

  /* Wish list rows use the design's own markup/styles (numbered rows under the card). */
  function paintWishes() {
    var box = R.wishBox, rows = R.wishRows;
    if (!box || !rows) return;
    var key = state.wishes.join('|');
    if (rows.dataset.key === key) return;
    rows.dataset.key = key;
    rows.textContent = '';
    state.wishes.forEach(function (w, i) {
      var row = document.createElement('div');
      row.style.cssText = 'display:grid;grid-template-columns:64px minmax(0,1fr);border-bottom:2px solid var(--color-neutral-200)';
      var n = document.createElement('div');
      n.style.cssText = 'padding:14px 0 14px 24px;font-weight:800;font-size:15px;color:var(--color-accent)';
      n.textContent = String(i + 1).padStart(2, '0');
      var t = document.createElement('div');
      t.style.cssText = 'padding:14px 24px 14px 0;font-size:17px;font-weight:500;line-height:1.35;overflow-wrap:anywhere';
      t.textContent = w;
      row.appendChild(n); row.appendChild(t); rows.appendChild(row);
    });
    box.hidden = state.wishes.length === 0;
  }

  function render() {
    var s = state;
    kit.text({
      year: cfg.year,
      groupTitle: cfg.groupTitle,
      name: s.name,
      wishCount: s.wishes.length + (s.wishes.length === 1 ? ' item' : ' items'),
      musicLabel: s.playing ? 'Pause music' : 'Play music',
      greeting: cfg.participant ? 'Merry Christmas, ' + cfg.participant + '!' : 'Merry Christmas!',
      subline: s.error ? s.error : (s.revealed ? 'Now you know who to shop for. Keep it secret.' : 'Your Secret Santa pick is buried in the snow. Ask the snowman.'),
      bubble: s.revealed ? 'Ho ho ho! Tap the card to see it again.' : cfg.bubbleText,
      tapLabel: s.loading ? 'Opening…' : (s.error ? 'Try again' : (s.revealed ? 'Open again' : 'Tap to open'))
    });
    kit.show({
      open: s.open, revealed: s.revealed, notRevealed: !s.revealed,
      playing: s.playing, notPlaying: !s.playing,
      showScratch: !s.revealed || s.clearing
    });
    kit.vars({ scratchOpacity: s.revealed ? 0 : 1 });
    paintWishes();
    if (R.envelopeBtn) R.envelopeBtn.disabled = s.loading;
  }

  /* Shrinks the recipient name only when a single long word would not fit the card (keeps the original size otherwise). */
  function fitName() {
    var t = root.querySelector('[data-ref="cardRef"] x-t[data-t="name"]'), el = t && t.parentElement;
    if (!el) return;
    if (el.dataset.origFs === undefined) el.dataset.origFs = el.style.fontSize;   // the design's own responsive size
    el.style.fontSize = el.dataset.origFs;
    var size = parseFloat(getComputedStyle(el).fontSize);
    while (el.scrollWidth > el.clientWidth + 1 && size > 22) { size -= 2; el.style.fontSize = size + 'px'; }
  }

  var handlers = {
    openCard: async function () {
      if (state.loading) return;
      // Start the music inside the tap itself so mobile browsers allow it.
      if (!muted && cfg.autoMusic) playMusic();
      setState({ loading: true, error: '' });
      try {
        var res = await getName();
        scratchReady = false;
        setState({ name: res.name, wishes: res.wishes || [], open: true, loading: false });
        fitName();
        queueMicrotask(function () { if (R.cardRef) R.cardRef.focus({ preventScroll: true }); });
      } catch (err) {
        setState({ loading: false, error: (err && err.userMessage) || 'Something went wrong. Please try again.' });
      }
    },
    close: function () { setState({ open: false }); },
    backdropClick: function () { if (state.revealed) setState({ open: false }); },
    stop: function (e) { e.stopPropagation(); },
    clearAll: function () { reveal(); },
    toggleMusic: function () {
      var a = R.audioRef; if (!a) return;
      if (a.paused) { muted = false; playMusic(); } else { muted = true; a.pause(); }
    },
    scratchDown: function (e) { drag = true; try { e.currentTarget.setPointerCapture(e.pointerId); } catch (x) {} var q = pt(e); last = q; erase(q, q); },
    scratchMove: function (e) { if (!drag) return; var q = pt(e); erase(last, q); last = q; },
    scratchUp: function () { if (drag) { drag = false; check(); } }
  };
  kit.bind(handlers);

  // Accessibility touches that do not change the look: dialog semantics, Escape to close, envelope handle.
  R.envelopeBtn = root.querySelector('[data-envelope]');
  if (R.cardRef) { R.cardRef.setAttribute('role', 'dialog'); R.cardRef.setAttribute('aria-modal', 'true'); R.cardRef.setAttribute('aria-label', 'Your Secret Santa card'); R.cardRef.tabIndex = -1; }
  document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && state.open && state.revealed) setState({ open: false }); });

  var audio = R.audioRef;
  if (audio) {
    audio.onplay = function () { setState({ playing: true }); };
    audio.onpause = function () { setState({ playing: false }); };
  }

  resize();
  addEventListener('resize', resize);
  render();
  var started = false;
  function loop(t) {
    if (state.open && !state.revealed && !scratchReady) initScratch();
    draw(t);
    raf = requestAnimationFrame(loop);
  }
  started = true;
  raf = requestAnimationFrame(loop);

  document.addEventListener('visibilitychange', function () {
    if (document.hidden) { cancelAnimationFrame(raf); started = false; }
    else if (!started) { started = true; raf = requestAnimationFrame(loop); }
  });
})();
