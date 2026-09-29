(function () {
	'use strict';
	var root = document.body.getAttribute('data-root') || './';
	var html = document.documentElement;

	// Theme toggle: system by default, then remembered.
	function effectiveDark() {
		var t = html.dataset.theme;
		if (t) return t === 'dark';
		return window.matchMedia && matchMedia('(prefers-color-scheme: dark)').matches;
	}
	function syncThemedImages() {
		var dark = effectiveDark();
		document.querySelectorAll('img.theme-aware').forEach(function (img) {
			var src = img.getAttribute(dark ? 'data-dark' : 'data-light');
			if (src && img.getAttribute('src') !== src) img.setAttribute('src', src);
		});
	}
	var toggle = document.querySelector('.theme-toggle');
	if (toggle) toggle.addEventListener('click', function () {
		var next = effectiveDark() ? 'light' : 'dark';
		html.dataset.theme = next;
		try { localStorage.setItem('cs-theme', next); } catch (e) {}
		syncThemedImages();
	});
	if (window.matchMedia) matchMedia('(prefers-color-scheme: dark)').addEventListener('change', syncThemedImages);
	syncThemedImages();

	// Mobile navigation
	var menuToggle = document.querySelector('.menu-toggle');
	if (menuToggle) menuToggle.addEventListener('click', function () {
		var open = document.body.classList.toggle('nav-open');
		menuToggle.setAttribute('aria-expanded', open ? 'true' : 'false');
	});
	document.addEventListener('click', function (e) {
		if (document.body.classList.contains('nav-open') && !e.target.closest('.sidebar') && !e.target.closest('.menu-toggle'))
			document.body.classList.remove('nav-open');
	});
	var active = document.querySelector('.sidebar-nav a.active');
	if (active) active.scrollIntoView({ block: 'center' });

	// Syntax highlighting for .nss code (class lang-shell in the docs)
	var KEYWORDS = /^(menu|item|separator|sep|modify|remove|import|theme|settings|if|true|false|null|default|auto|lang|loc|and|or|not)$/i;
	function escapeHtml(s) { return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;'); }
	function highlight(src) {
		var re = /(\/\/[^\n]*|\/\*[\s\S]*?\*\/)|('(?:[^'\\]|\\.)*'|"(?:[^"\\]|\\.)*")|(#[0-9a-fA-F]{8}\b|#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b)|(\\u[0-9a-fA-F]{4})|(\b\d+(?:\.\d+)?\b)|(@?[A-Za-z_][\w.]*)(?=\s*\()|([A-Za-z_][\w.]*)(?=\s*=(?!=))|([A-Za-z_][\w]*)/g;
		var out = '', last = 0, m;
		while ((m = re.exec(src))) {
			out += escapeHtml(src.slice(last, m.index));
			var t = escapeHtml(m[0]);
			if (m[1]) out += '<span class="tok-comment">' + t + '</span>';
			else if (m[2]) out += '<span class="tok-string">' + t + '</span>';
			else if (m[3]) {
				var hex = m[3].length === 9 ? '#' + m[3].slice(3) : m[3];
				out += '<span class="swatch" style="background:' + hex + '"></span><span class="tok-number">' + t + '</span>';
			}
			else if (m[4] || m[5]) out += '<span class="tok-number">' + t + '</span>';
			else if (m[6]) out += '<span class="' + (KEYWORDS.test(m[6]) ? 'tok-keyword' : 'tok-func') + '">' + t + '</span>';
			else if (m[7]) out += '<span class="tok-prop">' + t + '</span>';
			else if (m[8] && KEYWORDS.test(m[8])) out += '<span class="tok-keyword">' + t + '</span>';
			else out += t;
			last = re.lastIndex;
		}
		return out + escapeHtml(src.slice(last));
	}
	document.querySelectorAll('pre > code').forEach(function (code) {
		var text = code.textContent;
		code.innerHTML = highlight(text);
		var pre = code.parentNode;
		var btn = document.createElement('button');
		btn.className = 'copy';
		btn.type = 'button';
		btn.textContent = 'Copy';
		btn.addEventListener('click', function () {
			navigator.clipboard.writeText(text.replace(/\s+$/, '')).then(function () {
				btn.textContent = 'Copied';
				setTimeout(function () { btn.textContent = 'Copy'; }, 1500);
			});
		});
		pre.appendChild(btn);
	});

	// Search over a small prebuilt index
	var input = document.getElementById('search');
	var list = document.getElementById('search-results');
	var index = null, selected = -1;
	function load() {
		if (index) return Promise.resolve(index);
		return fetch(root + 'assets/search.json').then(function (r) { return r.json(); }).then(function (d) { index = d; return d; });
	}
	function snippet(text, q) {
		var i = text.toLowerCase().indexOf(q);
		if (i < 0) return text.slice(0, 110) + (text.length > 110 ? '…' : '');
		var start = Math.max(0, i - 40);
		var s = (start ? '…' : '') + text.slice(start, i + q.length + 70) + '…';
		return escapeHtml(s).replace(new RegExp(escapeHtml(q).replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'ig'), function (x) { return '<mark>' + x + '</mark>'; });
	}
	function render(q) {
		q = q.trim().toLowerCase();
		if (!q) { list.hidden = true; return; }
		load().then(function (pages) {
			var words = q.split(/\s+/);
			var hits = pages.map(function (p) {
				var t = p.t.toLowerCase(), x = p.x.toLowerCase(), score = 0;
				for (var i = 0; i < words.length; i++) {
					var w = words[i];
					if (t.indexOf(w) >= 0) score += t === w ? 20 : 10;
					else if (x.indexOf(w) >= 0) score += 2;
					else return null;
				}
				return { p: p, score: score };
			}).filter(Boolean).sort(function (a, b) { return b.score - a.score; }).slice(0, 12);
			selected = -1;
			list.innerHTML = hits.length ? hits.map(function (h) {
				return '<li><a href="' + root + h.p.u + '">' + escapeHtml(h.p.t) + (h.p.s ? ' <small style="display:inline">· ' + escapeHtml(h.p.s) + '</small>' : '') +
					'<small>' + snippet(h.p.x, words[0]) + '</small></a></li>';
			}).join('') : '<li class="empty">No results for “' + escapeHtml(q) + '”</li>';
			list.hidden = false;
		});
	}
	if (input) {
		input.addEventListener('input', function () { render(input.value); });
		input.addEventListener('focus', function () { load(); if (input.value) render(input.value); });
		input.addEventListener('keydown', function (e) {
			var links = list.querySelectorAll('a');
			if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
				e.preventDefault();
				if (!links.length) return;
				selected = (selected + (e.key === 'ArrowDown' ? 1 : -1) + links.length) % links.length;
				links.forEach(function (a, i) { a.classList.toggle('selected', i === selected); });
			} else if (e.key === 'Enter') {
				var target = links[selected >= 0 ? selected : 0];
				if (target) location.href = target.href;
			} else if (e.key === 'Escape') {
				list.hidden = true; input.blur();
			}
		});
		document.addEventListener('click', function (e) { if (!e.target.closest('.search')) list.hidden = true; });
		document.addEventListener('keydown', function (e) {
			if (e.key === '/' && document.activeElement !== input && !/input|textarea/i.test(document.activeElement.tagName)) {
				e.preventDefault(); input.focus();
			}
		});
	}
})();
