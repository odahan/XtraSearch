# XtraSearch

XtraSearch is a Windows desktop app for finding files by name or by the text they contain. It searches the folders you select directly, without requiring a search index or an account.

## Search features

- Use wildcard patterns, Boolean expressions, or regular expressions. File names and contents have independent matching modes and case settings.
- Search multiple folders and Windows network shares. Separate paths with `;` and prefix a path with `!` to exclude it.
- Filter by file size or modification date, and preview matching text in the results.
- Search plain text, PDFs with embedded text, modern Office and OpenDocument files, and text entries in ZIP archives. See the [format and matching details](app/MECHANICS.md) for coverage and limits.
- Press **F5** to start a search or **Esc** to stop and keep the results found so far.

## Build from source

The app uses WPF and targets .NET 11 on Windows. Install the .NET 11 SDK and make NuGet packages available for restore. The project uses the SDK's standard C# compiler.

```powershell
dotnet build app/XtraSearch.csproj -m:1 -c Release
.\app\bin\Release\net11.0-windows\XtraSearch.exe
```

## License

XtraSearch is freeware for personal, non-commercial use under the [XtraSearch Personal Use License](LICENSE). Third-party components retain their respective licenses. See the [dependency credits](app/README.md#dependencies-and-attribution).
