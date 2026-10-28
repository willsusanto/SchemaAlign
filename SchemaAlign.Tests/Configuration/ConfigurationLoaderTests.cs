using AwesomeAssertions;
using SchemaAlign.Configuration;
using Xunit;

namespace SchemaAlign.Tests.Configuration;

public class ConfigurationLoaderTests
{
    [Theory]
    [InlineData("{\"source\":\"masked\"}", "source")]
    [InlineData("{\"mode\":\"snapshot\"}", "mode")]
    [InlineData("{\"allowDrop\":true}", "allowDrop")]
    [InlineData("{\"output\":\"json\"}", "output")]
    [InlineData("{\"detailed\":true}", "detailed")]
    [InlineData("{\"csharp\":{\"tablePrefixes\":[\"masked_\"]}}", "tablePrefixes")]
    [InlineData("{\"curent\":\"masked\"}", "curent")]
    [InlineData("{\"csharp\":{\"namespce\":\"Masked.Entities\"}}", "namespce")]
    [InlineData("{\"csharp\":{\"foreignKeyPlacement\":\"invalid\"}}", "foreignKeyPlacement")]
    [InlineData("{\"csharp\":null}", "csharp")]
    [InlineData("{\"tablePrefixes\":null}", "tablePrefixes")]
    [InlineData("{\"csharp\":{\"usings\":[null]}}", "usings")]
    public void ParseJson_UnsupportedSettings_ReturnsActionableError(string json, string setting)
    {
        var action = () => ConfigurationLoader.Parse(json);
        action.Should().Throw<System.Text.Json.JsonException>().WithMessage("*" + setting + "*");
    }

    [Theory]
    [InlineData("{\"current\":false}", "current")]
    [InlineData("{\"target\":[]}", "target")]
    [InlineData("{\"current\":\"\"}", "current")]
    [InlineData("{\"csharp\":{\"namespace\":true}}", "namespace")]
    [InlineData("{\"csharp\":{\"baseClass\":[]}}", "baseClass")]
    [InlineData("{\"csharp\":{\"useDataAnnotations\":\"false\"}}", "useDataAnnotations")]
    [InlineData("{\"csharp\":{\"useFileScopedNamespaces\":null}}", "useFileScopedNamespaces")]
    [InlineData("{\"csharp\":{\"classAttributes\":null}}", "classAttributes")]
    [InlineData("{\"csharp\":{\"omitInheritedColumns\":[\"\"]}}", "omitInheritedColumns")]
    [InlineData("{\"current\":\"masked.cs\",\"Current\":\"other.cs\"}", "Current")]
    public void ParseJson_InvalidPropertyTypes_AreRejected(string json, string key)
    {
        var action = () => ConfigurationLoader.Parse(json);
        action.Should().Throw<System.Text.Json.JsonException>().WithMessage("*" + key + "*");
    }

    [Theory]
    [InlineData(true, "scalar")]
    [InlineData(false, "navigation")]
    public void ParseJson_AllCSharpValuesAndCaseInsensitiveKeys_ArePreserved(bool enabled, string placement)
    {
        var json = $$"""
            { "CURRENT":"masked.cs", "TARGET":"masked.mmd", "tablePrefixes":["masked_", "MASKED_"],
              "CSHARP": { "namespace":"Masked.Entities", "baseClass":"MaskedBase",
                "usings":["Masked.Core"], "classAttributes":["MaskedMarker"],
                "omitInheritedColumns":["MaskedStamp"],
                "useFileScopedNamespaces":{{enabled.ToString().ToLowerInvariant()}},
                "useDataAnnotations":{{enabled.ToString().ToLowerInvariant()}},
                "foreignKeyPlacement":"{{placement}}" } }
            """;
        var config = ConfigurationLoader.Parse(json);
        config.Current.Should().Be("masked.cs");
        config.Target.Should().Be("masked.mmd");
        config.TablePrefixes.Should().Equal("masked_");
        config.CSharp.Namespace.Should().Be("Masked.Entities");
        config.CSharp.BaseClass.Should().Be("MaskedBase");
        config.CSharp.Usings.Should().Equal("Masked.Core");
        config.CSharp.ClassAttributes.Should().Equal("[MaskedMarker]");
        config.CSharp.OmitInheritedColumns.Should().Equal("MaskedStamp");
        config.CSharp.UseFileScopedNamespaces.Should().Be(enabled);
        config.CSharp.UseDataAnnotations.Should().Be(enabled);
        config.CSharp.ForeignKeyPlacement.Should().Be(placement);
    }

