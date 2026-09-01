using System.Text.Json;
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
}
