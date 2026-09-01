using SchemaAlign.Cli.Services;
using SchemaAlign.Readers.CSharp;
using SchemaAlign.Readers.Mermaid;

namespace SchemaAlign.Tests.Cli;

public class SchemaDetectionServiceTests : IDisposable
{
    private readonly string _testDir;

    public SchemaDetectionServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SchemaAlign_DetectTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    [Theory]
    [InlineData("schema.mmd")]
    [InlineData("diagram.mermaid")]
    [InlineData("PATH/TO/SCHEMA.MMD")]
    public void DetectReader_ForMermaidFiles_ReturnsMermaidSchemaReader(string path)
    {
        var service = new SchemaDetectionService();
        var reader = service.DetectReader(path);

        reader.Should().NotBeNull();
        reader.Should().BeOfType<MermaidSchemaReader>();
    }

    [Fact]
    public void DetectReader_ForDirectoryWithCSharpFiles_ReturnsCSharpEntityReader()
    {
        var csDir = Path.Combine(_testDir, "Entities");
        Directory.CreateDirectory(csDir);
        File.WriteAllText(Path.Combine(csDir, "TestEntity.cs"), "public class TestEntity { public int Id { get; set; } }");

        var service = new SchemaDetectionService();
        var reader = service.DetectReader(csDir);

        reader.Should().NotBeNull();
        reader.Should().BeOfType<CSharpEntityReader>();
    }

    [Fact]
    public void DetectReader_ForSingleCSharpFile_ReturnsCSharpEntityReader()
    {
        var csFile = Path.Combine(_testDir, "OrderEntity.cs");
        File.WriteAllText(csFile, "public class OrderEntity { public int Id { get; set; } }");

        var service = new SchemaDetectionService();
        var reader = service.DetectReader(csFile);

        reader.Should().NotBeNull();
        reader.Should().BeOfType<CSharpEntityReader>();
    }

    [Theory]
    [InlineData("schema.json")]
    [InlineData("unknown.txt")]
    public void DetectReader_ForUnsupportedExtension_ThrowsNotSupportedException(string path)
    {
        var service = new SchemaDetectionService();
        var act = () => service.DetectReader(path);

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*'{path}'*");
    }

    [Theory]
    [InlineData("models.mmd", TargetType.Mermaid)]
    [InlineData("src/Entities", TargetType.CSharp)]
    [InlineData("migration.sql", TargetType.SqlServerScript)]
    [InlineData("Server=localhost;Database=TestDb;Trusted_Connection=True;", TargetType.SqlServerDatabase)]
    public void DetectTargetType_IdentifiesExpectedTarget(string pathOrConn, TargetType expected)
    {
        var service = new SchemaDetectionService();
        var targetType = service.DetectTargetType(pathOrConn);

        targetType.Should().Be(expected);
    }
}
