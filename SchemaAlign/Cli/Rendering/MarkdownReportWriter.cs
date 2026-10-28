using System.Text.RegularExpressions;
using SchemaAlign.Appliers;
using SchemaAlign.Diff;

namespace SchemaAlign.Cli.Rendering;

internal static class MarkdownReportWriter
{
    public static void Write(string path, SchemaDiff diff, string title, string current, string target,
        IReadOnlyList<FileDiffPreview>? previews = null)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, DiffConsoleRenderer.RenderMarkdown(diff, previews,
            $"{title}: {SourceLabel(current)} -> {SourceLabel(target)}"));
    }

    private static string SourceLabel(string source)
    {
        // Replace the entire connection description, including credentials supplied by config.
        return Regex.IsMatch(source,
            @"(?:^|;)\s*(?:Server|Data Source|Address|Addr|Network Address|Database|Initial Catalog|User ID|UID|Password|Pwd|Access Token)\s*=",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            ? "[database connection]"
            : source;
    }
}
