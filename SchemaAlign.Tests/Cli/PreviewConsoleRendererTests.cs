using SchemaAlign.Appliers;
using SchemaAlign.Cli.Rendering;
using SchemaAlign.Diff;
using Spectre.Console.Testing;

namespace SchemaAlign.Tests.Cli;

public class PreviewConsoleRendererTests
{
    [Fact]
    public void RenderPreviews_WhenPreviewsEmpty_OutputsNotice()
    {
        var console = new TestConsole();
        PreviewConsoleRenderer.RenderPreviews(new List<FileDiffPreview>(), console);

        console.Output.Should().Contain("No file changes to preview");
    }

    [Fact]
    public void RenderPreviews_WhenUnderLimit_RendersPanels()
    {
        var console = new TestConsole();
        console.Profile.Width = 120;
        var previews = new List<FileDiffPreview>
        {
            new()
            {
                FilePath = "Entities/Alpha.cs",
                DiffKind = DiffKind.Added,
                UnifiedDiff = "+public class Alpha { }"
            }
        };

        PreviewConsoleRenderer.RenderPreviews(previews, console, maxDetailedPanels: 3);

        console.Output.Should().Contain("Unified Diff Preview");
        console.Output.Should().Contain("Entities/Alpha.cs");
        console.Output.Should().Contain("+public class Alpha { }");
    }

    [Fact]
    public void RenderPreviews_WhenExceedingLimit_RendersCompactTableAndReportLink()
    {
        var console = new TestConsole();
        var previews = new List<FileDiffPreview>();
        for (var i = 1; i <= 6; i++)
        {
            previews.Add(new FileDiffPreview
            {
                FilePath = $"Entities/AlphaEntity_{i:D2}.cs",
                DiffKind = DiffKind.Modified,
                UnifiedDiff = $"--- a/Entities/AlphaEntity_{i:D2}.cs\n+++ b/Entities/AlphaEntity_{i:D2}.cs\n@@ -1,1 +1,2 @@\n-old\n+new"
            });
        }

        PreviewConsoleRenderer.RenderPreviews(previews, console, maxDetailedPanels: 3, reportPath: "reports/sync_preview.md");

        console.Output.Should().Contain("File Preview Summary (6 files):");
        console.Output.Should().Contain("AlphaEntity_01.cs");
        console.Output.Should().Contain("AlphaEntity_06.cs");
        console.Output.Should().Contain("reports/sync_preview.md");
        // Unified diff lines should NOT flood the console output in compact mode
        console.Output.Should().NotContain("@@ -1,1 +1,2 @@");
    }
}
