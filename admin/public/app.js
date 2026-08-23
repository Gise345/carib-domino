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
    panel.innerHTML =
      `<h3>${escape(u.name)}</h3>` +
      rows.map(([k, v]) => `<div class="row"><span class="k">${k}</span><span class="v">${v}</span></div>`).join('') +
      `<p class="muted">Ban / unban controls arrive in Phase D.</p>`;
  } catch (e) {
    panel.innerHTML = `<p class="error">${escape(e && e.message ? e.message : 'Failed to load user.')}</p>`;
  }
}
