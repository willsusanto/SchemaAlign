using System.Text.Json;
using SchemaAlign.Appliers;
using SchemaAlign.Cli.Rendering;
using SchemaAlign.Diff;
using SchemaAlign.Models;

namespace SchemaAlign.Tests.Cli;

public class DiffConsoleRendererTests
{
    [Fact]
    public void RenderMarkdown_ProducesStructuredMarkdown()
    {
        var diff = new SchemaDiff();

        var addedTable = new TableDiff { TableName = "CustomerTable", Schema = "dbo", Kind = DiffKind.Added };
        addedTable.Columns.Add(new ColumnDiff { ColumnName = "Id", Kind = DiffKind.Added });
        diff.Tables.Add(addedTable);

        var markdown = DiffConsoleRenderer.RenderMarkdown(diff);

        markdown.Should().Contain("# Schema Diff Summary");
        markdown.Should().Contain("CustomerTable");
        markdown.Should().Contain("Added");
    }

    [Fact]
    public void RenderJson_ProducesValidJson()
    {
        var diff = new SchemaDiff();

        var modTable = new TableDiff { TableName = "OrderTable", Schema = "sales", Kind = DiffKind.Modified };
        modTable.Columns.Add(new ColumnDiff { ColumnName = "Amount", Kind = DiffKind.Modified, Changes = ChangeDetail.TypeChanged });
        diff.Tables.Add(modTable);

        var json = DiffConsoleRenderer.RenderJson(diff);

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("hasChanges").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("tables").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void RenderMarkdown_WithDetailedColumnsAndForeignKeys_IncludesDetailedSection()
    {
        var diff = new SchemaDiff();

        var modTable = new TableDiff { TableName = "AlphaTable", Schema = "dbo", Kind = DiffKind.Modified };
        modTable.Columns.Add(new ColumnDiff
        {
            ColumnName = "ColA",
            Kind = DiffKind.Modified,
            Changes = ChangeDetail.TypeChanged | ChangeDetail.NullabilityChanged,
            Source = new ColumnSchema { Name = "ColA", Type = StandardType.Int, IsNullable = false },
            Target = new ColumnSchema { Name = "ColA", Type = StandardType.BigInt, IsNullable = true }
        });
        modTable.ForeignKeys.Add(new ForeignKeyDiff
        {
            ConstraintName = "FK_Alpha_Beta",
            Kind = DiffKind.Added,
            Target = new ForeignKeySchema
            {
                ConstraintName = "FK_Alpha_Beta",
                PrincipalTable = "BetaTable",
                DependentTable = "AlphaTable",
                DependentColumn = "ColA",
                PrincipalColumn = "Id"
            }
        });
        diff.Tables.Add(modTable);

        var markdown = DiffConsoleRenderer.RenderMarkdown(diff);

        markdown.Should().Contain("### `AlphaTable`");
        markdown.Should().Contain("ColA");
        markdown.Should().Contain("FK_Alpha_Beta");
    }

    [Fact]
    public void RenderMarkdown_WithFilePreviews_IncludesDiffCodeBlocks()
    {
        var diff = new SchemaDiff();
        var table = new TableDiff { TableName = "AlphaTable", Schema = "dbo", Kind = DiffKind.Added };
        diff.Tables.Add(table);

        var previews = new List<FileDiffPreview>
        {
            new()
            {
                FilePath = "Entities/AlphaEntity.cs",
                DiffKind = DiffKind.Added,
                UnifiedDiff = "@@ -0,0 +1,5 @@\n+namespace Entities;\n+public class AlphaEntity { }"
            }
        };

        var markdown = DiffConsoleRenderer.RenderMarkdown(diff, previews);

        markdown.Should().Contain("## File Previews");
        markdown.Should().Contain("Entities/AlphaEntity.cs");
        markdown.Should().Contain("```diff");
        markdown.Should().Contain("+public class AlphaEntity { }");
    }

    [Fact]
    public void RenderConsole_WhenTablesExceedLimit_TruncatesAndDisplaysOverflowNotice()
    {
        var testConsole = new Spectre.Console.Testing.TestConsole();
        var diff = new SchemaDiff();

        for (var i = 1; i <= 20; i++)
        {
            diff.Tables.Add(new TableDiff
            {
                TableName = $"Table_{i:D2}",
                Schema = "dbo",
                Kind = DiffKind.Added
            });
        }

        DiffConsoleRenderer.RenderConsole(diff, testConsole, detailed: false, maxTables: 5, reportPath: "reports/diff_report.md");

        var output = testConsole.Output;
        output.Should().Contain("Table_01");
        output.Should().Contain("Table_05");
        output.Should().NotContain("Table_06");
        output.Should().Contain("15 more changed tables");
        output.Should().Contain("reports/diff_report.md");
    }
}

