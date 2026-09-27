---
paths:
  - "tests/**"
---

# Policy tests (moved from CLAUDE.md)

- `tests/FusionLedger.Windows.PolicyTests` — console assertion tests. They link the pure policy `.cs` files and also inspect source/XAML/docs text.
- The tests are string-based: renaming the checked identifiers, XAML attributes or doc headings breaks them. Update an assertion together with an intentional design change, never to silence a regression.
- Moving a source file means updating the `Compile Include` links in the test csproj and the `Path.Combine(sourceRoot, ...)` reads.
- Run: `dotnet run --project tests\FusionLedger.Windows.PolicyTests\FusionLedger.Windows.PolicyTests.csproj`.
