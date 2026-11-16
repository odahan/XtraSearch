using System.Net;

namespace XtraSearch;

/// <summary>Writes search results in the formats offered by the export dialog.</summary>
internal static class ResultsExport
{
    public static void WriteCsv(TextWriter writer, IEnumerable<SearchResult> results)
    {
        static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

        writer.WriteLine("Name,Location,Bytes,Modified,Matching lines,Path");
        foreach (var result in results)
        {
            writer.WriteLine($"{Csv(result.Name)},{Csv(result.Location)},{result.Bytes},{result.Modified:O},{result.Hits},{Csv(result.FullPath)}");
        }
    }

    public static void WriteHtml(TextWriter writer, IReadOnlyCollection<SearchResult> results, int totalCount, DateTimeOffset exportedAt)
    {
        writer.WriteLine(FormattableString.Invariant($$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>XtraSearch results</title>
              <style>
                :root { color-scheme: dark; }
                body { margin: 2rem; background: #14171c; color: #ecedf0; font: 1rem system-ui, sans-serif; }
                .export-details { color: #b8c2d0; }
                .results { overflow-x: auto; }
                table { width: 100%; border-collapse: collapse; }
                th, td { padding: .65rem .8rem; text-align: left; vertical-align: top; border-bottom: 1px solid #393e48; }
                th { background: #262b34; }
                tbody tr:nth-child(even) { background: #1b1f26; }
                td { overflow-wrap: anywhere; }
                .number { text-align: right; font-variant-numeric: tabular-nums; }
                a { color: #9dc9ff; }
                a:visited { color: #d3b4ff; }
              </style>
            </head>
            <body>
              <h1>XtraSearch results</h1>
              <p class="export-details">Exported on <time datetime="{{exportedAt:O}}">{{exportedAt:yyyy-MM-dd HH:mm:ss zzz}}</time><br>
              {{results.Count:N0}} / {{totalCount:N0}} results exported</p>
              <div class="results">
                <table>
                  <thead>
                    <tr><th scope="col">Name</th><th scope="col">Location</th><th scope="col" class="number">Bytes</th><th scope="col">Modified</th><th scope="col" class="number">Matching lines</th><th scope="col">Path</th></tr>
                  </thead>
                  <tbody>
            """));

        foreach (var result in results)
        {
            var href = WebUtility.HtmlEncode(FileUrl(result.PhysicalPath));
            var name = WebUtility.HtmlEncode(result.Name);
            var location = WebUtility.HtmlEncode(result.Location);
            var path = WebUtility.HtmlEncode(result.FullPath);
            writer.WriteLine(FormattableString.Invariant($"""
                <tr><td><a href="{href}">{name}</a></td><td>{location}</td><td class="number">{result.Bytes}</td><td>{result.Modified:yyyy-MM-dd HH:mm:ss}</td><td class="number">{result.Hits}</td><td>{path}</td></tr>
                """));
        }

        writer.WriteLine("""
                  </tbody>
                </table>
              </div>
            </body>
            </html>
            """);
    }

    /// <summary>Escapes each path segment so characters in a file name remain literal in the link.</summary>
    private static string FileUrl(string path)
    {
        var absolutePath = Path.GetFullPath(path).Replace('\\', '/');
        if (absolutePath.StartsWith("//?/UNC/", StringComparison.OrdinalIgnoreCase))
            absolutePath = "//" + absolutePath[8..];
        else if (absolutePath.StartsWith("//?/", StringComparison.Ordinal))
            absolutePath = absolutePath[4..];

        if (absolutePath.StartsWith("//", StringComparison.Ordinal))
        {
            var shareSeparator = absolutePath.IndexOf('/', 2);
            return "file:" + absolutePath[..shareSeparator] + "/" + EscapeSegments(absolutePath[(shareSeparator + 1)..]);
        }

        return "file:///" + absolutePath[..3] + EscapeSegments(absolutePath[3..]);
    }

    private static string EscapeSegments(string path) =>
        string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
}
