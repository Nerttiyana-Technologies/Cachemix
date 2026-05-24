/* Cachemix dashboard — front-end logic.
   Polls the /api/snapshot endpoint, renders the KPI strip, hit-ratio chart,
   key grid and event feed, and manages the dark/light theme. No dependencies. */
(function () {
  'use strict';

  var POLL_MS = 2000;
  var DIFF_POLL_MS = 15000;
  var MAX_POINTS = 60;
  var MAX_ROWS = 500;

  var state = {
    lastSnapshot: null,
    hitHistory: [],
    sortKey: 'key',
    sortAsc: true,
    filter: '',
    tagFilter: '',
    diffStarted: false
  };

  /* ---- small helpers ---------------------------------------------------- */

  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) { e.className = cls; }
    if (text !== undefined && text !== null) { e.textContent = String(text); }
    return e;
  }

  function byId(id) { return document.getElementById(id); }

  function fmtNum(n) {
    return (n == null) ? '0' : Number(n).toLocaleString();
  }

  function fmtBytes(n) {
    if (n == null) { return '—'; }
    if (n < 1024) { return n + ' B'; }
    if (n < 1048576) { return (n / 1024).toFixed(1) + ' KB'; }
    if (n < 1073741824) { return (n / 1048576).toFixed(1) + ' MB'; }
    return (n / 1073741824).toFixed(2) + ' GB';
  }

  function fmtPct(ratio) {
    return (ratio * 100).toFixed(1) + '%';
  }

  function fmtDuration(ms) {
    var s = Math.floor(ms / 1000);
    if (s < 60) { return s + 's'; }
    var m = Math.floor(s / 60);
    if (m < 60) { return m + 'm ' + (s % 60) + 's'; }
    var h = Math.floor(m / 60);
    if (h < 24) { return h + 'h ' + (m % 60) + 'm'; }
    return Math.floor(h / 24) + 'd ' + (h % 24) + 'h';
  }

  function fmtAgo(iso) {
    if (!iso) { return ''; }
    var diff = Date.now() - new Date(iso).getTime();
    if (diff < 1000) { return 'just now'; }
    return fmtDuration(diff) + ' ago';
  }

  function fmtExpiry(iso, inferred) {
    if (!iso) { return { text: '—', cls: '' }; }
    var diff = new Date(iso).getTime() - Date.now();
    if (diff <= 0) { return { text: 'expired', cls: 'cmx-expired' }; }
    var text = (inferred ? '~' : '') + fmtDuration(diff);
    var cls = diff < 60000 ? 'cmx-soon' : (inferred ? 'cmx-inferred' : '');
    return { text: text, cls: cls };
  }

  function shortType(typeName) {
    if (!typeName) { return '—'; }
    var s = typeName.split(',')[0];
    var tick = s.indexOf('`');
    if (tick >= 0) { s = s.substring(0, tick); }
    var bracket = s.indexOf('[');
    if (bracket >= 0) { s = s.substring(0, bracket); }
    var dot = s.lastIndexOf('.');
    if (dot >= 0) { s = s.substring(dot + 1); }
    return s || '—';
  }

  /* ---- theme ------------------------------------------------------------ */

  function applyTheme(theme) {
    if (theme === 'dark' || theme === 'light') {
      document.documentElement.setAttribute('data-theme', theme);
    }
  }

  function currentTheme() {
    var explicit = document.documentElement.getAttribute('data-theme');
    if (explicit) { return explicit; }
    return (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches)
      ? 'dark' : 'light';
  }

  function initTheme() {
    var saved = null;
    try { saved = localStorage.getItem('cachemix-theme'); } catch (e) { /* ignore */ }
    if (saved === 'dark' || saved === 'light') { applyTheme(saved); }
  }

  function toggleTheme() {
    var next = currentTheme() === 'dark' ? 'light' : 'dark';
    applyTheme(next);
    try { localStorage.setItem('cachemix-theme', next); } catch (e) { /* ignore */ }
    drawChart();
  }

  /* ---- rendering -------------------------------------------------------- */

  function setConnected(ok) {
    var s = byId('cmx-status');
    if (!s) { return; }
    s.textContent = ok ? 'connected' : 'disconnected';
    s.className = 'cmx-status ' + (ok ? 'cmx-status--on' : 'cmx-status--off');
  }

  function aggregate(caches) {
    var a = { keys: 0, hits: 0, misses: 0, evictions: 0, stampedes: 0, size: 0, hasSize: false };
    (caches || []).forEach(function (c) {
      a.keys += c.entryCount || 0;
      a.hits += c.totalHits || 0;
      a.misses += c.totalMisses || 0;
      a.evictions += c.totalEvictions || 0;
      a.stampedes += c.stampedeIncidents || 0;
      if (c.estimatedSizeBytes != null) { a.size += c.estimatedSizeBytes; a.hasSize = true; }
    });
    return a;
  }

  function renderKpis(snapshot) {
    var caches = snapshot.caches || [];
    var a = aggregate(caches);
    var ratio = (a.hits + a.misses) > 0 ? a.hits / (a.hits + a.misses) : 0;
    var health = snapshot.health;

    var cards = [
      { label: 'Tracked keys', value: fmtNum(a.keys),
        sub: caches.length + ' cache' + (caches.length === 1 ? '' : 's') },
      { label: 'Hit ratio', value: fmtPct(ratio),
        sub: fmtNum(a.hits) + ' hits / ' + fmtNum(a.misses) + ' misses' },
      { label: 'Estimated size', value: a.hasSize ? fmtBytes(a.size) : '—',
        sub: 'across tracked entries' },
      { label: 'Evictions', value: fmtNum(a.evictions), sub: 'since start' },
      { label: 'Stampedes', value: fmtNum(a.stampedes),
        sub: a.stampedes > 0 ? 'concurrent recomputes' : 'none detected' },
      { label: 'Health', value: health ? health.grade : '—',
        sub: health ? ('score ' + health.score) : '' }
    ];

    var host = byId('cmx-kpis');
    host.textContent = '';
    cards.forEach(function (c) {
      var card = el('div', 'cmx-kpi');
      card.appendChild(el('div', 'cmx-kpi-label', c.label));
      card.appendChild(el('div', 'cmx-kpi-value', c.value));
      card.appendChild(el('div', 'cmx-kpi-sub', c.sub));
      host.appendChild(card);
    });
  }

  function gradeColor(grade) {
    var cs = getComputedStyle(document.documentElement);
    if (grade === 'A' || grade === 'B') { return cs.getPropertyValue('--cmx-ok').trim(); }
    if (grade === 'C' || grade === 'D') { return cs.getPropertyValue('--cmx-warn').trim(); }
    return cs.getPropertyValue('--cmx-danger').trim();
  }

  function renderHealth(health) {
    var host = byId('cmx-health');
    host.textContent = '';
    if (!health) { return; }

    var grade = el('div', 'cmx-grade');
    var badge = el('div', 'cmx-grade-badge', health.grade);
    badge.style.background = gradeColor(health.grade);
    grade.appendChild(badge);
    var meta = el('div');
    meta.appendChild(el('div', null, 'Grade ' + health.grade));
    meta.appendChild(el('div', 'cmx-grade-score', 'Score ' + health.score + ' / 100'));
    grade.appendChild(meta);
    host.appendChild(grade);

    (health.findings || []).forEach(function (f) {
      var sev = (f.severity || 'Info').toLowerCase();
      var item = el('div', 'cmx-finding cmx-sev-' + sev);
      item.appendChild(el('div', 'cmx-finding-title', f.title));
      item.appendChild(el('div', 'cmx-finding-detail', f.detail));
      host.appendChild(item);
    });
  }

  function pushChart(snapshot) {
    var a = aggregate(snapshot.caches);
    var ratio = (a.hits + a.misses) > 0 ? (a.hits / (a.hits + a.misses)) * 100 : 0;
    state.hitHistory.push(ratio);
    while (state.hitHistory.length > MAX_POINTS) { state.hitHistory.shift(); }
  }

  function drawChart() {
    var canvas = byId('cmx-chart');
    if (!canvas || !canvas.getContext) { return; }
    var ctx = canvas.getContext('2d');
    var w = canvas.width, h = canvas.height;
    ctx.clearRect(0, 0, w, h);

    var cs = getComputedStyle(document.documentElement);
    var accent = cs.getPropertyValue('--cmx-accent').trim() || '#2563eb';
    var gridColor = cs.getPropertyValue('--cmx-border').trim() || '#e4e7eb';
    var muted = cs.getPropertyValue('--cmx-muted').trim() || '#6b7280';

    var padL = 38, padR = 10, padT = 10, padB = 8;
    var plotW = w - padL - padR, plotH = h - padT - padB;

    ctx.font = '11px sans-serif';
    ctx.textBaseline = 'middle';
    for (var p = 0; p <= 100; p += 25) {
      var gy = padT + plotH - (p / 100) * plotH;
      ctx.strokeStyle = gridColor;
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(padL, gy);
      ctx.lineTo(w - padR, gy);
      ctx.stroke();
      ctx.fillStyle = muted;
      ctx.fillText(p + '%', 6, gy);
    }

    var data = state.hitHistory;
    if (data.length < 2) { return; }
    var stepX = plotW / (MAX_POINTS - 1);
    var startIdx = MAX_POINTS - data.length;
    ctx.strokeStyle = accent;
    ctx.lineWidth = 2;
    ctx.beginPath();
    data.forEach(function (v, i) {
      var x = padL + (startIdx + i) * stepX;
      var y = padT + plotH - (v / 100) * plotH;
      if (i === 0) { ctx.moveTo(x, y); } else { ctx.lineTo(x, y); }
    });
    ctx.stroke();
  }

  function comparator(key, asc) {
    var dir = asc ? 1 : -1;
    return function (a, b) {
      var x = a[key], y = b[key];
      if (x == null && y == null) { return 0; }
      if (x == null) { return 1; }
      if (y == null) { return -1; }
      if (typeof x === 'number' && typeof y === 'number') { return (x - y) * dir; }
      return String(x).localeCompare(String(y)) * dir;
    };
  }

  function updateSortHeaders() {
    document.querySelectorAll('.cmx-table th[data-sort]').forEach(function (th) {
      th.classList.remove('cmx-sorted', 'cmx-sorted-desc');
      if (th.getAttribute('data-sort') === state.sortKey) {
        th.classList.add(state.sortAsc ? 'cmx-sorted' : 'cmx-sorted-desc');
      }
    });
  }

  function heatClass(hits, maxHits) {
    if (maxHits <= 0 || hits <= 0) { return 'cmx-heat-0'; }
    var ratio = hits / maxHits;
    if (ratio > 0.66) { return 'cmx-heat-3'; }
    if (ratio > 0.33) { return 'cmx-heat-2'; }
    return 'cmx-heat-1';
  }

  function evictKey(cache, key) {
    if (!window.confirm('Evict key "' + key + '" from cache "' + cache + '"?')) {
      return;
    }
    fetch('api/evict', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Cachemix-Action': '1' },
      body: JSON.stringify({ cache: cache, key: key })
    }).then(function (res) {
      if (res.ok) { poll(); }
    }).catch(function () {
      /* A failed eviction simply surfaces on the next poll. */
    });
  }

  /* ---- value inspector ------------------------------------------------- */

  function inspectorNote(status) {
    switch (status) {
      case 'NotFound': return 'This key is no longer in the cache.';
      case 'Redacted': return 'This value is hidden by the dashboard redaction policy.';
      case 'Disabled': return 'Value capture is turned off (CaptureValues = Never).';
      case 'Unsupported': return 'This cache cannot read values back.';
      default: return 'No value is available for this key.';
    }
  }

  function renderInspector(result) {
    var body = byId('cmx-inspector-body');
    var meta = byId('cmx-inspector-meta');
    if (!result || result.status !== 'Available') {
      body.classList.add('cmx-inspector-note');
      body.textContent = inspectorNote(result ? result.status : null);
      meta.textContent = '';
      return;
    }
    body.classList.remove('cmx-inspector-note');
    body.textContent = result.valueText || '';
    var bits = [];
    if (result.valueTypeName) { bits.push(result.valueTypeName); }
    if (result.sizeBytes != null) { bits.push(fmtBytes(result.sizeBytes)); }
    if (result.truncated) { bits.push('truncated to 16 KB'); }
    meta.textContent = bits.join('  ·  ');
  }

  function openInspector(cache, key) {
    var dialog = byId('cmx-inspector');
    if (!dialog) { return; }
    byId('cmx-inspector-key').textContent = key;
    byId('cmx-inspector-meta').textContent = cache;
    var body = byId('cmx-inspector-body');
    body.classList.add('cmx-inspector-note');
    body.textContent = 'Loading…';

    if (typeof dialog.showModal === 'function') {
      if (!dialog.open) { dialog.showModal(); }
    } else {
      dialog.setAttribute('open', '');
    }

    fetch('api/value?cache=' + encodeURIComponent(cache) + '&key=' + encodeURIComponent(key),
          { headers: { 'Accept': 'application/json' }, cache: 'no-store' })
      .then(function (res) {
        if (!res.ok) { throw new Error('HTTP ' + res.status); }
        return res.json();
      })
      .then(renderInspector)
      .catch(function () {
        body.classList.add('cmx-inspector-note');
        body.textContent = 'Could not read this value.';
        byId('cmx-inspector-meta').textContent = '';
      });
  }

  function closeInspector() {
    var dialog = byId('cmx-inspector');
    if (!dialog) { return; }
    if (typeof dialog.close === 'function' && dialog.open) {
      dialog.close();
    } else {
      dialog.removeAttribute('open');
    }
  }

  /* ---- tag explorer ---------------------------------------------------- */

  function collectTags(entries) {
    var map = {};
    (entries || []).forEach(function (e) {
      (e.tags || []).forEach(function (tag) {
        if (!tag) { return; }
        var rec = map[tag];
        if (!rec) { rec = map[tag] = { name: tag, count: 0, caches: {} }; }
        rec.count += 1;
        rec.caches[e.cacheName] = true;
      });
    });
    return Object.keys(map).map(function (k) {
      var rec = map[k];
      return { name: rec.name, count: rec.count, caches: Object.keys(rec.caches) };
    }).sort(function (a, b) {
      return (b.count - a.count) || a.name.localeCompare(b.name);
    });
  }

  function evictTag(tag) {
    var label = tag.count + ' entr' + (tag.count === 1 ? 'y' : 'ies');
    if (!window.confirm('Evict all ' + label + ' tagged "' + tag.name + '"?')) {
      return;
    }
    Promise.all(tag.caches.map(function (cache) {
      return fetch('api/evict-tag', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Cachemix-Action': '1' },
        body: JSON.stringify({ cache: cache, tag: tag.name })
      });
    })).then(function () {
      poll();
    }).catch(function () {
      /* A failed invalidation simply surfaces on the next poll. */
    });
  }

  function renderTags() {
    var snapshot = state.lastSnapshot;
    var panel = byId('cmx-tags-panel');
    var host = byId('cmx-tags');
    var title = byId('cmx-tags-title');
    var clearBtn = byId('cmx-tags-clear');
    if (!panel || !host) { return; }
    host.textContent = '';

    var tags = snapshot ? collectTags(snapshot.entries) : [];
    panel.hidden = tags.length === 0;

    // Drop a stale filter pointing at a tag that no longer exists.
    var stillPresent = tags.some(function (t) { return t.name === state.tagFilter; });
    if (state.tagFilter && !stillPresent) { state.tagFilter = ''; }

    title.textContent = 'Tags (' + tags.length + ')';
    clearBtn.hidden = !state.tagFilter;

    var allowEvict = snapshot && snapshot.destructiveActionsAllowed === true;

    tags.forEach(function (tag) {
      var item = el('div', 'cmx-tag' + (tag.name === state.tagFilter ? ' cmx-tag--active' : ''));

      var pick = el('button', 'cmx-tag-pick');
      pick.type = 'button';
      pick.setAttribute('aria-pressed', tag.name === state.tagFilter ? 'true' : 'false');
      pick.appendChild(el('span', null, tag.name));
      pick.appendChild(el('span', 'cmx-tag-count', tag.count));
      pick.addEventListener('click', function () {
        state.tagFilter = (state.tagFilter === tag.name) ? '' : tag.name;
        renderTags();
        renderGrid();
      });
      item.appendChild(pick);

      if (allowEvict) {
        var evictBtn = el('button', 'cmx-tag-evict', '✕');
        evictBtn.type = 'button';
        evictBtn.title = 'Evict all entries tagged "' + tag.name + '"';
        evictBtn.setAttribute('aria-label', 'Evict all entries tagged ' + tag.name);
        evictBtn.addEventListener('click', function () { evictTag(tag); });
        item.appendChild(evictBtn);
      }

      host.appendChild(item);
    });
  }

  function rowFor(entry, maxHits, allowEvict) {
    var tr = document.createElement('tr');

    var keyCell = el('td', 'cmx-key');
    keyCell.appendChild(el('span', null, entry.key));
    (entry.tags || []).forEach(function (tag) {
      var badge = el('span', 'cmx-badge', tag);
      badge.style.marginLeft = '6px';
      keyCell.appendChild(badge);
    });
    tr.appendChild(keyCell);

    tr.appendChild(el('td', null, entry.cacheName));
    tr.appendChild(el('td', null, shortType(entry.valueTypeName)));
    tr.appendChild(el('td', 'cmx-num', fmtBytes(entry.estimatedSizeBytes)));

    var hits = entry.hitCount || 0;
    tr.appendChild(el('td', 'cmx-num ' + heatClass(hits, maxHits), fmtNum(hits)));

    var expiry = fmtExpiry(entry.absoluteExpirationUtc, entry.expirationInferred);
    tr.appendChild(el('td', expiry.cls, expiry.text));

    var actionCell = el('td', 'cmx-actions');

    var inspectBtn = el('button', 'cmx-inspect', 'Inspect');
    inspectBtn.type = 'button';
    inspectBtn.addEventListener('click', function () {
      openInspector(entry.cacheName, entry.key);
    });
    actionCell.appendChild(inspectBtn);

    if (allowEvict) {
      var evictBtn = el('button', 'cmx-evict', 'Evict');
      evictBtn.type = 'button';
      evictBtn.addEventListener('click', function () {
        evictKey(entry.cacheName, entry.key);
      });
      actionCell.appendChild(evictBtn);
    }
    tr.appendChild(actionCell);
    return tr;
  }

  function renderGrid() {
    var snapshot = state.lastSnapshot;
    var tbody = byId('cmx-rows');
    var empty = byId('cmx-keys-empty');
    var title = byId('cmx-keys-title');
    tbody.textContent = '';
    if (!snapshot) { return; }

    var entries = (snapshot.entries || []).slice();
    if (state.tagFilter) {
      entries = entries.filter(function (e) {
        return (e.tags || []).indexOf(state.tagFilter) >= 0;
      });
    }
    if (state.filter) {
      entries = entries.filter(function (e) {
        return (e.key || '').toLowerCase().indexOf(state.filter) >= 0;
      });
    }
    entries.sort(comparator(state.sortKey, state.sortAsc));

    var total = entries.length;
    var shown = entries.slice(0, MAX_ROWS);
    title.textContent = 'Keys (' + total +
      (total > MAX_ROWS ? ', showing ' + MAX_ROWS : '') + ')';
    empty.hidden = total !== 0;

    var maxHits = 0;
    shown.forEach(function (e) {
      if ((e.hitCount || 0) > maxHits) { maxHits = e.hitCount || 0; }
    });
    var allowEvict = snapshot.destructiveActionsAllowed === true;
    shown.forEach(function (e) { tbody.appendChild(rowFor(e, maxHits, allowEvict)); });
    updateSortHeaders();
  }

  function renderEvents(events) {
    var host = byId('cmx-events');
    var empty = byId('cmx-events-empty');
    host.textContent = '';
    events = events || [];
    empty.hidden = events.length !== 0;

    events.slice(0, 60).forEach(function (ev) {
      var li = el('li', 'cmx-event');
      var kind = ev.kind || '';
      li.appendChild(el('span', 'cmx-event-kind cmx-k-' + kind.toLowerCase(), kind));
      li.appendChild(el('span', 'cmx-event-key cmx-key', ev.key));

      var meta = ev.cacheName || '';
      if (kind === 'Miss' && ev.durationMs != null) {
        meta += '  ·  ' + ev.durationMs.toFixed(1) + ' ms';
      }
      if (kind === 'Eviction' && ev.evictionReason) {
        meta += '  ·  ' + ev.evictionReason;
      }
      meta += '  ·  ' + fmtAgo(ev.timestampUtc);
      li.appendChild(el('span', 'cmx-event-meta', meta));
      host.appendChild(li);
    });
  }

  /* ---- instance diff --------------------------------------------------- */

  function renderDiff(report) {
    var instancesHost = byId('cmx-diff-instances');
    var table = byId('cmx-diff-table');
    var head = byId('cmx-diff-head');
    var rowsHost = byId('cmx-diff-rows');
    var empty = byId('cmx-diff-empty');
    var title = byId('cmx-diff-title');
    if (!instancesHost || !table) { return; }
    instancesHost.textContent = '';
    head.textContent = '';
    rowsHost.textContent = '';

    var instances = (report && report.instances) || [];
    instances.forEach(function (inst) {
      var chip = el('span', 'cmx-diff-instance');
      chip.appendChild(el('span',
        'cmx-diff-dot ' + (inst.reachable ? 'cmx-diff-dot--ok' : 'cmx-diff-dot--bad')));
      chip.appendChild(el('span', null, inst.name));
      chip.appendChild(el('span', 'cmx-diff-instance-meta',
        inst.reachable ? (fmtNum(inst.entryCount) + ' keys') : (inst.error || 'unreachable')));
      if (!inst.reachable && inst.error) { chip.title = inst.error; }
      instancesHost.appendChild(chip);
    });

    var reachable = instances.filter(function (i) { return i.reachable; });
    var divergences = (report && report.divergences) || [];
    title.textContent = 'Instance diff (' + divergences.length + ')';

    if (reachable.length < 2) {
      table.hidden = true;
      empty.hidden = false;
      empty.textContent = 'Need at least two reachable instances to compare.';
      return;
    }
    if (divergences.length === 0) {
      table.hidden = true;
      empty.hidden = false;
      empty.textContent = 'All reachable instances hold identical cache state.';
      return;
    }

    empty.hidden = true;
    table.hidden = false;

    head.appendChild(el('th', null, 'Key'));
    head.appendChild(el('th', null, 'Cache'));
    head.appendChild(el('th', null, 'Kind'));
    reachable.forEach(function (inst) {
      head.appendChild(el('th', null, inst.name));
    });

    divergences.forEach(function (d) {
      var tr = document.createElement('tr');
      tr.appendChild(el('td', 'cmx-key', d.key));
      tr.appendChild(el('td', null, d.cacheName));

      var kindCell = el('td');
      kindCell.appendChild(el('span',
        'cmx-diff-kind cmx-diff-kind--' + String(d.kind || '').toLowerCase(), d.kind));
      tr.appendChild(kindCell);

      // The first present cell is the baseline for highlighting divergence.
      var baseType = null, baseSize = null, sawBase = false;
      (d.cells || []).forEach(function (c) {
        if (c.present && !sawBase) {
          baseType = c.valueTypeName || null;
          baseSize = (c.sizeBytes == null) ? null : c.sizeBytes;
          sawBase = true;
        }
      });

      (d.cells || []).forEach(function (cell) {
        var td = el('td');
        if (!cell.present) {
          td.className = 'cmx-diff-cell--absent';
          td.textContent = 'absent';
        } else {
          td.textContent = shortType(cell.valueTypeName) + '  ' + fmtBytes(cell.sizeBytes);
          var cellType = cell.valueTypeName || null;
          var cellSize = (cell.sizeBytes == null) ? null : cell.sizeBytes;
          if (cellType !== baseType || cellSize !== baseSize) {
            td.className = 'cmx-diff-cell--differ';
          }
        }
        tr.appendChild(td);
      });
      rowsHost.appendChild(tr);
    });
  }

  function loadDiff() {
    fetch('api/diff', { headers: { 'Accept': 'application/json' }, cache: 'no-store' })
      .then(function (res) {
        if (!res.ok) { throw new Error('HTTP ' + res.status); }
        return res.json();
      })
      .then(renderDiff)
      .catch(function () {
        var empty = byId('cmx-diff-empty');
        var table = byId('cmx-diff-table');
        if (table) { table.hidden = true; }
        if (empty) { empty.hidden = false; empty.textContent = 'Could not load the instance diff.'; }
      });
  }

  /* ---- polling --------------------------------------------------------- */

  function poll() {
    fetch('api/snapshot', { headers: { 'Accept': 'application/json' }, cache: 'no-store' })
      .then(function (res) {
        if (!res.ok) { throw new Error('HTTP ' + res.status); }
        return res.json();
      })
      .then(function (data) {
        state.lastSnapshot = data;
        setConnected(true);
        renderKpis(data);
        renderHealth(data.health);
        pushChart(data);
        drawChart();
        renderTags();
        renderGrid();
        renderEvents(data.recentEvents);

        if (data.multiInstanceEnabled && !state.diffStarted) {
          state.diffStarted = true;
          var diffPanel = byId('cmx-diff-panel');
          if (diffPanel) { diffPanel.hidden = false; }
          loadDiff();
          setInterval(loadDiff, DIFF_POLL_MS);
        }
        var updated = byId('cmx-updated');
        if (updated) { updated.textContent = 'Updated ' + new Date().toLocaleTimeString(); }
      })
      .catch(function () {
        setConnected(false);
      });
  }

  /* ---- wiring ---------------------------------------------------------- */

  function init() {
    initTheme();

    byId('cmx-theme').addEventListener('click', toggleTheme);

    var search = byId('cmx-search');
    search.addEventListener('input', function () {
      state.filter = search.value.trim().toLowerCase();
      renderGrid();
    });

    document.querySelectorAll('.cmx-table th[data-sort]').forEach(function (th) {
      var key = th.getAttribute('data-sort');
      function sort() {
        if (state.sortKey === key) {
          state.sortAsc = !state.sortAsc;
        } else {
          state.sortKey = key;
          state.sortAsc = true;
        }
        renderGrid();
      }
      th.addEventListener('click', sort);
      th.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); sort(); }
      });
    });

    var tagsClear = byId('cmx-tags-clear');
    if (tagsClear) {
      tagsClear.addEventListener('click', function () {
        state.tagFilter = '';
        renderTags();
        renderGrid();
      });
    }

    var diffRefresh = byId('cmx-diff-refresh');
    if (diffRefresh) {
      diffRefresh.addEventListener('click', loadDiff);
    }

    var inspector = byId('cmx-inspector');
    if (inspector) {
      byId('cmx-inspector-close').addEventListener('click', closeInspector);
      // A click on the backdrop (the dialog element itself) dismisses it.
      inspector.addEventListener('click', function (e) {
        if (e.target === inspector) { closeInspector(); }
      });
    }

    if (window.matchMedia) {
      window.matchMedia('(prefers-color-scheme: dark)')
        .addEventListener('change', function () { drawChart(); });
    }

    poll();
    setInterval(poll, POLL_MS);
  }

  init();
})();
