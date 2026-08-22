# Setup you own: Addressables remote content (OTA)

Push new art, tile skins and board themes **without a store build** by hosting the
Addressables catalog + bundles on a dedicated Firebase Hosting site. Strategy and the
hosting decision: [ADR 0021](../DECISIONS/0021-ota-updates.md).

The repo side is done: `firebase.json` has a `content` hosting target, and
`content/public/` exists (built bundles land here; git-ignored). What's left needs your
Firebase project + the Unity editor.

---

## Part 1 — Create the content Hosting site (one-time, CLI)

`firebase.json` now uses a **targeted array** (`web` + `content`), so you must apply both
targets before deploying hosting. From the repo root:

```bash
# 1. Find your existing marketing site id (usually "carib-domino")
firebase hosting:sites:list

# 2. Bind the marketing config to that site
firebase target:apply hosting web <your-marketing-site-id>

# 3. Create + bind the content site
firebase hosting:sites:create pose-content
firebase target:apply hosting content pose-content

# 4. Deploy the placeholder to prove it works
firebase deploy --only hosting:content
```

Your content base URL is then **`https://pose-content.web.app`** (or the URL step 3
prints). From now on:

- marketing site deploy → `firebase deploy --only hosting:web`
- content deploy → `firebase deploy --only hosting:content`

> The `web` config is byte-for-byte what it was, plus `"target": "web"`. The only change
> to your workflow is `--only hosting` becomes `--only hosting:web`.

---

## Part 2 — Point Addressables at the content site (one-time, Unity editor)

**Window ▸ Asset Management ▸ Addressables ▸ Groups**, then the **Profiles** window
(gear ▸ *Manage Profiles*). On the profile you build with (make a `Production` profile if
you like):

| Variable | Value |
|---|---|
| `RemoteLoadPath` | `https://pose-content.web.app/[BuildTarget]` |
| `RemoteBuildPath` | `../content/public/[BuildTarget]` |

`RemoteBuildPath` is relative to the `unity/` project folder, so `../content/public/…`
lands the build in the repo's `content/public/`. If the relative path misbehaves, keep the
default `ServerData/[BuildTarget]` and copy `unity/ServerData/*` → `content/public/` before
deploying.

Then in **Addressables Settings** (gear ▸ *Manage Settings* / the AddressableAssetSettings
inspector):
- tick **Build Remote Catalog**, with its Build Path = `RemoteBuildPath`, Load Path =
  `RemoteLoadPath`.

Finally, mark the group(s) you want OTA (e.g. a future **TileSkins** / **BoardThemes**
group): select the group → Inspector → **Content Packing & Loading** → Build Path =
`RemoteBuildPath`, Load Path = `RemoteLoadPath`.

> Leave the **Localization-*** groups **Local**. Localization is already OTA via the
> Firestore remote string tables — no need to make those remote.

---

## Part 3 — First build + the ship requirement

```
Addressables Groups window ▸ Build ▸ New Build ▸ Default Build Script
```
writes the catalog + bundles into `content/public/<BuildTarget>/`. Deploy them:

```bash
firebase deploy --only hosting:content
```

**Critical:** the app must be shipped **once** with Build Remote Catalog on and the group
marked Remote, so the installed app knows to check the remote catalog at launch. A
content-only push cannot reach an app that shipped with no remote awareness. So the
sequence for a new content-backed feature is:

1. Configure remote (Part 2) → build Addressables → deploy content.
2. Build + upload the **app** once (`scripts/build-android.ps1`, then Play).
3. After that, content updates skip step 2 entirely (Part 4).

---

## Part 4 — The update loop (no app build)

To push new/changed content to already-installed apps:

```
Addressables Groups window ▸ Build ▸ Update a Previous Build
    → pick unity/Assets/AddressableAssetsData/<BuildTarget>/addressables_content_state.bin
```
then

```bash
firebase deploy --only hosting:content
```

Installed apps pick up the new catalog on their next launch.

- **Use *Update a Previous Build*, not *New Build*, for updates** — it reads
  `addressables_content_state.bin` to produce a catalog compatible with the shipped app.
  A fresh *New Build* can invalidate bundles the shipped app expects.
- Keep `addressables_content_state.bin` committed (it's under `AddressableAssetsData/<target>/`)
  — it's the record of what the live app shipped with.

---

## Caching (already configured in firebase.json)

The `content` target sets, per path:
- `*.bundle` → `max-age=31536000, immutable` (bundle names are content-hashed, so safe forever)
- `*.json` / `*.hash` (the catalog) → `max-age=60` (so a new catalog is seen quickly)
- `Access-Control-Allow-Origin: *` throughout (harmless for native, needed if a WebGL build ever loads it)

## Gotchas

- **Nothing to make remote yet.** Until real tile-skin / board-theme art exists, this is
  set-up-and-wait — the mechanism is ready; there's just no content pushing through it.
- **`RemoteLoadPath` must match the deployed structure.** Files at
  `content/public/Android/*` serve at `https://pose-content.web.app/Android/*`, which is why
  the profile path ends in `/[BuildTarget]`.
- **A custom domain is optional** — you can map one to the content site later; the
  `.web.app` URL works fine as a CDN in the meantime.
