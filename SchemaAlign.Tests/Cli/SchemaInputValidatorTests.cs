using SchemaAlign.Cli.Services;

namespace SchemaAlign.Tests.Cli;

public class SchemaInputValidatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SchemaAlign_Path_" + Guid.NewGuid().ToString("N"));
    public SchemaInputValidatorTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void RelativeQuotedPath_ResolvesAgainstWorkingDirectory()
    {
        var path = CreateFile("masked.sql");
        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
        SchemaInputValidator.ValidateAndNormalize("  \"" + relative + "\"  ").Should().Be(path);
    }

    [Theory]
    [InlineData(";")]
    [InlineData(",")]
    public void MultipleCSharpPaths_ValidatesEveryEntryAndRemovesQuotes(string separator)
    {
        var file = CreateFile("MaskedRecord.cs");
        var directory = Path.Combine(_directory, "Models");
        Directory.CreateDirectory(directory);
        SchemaInputValidator.ValidateAndNormalize($"\"{file}\"{separator}\"{directory}\"")
            .Should().Be(file + ";" + directory);
        var missing = Path.Combine(_directory, "Missing.cs");
        Action validate = () => SchemaInputValidator.ValidateAndNormalize(file + separator + missing);
        validate.Should().Throw<ArgumentException>().WithMessage("*File not found:*Missing.cs*");
    }

    [Theory]
    [InlineData("Server=masked-host;Database=masked-db;Integrated Security=true")]
    [InlineData("Data Source=masked-host;Initial Catalog=masked-db;Integrated Security=true")]
    public void ConnectionString_BypassesFilesystemValidation(string connectionString)
        => SchemaInputValidator.ValidateAndNormalize(connectionString).Should().Be(connectionString);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyInput_IsRejected(string input)
    {
        Action validate = () => SchemaInputValidator.ValidateAndNormalize(input);
        validate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ExistingUnsupportedFile_IsRejected()
    {
        var path = CreateFile("masked.txt");
        Action validate = () => SchemaInputValidator.ValidateAndNormalize(path);
        validate.Should().Throw<ArgumentException>().WithMessage("*Unsupported schema file*");
    }

    [Fact]
    public void MultipleNonCSharpFiles_AreRejected()
    {
        var first = CreateFile("masked.sql");
        var second = CreateFile("other.sql");
        Action validate = () => SchemaInputValidator.ValidateAndNormalize(first + ";" + second);
        validate.Should().Throw<ArgumentException>().WithMessage("*Multiple paths*only*C#*");
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "");
        return path;
    }
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
