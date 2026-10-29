using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SchemaAlign.Appliers.CSharp;
using SchemaAlign.Readers.Mermaid;

namespace SchemaAlign.Tests;

public class EntityGenerationRegressionTests
{
    private const string Diagram = """
        erDiagram
            MsActor ||--o{ TrBatch : ""
            TrBatch ||--o{ TrEntry : ""
            MsActor ||--o{ TrEntry : ""
            MsAsset ||--o{ TrEntry : ""
            MsActor { string IdActor PK }
            MsAsset { string IdAsset PK }
            LtState { string IdState PK }
            TrBatch {
                string IdBatch PK
                string Label
                string IdActorOpen FK
                string IdActorClose FK "NULL"
            }
            TrEntry {
                string IdEntry PK
                string IdBatch FK
                string IdAsset FK
                string IdActorCheck FK
                string IdState FK
            }
        """;

    [Fact]
    public void Read_RoleForeignKeysAndMissingEdges_ResolveEachPrincipalWithoutReusingColumns()
    {
        var schema = new MermaidSchemaReader().Read(Diagram);
        var entry = schema.Tables["TrEntry"];
        entry.ForeignKeys.Should().HaveCount(4);
        entry.ForeignKeys.Should().ContainSingle(f => f.DependentColumn == "IdAsset" && f.PrincipalTable == "MsAsset");
        entry.ForeignKeys.Should().ContainSingle(f => f.DependentColumn == "IdActorCheck" && f.PrincipalTable == "MsActor");
        entry.ForeignKeys.Should().ContainSingle(f => f.DependentColumn == "IdState" && f.PrincipalTable == "LtState");
        var batch = schema.Tables["TrBatch"];
        batch.ForeignKeys.Should().HaveCount(2);
        batch.ForeignKeys.Should().ContainSingle(f => f.DependentColumn == "IdActorClose" && f.PrincipalTable == "MsActor");
    }

    [Fact]
    public void Generate_RoleForeignKeys_EmitOneScalarBindingAndCorrectReferencePerColumn()
    {
        var table = new MermaidSchemaReader().Read(Diagram).Tables["TrEntry"];
        var code = CSharpEntityGenerator.Generate(table, new CSharpApplierOptions { ForeignKeyPlacement = ForeignKeyPlacement.Scalar });
        var properties = CSharpSyntaxTree.ParseText(code).GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>().ToDictionary(p => p.Identifier.Text);
        foreach (var (scalar, navigation, type) in new[]
        {
            ("IdBatch", "Batch", "TrBatch?"), ("IdAsset", "Asset", "MsAsset?"),
            ("IdActorCheck", "ActorCheck", "MsActor?"), ("IdState", "State", "LtState?")
        })
        {
            properties[scalar].AttributeLists.SelectMany(a => a.Attributes).Where(a => a.Name.ToString() == "ForeignKey")
                .Should().ContainSingle().Which.ArgumentList!.Arguments.Single().ToString().Should().Be($"\"{navigation}\"");
            properties[navigation].Type.ToString().Should().Be(type);
        }
        properties.Values.Should().HaveCount(9);
    }

    [Fact]
    public void Read_ForeignKeyMarkerWithAmbiguousPrincipal_DoesNotGuess()
    {
        var schema = new MermaidSchemaReader().Read("""
            erDiagram
                MsActor { string IdActor PK }
                LtActor { string IdActor PK }
                TrEntry {
                    string IdEntry PK
                    string IdActorCheck FK
                }
            """);
        schema.Tables["TrEntry"].ForeignKeys.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generate_RequiredStrings_HaveExplicitAnnotations(bool nullableReferenceTypes)
    {
        var table = new MermaidSchemaReader().Read(Diagram).Tables["TrBatch"];
        var code = CSharpEntityGenerator.Generate(table, new CSharpApplierOptions { UseNullableReferenceTypes = nullableReferenceTypes });
        var properties = CSharpSyntaxTree.ParseText(code).GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>().ToDictionary(p => p.Identifier.Text);
        bool Required(string name) => properties[name].AttributeLists.SelectMany(a => a.Attributes).Any(a => a.Name.ToString() == "Required");
        Required("Label").Should().BeTrue();
        Required("IdActorOpen").Should().BeTrue();
        Required("IdActorClose").Should().BeFalse();
        Required("IdBatch").Should().BeFalse();
    }
}
