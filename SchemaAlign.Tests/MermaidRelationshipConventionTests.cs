using SchemaAlign.Models;
using SchemaAlign.Readers.Mermaid;

namespace SchemaAlign.Tests;

public class MermaidRelationshipConventionTests
{
    [Fact]
    public void Read_RelationshipsDeclaredBeforeEntities_ResolvesCorrectColumns()
    {
        var mermaid = """
            erDiagram
                tbl_masked_master ||--o{ tbl_masked_detail : "places"

                tbl_masked_master {
                    int ColIdMaster PK
                    string ColName
                }
                tbl_masked_detail {
                    int ColIdDetail PK
                    int ColIdMaster FK
                    decimal ColAmount
                }
            """;

        var reader = new MermaidSchemaReader();
        var schema = reader.Read(mermaid);

        var detailTable = schema.Tables["tbl_masked_detail"];
        detailTable.ForeignKeys.Should().HaveCount(1);
        var fk = detailTable.ForeignKeys[0];
        fk.PrincipalTable.Should().Be("tbl_masked_master");
        fk.PrincipalColumn.Should().Be("ColIdMaster");
        fk.DependentColumn.Should().Be("ColIdMaster");
    }

    [Fact]
    public void Read_HungarianPrefixConventions_StripsPrefixToResolveDependentColumn()
    {
        var mermaid = """
            erDiagram
                MsParentSample {
                    string IdParentSample PK
                    string ColName
                }
                TrChildSample {
                    string IdChildSample PK
                    string IdParentSample FK
                    datetime ColDate
                }
                MsParentSample ||--o{ TrChildSample : ""
            """;

        var reader = new MermaidSchemaReader();
        var schema = reader.Read(mermaid);

        var child = schema.Tables["TrChildSample"];
        child.ForeignKeys.Should().HaveCount(1);
        var fk = child.ForeignKeys[0];
        fk.PrincipalTable.Should().Be("MsParentSample");
        fk.PrincipalColumn.Should().Be("IdParentSample");
        fk.DependentColumn.Should().Be("IdParentSample");
    }

    [Fact]
    public void Read_HungarianPrefixConventions_WhenRelationshipPrecedesEntities_ResolvesColumns()
    {
        var mermaid = """
            erDiagram
                MsParentSample ||--o{ TrChildSample : ""

                MsParentSample {
                    string IdParentSample PK
                    string ColTitle
                }
                TrChildSample {
                    string IdChildSample PK
                    string IdParentSample
                }
            """;

        var reader = new MermaidSchemaReader();
        var schema = reader.Read(mermaid);

        var child = schema.Tables["TrChildSample"];
        child.ForeignKeys.Should().HaveCount(1);
        var fk = child.ForeignKeys[0];
        fk.PrincipalTable.Should().Be("MsParentSample");
        fk.PrincipalColumn.Should().Be("IdParentSample");
        fk.DependentColumn.Should().Be("IdParentSample");
    }

    [Fact]
    public void Read_ConfigurableCustomPrefixes_ResolvesCustomPrefixDependentColumn()
    {
        var mermaid = """
            erDiagram
                DimAccountSample {
                    int IdAccountSample PK
                }
                FactLedgerSample {
                    int IdLedgerSample PK
                    int IdAccountSample
                }
                DimAccountSample ||--o{ FactLedgerSample : ""
            """;

        var reader = new MermaidSchemaReader(new MermaidReaderOptions
        {
            TablePrefixes = new List<string> { "Dim", "Fact" }
        });
        var schema = reader.Read(mermaid);

        var ledger = schema.Tables["FactLedgerSample"];
        ledger.ForeignKeys.Should().HaveCount(1);
        var fk = ledger.ForeignKeys[0];
        fk.PrincipalTable.Should().Be("DimAccountSample");
        fk.PrincipalColumn.Should().Be("IdAccountSample");
        fk.DependentColumn.Should().Be("IdAccountSample");
    }
}
