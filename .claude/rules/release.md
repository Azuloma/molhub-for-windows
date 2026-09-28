---
paths:
  - "*.csproj"
  - "Package.appxmanifest"
  - "VERSION.md"
  - "CHANGELOG.md"
  - "README.md"
---

# Versioning and release records
- `<Version>` in `FusionLedger.Windows.csproj` is the display-version source of truth; `Package.appxmanifest` carries the four-part `X.Y.Z.0`. Do not add another version constant.
- A release updates the csproj, manifest, `VERSION.md`, `CHANGELOG.md` and `README.md`, and the version assertions in the policy tests.
- Bump size: patch by default, also for a single-page feature (user decision 2026-09-27); ask before a minor bump. Bump the patch version whenever an in-place upgrade is needed; a same-version reinstall requires uninstalling, which deletes the sign-in profile.
- Proposed commit messages follow the existing `vX.Y.Z, summary` history format.
- The build emits an unsigned MSIX under `AppPackages\` (git-ignored). Pass `-PackageDirectory` to `Install-Prototype.ps1` when more than one `*_x64_Debug_Test` directory exists; the script refuses to guess.
