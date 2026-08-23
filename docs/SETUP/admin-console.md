# Setup you own: the admin console

The admin dashboard is a static web app (`admin/public/`) on its own Firebase
Hosting site, gated by the `admin` custom claim. Security model + phases:
[ADR 0022](../DECISIONS/0022-admin-and-moderation.md).

Repo side is done: the `admin` hosting target, the dashboard shell (Google sign-in
→ `syncAdminClaim` → claim gate), and the server security spine (Phase A).

---

## One-time setup

```bash
# 1. Create the admin Hosting site + bind the target
firebase hosting:sites:create pose-admin
firebase target:apply hosting admin pose-admin

# 2. Deploy the functions (needs syncAdminClaim live) and the dashboard
cd functions && npm run deploy && cd ..
firebase deploy --only hosting:admin
```

Then in the **Firebase console → Authentication → Sign-in method**, enable
**Google** as a provider (the game uses Facebook/email/anonymous; the dashboard uses
Google). The `pose-admin.web.app` domain is auto-authorised as a Hosting domain.

Open **https://pose-admin.web.app**, sign in with an allowlisted Google account →
you land on the dashboard. A non-allowlisted account sees "Not authorised".

---

## Adding or removing an admin

The allowlist is **code**, on purpose (deliberate + auditable, not a console toggle):

1. Edit `functions/src/admin/admins.ts` → add/remove the email.
2. `cd functions && npm run deploy`.
3. New admin signs in — `syncAdminClaim` grants the claim from the allowlist.
   A removed admin is stripped on their next sign-in; to revoke immediately, an
   existing admin can (Phase D) or you can clear the claim in the console.

Current admins: `gise.a.k@gmail.com`, `i.t.cayman@invovibetech.com`,
`micheeboo2191@gmail.com`, `mtjohnson50@gmail.com`
*(the last was given as `…@gmail.om` — corrected to `.com`; fix `admins.ts` if wrong).*

---

## Security recap (ADR 0022)

- Admin = an **unforgeable Firebase custom claim**, granted only from the
  server-side allowlist via the **verified** Google token email.
- Every admin function re-checks the live allowlist (`assertAdmin`), so a stale or
  forged claim is useless.
- All admin actions run server-side and write an immutable `/adminAudit` record.
- **Turn on 2FA** for the admin Google accounts — that's the realistic attack surface.

---

## Notes / gotchas

- **Functions region:** the dashboard uses `getFunctions(app)` = `us-central1`
  (matches the current deploy). If you ever pin functions to another region, set
  `getFunctions(app, '<region>')` in `admin/public/app.js`.
- **⚠️ Your `web` hosting target looks wrong.** `.firebaserc` maps `web` to
  `1:650525007766:web:…` — that's a **web app ID, not a Hosting site name**, so
  `firebase deploy --only hosting:web` will fail. Fix it:
  ```bash
  firebase hosting:sites:list                       # find the marketing site id
  firebase target:apply hosting web <marketing-site-id>   # likely: carib-domino
  ```
- The Firebase **web config** committed in `app.js` is public (an API key is a
  project identifier, not a secret — same key already ships in
  `google-services-desktop.json`).
