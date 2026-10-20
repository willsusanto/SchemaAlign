using AwesomeAssertions;
using SchemaAlign.Configuration;
using Xunit;

namespace SchemaAlign.Tests.Configuration;

public class ConfigurationLoaderTests
{
    [Fact]
    public void ParseJson_ValidSectionedConfig_ParsesAllPropertiesCorrectly()
    {
        var json = """
            {
              "source": "./src/Models",
              "target": "./docs/architecture.mmd",
              "mode": "snapshot",
              "allowDrop": true,
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
        config.Source.Should().Be("./src/Models");
        config.Target.Should().Be("./docs/architecture.mmd");
        config.Mode.Should().Be("snapshot");
        config.AllowDrop.Should().BeTrue();

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
                  "mode": "incremental",
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
            loadedConfig!.Mode.Should().Be("incremental");
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
            config!.Target.Should().Be("custom_schema.mmd");
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
                "foreignKeyPlacement": "scalar",
                "tablePrefixes": ["mock_"]
              }
            }
            """;

        var config = ConfigurationLoader.Parse(json);

        config.Should().NotBeNull();
        config.TablePrefixes.Should().ContainInOrder("tbl_", "px_");
        config.CSharp.ForeignKeyPlacement.Should().Be("scalar");
        config.CSharp.TablePrefixes.Should().Contain("mock_");
    }
}

