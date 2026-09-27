---
paths:
  - "Strings/**"
---

# Strings (moved from CLAUDE.md)

- `Strings/en-US` and `Strings/ja-JP` `Resources.resw` hold all user-facing strings. Put every new string in both files; match the web wording where it exists.
- Windows PowerShell 5.1 garbles Japanese in inline commands and in BOM-less `.ps1` files. Put Japanese strings in a UTF-8 JSON file and insert them with `[IO.File]::ReadAllText/WriteAllText` and an explicit UTF-8 encoding. The `.resw` files are UTF-8 without BOM, LF line endings.
