// Pose admin dashboard (ADR 0022, phases B-C). Google sign-in, then the server
// decides admin-ness: syncAdminClaim grants the `admin` custom claim only for
// allowlisted verified emails, and every admin callable re-checks the allowlist
// server-side. This client only reflects that decision — it grants nothing.
//
// The Firebase web config below is public (an API key is a project identifier,
// not a secret; security is enforced by auth + rules + the admin claim).
import { initializeApp } from 'https://www.gstatic.com/firebasejs/10.13.2/firebase-app.js';
import {
  getAuth,
  GoogleAuthProvider,
  signInWithPopup,
  signOut,
  onAuthStateChanged,
} from 'https://www.gstatic.com/firebasejs/10.13.2/firebase-auth.js';
import {
  getFunctions,
  httpsCallable,
} from 'https://www.gstatic.com/firebasejs/10.13.2/firebase-functions.js';

const firebaseConfig = {
  apiKey: 'AIzaSyDev329rNxsDD3iQ_rCuvwy5jILAmzmb4Y',
  authDomain: 'carib-domino.firebaseapp.com',
  projectId: 'carib-domino',
  storageBucket: 'carib-domino.firebasestorage.app',
  messagingSenderId: '650525007766',
  appId: '1:650525007766:web:c428e1101152fc46d2c7b7',
};

const app = initializeApp(firebaseConfig);
const auth = getAuth(app);
const functions = getFunctions(app); // default region us-central1

