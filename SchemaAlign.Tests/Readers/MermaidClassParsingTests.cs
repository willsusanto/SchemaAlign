using SchemaAlign.Models;
using SchemaAlign.Readers.Mermaid;
using Xunit;

namespace SchemaAlign.Tests.Readers;

public class MermaidClassParsingTests
{
    private readonly MermaidSchemaReader _reader = new();

    [Fact]
    public void Parse_ClassDefLines_ShouldBeIgnoredWithoutRegisteringAsTables()
    {
        var mermaid = """
            erDiagram
                classDef existingTbl fill:#c8e6c9,stroke:#2e7d32,color:#1b1b1b
                classDef newTbl fill:#bbdefb,stroke:#1565c0,color:#1b1b1b
                classDef updatedTbl fill:#FFE8CB,stroke:#E89C3D,color:#1b1b1b

                TblAlpha {
                    int ColA PK
                    string ColB
                }
            """;

        var schema = _reader.Read(mermaid);

        schema.Tables.Should().HaveCount(1);
        schema.Tables.Should().ContainKey("TblAlpha");
        schema.Tables.Should().NotContainKey("classDef");
        schema.Tables.Should().NotContainKey("existingTbl");
    }

    [Fact]
    public void Parse_MultiTableClassStatements_ShouldAssignClassToAllTables()
    {
        var mermaid = """
            erDiagram
                classDef existingTbl fill:#c8e6c9,stroke:#2e7d32,color:#1b1b1b
                classDef newTbl fill:#bbdefb,stroke:#1565c0,color:#1b1b1b
                classDef updatedTbl fill:#FFE8CB,stroke:#E89C3D,color:#1b1b1b

                class TblAlpha, TblBeta, TblGamma existingTbl
                class TblDelta, TblEpsilon updatedTbl
                class TblZeta, TblEta newTbl

                TblAlpha {
                    int ColA PK
                }
                TblBeta {
                    int ColB PK
                }
                TblDelta {
                    int ColD PK
                }
                TblZeta {
                    int ColZ PK
                }
            """;

        var schema = _reader.Read(mermaid);

        schema.Tables.Should().ContainKey("TblAlpha");
        schema.Tables["TblAlpha"].HasClass("existingTbl").Should().BeTrue();

        schema.Tables.Should().ContainKey("TblBeta");
        schema.Tables["TblBeta"].HasClass("existingTbl").Should().BeTrue();

        schema.Tables.Should().ContainKey("TblGamma");
        schema.Tables["TblGamma"].HasClass("existingTbl").Should().BeTrue();

        schema.Tables.Should().ContainKey("TblDelta");
        schema.Tables["TblDelta"].HasClass("updatedTbl").Should().BeTrue();

        schema.Tables.Should().ContainKey("TblEpsilon");
        schema.Tables["TblEpsilon"].HasClass("updatedTbl").Should().BeTrue();

        schema.Tables.Should().ContainKey("TblZeta");
        schema.Tables["TblZeta"].HasClass("newTbl").Should().BeTrue();

        schema.Tables.Should().ContainKey("TblEta");
        schema.Tables["TblEta"].HasClass("newTbl").Should().BeTrue();
    }

    [Fact]
    public void Parse_ClassStatement_WithArbitraryWhitespace_ShouldParseProperly()
    {
        var mermaid = """
            erDiagram
                class    TblAlpha   ,    TblBeta      existingTbl   
                class  TblGamma   newTbl

                TblAlpha {
                    int ColA PK
                }
            """;

        var schema = _reader.Read(mermaid);

        schema.Tables["TblAlpha"].HasClass("existingTbl").Should().BeTrue();
        schema.Tables["TblBeta"].HasClass("existingTbl").Should().BeTrue();
        schema.Tables["TblGamma"].HasClass("newTbl").Should().BeTrue();
    }

    [Fact]
    public void Parse_ClassStatement_CaseInsensitiveCheck_ShouldReturnTrue()
    {
        var mermaid = """
            erDiagram
                class TblAlpha newTbl
                TblAlpha {
                    int ColA PK
                }
            """;

        var schema = _reader.Read(mermaid);

        schema.Tables["TblAlpha"].HasClass("newTbl").Should().BeTrue();
        schema.Tables["TblAlpha"].HasClass("NEWTBL").Should().BeTrue();
        schema.Tables["TblAlpha"].HasClass("NewTbl").Should().BeTrue();
        schema.Tables["TblAlpha"].HasClass("existingTbl").Should().BeFalse();
    }

    [Fact]
    public void Parse_InlineClass_OnEntityBlock_ShouldAssignClass()
    {
        var mermaid = """
            erDiagram
                TblAlpha:::newTbl {
                    int ColA PK
                    string ColB
                }
                TblBeta:::updatedTbl["Masked Table Description"] {
                    int ColC PK
                }
            """;

        var schema = _reader.Read(mermaid);

        schema.Tables.Should().ContainKey("TblAlpha");
        schema.Tables["TblAlpha"].HasClass("newTbl").Should().BeTrue();
        schema.Tables["TblAlpha"].Columns.Should().ContainKey("ColA");
        schema.Tables["TblAlpha"].Columns.Should().ContainKey("ColB");

        schema.Tables.Should().ContainKey("TblBeta");
        schema.Tables["TblBeta"].HasClass("updatedTbl").Should().BeTrue();
        schema.Tables["TblBeta"].Comment.Should().Be("Masked Table Description");
    }

    [Fact]
    public void Parse_InlineClass_SingleLineEntity_ShouldAssignClass()
    {
        var mermaid = """
            erDiagram
                TblAlpha:::newTbl { int ColA PK; string ColB }
            """;

        var schema = _reader.Read(mermaid);

        schema.Tables.Should().ContainKey("TblAlpha");
        schema.Tables["TblAlpha"].HasClass("newTbl").Should().BeTrue();
        schema.Tables["TblAlpha"].Columns.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_ClassStatement_WhenTableEncounteredBeforeEntityBlock_PreservesColumns()
    {
        var mermaid = """
            erDiagram
                class TblAlpha newTbl

                TblAlpha {
                    int ColA PK
                    string ColB
                }
            """;

        var schema = _reader.Read(mermaid);

        schema.Tables.Should().ContainKey("TblAlpha");
        var table = schema.Tables["TblAlpha"];
        table.HasClass("newTbl").Should().BeTrue();
        table.Columns.Should().HaveCount(2);
        table.Columns.Should().ContainKey("ColA");
        table.Columns.Should().ContainKey("ColB");
    }
}
