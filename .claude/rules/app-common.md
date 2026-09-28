---
paths:
  - "App.xaml"
  - "App.xaml.cs"
  - "Auth/**"
  - "Bridge/**"
  - "Shell/**"
  - "Settings/**"
  - "Pages/**"
---

# App code — common rules

- Source is grouped in folders; the namespace stays `FusionLedger.Windows` everywhere (folders are not namespaces). A new data page gets its own `Pages/<Name>/` folder (`*View`, `*Model`), a `.claude/rules/pages-<name>.md` file and a row in the `CLAUDE.md` repository map. Moving a file: see `.claude/rules/tests.md`.
- UI: WinUI ThemeResources, Mica with fallback, Segoe typography and Segoe Fluent glyphs; support Light/Dark/High Contrast, keyboard access, localized automation names, English default plus Japanese. All user-facing strings go through `L(key)` and exist in both `.resw` files.
- Code-built pages must not read brushes from `Application.Current.Resources` directly (the theme is set on `RootGrid`, not the app); put `{ThemeResource}` setters in `App.xaml` styles and apply the style. Show avatars with `AvatarImage.Attach`. Keep `ContentFrame` stretched.

## Coding pitfalls

- Write Segoe Fluent glyphs as `""` escapes. Pasted private-use characters come out empty, and a `\uXXXX` typed in any tool input (Edit, Write, PowerShell) arrives as the raw character: build the backslash with `[char]92` in PowerShell (replace each private-use character with `[char]92 + 'u' + hex`) and confirm with a private-use scan. Never copy a glyph from Read output, where it shows as `""`.
- `JsonElement.TryGetInt32` throws on non-numbers (`nextOffset` is `null` at the end of a list): check `ValueKind == Number` first.
- `AutomationHeadingLevel` is in `Microsoft.UI.Xaml.Automation.Peers`.
- A horizontal `StackPanel` never wraps — use `InlineWrapPanel` for a long name next to a label.
- Windows PowerShell 5.1 garbles Japanese in inline commands and in BOM-less `.ps1` files (see `.claude/rules/strings.md`).