const el = (id) => document.getElementById(id);
const call = (name, data) => httpsCallable(functions, name)(data).then((r) => r.data);
const escape = (s) =>
  String(s ?? '').replace(/[&<>"']/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c],
  );
const num = (n) => Number(n ?? 0).toLocaleString();

function show(view) {
  for (const v of ['loading', 'signedOut', 'denied', 'dashboard']) {
    el(v).classList.toggle('hidden', v !== view);
  }
}

// ---- Auth ------------------------------------------------------------------

el('signInBtn').addEventListener('click', async () => {
  el('signInError').classList.add('hidden');
  try {
    await signInWithPopup(auth, new GoogleAuthProvider());
  } catch (e) {
    el('signInError').textContent = e && e.message ? e.message : 'Sign-in failed.';
    el('signInError').classList.remove('hidden');
  }
});
el('signOutBtn').addEventListener('click', () => signOut(auth));
el('signOutBtn2').addEventListener('click', () => signOut(auth));

onAuthStateChanged(auth, async (user) => {
  if (!user) {
    show('signedOut');
    return;
  }
  show('loading');
  try {
    await call('syncAdminClaim');
    const token = await user.getIdTokenResult(true);
    if (token.claims.admin === true) {
      el('whoami').textContent = user.email || '';
      show('dashboard');
      loadStats();
    } else {
      el('deniedEmail').textContent = user.email ? `Signed in as ${user.email}` : '';
      show('denied');
    }
  } catch (e) {
    el('deniedEmail').textContent = e && e.message ? e.message : 'Could not verify admin access.';
    show('denied');
  }
});

// ---- Tabs ------------------------------------------------------------------

for (const tab of document.querySelectorAll('.tab')) {
  tab.addEventListener('click', () => {
    for (const t of document.querySelectorAll('.tab')) t.classList.remove('is-active');
    tab.classList.add('is-active');
    const panel = tab.dataset.panel;
    for (const p of document.querySelectorAll('.panel')) {
      p.classList.toggle('hidden', p.id !== panel);
    }
    if (panel === 'analytics') loadStats();
    if (panel === 'promotions') loadPromotions();
    if (panel === 'reports') loadReports();
  });
}

// ---- Analytics -------------------------------------------------------------

el('refreshStats').addEventListener('click', () => loadStats());

async function loadStats() {
  const grid = el('statsGrid');
  grid.innerHTML = '<p class="muted">Loading…</p>';
  try {
    const s = await call('getAdminStats');
    const tiles = [
      ['Total users', num(s.totalUsers)],
      ['Active (7 days)', num(s.activeUsers7d)],
      ['Rounds played', num(s.rounds)],
      ['Coins in circulation', num(s.coinsInCirculation)],
    ];
    grid.innerHTML = tiles
      .map(([label, value]) => `<div class="tile"><div class="tile-value">${value}</div><div class="tile-label">${label}</div></div>`)
      .join('');
  } catch (e) {
    grid.innerHTML = `<p class="error">${escape(e && e.message ? e.message : 'Failed to load stats.')}</p>`;
  }
}

// ---- Users -----------------------------------------------------------------

el('searchForm').addEventListener('submit', async (ev) => {
  ev.preventDefault();
  const q = el('searchInput').value.trim();
  if (!q) return;
  el('userDetail').classList.add('hidden');
  el('searchStatus').textContent = 'Searching…';
  el('searchResults').innerHTML = '';
  try {
    const { users } = await call('searchUsers', { query: q });
    el('searchStatus').textContent = users.length ? '' : 'No matches.';
    el('searchResults').innerHTML = users
      .map(
        (u) =>
          `<button class="result" data-uid="${escape(u.uid)}"><span>${escape(u.name)}</span><span class="muted">${escape(u.uid)}</span></button>`,
      )
      .join('');
    for (const btn of document.querySelectorAll('.result')) {
      btn.addEventListener('click', () => loadUserDetail(btn.dataset.uid));
    }
  } catch (e) {
    el('searchStatus').textContent = e && e.message ? e.message : 'Search failed.';
  }
});

async function loadUserDetail(uid) {
  const panel = el('userDetail');
  panel.classList.remove('hidden');
  panel.innerHTML = '<p class="muted">Loading…</p>';
  try {
    const u = await call('getUserDetail', { uid });
    const rows = [
      ['Name', escape(u.name)],
      ['UID', escape(u.uid)],
      ['Email', escape(u.email) || '—'],
      ['Providers', escape((u.providers || []).join(', ')) || '—'],
      ['Coins', num(u.coins)],
      ['Record', `${num(u.wins)}W · ${num(u.losses)}L · ${num(u.draws)}D`],
      ['Matches', num(u.matchesPlayed)],
      ['Win rate', `${Math.round((u.winRate || 0) * 100)}%`],
      ['Status', u.banned ? `<span class="error">Banned${u.banReason ? ` — ${escape(u.banReason)}` : ''}</span>` : 'Active'],
    ];
    const action = u.banned
      ? '<button class="btn btn--small" id="unbanBtn">Unban</button>'
      : '<button class="btn btn--small btn--danger" id="banBtn">Ban…</button>';
    panel.innerHTML =
      `<h3>${escape(u.name)}</h3>` +
      rows.map(([k, v]) => `<div class="row"><span class="k">${k}</span><span class="v">${v}</span></div>`).join('') +
      `<div class="detail-actions">${action}</div>`;

    const banBtn = document.getElementById('banBtn');
    if (banBtn) {
      banBtn.addEventListener('click', async () => {
        const reason = window.prompt('Ban reason (optional):', '');
        if (reason === null) return; // cancelled
        banBtn.disabled = true;
        try {
          await call('banUser', { uid: u.uid, reason });
          await loadUserDetail(u.uid);
        } catch (e) {
          window.alert(e && e.message ? e.message : 'Ban failed.');
          banBtn.disabled = false;
        }
      });
    }
    const unbanBtn = document.getElementById('unbanBtn');
    if (unbanBtn) {
      unbanBtn.addEventListener('click', async () => {
        unbanBtn.disabled = true;
        try {
          await call('unbanUser', { uid: u.uid });
          await loadUserDetail(u.uid);
        } catch (e) {
          window.alert(e && e.message ? e.message : 'Unban failed.');
          unbanBtn.disabled = false;
        }
      });
    }
  } catch (e) {
    panel.innerHTML = `<p class="error">${escape(e && e.message ? e.message : 'Failed to load user.')}</p>`;
  }
}

// ---- Promotions ------------------------------------------------------------

el('promoForm').addEventListener('submit', async (ev) => {
  ev.preventDefault();
  const code = el('promoCode').value.trim();
  const coins = parseInt(el('promoCoins').value, 10);
  const max = parseInt(el('promoMax').value, 10);
  const expiry = el('promoExpiry').value;
  if (!code || !Number.isFinite(coins) || coins < 1) {
    el('promoStatus').textContent = 'Enter a code and a coin amount.';
    return;
  }
  const payload = { code, coins };
  if (Number.isFinite(max) && max > 0) payload.maxRedemptions = max;
  if (expiry) payload.expiresAtMs = new Date(`${expiry}T23:59:59`).getTime();
  el('promoStatus').textContent = 'Creating…';
  try {
    await call('createPromotion', payload);
    el('promoForm').reset();
    el('promoStatus').textContent = '';
    await loadPromotions();
  } catch (e) {
    el('promoStatus').textContent = e && e.message ? e.message : 'Create failed.';
  }
});

async function loadPromotions() {
  const list = el('promoList');
  list.innerHTML = '<p class="muted">Loading…</p>';
  try {
    const { promotions } = await call('listPromotions');
    if (!promotions.length) {
      list.innerHTML = '<p class="muted">No promotions yet.</p>';
      return;
    }
    list.innerHTML = promotions
      .map((p) => {
        const redeemed = p.maxRedemptions > 0 ? `${num(p.redemptionCount)}/${num(p.maxRedemptions)}` : num(p.redemptionCount);
        const expiry = p.expiresAtMs > 0 ? new Date(p.expiresAtMs).toLocaleDateString() : 'never';
        const toggle = p.active
          ? `<button class="btn btn--small" data-code="${escape(p.code)}" data-active="false">Disable</button>`
          : `<button class="btn btn--small btn--primary" data-code="${escape(p.code)}" data-active="true">Enable</button>`;
        return `<div class="promo-row">
          <span class="promo-code">${escape(p.code)}</span>
          <span>${num(p.coins)} coins</span>
          <span class="muted">redeemed ${redeemed}</span>
          <span class="muted">expires ${escape(expiry)}</span>
          <span class="${p.active ? 'ok' : 'muted'}">${p.active ? 'Active' : 'Off'}</span>
          ${toggle}
        </div>`;
      })
      .join('');
    for (const btn of list.querySelectorAll('button[data-code]')) {
      btn.addEventListener('click', async () => {
        btn.disabled = true;
        try {
          await call('setPromotionActive', { code: btn.dataset.code, active: btn.dataset.active === 'true' });
          await loadPromotions();
        } catch (e) {
          window.alert(e && e.message ? e.message : 'Update failed.');
          btn.disabled = false;
        }
      });
    }
  } catch (e) {
    list.innerHTML = `<p class="error">${escape(e && e.message ? e.message : 'Failed to load promotions.')}</p>`;
  }
}

// ---- Chat reports (ADR 0023) -----------------------------------------------
//
// The moderation queue. A report carries a FROZEN transcript — the messages as
// they were typed, unmasked — plus the room's roster and the server-issued match
// ids, so a decision is made on evidence rather than on one line out of context.
// Every action here runs through an assertAdmin-gated callable and is audited.

const REASON_LABELS = {
  harassment: 'Harassment',
  hate: 'Hate speech',
  threats: 'Threats',
  sexual: 'Sexual content',
  spam: 'Spam',
  cheating: 'Cheating',
  other: 'Other',
};

const when = (iso) => (iso ? new Date(iso).toLocaleString() : '—');

el('refreshReports').addEventListener('click', () => loadReports());
el('reportStatus').addEventListener('change', () => loadReports());

async function loadReports() {
  const list = el('reportList');
  el('reportDetail').classList.add('hidden');
  el('reportStatusLine').textContent = 'Loading…';
  list.innerHTML = '';
  try {
    const { reports } = await call('listChatReports', { status: el('reportStatus').value });
    el('reportStatusLine').textContent = reports.length
      ? `${reports.length} report${reports.length === 1 ? '' : 's'}`
      : 'Nothing to review.';
    list.innerHTML = reports
      .map(
        (r) => `
        <button class="result report-row" data-id="${escape(r.id)}">
          <span class="report-main">
            <span class="report-who">${escape(r.reportedName || r.reportedUid)}</span>
            <span class="muted">${escape(r.reportedText).slice(0, 120)}</span>
          </span>
          <span class="report-meta">
            ${r.severe ? '<span class="pill pill--danger">severe</span>' : ''}
            <span class="pill">${escape(REASON_LABELS[r.reason] || r.reason)}</span>
            <span class="muted">${escape(when(r.createdAt))}</span>
          </span>
        </button>`,
      )
      .join('');
    for (const row of list.querySelectorAll('.report-row')) {
      row.addEventListener('click', () => openReport(row.dataset.id));
    }
  } catch (e) {
    el('reportStatusLine').textContent = e && e.message ? e.message : 'Failed to load reports.';
  }
}

async function openReport(reportId) {
  const panel = el('reportDetail');
  panel.classList.remove('hidden');
  panel.innerHTML = '<p class="muted">Loading…</p>';
  try {
    const r = await call('getChatReport', { reportId });
    panel.innerHTML = renderReport(r);
    wireReportActions(r);
  } catch (e) {
    panel.innerHTML = `<p class="error">${escape(e && e.message ? e.message : 'Failed to load report.')}</p>`;
  }
}

function renderReport(r) {
  const members = Object.entries(r.members || {})
    .map(([uid, m]) => `${escape(m.name || uid)}${m.seat >= 0 ? ` (seat ${m.seat})` : ''}`)
    .join(', ');

  const facts = [
    ['Reported', `${escape(r.reportedName || '—')} <span class="muted">${escape(r.reportedUid)}</span>`],
    ['Reported by', `${escape(r.reporterName || '—')} <span class="muted">${escape(r.reporterUid)}</span>`],
    ['Reason', escape(REASON_LABELS[r.reason] || r.reason)],
    ['Note', escape(r.note) || '—'],
    ['Table', `${escape(r.roomId)} · ${escape(r.mode)}`],
    ['Players', members || '—'],
    ['Matches', escape((r.matchIds || []).join(', ')) || '—'],
    ['Filed', escape(when(r.createdAt))],
    ['Prior reports against them', num(r.priorReportCount)],
    [
      'Account',
      r.isBanned
        ? '<span class="error">Banned</span>'
        : r.muteUntil
          ? `Muted until ${escape(when(r.muteUntil))}`
          : 'Active',
    ],
    ['Status', escape(r.status)],
  ];

  const transcript = (r.transcript || [])
    .map(
      (line) => `
      <div class="line${line.reported ? ' line--reported' : ''}">
        <span class="line-who">${escape(line.senderName || line.senderUid)}</span>
        <span class="line-text">${escape(line.text)}</span>
        <span class="line-at muted">${escape(when(line.at))}</span>
      </div>`,
    )
    .join('');

  const resolved =
    r.status === 'open'
      ? ''
      : `<p class="muted">Resolved by ${escape(r.resolvedByEmail || '—')} on ${escape(when(r.resolvedAt))}.</p>`;

  return (
    `<h3>Report</h3>` +
    facts.map(([k, v]) => `<div class="row"><span class="k">${k}</span><span class="v">${v}</span></div>`).join('') +
    `<h4>Transcript <span class="muted">(as typed, unmasked)</span></h4>` +
    `<div class="transcript">${transcript || '<p class="muted">No messages captured.</p>'}</div>` +
    resolved +
    `<div class="detail-actions">
       <button class="btn btn--small" id="muteDayBtn">Mute 24h</button>
       <button class="btn btn--small" id="muteWeekBtn">Mute 7 days</button>
       <button class="btn btn--small btn--danger" id="reportBanBtn">Ban…</button>
       <button class="btn btn--small" id="redactBtn">Remove message</button>
       <span class="spacer"></span>
       <button class="btn btn--small" id="dismissBtn">Dismiss</button>
       <button class="btn btn--small btn--primary" id="actionedBtn">Mark actioned</button>
     </div>
     <p id="reportActionStatus" class="muted"></p>`
  );
}

function wireReportActions(r) {
  const status = (text, isError) => {
    const line = document.getElementById('reportActionStatus');
    if (line) {
      line.textContent = text;
      line.className = isError ? 'error' : 'muted';
    }
  };

  const run = async (label, fn, reopen = true) => {
    status(`${label}…`);
    try {
      await fn();
      status(`${label} done.`);
      // Re-read rather than patch the DOM: the callables are the source of
      // truth for ban/mute state, and a stale panel invites a double action.
      if (reopen) await openReport(r.id);
      await loadReports();
    } catch (e) {
      status(e && e.message ? e.message : `${label} failed.`, true);
    }
  };

  const on = (id, handler) => {
    const btn = document.getElementById(id);
    if (btn) btn.addEventListener('click', handler);
  };

  on('muteDayBtn', () => run('Mute 24h', () => call('muteUser', { uid: r.reportedUid, hours: 24, reason: r.reason })));
  on('muteWeekBtn', () =>
    run('Mute 7 days', () => call('muteUser', { uid: r.reportedUid, hours: 168, reason: r.reason })));
  on('reportBanBtn', () => {
    const reason = window.prompt('Ban reason (optional):', `Chat: ${REASON_LABELS[r.reason] || r.reason}`);
    if (reason === null) return;
    run('Ban', () => call('banUser', { uid: r.reportedUid, reason }));
  });
  on('redactBtn', () =>
    run('Remove message', () =>
      call('redactChatMessage', { roomId: r.roomId, messageId: r.reportedMessageId })));
  on('dismissBtn', () => {
    const note = window.prompt('Why is this being dismissed?', '');
    if (note === null) return;
    run('Dismiss', () => call('resolveChatReport', { reportId: r.id, resolution: 'dismissed', note }), false);
  });
  on('actionedBtn', () => {
    const note = window.prompt('What action was taken?', '');
    if (note === null) return;
    run('Mark actioned', () => call('resolveChatReport', { reportId: r.id, resolution: 'actioned', note }), false);
  });
}
