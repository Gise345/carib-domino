/* ============================================================================
   Pose: Caribbean Dominoes — marketing site behaviour
   No dependencies. Everything degrades to a readable static page if JS fails.
   ========================================================================= */
(function () {
  'use strict';

  var reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  /* ── soft launch instant ────────────────────────────────────────────────
     1 September 2026, 00:00 Cayman time (UTC-5, no daylight saving).        */
  var LAUNCH = new Date('2026-09-01T00:00:00-05:00').getTime();

  var SIGNUP_ENDPOINT = '/api/tester-signup';

  /* ── bunting ────────────────────────────────────────────────────────────
     Decorative only (the strip is aria-hidden), so these are simplified
     colour impressions of each flag rather than exact heraldry.            */
  var FLAGS = [
    ['Jamaica',
      'linear-gradient(45deg,transparent 40%,#FED100 40% 60%,transparent 60%),' +
      'linear-gradient(135deg,transparent 40%,#FED100 40% 60%,transparent 60%),' +
      'conic-gradient(from 45deg,#000 0 90deg,#009B3A 0 180deg,#000 0 270deg,#009B3A 0)'],
    ['Trinidad and Tobago',
      'linear-gradient(135deg,transparent 32%,#fff 32% 39%,#000 39% 58%,#fff 58% 65%,transparent 65%),#CE1126'],
    ['Barbados',
      'linear-gradient(90deg,#00267F 0 33.3%,#FFC726 0 66.6%,#00267F 0)'],
    ['Haiti',
      'linear-gradient(180deg,#00209F 0 50%,#D21034 0)'],
    ['Grenada',
      'linear-gradient(180deg,#CE1126 0 22%,#FCD116 22% 50%,#007A5E 50% 78%,#CE1126 0)'],
    ['Saint Kitts and Nevis',
      'linear-gradient(45deg,#009E49 0 40%,#000 40% 60%,#C8102E 0)'],
    ['Bahamas',
      'linear-gradient(135deg,#000 0 24%,transparent 24%),' +
      'linear-gradient(180deg,#00778B 0 33.3%,#FFC72C 33.3% 66.6%,#00778B 0)'],
    ['Cayman Islands',
      'linear-gradient(135deg,#C8102E 0 8%,#fff 8% 14%,transparent 14%),#00247D'],
    ['Dominican Republic',
      'linear-gradient(90deg,transparent 44%,#fff 44% 56%,transparent 56%),' +
      'linear-gradient(180deg,transparent 42%,#fff 42% 58%,transparent 58%),' +
      'conic-gradient(from 0deg,#CE1126 0 90deg,#002D62 0 180deg,#CE1126 0 270deg,#002D62 0)'],
    ['Puerto Rico',
      'linear-gradient(135deg,#0050F0 0 30%,transparent 30%),' +
      'linear-gradient(180deg,#EF0000 0 25%,#fff 25% 50%,#EF0000 50% 75%,#fff 0)'],
    ['Saint Lucia',
      'linear-gradient(180deg,transparent 30%,#FCD116 30% 74%,transparent 74%),#66CCFF'],
    ['Antigua and Barbuda',
      'linear-gradient(180deg,#000 0 40%,#0072C6 40% 62%,#fff 62% 74%,#CE1126 0)'],
    ['British Virgin Islands',
      'linear-gradient(135deg,#C8102E 0 8%,#fff 8% 14%,transparent 14%),#012169'],
  ];

  function buildBunting() {
    var host = document.getElementById('buntingFlags');
    if (!host) return;

    var n = FLAGS.length;
    var frag = document.createDocumentFragment();

    for (var i = 0; i < n; i++) {
      var t = i / (n - 1);
      // follow the sag of the wire: a shallow parabola, deepest at centre
      var sag = 42 * (1 - Math.pow(2 * t - 1, 2));

      var el = document.createElement('span');
      el.className = 'flag';
      el.style.setProperty('--fx', (2 + t * 94) + '%');
      el.style.setProperty('--fy', sag + 'px');
      el.style.setProperty('--fd', (i * 0.17).toFixed(2) + 's');
      el.style.background = FLAGS[i][1];
      frag.appendChild(el);
    }
    host.appendChild(frag);
  }

  /* ── fireflies ─────────────────────────────────────────────────────────── */

  function buildFireflies() {
    var host = document.getElementById('fireflies');
    if (!host || reduceMotion) return;

    // fewer on small screens — this is atmosphere, not a feature
    var count = window.innerWidth < 640 ? 9 : 18;
    var frag = document.createDocumentFragment();

    for (var i = 0; i < count; i++) {
      var f = document.createElement('span');
      f.className = 'fly';
      f.style.setProperty('--fl', (Math.random() * 100).toFixed(1) + '%');
      f.style.setProperty('--ft', (35 + Math.random() * 55).toFixed(1) + '%');
      f.style.setProperty('--fdx', (Math.random() * 90 - 45).toFixed(0) + 'px');
      f.style.setProperty('--fdy', (30 + Math.random() * 70).toFixed(0) + 'px');
      f.style.setProperty('--fdur', (7 + Math.random() * 7).toFixed(1) + 's');
      f.style.setProperty('--fdel', (Math.random() * 9).toFixed(1) + 's');
      frag.appendChild(f);
    }
    host.appendChild(frag);
  }

  /* ── domino chain ──────────────────────────────────────────────────────── */

  // standard dice arrangements on a 3x3 grid, [col, row] with 0-2 indices
  var PIPS = {
    0: [],
    1: [[1, 1]],
    2: [[0, 0], [2, 2]],
    3: [[0, 0], [1, 1], [2, 2]],
    4: [[0, 0], [2, 0], [0, 2], [2, 2]],
    5: [[0, 0], [2, 0], [1, 1], [0, 2], [2, 2]],
    6: [[0, 0], [2, 0], [0, 1], [2, 1], [0, 2], [2, 2]],
  };

  var COL_X = [26, 50, 74];
  var ROW_TOP = [12, 25, 38];
  var ROW_BOT = [62, 75, 88];

  function paintHalf(tile, value, rows) {
    var spots = PIPS[value] || [];
    for (var i = 0; i < spots.length; i++) {
      var p = document.createElement('span');
      p.className = 'p';
      p.style.left = COL_X[spots[i][0]] + '%';
      p.style.top = rows[spots[i][1]] + '%';
      p.style.transform = 'translate(-50%, -50%)';
      tile.appendChild(p);
    }
  }

  function buildDominoes() {
    var tiles = document.querySelectorAll('.dom');
    for (var i = 0; i < tiles.length; i++) {
      var spec = (tiles[i].getAttribute('data-t') || '0-0').split('-');
      tiles[i].style.setProperty('--i', i);
      paintHalf(tiles[i], parseInt(spec[0], 10), ROW_TOP);
      paintHalf(tiles[i], parseInt(spec[1], 10), ROW_BOT);
    }
  }

  /* ── countdown ─────────────────────────────────────────────────────────── */

  function pad(n) { return n < 10 ? '0' + n : String(n); }

  function tick() {
    var d = document.getElementById('cd-d');
    if (!d) return;

    var left = LAUNCH - Date.now();

    if (left <= 0) {
      var note = document.getElementById('cd-note');
      d.textContent = '00';
      document.getElementById('cd-h').textContent = '00';
      document.getElementById('cd-m').textContent = '00';
      document.getElementById('cd-s').textContent = '00';
      if (note) note.textContent = 'The soft launch is open';
      return false;
    }

    var s = Math.floor(left / 1000);
    d.textContent = pad(Math.floor(s / 86400));
    document.getElementById('cd-h').textContent = pad(Math.floor(s / 3600) % 24);
    document.getElementById('cd-m').textContent = pad(Math.floor(s / 60) % 60);
    document.getElementById('cd-s').textContent = pad(s % 60);
    return true;
  }

  function startCountdown() {
    if (tick() === false) return;
    var id = setInterval(function () {
      if (tick() === false) clearInterval(id);
    }, 1000);
  }

  /* ── scroll reveal ─────────────────────────────────────────────────────── */

  function startReveals() {
    var items = document.querySelectorAll('.reveal, .chain');

    if (reduceMotion || !('IntersectionObserver' in window)) {
      for (var i = 0; i < items.length; i++) items[i].classList.add('is-in');
      return;
    }

    var io = new IntersectionObserver(function (entries) {
      for (var i = 0; i < entries.length; i++) {
        if (entries[i].isIntersecting) {
          entries[i].target.classList.add('is-in');
          io.unobserve(entries[i].target);
        }
      }
    }, { rootMargin: '0px 0px -12% 0px', threshold: 0.12 });

    for (var j = 0; j < items.length; j++) io.observe(items[j]);
  }

  /* ── parallax + sticky nav ─────────────────────────────────────────────── */

  function startScrollFx() {
    var nav = document.getElementById('nav');
    var layers = document.querySelectorAll('[data-parallax]');
    var hero = document.querySelector('.hero');
    var queued = false;

    function frame() {
      queued = false;
      var y = window.pageYOffset || document.documentElement.scrollTop;

      if (nav) nav.classList.toggle('is-stuck', y > 40);

      // parallax only while the hero is anywhere near the viewport
      if (reduceMotion || !hero || y > hero.offsetHeight) return;

      for (var i = 0; i < layers.length; i++) {
        var f = parseFloat(layers[i].getAttribute('data-parallax')) || 0;
        layers[i].style.transform = 'translate3d(0,' + (y * f).toFixed(1) + 'px,0)';
      }
    }

    function onScroll() {
      if (queued) return;
      queued = true;
      window.requestAnimationFrame(frame);
    }

    window.addEventListener('scroll', onScroll, { passive: true });
    frame();
  }

  /* ── tester signup ─────────────────────────────────────────────────────── */

  var EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

  function setError(id, input, message) {
    var el = document.getElementById(id);
    if (el) {
      el.textContent = message || '';
      el.hidden = !message;
    }
    if (input) {
      if (message) input.setAttribute('aria-invalid', 'true');
      else input.removeAttribute('aria-invalid');
    }
  }

  function setStatus(kind, message) {
    var el = document.getElementById('formStatus');
    if (!el) return;
    el.textContent = message;
    el.className = 'form__status ' + (kind === 'ok' ? 'is-ok' : 'is-bad');
    el.hidden = false;
  }

  function startSignup() {
    var form = document.getElementById('signupForm');
    if (!form) return;

    var emailInput = document.getElementById('email');
    var btn = document.getElementById('submitBtn');

    emailInput.addEventListener('input', function () {
      if (emailInput.getAttribute('aria-invalid')) setError('email-err', emailInput, '');
    });

    form.addEventListener('submit', function (ev) {
      ev.preventDefault();

      var email = emailInput.value.trim();
      var boxes = form.querySelectorAll('input[name="platforms"]:checked');
      var platforms = [];
      for (var i = 0; i < boxes.length; i++) platforms.push(boxes[i].value);

      var ok = true;

      if (!EMAIL_RE.test(email)) {
        setError('email-err', emailInput, 'Enter a valid email address so we can send your build.');
        ok = false;
      } else {
        setError('email-err', emailInput, '');
      }

      if (platforms.length === 0) {
        setError('platforms-err', null, 'Pick at least one platform.');
        ok = false;
      } else {
        setError('platforms-err', null, '');
      }

      if (!ok) {
        var status = document.getElementById('formStatus');
        if (status) status.hidden = true;
        (ok === false && !EMAIL_RE.test(email) ? emailInput : form).focus();
        return;
      }

      btn.disabled = true;
      btn.classList.add('is-busy');
      btn.querySelector('.btn__label').textContent = 'Sending…';

      fetch(SIGNUP_ENDPOINT, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          email: email,
          platforms: platforms,
          country: document.getElementById('country').value.trim() || undefined,
          nickname: document.getElementById('nickname').value,
        }),
      })
        .then(function (res) {
          return res.json().catch(function () { return {}; }).then(function (body) {
            return { ok: res.ok, body: body };
          });
        })
        .then(function (r) {
          if (!r.ok) throw new Error(r.body && r.body.error ? r.body.error : 'request-failed');

          form.reset();
          setStatus('ok',
            'You\'re on the list. We\'ll email build instructions before 1 September — ' +
            'check your spam folder if nothing lands.');
          btn.querySelector('.btn__label').textContent = 'Seat requested';
        })
        .catch(function () {
          setStatus('bad',
            'Something went wrong on our side. Try again in a moment, or email ' +
            'hello@caribbeandominos.com and we\'ll add you by hand.');
          btn.disabled = false;
          btn.querySelector('.btn__label').textContent = 'Request a seat';
        })
        .then(function () {
          btn.classList.remove('is-busy');
        });
    });
  }

  /* ── boot ──────────────────────────────────────────────────────────────── */

  function init() {
    buildBunting();
    buildFireflies();
    buildDominoes();
    startCountdown();
    startReveals();
    startScrollFx();
    startSignup();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
