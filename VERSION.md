# Version policy

Current version: **v0.14.4**

The `<Version>` element in `FusionLedger.Windows.csproj` is the source of truth for the app's three-part display version. `Package.appxmanifest` carries the corresponding four-part package version (`0.14.4.0`) required by MSIX. Do not introduce a second display-version constant. Patch releases increment the third component; package versions append `.0`.
