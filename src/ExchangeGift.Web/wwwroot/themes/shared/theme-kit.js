/*
 * Shared helpers for the participant themes.
 *  - ThemeKit.create(root)         binds data-ref / x-t / x-if / data-on-* markers produced from the approved designs
 *  - ThemeKit.createRevealer(root) asks the server for the recipient (idempotent, de-duplicated, retried on network loss)
 * The recipient name is never present in the page until the participant starts the reveal.
 */
(function () {
  'use strict';

  function create(root) {
    var refs = {}, texts = {}, ifs = {}, attrBindings = [];
    root.querySelectorAll('[data-ref]').forEach(function (el) { refs[el.dataset.ref] = el; });
    root.querySelectorAll('x-t').forEach(function (el) { (texts[el.dataset.t] = texts[el.dataset.t] || []).push(el); });
    root.querySelectorAll('x-if').forEach(function (el) { (ifs[el.dataset['if']] = ifs[el.dataset['if']] || []).push(el); });
    root.querySelectorAll('*').forEach(function (el) {
      Array.prototype.forEach.call(el.attributes, function (a) {
        if (a.name.indexOf('data-attr-') === 0) attrBindings.push({ el: el, attr: a.name.slice(10), key: a.value });
      });
    });
    var lastText = {}, lastVars = {};

    return {
      refs: refs,
      /** Sets the text of every <x-t data-t="key"> element. */
      text: function (map) {
        Object.keys(map).forEach(function (k) {
          var v = map[k] == null ? '' : String(map[k]);
          if (lastText[k] === v) return;
          lastText[k] = v;
          (texts[k] || []).forEach(function (el) { el.textContent = v; });
        });
      },
      /** Shows or hides every <x-if data-if="key"> region. */
      show: function (map) {
        Object.keys(map).forEach(function (k) { (ifs[k] || []).forEach(function (el) { el.hidden = !map[k]; }); });
      },
      /** Sets CSS custom properties (--b-key) consumed by the design's inline styles. */
      vars: function (map) {
        Object.keys(map).forEach(function (k) {
          var v = String(map[k]);
          if (lastVars[k] === v) return;
          lastVars[k] = v;
          root.style.setProperty('--b-' + k, v);
        });
      },
      /** Applies attribute bindings (data-attr-aria-label="key" etc.). */
      attrs: function (map) {
        attrBindings.forEach(function (b) {
          if (!(b.key in map)) return;
          var v = map[b.key];
          if (v === false || v == null) b.el.removeAttribute(b.attr);
          else b.el.setAttribute(b.attr, v === true ? 'true' : String(v));
        });
      },
      /** Wires data-on-<event>="handlerName" attributes to the handlers object. */
      bind: function (handlers) {
        root.querySelectorAll('*').forEach(function (el) {
          Array.prototype.slice.call(el.attributes).forEach(function (a) {
            if (a.name.indexOf('data-on-') !== 0) return;
            var fn = handlers[a.value];
            if (fn) el.addEventListener(a.name.slice(8), fn);
          });
        });
      }
    };
  }

  function sleep(ms) { return new Promise(function (r) { setTimeout(r, ms); }); }

  function createRevealer(root) {
    var url = root.dataset.revealUrl;
    var token = root.dataset.token;
    var preview = root.dataset.preview === '1';
    var cached = null;     // recipient (name + wishes) once known (kept only in memory)
    var inflight = null;   // single shared request, so repeated taps cannot double-submit

    function fail(message) { var e = new Error(message); e.userMessage = message; return e; }

    async function attempt() {
      var ctl = new AbortController();
      var timer = setTimeout(function () { ctl.abort(); }, 15000);
      try {
        var res = await fetch(url, {
          method: 'POST',
          credentials: 'same-origin',
          cache: 'no-store',
          headers: { 'RequestVerificationToken': token || '', 'Accept': 'application/json' },
          signal: ctl.signal
        });
        if (res.status === 429) throw fail('Too many tries. Please wait a minute and try again.');
        if (res.status === 404) throw fail('This link is not available any more.');
        if (res.status >= 500) { var e5 = new Error('server'); e5.retry = true; throw e5; }
        if (!res.ok) throw fail('Something went wrong. Please try again.');
        var data = await res.json();
        if (!data || typeof data.name !== 'string' || !data.name) throw fail('Something went wrong. Please try again.');
        return { name: data.name, wishes: Array.isArray(data.wishes) ? data.wishes.slice(0, 3).map(String) : [] };
      } catch (err) {
        if (err && err.userMessage) throw err;
        err.retry = true; // network failure, timeout, or 5xx: worth retrying
        throw err;
      } finally { clearTimeout(timer); }
    }

    async function request() {
      if (preview) { await sleep(250); return { name: 'SAMPLE NAME', wishes: ['A cozy cashmere scarf', 'A good book to read', 'Something sweet'] }; }
      var last;
      for (var i = 0; i < 3; i++) {
        try { return await attempt(); }
        catch (err) {
          last = err;
          if (!err.retry) break;
          await sleep(500 * (i + 1));
        }
      }
      throw last && last.userMessage ? last : fail('Could not reach the server. Check your connection and try again.');
    }

    return function getName() {
      if (cached) return Promise.resolve(cached);
      if (!inflight) {
        inflight = request().then(function (n) { cached = n; inflight = null; return n; },
                                  function (e) { inflight = null; throw e; });
      }
      return inflight;
    };
  }

  window.ThemeKit = { create: create, createRevealer: createRevealer };
})();