    [Fact]
    public void ParseJson_EmptyConfiguration_UsesOptionalDefaults()
    {
        var config = ConfigurationLoader.Parse("{}");
        config.Current.Should().BeNull();
        config.Target.Should().BeNull();
        config.TablePrefixes.Should().BeEmpty();
        config.CSharp.Usings.Should().BeEmpty();
        config.CSharp.ClassAttributes.Should().BeEmpty();
        config.CSharp.OmitInheritedColumns.Should().BeEmpty();
        config.CSharp.Namespace.Should().BeNull();
        config.CSharp.BaseClass.Should().BeNull();
        config.CSharp.UseFileScopedNamespaces.Should().BeNull();
        config.CSharp.UseDataAnnotations.Should().BeNull();
        config.CSharp.ForeignKeyPlacement.Should().BeNull();
    }

    [Fact]
    public async Task FindAndLoadAsync_UsesNearestFileWithoutParentMerging_AndStopsAtGitRoot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SchemaAlign_MaskedDiscovery_" + Guid.NewGuid().ToString("N"));
        var child = Path.Combine(directory, "MaskedChild");
        Directory.CreateDirectory(child);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, ".schemaalign.json"), """{"csharp":{"baseClass":"MaskedBase"}}""");
            await File.WriteAllTextAsync(Path.Combine(child, ".schemaalign.json"), """{"csharp":{"namespace":"Masked.Entities"}}""");
            var config = await ConfigurationLoader.FindAndLoadAsync(child);
            config!.CSharp.Namespace.Should().Be("Masked.Entities");
            config.CSharp.BaseClass.Should().BeNull();
            File.Delete(Path.Combine(child, ".schemaalign.json"));
            File.WriteAllText(Path.Combine(child, ".git"), "masked-worktree-marker");
            (await ConfigurationLoader.FindAndLoadAsync(child)).Should().BeNull();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void FindConfigFile_LegacyName_ExplainsCanonicalFilename()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SchemaAlign_MaskedLegacy_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "schemaalign.json"), "{}");
            var action = () => ConfigurationLoader.FindConfigFile(directory);
            action.Should().Throw<System.Text.Json.JsonException>().WithMessage("*Rename*.schemaalign.json*");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task LoadAsync_ConnectionStrings_AreNotSplitOrRebased()
    {
        var path = Path.GetTempFileName();
        const string connection = "Server=masked-host;Database=masked-store;";
        try
        {
            await File.WriteAllTextAsync(path, """{"current":"Server=masked-host;Database=masked-store;"}""");
            (await ConfigurationLoader.LoadAsync(path))!.Current.Should().Be(connection);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task LoadAsync_ResolvesSchemaPathsRelativeToConfigFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SchemaAlign_MaskedConfig_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, ".schemaalign.json");
            await File.WriteAllTextAsync(path, """{"current":"./Masked.Entities;./Masked.Base.cs","target":"./masked.mmd"}""");
            var config = await ConfigurationLoader.LoadAsync(path);
            config!.Current.Should().Be(Path.Combine(directory, "Masked.Entities") + ";" + Path.Combine(directory, "Masked.Base.cs"));
            config.Target.Should().Be(Path.Combine(directory, "masked.mmd"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task LoadAsync_MissingExplicitFile_ThrowsInsteadOfIgnoringIt()
    {
        var action = () => ConfigurationLoader.LoadAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        await action.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public void ParseJson_EquivalentUsingsAndAttributes_AreNormalizedOnce()
    {
        var config = ConfigurationLoader.Parse("""
            {"csharp":{"usings":["Masked.Core"," using Masked.Core; ","Masked.Core;"],
            "classAttributes":["MaskedMarker","[MaskedMarker]"," [MaskedMarker] "]}}
            """);
        config.CSharp.Usings.Should().Equal("Masked.Core");
        config.CSharp.ClassAttributes.Should().Equal("[MaskedMarker]");
    }
    [Fact]
    public void ParseJson_ValidSectionedConfig_ParsesAllPropertiesCorrectly()
    {
        var json = """
            {
              "current": "./src/Models",
              "target": "./docs/architecture.mmd",

              "csharp": {
                "namespace": "MockOrg.Domain.Entities",
                "baseClass": "MockEntityBase",
                "useFileScopedNamespaces": false,
                "useDataAnnotations": true,
                "usings": [
                  "MockOrg.Pattern.Core",
                  "MockOrg.Domain.Base"
                ],
                "classAttributes": [
                  "[TableGroup(\"CoreStore\")]",
                  "[CustomAuditTracking]"
                ],
                "omitInheritedColumns": [
                  "AuditCreatedBy",
                  "AuditCreatedAt",
                  "AuditUpdatedBy",
                  "AuditUpdatedAt",
                  "RowStatus"
                ]
              }
            }
            """;

        var config = ConfigurationLoader.Parse(json);

        config.Should().NotBeNull();
        config.Current.Should().Be("./src/Models");
        config.Target.Should().Be("./docs/architecture.mmd");

        config.CSharp.Should().NotBeNull();
        config.CSharp.Namespace.Should().Be("MockOrg.Domain.Entities");
        config.CSharp.BaseClass.Should().Be("MockEntityBase");
        config.CSharp.UseFileScopedNamespaces.Should().Be(false);
        config.CSharp.UseDataAnnotations.Should().Be(true);
        config.CSharp.Usings.Should().Contain("MockOrg.Pattern.Core");
        config.CSharp.Usings.Should().Contain("MockOrg.Domain.Base");
        config.CSharp.ClassAttributes.Should().Contain("[TableGroup(\"CoreStore\")]");
        config.CSharp.ClassAttributes.Should().Contain("[CustomAuditTracking]");
        config.CSharp.OmitInheritedColumns.Should().Contain("AuditCreatedBy");
        config.CSharp.OmitInheritedColumns.Should().Contain("RowStatus");
    }

    [Fact]
    public async Task FindAndLoadAsync_TraversesUpwardToFindConfigFile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"SchemaAlign_Config_{Guid.NewGuid():N}");
        var subDir = Path.Combine(tempRoot, "src", "Nested", "Project");
        Directory.CreateDirectory(subDir);

        try
        {
            var configContent = """
                {

                  "csharp": {
                    "baseClass": "TenantRecordBase",
                    "usings": ["Tenant.Framework.Pattern"]
                  }
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(tempRoot, ".schemaalign.json"), configContent);

            // Starting search from subDir should find .schemaalign.json in tempRoot
            var loadedConfig = await ConfigurationLoader.FindAndLoadAsync(startDirectory: subDir);

            loadedConfig.Should().NotBeNull();

            loadedConfig.CSharp.BaseClass.Should().Be("TenantRecordBase");
            loadedConfig.CSharp.Usings.Should().Contain("Tenant.Framework.Pattern");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_WithExplicitPath_LoadsConfigDirectly()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var configContent = """
                {
                  "target": "custom_schema.mmd",
                  "csharp": {
                    "namespace": "Custom.Test.Namespace"
                  }
                }
                """;
            await File.WriteAllTextAsync(tempFile, configContent);

            var config = await ConfigurationLoader.LoadAsync(tempFile);

            config.Should().NotBeNull();
            config!.Target.Should().Be(Path.Combine(Path.GetDirectoryName(tempFile)!, "custom_schema.mmd"));
            config.CSharp.Namespace.Should().Be("Custom.Test.Namespace");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void ParseJson_WithForeignKeyPlacementAndTablePrefixes_ParsesCorrectly()
    {
        var json = """
            {
              "tablePrefixes": ["tbl_", "px_"],
              "csharp": {
                "foreignKeyPlacement": "scalar"

              }
            }
            """;

        var config = ConfigurationLoader.Parse(json);

        config.Should().NotBeNull();
        config.TablePrefixes.Should().ContainInOrder("tbl_", "px_");
        config.CSharp.ForeignKeyPlacement.Should().Be("scalar");

    }
}
