// Pose admin dashboard (ADR 0022, phase B). Google sign-in, then the server
// decides admin-ness: syncAdminClaim grants the `admin` custom claim only for
// allowlisted verified emails, and every admin action re-checks the allowlist
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

function show(view) {
  for (const v of ['loading', 'signedOut', 'denied', 'dashboard']) {
    el(v).classList.toggle('hidden', v !== view);
  }
}

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

for (const tab of document.querySelectorAll('.tab')) {
  tab.addEventListener('click', () => {
    for (const t of document.querySelectorAll('.tab')) t.classList.remove('is-active');
    tab.classList.add('is-active');
    const panel = tab.dataset.panel;
    for (const p of document.querySelectorAll('.panel')) {
      p.classList.toggle('hidden', p.id !== panel);
    }
  });
}

onAuthStateChanged(auth, async (user) => {
  if (!user) {
    show('signedOut');
    return;
  }
  show('loading');
  try {
    // Ask the server to grant/refresh the admin claim from its allowlist, then
    // force-refresh the ID token so the claim is live in token.claims.
    await httpsCallable(functions, 'syncAdminClaim')();
    const token = await user.getIdTokenResult(true);
    if (token.claims.admin === true) {
      el('whoami').textContent = user.email || '';
      show('dashboard');
    } else {
      el('deniedEmail').textContent = user.email ? `Signed in as ${user.email}` : '';
      show('denied');
    }
  } catch (e) {
    el('deniedEmail').textContent = e && e.message ? e.message : 'Could not verify admin access.';
    show('denied');
  }
});
