using SchemaAlign.Cli.Services;
using Spectre.Console;

namespace SchemaAlign.Cli.Rendering;

/// <summary>Shows the effective inputs and scope without exposing database credentials.</summary>
public static class ComparisonSetupRenderer
{
    public static void Render(IAnsiConsole console, string current, string target, string mode, bool sync)
    {
        console.MarkupLine($"[grey]Comparison mode: {Markup.Escape(mode)}[/]");
        Show(console, "Existing schema", current);
        Show(console, "Desired schema", target);
        if (sync)
        {
            var destination = SchemaDetectionService.IsConnectionString(current)
                ? current : current.Split(';', ',')[0];
            Show(console, "Write destination", destination);
        }
    }

    private static void Show(IAnsiConsole console, string label, string input)
    {
        var display = SchemaDetectionService.IsConnectionString(input) ? "SQL Server database connection" : input;
        console.MarkupLine($"[grey]{label}: {Markup.Escape(display)}[/]");
    }
}
