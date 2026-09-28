---
paths:
  - "tests/**"
---

# Policy tests

- `tests/FusionLedger.Windows.PolicyTests/` — console assertion tests: `Program.cs` (entry point and most assertions; grep, never read in full), `AdministrationPolicyTests.cs`, `AdminUserWritePolicyTests.cs`, and the `.csproj` that links the pure policy `.cs` files via `Compile Include`. The tests also read source/XAML/doc text through `Path.Combine(sourceRoot, ...)`.
- The tests are string-based: renaming the checked identifiers, XAML attributes or doc headings breaks them. Update an assertion together with an intentional design change, never to silence a regression.
- Moving a source file means updating both the `Compile Include` links and the `Path.Combine(sourceRoot, ...)` reads.
- Run: `dotnet run --project tests\FusionLedger.Windows.PolicyTests\FusionLedger.Windows.PolicyTests.csproj`.
