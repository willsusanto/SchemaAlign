using ClosedXML.Excel;
using SchemaAlign.Exporters.Excel;
using SchemaAlign.Models;
using SchemaAlign.Readers.Mermaid;

namespace SchemaAlign.Tests.Exporters;

public class ExcelDataDictionaryExporterTests : IDisposable
{
    private readonly string _tempOutputDir;

    public ExcelDataDictionaryExporterTests()
    {
        _tempOutputDir = Path.Combine(Path.GetTempPath(), "SchemaAlign_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempOutputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempOutputDir))
        {
            try
            {
                Directory.Delete(_tempOutputDir, true);
            }
            catch
            {
                // ignore temp cleanup error
            }
        }
    }

    [Fact]
    public void Export_GeneratesExpectedHeaderStructureAndSheetName()
    {
        // Masked Mermaid schema
        var mermaidContent = """
            erDiagram
                TblAlpha {
                    nvarchar(50) IdAlpha PK
                    nvarchar(100) AlphaName
                }
            """;

        var reader = new MermaidSchemaReader();
        var schema = reader.Read(mermaidContent);

        var options = new DictionaryExportOptions
        {
            Aid = "9999",
            IpDomain = "mock-db-host.internal",
            DatabaseName = "MOCK_DB",
            SystemTitle = "Mock Asset System",
            OutputPath = Path.Combine(_tempOutputDir, "output_headers.xlsx")
        };

        var exporter = new ExcelDataDictionaryExporter();
        exporter.Export(schema, options);

        File.Exists(options.OutputPath).Should().BeTrue();

        using var workbook = new XLWorkbook(options.OutputPath);
        workbook.Worksheets.Contains("MOCK_DB").Should().BeTrue();

        var ws = workbook.Worksheet("MOCK_DB");

        // Row 1 merged headers
        ws.Cell("A1").GetString().Should().Be("Mock Asset System");
        ws.Cell("J1").GetString().Should().Be("Source");
        ws.Cell("N1").GetString().Should().Be("Notes");
        ws.Cell("O1").GetString().Should().Be("Sample Data");

        // Row 2 column headers
        ws.Cell("A2").GetString().Should().Be("AID");
        ws.Cell("B2").GetString().Should().Be("IP / Domain / Azure Cosmos");
        ws.Cell("C2").GetString().Should().Be("SQL DB / Azure DB / Cosmos DB");
        ws.Cell("D2").GetString().Should().Be("Table / Container");
        ws.Cell("E2").GetString().Should().Be("Field / Attribute");
        ws.Cell("F2").GetString().Should().Be("Is Primary Key");
        ws.Cell("G2").GetString().Should().Be("Is Foreign Key");
        ws.Cell("H2").GetString().Should().Be("Nullable");
        ws.Cell("I2").GetString().Should().Be("Datatype (Datalength)");
        ws.Cell("J2").GetString().Should().Be("IP / Domain / Azure Cosmos (References)");
        ws.Cell("K2").GetString().Should().Be("SQL DB / Azure DB / Cosmos DB (References)");
        ws.Cell("L2").GetString().Should().Be("Table / Container (References)");
        ws.Cell("M2").GetString().Should().Be("Field / Attribute (References)");
    }

    [Fact]
    public void Export_MapsColumnsCorrectly_WithPkFkAndNullability()
    {
        // Masked Mermaid schema with relations
        var mermaidContent = """
            erDiagram
                TblAlpha {
                    nvarchar(36) IdAlpha PK
                    nvarchar(100) AlphaTitle
                    datetime CreatedDate "NULL"
                }
                TblBeta {
                    nvarchar(36) IdBeta PK
                    nvarchar(36) IdAlpha FK
                    int SequenceNumber
                }
                TblBeta }o--|| TblAlpha : "IdAlpha"
            """;

        var reader = new MermaidSchemaReader();
        var schema = reader.Read(mermaidContent);

        var options = new DictionaryExportOptions
        {
            Aid = "1191",
            IpDomain = "db-server.example.org",
            DatabaseName = "CATALOG_DB",
            SystemTitle = "Catalog Registry",
            OutputPath = Path.Combine(_tempOutputDir, "output_rows.xlsx")
        };

        var exporter = new ExcelDataDictionaryExporter();
        exporter.Export(schema, options);

        using var workbook = new XLWorkbook(options.OutputPath);
        var ws = workbook.Worksheet("CATALOG_DB");

        // Row 3: TblAlpha.IdAlpha (PK, not FK, not nullable)
        ws.Cell("A3").GetString().Should().Be("1191");
        ws.Cell("B3").GetString().Should().Be("db-server.example.org");
        ws.Cell("C3").GetString().Should().Be("CATALOG_DB");
        ws.Cell("D3").GetString().Should().Be("TblAlpha");
        ws.Cell("E3").GetString().Should().Be("IdAlpha");
        ws.Cell("F3").GetString().Should().Be("YES");
        ws.Cell("G3").GetString().Should().Be("NO");
        ws.Cell("H3").GetString().Should().Be("NO");
        ws.Cell("I3").GetString().Should().Be("nvarchar(36)");
        ws.Cell("J3").GetString().Should().Be("-");
        ws.Cell("K3").GetString().Should().Be("-");
        ws.Cell("L3").GetString().Should().Be("-");
        ws.Cell("M3").GetString().Should().Be("-");

        // Row 5: TblAlpha.CreatedDate (nullable, not PK, not FK)
        ws.Cell("D5").GetString().Should().Be("TblAlpha");
        ws.Cell("E5").GetString().Should().Be("CreatedDate");
        ws.Cell("F5").GetString().Should().Be("NO");
        ws.Cell("G5").GetString().Should().Be("NO");
        ws.Cell("H5").GetString().Should().Be("YES");
        ws.Cell("I5").GetString().Should().Be("datetime");

        // Row 7: TblBeta.IdAlpha (FK to TblAlpha.IdAlpha)
        ws.Cell("D7").GetString().Should().Be("TblBeta");
        ws.Cell("E7").GetString().Should().Be("IdAlpha");
        ws.Cell("F7").GetString().Should().Be("NO");
        ws.Cell("G7").GetString().Should().Be("YES");
        ws.Cell("H7").GetString().Should().Be("NO");
        ws.Cell("I7").GetString().Should().Be("nvarchar(36)");
        ws.Cell("J7").GetString().Should().Be("db-server.example.org");
        ws.Cell("K7").GetString().Should().Be("CATALOG_DB");
        ws.Cell("L7").GetString().Should().Be("TblAlpha");
        ws.Cell("M7").GetString().Should().Be("IdAlpha");
    }

    [Fact]
    public void Export_HandlesFkMarkedWithoutExplicitRelationshipByConvention()
    {
        // Masked schema where FK is flagged on column, but no relationship line is drawn
        var mermaidContent = """
            erDiagram
                TblAlpha {
                    nvarchar(36) IdAlpha PK
                }
                TblGamma {
                    nvarchar(36) IdGamma PK
                    nvarchar(36) IdAlpha FK
                }
            """;

        var reader = new MermaidSchemaReader();
        var schema = reader.Read(mermaidContent);

        var options = new DictionaryExportOptions
        {
            Aid = "1191",
            IpDomain = "db-server.example.org",
            DatabaseName = "CATALOG_DB",
            SystemTitle = "Catalog Registry",
            OutputPath = Path.Combine(_tempOutputDir, "output_inferred_fk.xlsx")
        };

        var exporter = new ExcelDataDictionaryExporter();
        exporter.Export(schema, options);

        using var workbook = new XLWorkbook(options.OutputPath);
        var ws = workbook.Worksheet("CATALOG_DB");

        // Row 3: TblAlpha.IdAlpha
        // Row 4: TblGamma.IdGamma
        // Row 5: TblGamma.IdAlpha
        ws.Cell("D5").GetString().Should().Be("TblGamma");
        ws.Cell("E5").GetString().Should().Be("IdAlpha");
        ws.Cell("F5").GetString().Should().Be("NO");
        ws.Cell("G5").GetString().Should().Be("YES");
        ws.Cell("L5").GetString().Should().Be("TblAlpha");
        ws.Cell("M5").GetString().Should().Be("IdAlpha");
    }
}
