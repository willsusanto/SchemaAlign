using SchemaAlign.Appliers;
using Spectre.Console;

namespace SchemaAlign.Cli.Rendering;

/// <summary>
/// Renders unified diff previews in colorized Spectre.Console panels.
/// </summary>
public static class PreviewConsoleRenderer
{
    /// <summary>
    /// Renders unified diff previews for a list of file diffs to the console.
    /// </summary>
    /// <param name="previews">Collection of file diff previews.</param>
    /// <param name="console">The AnsiConsole instance.</param>
    public static void RenderPreviews(IReadOnlyList<FileDiffPreview> previews, IAnsiConsole console)
    {
        if (previews.Count == 0)
        {
            console.MarkupLine("[yellow]No file changes to preview.[/]");
            return;
        }

        console.MarkupLine($"[bold blue]Unified Diff Preview ({previews.Count} files):[/]\n");

        foreach (var preview in previews)
        {
            var headerColor = preview.DiffKind switch
            {
                Diff.DiffKind.Added => "green",
                Diff.DiffKind.Deleted => "red",
                _ => "yellow"
            };

            var header = $"[{headerColor}]{preview.DiffKind}: {Markup.Escape(preview.FilePath)}[/]";

            if (string.IsNullOrWhiteSpace(preview.UnifiedDiff) && string.IsNullOrWhiteSpace(preview.NewContent))
            {
                console.Write(new Panel("[grey](No textual diff available)[/]")
                {
                    Header = new PanelHeader(header),
                    Border = BoxBorder.Rounded
                });
                continue;
            }

            if (preview.OriginalContent == null && !string.IsNullOrWhiteSpace(preview.NewContent))
            {
                var contentLines = preview.NewContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                var contentSb = new System.Text.StringBuilder();
                foreach (var line in contentLines)
                {
                    contentSb.AppendLine($"[green]{Markup.Escape(line)}[/]");
                }

                console.Write(new Panel(contentSb.ToString().TrimEnd())
                {
                    Header = new PanelHeader(header),
                    Border = BoxBorder.Rounded
                });
                continue;
            }

            var lines = preview.UnifiedDiff.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var sb = new System.Text.StringBuilder();

            foreach (var line in lines)
            {
                if (line.StartsWith("+++") || line.StartsWith("---"))
                {
                    sb.AppendLine($"[bold]{Markup.Escape(line)}[/]");
                }
                else if (line.StartsWith("+"))
                {
                    sb.AppendLine($"[green]{Markup.Escape(line)}[/]");
                }
                else if (line.StartsWith("-"))
                {
                    sb.AppendLine($"[red]{Markup.Escape(line)}[/]");
                }
                else if (line.StartsWith("@@"))
                {
                    sb.AppendLine($"[blue]{Markup.Escape(line)}[/]");
                }
                else
                {
                    sb.AppendLine($"[grey]{Markup.Escape(line)}[/]");
                }
            }

            console.Write(new Panel(sb.ToString().TrimEnd())
            {
                Header = new PanelHeader(header),
                Border = BoxBorder.Rounded
            });
        }
    }
}
