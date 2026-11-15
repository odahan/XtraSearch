# XtraSearch

English WPF desktop file search, targeting .NET 11. Uses CommunityToolkit.Mvvm attributes for properties and commands. No licensing service, registration, trial period, result-count limit, or edition checks.

Build: `dotnet build XtraSearch.csproj -m:1 -c Release`

Run the generated XtraSearch executable normally. F5 starts a search; Escape stops it. A stop retains results. Pause suspends enumeration and reading. Searches can use multiple paths separated by semicolons and excluded paths prefixed with `!`.

Verification: run `XtraSearch.exe --self-test` from this folder; read `verification-results.json`. Capture: `XtraSearch.exe --screenshot shots/now.png` saves a PNG without taking focus and exits.

Use the in-app search guide for matching syntax and supported formats. See MECHANICS.md for code evidence, implementation choices and compatibility limits. Performance claims are limited to the recorded local benchmark; comparison against the original engine has not been measured.

## Dependencies and attribution

- CommunityToolkit.Mvvm 8.4.0 — .NET Foundation and contributors, MIT.
- PdfPig 0.1.15 — UglyToad and contributors, Apache-2.0.
- Microsoft .NET / WPF — Microsoft and contributors, MIT.

The UI graphics are original WPF text and shapes. Agent Ransack is the investigated reference, not the name or branding of this product. No original logos or resource images ship in the application.


