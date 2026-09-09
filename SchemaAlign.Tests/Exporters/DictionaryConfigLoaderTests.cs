using SchemaAlign.Exporters.Excel;

namespace SchemaAlign.Tests.Exporters;

public class DictionaryConfigLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public DictionaryConfigLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SchemaAlign_CfgTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void LoadOptions_WhenNoConfigAndNoCli_ReturnsStandardDefaults()
    {
        var options = DictionaryConfigLoader.Load(
            configPath: null,
            cliAid: null,
            cliIp: null,
            cliDb: null,
            cliTitle: null,
            sourcePath: "masked-diagram.mmd",
            outputPath: "masked-output.xlsx");

        options.Aid.Should().Be("1191");
        options.IpDomain.Should().Be("ssg5-newlibrary-dev.binus.db");
        options.DatabaseName.Should().Be("LIBRARY_DB");
        options.SystemTitle.Should().Be("New Library System");
        options.SourcePath.Should().Be("masked-diagram.mmd");
        options.OutputPath.Should().Be("masked-output.xlsx");
    }

    [Fact]
    public void LoadOptions_WhenConfigFileExists_LoadsValuesFromConfig()
    {
        var configFile = Path.Combine(_tempDir, "schemaalign.json");
        File.WriteAllText(configFile, """
            {
                "dictionary": {
                    "aid": "4321",
                    "ip": "custom-ip.domain.local",
                    "database": "CUSTOM_DB",
                    "systemTitle": "Custom Title System"
                }
            }
            """);

        var options = DictionaryConfigLoader.Load(
            configPath: configFile,
            cliAid: null,
            cliIp: null,
            cliDb: null,
            cliTitle: null,
            sourcePath: "diagram.mmd",
            outputPath: "out.xlsx");

        options.Aid.Should().Be("4321");
        options.IpDomain.Should().Be("custom-ip.domain.local");
        options.DatabaseName.Should().Be("CUSTOM_DB");
        options.SystemTitle.Should().Be("Custom Title System");
    }

    [Fact]
    public void LoadOptions_WhenCliOverridesProvided_OverridesConfigFile()
    {
        var configFile = Path.Combine(_tempDir, "schemaalign.json");
        File.WriteAllText(configFile, """
            {
                "dictionary": {
                    "aid": "4321",
                    "ip": "custom-ip.domain.local",
                    "database": "CUSTOM_DB",
                    "systemTitle": "Custom Title System"
                }
            }
            """);

        var options = DictionaryConfigLoader.Load(
            configPath: configFile,
            cliAid: "9999",
            cliIp: "cli-ip.override.org",
            cliDb: "OVERRIDE_DB",
            cliTitle: "Override Title",
            sourcePath: "diagram.mmd",
            outputPath: "out.xlsx");

        options.Aid.Should().Be("9999");
        options.IpDomain.Should().Be("cli-ip.override.org");
        options.DatabaseName.Should().Be("OVERRIDE_DB");
        options.SystemTitle.Should().Be("Override Title");
    }

    [Fact]
    public void LoadOptions_DefaultHighlightOptions_AreSet()
    {
        var options = DictionaryConfigLoader.Load(
            configPath: null,
            cliAid: null,
            cliIp: null,
            cliDb: null,
            cliTitle: null,
            sourcePath: "diagram.mmd",
            outputPath: "out.xlsx");

        options.HighlightClasses.Should().BeEquivalentTo(new[] { "newTbl", "updatedTbl" });
        options.HighlightColor.Should().Be("#ffcccc");
    }

    [Fact]
    public void LoadOptions_WhenConfigContainsHighlightOptions_LoadsCustomHighlightClassesAndColor()
    {
        var configFile = Path.Combine(_tempDir, "schemaalign.json");
        File.WriteAllText(configFile, """
            {
                "dictionary": {
                    "highlightClasses": ["specialTbl", "auditTbl"],
                    "highlightColor": "#e0f7fa"
                }
            }
            """);

        var options = DictionaryConfigLoader.Load(
            configPath: configFile,
            cliAid: null,
            cliIp: null,
            cliDb: null,
            cliTitle: null,
            sourcePath: "diagram.mmd",
            outputPath: "out.xlsx");

        options.HighlightClasses.Should().BeEquivalentTo(new[] { "specialTbl", "auditTbl" });
        options.HighlightColor.Should().Be("#e0f7fa");
    }
}
