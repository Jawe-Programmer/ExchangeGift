/* Administrator area behaviour: copy buttons, eye toggles, dialogs, slug helper, local times. No inline scripts (CSP). */
(function () {
  'use strict';

  // ---- local time rendering (timestamps are stored and rendered by the server in UTC) ----
  document.querySelectorAll('time[data-local]').forEach(function (t) {
    var d = new Date(t.getAttribute('datetime'));
    if (isNaN(d)) return;
    t.textContent = t.dataset.local === 'date'
      ? d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
      : d.toLocaleString(undefined, { year: 'numeric', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
    t.title = d.toISOString();
  });

  // ---- toast ----
  var toastEl;
  function toast(msg) {
    if (!toastEl) {
      toastEl = document.createElement('div');
      toastEl.className = 'toast'; toastEl.setAttribute('role', 'status'); toastEl.hidden = true;
      document.body.appendChild(toastEl);
    }
    toastEl.textContent = msg; toastEl.hidden = false;
    clearTimeout(toast.t); toast.t = setTimeout(function () { toastEl.hidden = true; }, 2200);
  }

  // ---- copy ----
  async function copyText(text) {
    try { await navigator.clipboard.writeText(text); return true; }
    catch (e) {
      var ta = document.createElement('textarea');
      ta.value = text; ta.setAttribute('readonly', ''); ta.style.position = 'fixed'; ta.style.opacity = '0';
      document.body.appendChild(ta); ta.select();
      var ok = false; try { ok = document.execCommand('copy'); } catch (e2) {}
      ta.remove(); return ok;
    }
  }
  document.addEventListener('click', async function (e) {
    var btn = e.target.closest('[data-copy]');
    if (btn) {
      var text = btn.dataset.copy;
      var ok = await copyText(text);
      var label = btn.querySelector('.copy-label');
      var original = label ? label.textContent : btn.textContent;
      if (label) label.textContent = ok ? 'Copied!' : 'Copy failed'; else btn.textContent = ok ? 'Copied!' : 'Copy failed';
      toast(ok ? 'Link copied to the clipboard' : 'Could not copy. Select the link and copy it manually.');
      setTimeout(function () { if (label) label.textContent = original; else btn.textContent = original; }, 1800);
      return;
    }
    var all = e.target.closest('[data-copy-all]');
    if (all) {
      var lines = [];
      document.querySelectorAll('[data-link-name]').forEach(function (el) { lines.push(el.dataset.linkName + ': ' + el.dataset.linkUrl); });
      var done = await copyText(lines.join('\n'));
      toast(done ? 'All links copied (' + lines.length + ')' : 'Could not copy the links.');
      return;
    }
    var open = e.target.closest('[data-open-dialog]');
    if (open) {
      var dlg = document.getElementById(open.dataset.openDialog);
      if (dlg && dlg.showModal) dlg.showModal();
      return;
    }
    var close = e.target.closest('[data-close-dialog]');
    if (close) { var d = close.closest('dialog'); if (d) d.close(); }
  });

  // ---- recipient eye toggles (each one independent; the name is fetched from the server on demand) ----
  var MASK = '••••••••';
  document.querySelectorAll('[data-eye]').forEach(function (btn) {
    var out = document.getElementById(btn.dataset.eye);
    var shown = false, cached = null;
    var on = btn.querySelector('.eye-on'), off = btn.querySelector('.eye-off');
    function paint() {
      out.textContent = shown ? cached : MASK;
      out.classList.toggle('masked', !shown);
      btn.setAttribute('aria-pressed', shown ? 'true' : 'false');
      btn.setAttribute('aria-label', (shown ? 'Hide' : 'Show') + ' recipient for ' + btn.dataset.person);
      if (on) on.style.display = shown ? "none" : ""; if (off) off.style.display = shown ? "" : "none";
    }
    btn.addEventListener('click', async function () {
      if (shown) { shown = false; paint(); return; }
      if (cached === null) {
        btn.disabled = true;
        try {
          var res = await fetch(btn.dataset.url, { credentials: 'same-origin', cache: 'no-store', headers: { 'Accept': 'application/json' } });
          if (!res.ok) throw new Error(String(res.status));
          cached = (await res.json()).name;
        } catch (err) { toast('Could not load the recipient.'); btn.disabled = false; return; }
        btn.disabled = false;
      }
      shown = true; paint();
    });
    paint();
  });

  // ---- slug helper (mirrors SlugService.Slugify on the server) ----
  function slugify(s) {
    return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 60).replace(/-+$/g, '');
  }
  var title = document.querySelector('[data-slug-source]'), slug = document.querySelector('[data-slug-target]');
  if (title && slug) {
    var touched = slug.value !== '' && slug.value !== slugify(title.value);
    slug.addEventListener('input', function () { touched = slug.value !== slugify(title.value); });
    title.addEventListener('input', function () {
      if (!touched) slug.value = slugify(title.value);
      var prev = document.querySelector('[data-slug-preview]');
      if (prev) prev.textContent = slug.value || 'your-group';
    });
    slug.addEventListener('input', function () {
      var prev = document.querySelector('[data-slug-preview]');
      if (prev) prev.textContent = slug.value || 'your-group';
    });
  }

  // ---- participant counter on the create/add forms ----
  document.querySelectorAll('[data-count-lines]').forEach(function (ta) {
    var target = document.getElementById(ta.dataset.countLines);
    function count() {
      var seen = {}, n = 0;
      ta.value.split(/[\r\n;]+/).forEach(function (l) { l = l.trim().replace(/\s+/g, ' '); if (l && !seen[l.toLowerCase()]) { seen[l.toLowerCase()] = 1; n++; } });
      if (target) target.textContent = n + (n === 1 ? ' participant' : ' participants');
    }
    ta.addEventListener('input', count); count();
  });

  // ---- confirmation dialogs that require typing a word ----
  document.querySelectorAll('[data-confirm-word]').forEach(function (input) {
    var btn = document.getElementById(input.dataset.confirmWord);
    function check() { btn.disabled = input.value.trim().toLowerCase() !== input.dataset.expect.toLowerCase(); }
    input.addEventListener('input', check); check();
  });

  // ---- disable submit buttons after the first click to prevent double submits ----
  document.querySelectorAll('form[data-once]').forEach(function (f) {
    f.addEventListener('submit', function () {
      f.querySelectorAll('button[type=submit]').forEach(function (b) { setTimeout(function () { b.disabled = true; }, 0); });
    });
  });
})();
