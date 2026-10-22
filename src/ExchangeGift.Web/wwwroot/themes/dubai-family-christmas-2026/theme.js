/*
 * Dubai Family Christmas 2026 — behaviour.
 * Ported from the approved "Dubai Family Christmas 2026.html" design: press & hold to light the Burj Khalifa,
 * rockets and fireworks, Santa's sleigh, synthesized music and the glowing name card.
 * The recipient is requested from the server when the hold completes and is only drawn once the rockets burst.
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
    eyebrow: (root.dataset.eyebrow || '').toUpperCase(),
    holdSeconds: 2.5,
    atmosphere: true,
    ending: 'card'
  };

  var state = { phase: 'idle', holding: false, music: false, nameLit: false, name: '', wishes: [] };
  var timers = [], progress = 0, parts = [], rockets = [], nameParts = [];
  var holding = false, paintedOnce = false, launchId = 0, userMuted = false;
  var W = 0, H = 0, ctx, stars = [], snow = [], motes = [], raf = 0, ambient = 0, musicTimer = 0;
  var santa = null, nextSanta = 0, santaDir = 0, nameFade = 0, errorText = '';
  var actx = null, master = null, sfx = null, noise = null;
  var DEFAULT_FLOOR = 'PRESS · HOLD · WATCH THE SKY';

  kit.text({ year: cfg.year, eyebrow: cfg.eyebrow });

  function motionOn() { return !reduced && cfg.atmosphere; }
  function later(ms, fn) { timers.push(setTimeout(fn, ms)); }
  function clearTimers() { timers.forEach(clearTimeout); timers = []; }
  function rand(a, b) { return a + Math.random() * (b - a); }

  /* ---------- name helpers (long names wrap onto two lines so they stay readable on phones) ---------- */
  function nameText() { return (state.name || '').toUpperCase(); }
  function nameLines() {
    var n = nameText();
    if (n.length <= 10 || n.indexOf(' ') < 0) return [n];
    var best = -1, bd = 1e9;
    for (var i = 0; i < n.length; i++) if (n[i] === ' ' && Math.abs(i - n.length / 2) < bd) { bd = Math.abs(i - n.length / 2); best = i; }
    return [n.slice(0, best), n.slice(best + 1)];
  }
  function metrics() {
    var w = W || window.innerWidth, h = H || window.innerHeight, lines = nameLines();
    var longest = Math.max(3, lines.reduce(function (m, l) { return Math.max(m, l.length); }, 0));
    var cap = lines.length > 1 ? h * 0.115 : h * 0.15;
    return { fs: Math.round(Math.min((w * 0.84) / (longest * 0.8), cap, 140)), cy: Math.round(h * (w < 700 ? 0.28 : 0.3)), lines: lines };
  }

  /* ---------- canvas ---------- */
  function initCanvas() {
    var c = R.canvasRef; if (!c) return;
    ctx = c.getContext('2d');
    function onResize() {
      var dpr = Math.min(window.devicePixelRatio || 1, 2);
      W = window.innerWidth; H = window.innerHeight;
      c.width = W * dpr; c.height = H * dpr; ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      var area = W * H;
      stars = Array.from({ length: Math.round(area / 16000) }, function () { return { x: rand(0, W), y: rand(0, H * 0.5), r: rand(0.4, 1.2), p: rand(0, 6.28), s: rand(0.4, 1.2) }; });
      snow = Array.from({ length: Math.round(area / 15000) }, function () { return { x: rand(0, W), y: rand(0, H), r: rand(0.6, 2), v: rand(0.25, 0.7), p: rand(0, 6.28), a: rand(0.35, 0.8) }; });
      motes = Array.from({ length: Math.max(6, Math.round(area / 90000)) }, function () { return { x: rand(0, W), y: rand(H * 0.3, H), r: rand(1, 2.2), v: rand(0.08, 0.25), p: rand(0, 6.28) }; });
      render();
    }
    onResize();
    window.addEventListener('resize', onResize);
    window.addEventListener('orientationchange', function () { setTimeout(onResize, 250); });
    var last = performance.now();
    function loop(now) {
      var dt = Math.min(64, now - last); last = now;
      tickHold(dt); tickSanta(now); draw(now / 1000);
      raf = requestAnimationFrame(loop);
    }
    raf = requestAnimationFrame(loop);
  }

  function holdMs() { return cfg.holdSeconds * 1000; }

  function tickHold(dt) {
    var ph = state.phase;
    if (ph === 'idle') {
      var prev = progress;
      if (holding) progress = Math.min(1, progress + dt / holdMs());
      else progress = Math.max(0, progress - dt / 700);
      if (progress >= 1 && holding) { holding = false; launch(); }
      if (prev === progress && !paintedOnce) return;
    }
    paintedOnce = true;
    var p = progress, e = 1 - Math.pow(1 - p, 1.4);
    var y = 1000 * (1 - e), h = 1000 * e;
    [R.litRef, R.litWinRef].forEach(function (r) { if (r) { r.setAttribute('y', y); r.setAttribute('height', h); } });
    if (R.ringRef) R.ringRef.setAttribute('stroke-dashoffset', 326.7 * (1 - p));
    if (R.hazeRef) R.hazeRef.style.opacity = 0.6 + 0.4 * p;
    if (R.starRef) R.starRef.style.filter = 'drop-shadow(0 0 ' + (4 + p * 18) + 'px #F3DCA0)';
    if (R.floorRef && ph === 'idle') {
      R.floorRef.textContent = p > 0 ? 'FLOOR ' + Math.max(1, Math.round(p * 163)) + ' OF 163' : (errorText || DEFAULT_FLOOR);
    }
  }

  function glow(x, y, r, col, a) {
    var g = ctx.createRadialGradient(x, y, 0, x, y, r);
    g.addColorStop(0, 'rgba(255,248,235,' + a + ')'); g.addColorStop(0.35, 'rgba(' + col + ',' + a * 0.8 + ')'); g.addColorStop(1, 'rgba(' + col + ',0)');
    ctx.fillStyle = g; ctx.beginPath(); ctx.arc(x, y, r, 0, 6.283); ctx.fill();
  }

  function draw(t) {
    var moving = motionOn();
    ctx.clearRect(0, 0, W, H);
    ctx.globalCompositeOperation = 'source-over';
    stars.forEach(function (s) {
      ctx.fillStyle = 'rgba(255,248,235,' + (moving ? 0.35 + 0.45 * (0.5 + 0.5 * Math.sin(t * s.s + s.p)) : 0.6) + ')';
      ctx.beginPath(); ctx.arc(s.x, s.y, s.r, 0, 6.283); ctx.fill();
    });
    snow.forEach(function (f) {
      if (moving) { f.y += f.v; f.x += Math.sin(t * 0.6 + f.p) * 0.25; if (f.y > H + 4) { f.y = -4; f.x = Math.random() * W; } }
      ctx.fillStyle = 'rgba(255,248,235,' + f.a * 0.7 + ')'; ctx.beginPath(); ctx.arc(f.x, f.y, f.r, 0, 6.283); ctx.fill();
    });
    motes.forEach(function (m) {
      if (moving) { m.y -= m.v; m.x += Math.sin(t * 0.4 + m.p) * 0.2; if (m.y < H * 0.2) { m.y = H + 4; m.x = Math.random() * W; } }
      glow(m.x, m.y, m.r * 4, '243,220,160', 0.85);
    });
    ctx.globalCompositeOperation = 'lighter';
    var now = performance.now();
    rockets = rockets.filter(function (r) {
      var k = Math.min(1, (now - r.t0) / r.dur); if (k < 0) return true;
      var e = 1 - Math.pow(1 - k, 2.2);
      var x = r.sx + (r.tx - r.sx) * e, y = r.sy + (r.ty - r.sy) * e;
      var ke = Math.max(0, e - 0.12), tx = r.sx + (r.tx - r.sx) * ke, ty = r.sy + (r.ty - r.sy) * ke;
      var g = ctx.createLinearGradient(tx, ty, x, y); g.addColorStop(0, 'rgba(243,220,160,0)'); g.addColorStop(1, 'rgba(255,241,198,.9)');
      ctx.strokeStyle = g; ctx.lineWidth = 2; ctx.beginPath(); ctx.moveTo(tx, ty); ctx.lineTo(x, y); ctx.stroke();
      glow(x, y, 6, '255,248,235', 1);
      if (k >= 1) { if (r.onArrive) r.onArrive(); return false; }
      return true;
    });
    parts = parts.filter(function (p) { return now - p.t0 < p.life; });
    parts.forEach(function (p) {
      var age = now - p.t0; if (age < 0) return;
      var k = age / p.life;
      p.px = p.x; p.py = p.y;
      p.vx *= 0.965; p.vy = p.vy * 0.965 + p.g; p.x += p.vx; p.y += p.vy;
      var a = Math.pow(1 - k, 1.4) * (k > 0.6 && Math.random() < 0.3 ? 0.3 : 1);
      ctx.strokeStyle = 'rgba(' + p.col + ',' + a * 0.7 + ')'; ctx.lineWidth = p.r; ctx.lineCap = 'round';
      ctx.beginPath(); ctx.moveTo(p.px, p.py); ctx.lineTo(p.x, p.y); ctx.stroke();
      glow(p.x, p.y, p.r * 2.6, p.col, a);
    });
    if (nameParts.length) {
      var fade = nameFade ? Math.max(0, 1 - (now - nameFade) / 900) : 1;
      if (fade <= 0) nameParts = [];
      nameParts.forEach(function (p) {
        var age = now - p.t0; if (age < 0) return;
        var k = Math.min(1, Math.max(0, (age - 180) / 1300)), e = 1 - Math.pow(1 - k, 3);
        var out = 1 - Math.exp(-age / 220);
        var ex = p.ox + p.dx * out, ey = p.oy + p.dy * out;
        var x = ex + (p.tx - ex) * e, y = ey + (p.ty - ey) * e + (nameFade ? (now - nameFade) * 0.03 * p.fall : 0);
        var tw = moving ? 0.7 + 0.3 * Math.sin(now / 180 + p.ph) : 1;
        glow(x, y, p.r * 2.4, p.col, tw * fade * (0.25 + 0.75 * e));
      });
    }
  }

  function burst(x, y, n, scale) {
    scale = scale || 1;
    var cols = ['243,220,160', '216,183,119', '255,248,235', '232,170,182'];
    var base = cols[Math.floor(Math.random() * 3)], now = performance.now();
    for (var i = 0; i < n; i++) {
      var a = (i / n) * 6.283 + Math.random() * 0.2, sp = (2.2 + Math.random() * 2.6) * scale;
      parts.push({ x: x, y: y, px: x, py: y, vx: Math.cos(a) * sp, vy: Math.sin(a) * sp, g: 0.035, r: 1.2 + Math.random() * 1.2, col: Math.random() < 0.18 ? cols[3] : base, t0: now, life: 1300 + Math.random() * 900 });
    }
  }

  function rocket(tx, ty, dur, onArrive, delay) {
    delay = delay || 0;
    var rc = R.towerRef ? R.towerRef.getBoundingClientRect() : null;
    var sx = rc ? rc.left + rc.width / 2 : W / 2, sy = rc ? Math.max(rc.top, 0) : H;
    sfxLaunch(dur, delay);
    var arrive = function () { sfxBoom(ty < H * 0.25 ? 0.55 : 1); if (onArrive) onArrive(); };
    rockets.push({ sx: sx, sy: sy, tx: tx, ty: ty, dur: dur, onArrive: arrive, t0: performance.now() + delay });
  }

  function spawnName(cx, cy, fs) {
    var lines = nameLines();
    var off = document.createElement('canvas'); off.width = W; off.height = H;
    var o = off.getContext('2d');
    o.fillStyle = '#fff'; o.textAlign = 'center'; o.textBaseline = 'middle';
    o.font = '400 ' + fs + 'px "Bodoni Moda", serif';
    lines.forEach(function (l, i) { o.fillText(l, cx, cy + (i - (lines.length - 1) / 2) * fs * 1.05); });
    var data = o.getImageData(0, 0, W, H).data;
    var step = Math.max(2, Math.round(fs / 30)), pts = [];
    function collect() { pts = []; for (var y = 0; y < H; y += step) for (var x = 0; x < W; x += step) if (data[(y * W + x) * 4 + 3] > 140) pts.push([x, y]); }
    collect(); while (pts.length > 2600) { step++; collect(); }
    var now = performance.now(), cols = ['243,220,160', '255,241,198', '216,183,119'];
    nameFade = 0;
    nameParts = pts.map(function (pt) {
      var a = Math.random() * 6.283, d = 40 + Math.random() * fs * 1.6;
      return { ox: cx, oy: cy, dx: Math.cos(a) * d, dy: Math.sin(a) * d, tx: pt[0], ty: pt[1], r: Math.max(1.6, step * 0.78), col: cols[Math.floor(Math.random() * 3)], ph: Math.random() * 6.28, fall: 0.5 + Math.random(), t0: now + Math.random() * 120 };
    });
  }

  function tickSanta(now) {
    var el = R.santaRef; if (!el || !W) return;
    if (!motionOn()) { el.style.opacity = 0; santa = null; return; }
    if (!santa) {
      if (!nextSanta) nextSanta = now + 4500;
      if (now < nextSanta) return;
      var ph = state.phase;
      if (ph === 'launch' || ph === 'sky') { nextSanta = now + 3000; return; }
      santaDir = santaDir === 1 ? -1 : 1;
      santa = { t0: now, dur: 9000 + Math.random() * 3500, dir: santaDir, baseY: H * (0.06 + Math.random() * 0.16), amp: 10 + Math.random() * 18, w: el.offsetWidth, h: el.offsetHeight };
    }
    var s = santa, k = (now - s.t0) / s.dur;
    if (k >= 1) { santa = null; el.style.opacity = 0; nextSanta = now + 12000 + Math.random() * 10000; return; }
    var span = W + s.w, w2 = 2.2 * Math.PI;
    var x = s.dir === 1 ? -s.w + span * k : W - span * k;
    var y = s.baseY + Math.sin(k * w2) * s.amp;
    var ang = Math.atan((s.amp * w2 * Math.cos(k * w2)) / span) * s.dir;
    el.style.opacity = 1;
    el.style.transform = 'translate3d(' + x + 'px,' + y + 'px,0) rotate(' + ang + 'rad) scaleX(' + s.dir + ')';
    var rx = s.dir === 1 ? x + s.w * 0.01 : x + s.w * 0.99, ry = y + s.h * 0.78;
    for (var i = 0; i < 3; i++) {
      var gold = Math.random() < 0.6;
      parts.push({ x: rx, y: ry + (Math.random() - 0.5) * 6, px: rx, py: ry, vx: -s.dir * (0.2 + Math.random() * 0.7), vy: (Math.random() - 0.35) * 0.45, g: 0.008, r: 0.7 + Math.random() * 1.4, col: gold ? '243,220,160' : '255,248,235', t0: now, life: 1400 + Math.random() * 1600 });
    }
  }

  /* ---------- audio (all synthesized, no files; keeps working when muted or blocked) ---------- */
  function audio() {
    var AC = window.AudioContext || window.webkitAudioContext; if (!AC) return null;
    if (!actx) {
      try {
        var c = actx = new AC();
        master = c.createGain(); master.gain.value = 0.07;
        var lp = c.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = 3200;
        master.connect(lp); lp.connect(c.destination);
        sfx = c.createGain(); sfx.gain.value = 0.45; sfx.connect(c.destination);
        var len = c.sampleRate * 2; noise = c.createBuffer(1, len, c.sampleRate);
        var d = noise.getChannelData(0); for (var i = 0; i < len; i++) d[i] = Math.random() * 2 - 1;
      } catch (err) { actx = null; return null; }
    }
    if (actx.state === 'suspended') actx.resume().catch(function () {});
    return actx;
  }
  function noiseSrc(t, dur) { var n = actx.createBufferSource(); n.buffer = noise; n.start(t, Math.random() * 1.2); n.stop(t + dur); return n; }
  function sfxLaunch(dur, delay) {
    var c = actx; if (!c || userMuted) return;
    var t = c.currentTime + delay / 1000, d = dur / 1000;
    var bp = c.createBiquadFilter(); bp.type = 'bandpass'; bp.Q.value = 5;
    bp.frequency.setValueAtTime(450, t); bp.frequency.exponentialRampToValueAtTime(2800, t + d);
    var g = c.createGain(); g.gain.setValueAtTime(0, t); g.gain.linearRampToValueAtTime(0.16, t + 0.12); g.gain.linearRampToValueAtTime(0.04, t + d); g.gain.linearRampToValueAtTime(0, t + d + 0.05);
    noiseSrc(t, d + 0.1).connect(bp); bp.connect(g); g.connect(sfx);
  }
  function sfxBoom(k) {
    var c = actx; if (!c || userMuted) return;
    var t = c.currentTime;
    var lp = c.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.setValueAtTime(1400, t); lp.frequency.exponentialRampToValueAtTime(200, t + 1.2);
    var g = c.createGain(); g.gain.setValueAtTime(0.9 * k, t); g.gain.exponentialRampToValueAtTime(0.001, t + 1.6);
    noiseSrc(t, 1.7).connect(lp); lp.connect(g); g.connect(sfx);
    var o = c.createOscillator(), og = c.createGain(); o.frequency.setValueAtTime(80, t); o.frequency.exponentialRampToValueAtTime(32, t + 0.7);
    og.gain.setValueAtTime(0.7 * k, t); og.gain.exponentialRampToValueAtTime(0.001, t + 0.9); o.connect(og); og.connect(sfx); o.start(t); o.stop(t + 1);
    for (var i = 0; i < 16; i++) {
      var t2 = t + 0.25 + Math.random() * 1.3, hp = c.createBiquadFilter(); hp.type = 'highpass'; hp.frequency.value = 2500 + Math.random() * 3000;
      var cg = c.createGain(); cg.gain.setValueAtTime(0.16 * k * Math.random(), t2); cg.gain.exponentialRampToValueAtTime(0.001, t2 + 0.04);
      noiseSrc(t2, 0.05).connect(hp); hp.connect(cg); cg.connect(sfx);
    }
  }
  function startMusic() {
    if (state.music) return;
    var c = audio(); if (!c) return;
    var notes = [659.25, 783.99, 1046.5, 987.77, 783.99, 880, 1046.5, 1318.5, 1174.66, 987.77, 783.99, 880, 0, 659.25, 587.33, 523.25], i = 0;
    function play() {
      var fq = notes[i++ % notes.length]; if (!fq) return;
      var t = c.currentTime;
      [[1, 1], [2, 0.25], [3, 0.08]].forEach(function (hv) {
        var o = c.createOscillator(), g = c.createGain();
        o.type = 'sine'; o.frequency.value = fq * hv[0];
        g.gain.setValueAtTime(0, t); g.gain.linearRampToValueAtTime(hv[1], t + 0.006); g.gain.exponentialRampToValueAtTime(0.0001, t + 1.8);
        o.connect(g); g.connect(master); o.start(t); o.stop(t + 1.9);
      });
    }
    play(); clearInterval(musicTimer); musicTimer = setInterval(play, 420);
    setState({ music: true });
  }

  /* ---------- card fitting ---------- */
  function fitCard() {
    var el = R.cardFitRef, outer = R.cardOuterRef; if (!el || !outer) return;
    el.style.zoom = 1;
    var cs = getComputedStyle(outer), avail = outer.clientHeight - parseFloat(cs.paddingTop) - parseFloat(cs.paddingBottom);
    el.style.zoom = Math.min(1, avail / el.offsetHeight).toFixed(3);
  }

  /* ---------- reveal flow ---------- */
  function failReveal(err) {
    clearTimers(); clearInterval(ambient);
    rockets = []; parts = []; nameParts = []; progress = 0; holding = false; paintedOnce = false;
    errorText = (err && err.userMessage) || 'Something went wrong. Hold again to retry.';
    setState({ phase: 'idle', holding: false, nameLit: false });
    if (R.floorRef) R.floorRef.textContent = errorText;
  }

  function launch() {
    if (navigator.vibrate) navigator.vibrate(30);
    clearTimers();
    var token = ++launchId;
    var req = getName();   // the only moment the recipient is requested from the server
    var ok = function (n) { return token === launchId ? n : null; };
    var bad = function (err) { if (token === launchId) failReveal(err); };

    if (!motionOn()) {
      req.then(function (n) { if (!ok(n)) return; state.name = n.name; state.wishes = n.wishes || []; setState({ phase: 'revealed', holding: false, nameLit: true }); }, bad);
      return;
    }
    setState({ phase: 'launch', holding: false, nameLit: false });
    var cy = metrics().cy;
    rocket(W * 0.22, H * 0.2, 900, function () { burst(W * 0.22, H * 0.2, 50, 0.8); });
    rocket(W / 2, cy, 1150, function () {
      req.then(function (n) {
        if (!ok(n)) return;
        state.name = n.name; state.wishes = n.wishes || [];
        var m = metrics();
        if (!userMuted) startMusic();
        burst(W / 2, cy, 90, 1.25);
        spawnName(W / 2, cy, m.fs);
        setState({ phase: 'sky' });
        later(1400, function () { setState({ nameLit: true }); });
        later(1900, function () { nameFade = performance.now(); });
        later(4100, function () { setState({ phase: 'revealed' }); });
      }, bad);
    }, 350);
    rocket(W * 0.8, H * 0.17, 900, function () { burst(W * 0.8, H * 0.17, 55, 0.85); }, 1500);
    rocket(W * 0.14, H * 0.12, 900, function () { burst(W * 0.14, H * 0.12, 45, 0.7); }, 2700);
    rocket(W * 0.86, H * 0.1, 900, function () { burst(W * 0.86, H * 0.1, 45, 0.7); }, 3700);
    clearInterval(ambient);
    ambient = setInterval(function () {
      if (state.phase !== 'revealed' || !motionOn() || document.hidden) return;
      var left = Math.random() < 0.5, x = W * (left ? 0.06 + Math.random() * 0.16 : 0.78 + Math.random() * 0.16), y = H * (0.08 + Math.random() * 0.2);
      rocket(x, y, 900, function () { burst(x, y, 40, 0.65); });
    }, 4200);
  }

  var handlers = {
    holdStart: function (e) {
      if (state.phase !== 'idle') return;
      e.preventDefault();
      try { e.currentTarget.setPointerCapture(e.pointerId); } catch (_) {}
      holding = true; errorText = ''; paintedOnce = false;
      if (navigator.vibrate) navigator.vibrate(8);
      if (!userMuted) audio();
      setState({ holding: true });
    },
    holdEnd: function () { if (!holding) return; holding = false; setState({ holding: false }); },
    keyDown: function (e) {
      if ((e.key === ' ' || e.key === 'Enter') && !e.repeat) { e.preventDefault(); handlers.holdStart({ preventDefault: function () {}, currentTarget: e.currentTarget }); }
    },
    keyUp: function (e) { if (e.key === ' ' || e.key === 'Enter') handlers.holdEnd(); },
    noMenu: function (e) { e.preventDefault(); },
    replay: function () {
      launchId++; clearTimers(); clearInterval(ambient);
      progress = 0; holding = false; nameParts = []; rockets = []; parts = []; errorText = ''; paintedOnce = false;
      setState({ phase: 'idle', holding: false, nameLit: false });
      window.scrollTo({ top: 0, behavior: reduced ? 'auto' : 'smooth' });
    },
    toggleMusic: function () {
      if (state.music) { userMuted = true; clearInterval(musicTimer); setState({ music: false }); }
      else { userMuted = false; startMusic(); }
    }
  };
  kit.bind(handlers);

  /* ---------- wish list (design markup: roman numerals + italic wishes) ---------- */
  function paintWishes() {
    var box = R.wishBox, ol = R.wishList;
    if (!box || !ol) return;
    var key = state.wishes.join('|');
    if (ol.dataset.key === key) return;
    ol.dataset.key = key;
    ol.textContent = '';
    var nums = ['I', 'II', 'III'];
    state.wishes.forEach(function (w, i) {
      var li = document.createElement('li');
      li.style.cssText = 'display:grid;grid-template-columns:24px minmax(0,1fr) 24px;align-items:baseline;gap:8px';
      var a = document.createElement('span');
      a.style.cssText = "font-family:'Bodoni Moda',serif;font-size:14px;color:#D8B777;text-align:left";
      a.textContent = nums[i] || String(i + 1);
      var b = document.createElement('span');
      b.style.cssText = "font-family:'Cormorant Garamond',serif;font-style:italic;font-size:clamp(18px, 4.8vw, 21px);line-height:1.3;color:#FFF8EB;text-wrap:pretty;overflow-wrap:anywhere";
      b.textContent = w;
      li.appendChild(a); li.appendChild(b); li.appendChild(document.createElement('span'));
      ol.appendChild(li);
    });
    box.hidden = state.wishes.length === 0;
    box.style.display = state.wishes.length ? 'flex' : 'none';
  }

  /* ---------- state -> view ---------- */
  function setState(patch) { Object.assign(state, patch); render(); }

  function render() {
    var ph = state.phase, holdingNow = state.holding, nameLit = state.nameLit;
    var m = metrics(), cardEnd = cfg.ending === 'card', ending = cfg.ending;
    var idle = ph === 'idle', revealed = ph === 'revealed', sky = ph === 'sky', motion = motionOn();
    var longest = Math.max(3, m.lines.reduce(function (a, l) { return Math.max(a, l.length); }, 0));

    paintWishes();
    kit.text({ wishTitle: nameText() ? nameText() + "'S WISH LIST" : '', name: nameLines().join('\n'), holdTitle: holdingNow ? 'Keep holding…' : 'Press & hold to light up the Burj' });
    kit.show({ musicOn: state.music, musicOff: !state.music });
    kit.attrs({ musicLabel: state.music ? 'Pause music' : 'Play music', musicOn: state.music });
    kit.vars({
      rippleA: idle && !holdingNow && motion ? 'ripple 2.4s ease-out infinite' : 'none',
      rippleB: idle && !holdingNow && motion ? 'ripple 2.4s ease-out 1.2s infinite' : 'none',
      iconNudge: idle && !holdingNow && motion ? 'nudge 2.4s ease-in-out infinite' : 'none',
      btnScale: holdingNow ? 'scale(.92)' : 'scale(1)',
      introOpacity: idle ? 1 : 0,
      introTransform: idle ? 'none' : 'translateY(20px)',
      introPE: idle ? 'auto' : 'none',
      skyOpacity: sky || (revealed && !cardEnd) ? 1 : 0,
      cardNameFs: 'min(92px, calc(' + (0.95 / (0.84 * longest) * 100).toFixed(2) + 'cqw))',
      skyTop: m.cy + 'px', skyFs: m.fs + 'px', scrimH: Math.round(m.fs * 3.4 * m.lines.length) + 'px',
      nameTextOpacity: nameLit ? 1 : 0,
      nameTextScale: nameLit ? 'scale(1)' : 'scale(.96)',
      detailsOpacity: revealed && ending === 'sky' ? 1 : 0,
      detailsTransform: revealed && ending === 'sky' ? 'none' : 'translateY(14px)',
      detailsPE: revealed && ending === 'sky' ? 'auto' : 'none',
      sheetOpacity: revealed && ending === 'sheet' ? 1 : 0,
      sheetTransform: revealed && ending === 'sheet' ? 'none' : 'translateY(40px)',
      sheetPE: revealed && ending === 'sheet' ? 'auto' : 'none',
      sheetVisibility: revealed && ending === 'sheet' ? 'visible' : 'hidden',
      cardOpacity: revealed && cardEnd ? 1 : 0,
      cardTransform: revealed && cardEnd ? 'none' : 'translateY(46px) scale(.94)',
      cardPE: revealed && cardEnd ? 'auto' : 'none',
      cardVisibility: revealed && cardEnd ? 'visible' : 'hidden'
    });
    fitCard();
  }

  if (document.fonts) { document.fonts.load('400 100px "Bodoni Moda"').catch(function () {}); document.fonts.ready.then(fitCard); }
  initCanvas();
  render();

  document.addEventListener('visibilitychange', function () {
    if (document.hidden && actx && actx.state === 'running') actx.suspend().catch(function () {});
    else if (!document.hidden && actx && state.music) actx.resume().catch(function () {});
  });
})();
