# ADR 0020 — Release pipeline: Codemagic for TestFlight + Play internal testing

- **Status:** Accepted (pipeline landed; credentials pending — see [SETUP/store-releases](../SETUP/store-releases.md))
- **Date:** 2026-08-18
- **Scope:** `infra`, `unity`
- **Relates:** [0019-facebook-identity-and-friends](0019-facebook-identity-and-friends.md) (SDK that shapes signing/plist needs), [0001-tech-stack](0001-tech-stack.md)

## Context

Testing has been sideload-only: a locally built APK copied to a device. That is
enough for one developer and stops working the moment there is a second tester, an
iOS device, or a build anyone needs to reproduce.

Two constraints drive everything here:

1. **The only dev machine is Windows.** An iOS `.ipa` can only be produced on
   macOS. There is no workaround — Xcode is required and Apple does not ship it
   elsewhere.
2. **Store builds need credentials that must not enter the repo.** An Android
   upload key, an App Store Connect API key, a Play service account, and the two
   Firebase config files (already gitignored) all have to reach a build machine
   without being committed.

## Decision

### Codemagic runs both platforms, on macOS

iOS forces a macOS runner regardless. Putting Android on the same image means one
Unity installation path, one licence-activation step, and one place credentials
live — rather than a Windows-local Android path drifting away from a cloud iOS
path. The Android build is slower on a Mac mini than locally; that is an acceptable
price for the pipelines being the same shape.

The local path is kept as well (`scripts/build-android.ps1`) because the first Play
upload must be manual, and because a signed APK on a device is still the fastest
way to check something by hand.

Rejected: **GitHub Actions + GameCI** (macOS runners bill at 10×, and Unity licence
activation is materially fiddlier); **buying a Mac** (unnecessary capex for a solo
project whose only Mac need is a codesign step).

### Player settings are code, not Inspector state

`ReleaseBuildSettings.Apply()` sets identity, SDK levels, scripting backend,
architectures, version, build number, and signing on every build, from environment
variables. A build is therefore reproducible from a clean clone, and CI cannot
inherit whatever the Editor happened to be set to when someone last saved
ProjectSettings.

This also fixed identity that had never been set: `companyName` was
`DefaultCompany`, `productName` was `unity`, and the iOS bundle ID was still the
URP template default while Firebase and the Meta app both expect
`com.invovibe.posedominoes`.

### `targetSdk` 34 → 36

Google Play enforces API 36 for new uploads from 2026-08-31; API 34 is rejected
today. `minSdk` stays at 25.

### Build numbers come from the build system, never the repo

Play rejects a repeated `versionCode` and TestFlight rejects a repeated
`CFBundleVersion`, so both come from `POSE_BUILD_NUMBER`: `$PROJECT_BUILD_NUMBER`
in CI, and a counter at `~/.config/invovibe/build-number` locally. Neither writes
back to `ProjectSettings.asset`, which keeps release builds out of the diff.

`bundleVersion` (the marketing version) stays in ProjectSettings and is bumped
deliberately.

### Tag-triggered, not push-triggered

`android-v*` and `ios-v*` tags trigger their workflows. Publishing to real testers
should be a deliberate act, and Codemagic build minutes are finite.

### Signing material lives in `~/.config/invovibe/`

Consistent with the existing convention in CLAUDE.md for service-account JSON. The
upload keystore and its passwords are there; CI reads base64 copies from Codemagic
secure variables. Nothing signing-related is in the repo, and the gitignored
Firebase config files are restored on CI from secure variables too.

## Consequences

- **The upload keystore is now a thing that can be lost.** Play App Signing makes
  it recoverable by support request, which is slow. Backup is a manual step called
  out first in the setup doc.
- **Two certificate identities exist per Android build** — the upload key and
  Google's app signing key. Firebase SHA fingerprints and Facebook key hashes must
  be registered for both, or login works when sideloaded and fails from Play.
- **The Unity seat is Personal, which may block the iOS workflow entirely.** CI
  activation is designed around a Plus/Pro serial; Personal has no serial and needs
  a manually activated `.ulf`, which is issued per-machine and unsupported on cloud
  macOS runners (GameCI supports Personal on Linux containers only). `codemagic.yaml`
  handles both paths so the free attempt costs nothing, but the fallback is a real
  Mac — rented, borrowed or bought — not a configuration change. **Android is
  unaffected**: it builds locally on Windows under the same Personal licence, so
  Play internal testing is reachable today regardless of how this resolves.
- **`targetSdk 36` changes runtime behaviour** (predictive back, mandatory
  edge-to-edge on Android 15+). The first internal build needs a real device check
  before testers are added.
- **The Facebook SDK's legacy `com.android.support:*:25.3.1` dependencies are a
  live risk** at `targetSdk 36`. Jetifier may carry them; if Gradle fails on
  duplicate classes, they get removed in favour of the AndroidX artifacts Firebase
  already brings.
