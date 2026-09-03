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

    [Fact]
    public void Export_AppliesSeparatingBordersBetweenTablesAndHeaderStyling()
    {
        // Masked Mermaid schema with 2 tables
        var mermaidContent = """
            erDiagram
                TblAlpha {
                    nvarchar(50) IdAlpha PK
                    nvarchar(100) AlphaName
                }
                TblBeta {
                    nvarchar(36) IdBeta PK
                    nvarchar(50) BetaCode
                }
            """;

        var reader = new MermaidSchemaReader();
        var schema = reader.Read(mermaidContent);

        var options = new DictionaryExportOptions
        {
            DatabaseName = "STYLE_DB",
            OutputPath = Path.Combine(_tempOutputDir, "output_styles.xlsx")
        };

        var exporter = new ExcelDataDictionaryExporter();
        exporter.Export(schema, options);

        using var workbook = new XLWorkbook(options.OutputPath);
        var ws = workbook.Worksheet("STYLE_DB");

        // Headers have bold font
        ws.Cell("A1").Style.Font.Bold.Should().BeTrue();
        ws.Cell("A2").Style.Font.Bold.Should().BeTrue();
        ws.Cell("D2").Style.Font.Bold.Should().BeTrue();

        // Row 2 header has medium bottom border
        ws.Cell("D2").Style.Border.BottomBorder.Should().Be(XLBorderStyleValues.Medium);

        // Table 1 (TblAlpha): Rows 3 to 4
        // Table 1 starts at Row 3 -> TopBorder is Medium
        ws.Cell("D3").Style.Border.TopBorder.Should().Be(XLBorderStyleValues.Medium);
        // Table 1 ends at Row 4 -> BottomBorder is Medium
        ws.Cell("D4").Style.Border.BottomBorder.Should().Be(XLBorderStyleValues.Medium);

        // Table 2 (TblBeta): Rows 5 to 6
        // Table 2 starts at Row 5 -> TopBorder is Medium (separating line between tables)
        ws.Cell("D5").Style.Border.TopBorder.Should().Be(XLBorderStyleValues.Medium);
        // Table 2 ends at Row 6 -> BottomBorder is Medium
        ws.Cell("D6").Style.Border.BottomBorder.Should().Be(XLBorderStyleValues.Medium);
    }

    [Fact]
    public void Export_AppliesExactTemplateStyling_FillsHeightsAndFonts()
    {
        var mermaidContent = """
            erDiagram
                MockEntity {
                    nvarchar(36) IdMock PK
                    nvarchar(50) Name
                }
            """;

        var reader = new MermaidSchemaReader();
        var schema = reader.Read(mermaidContent);

        var options = new DictionaryExportOptions
        {
            DatabaseName = "EXACT_STYLE_DB",
            OutputPath = Path.Combine(_tempOutputDir, "output_exact_styles.xlsx")
        };

        var exporter = new ExcelDataDictionaryExporter();
        exporter.Export(schema, options);

        using var workbook = new XLWorkbook(options.OutputPath);
        var ws = workbook.Worksheet("EXACT_STYLE_DB");

        // Font name and size
        ws.Cell("A1").Style.Font.FontName.Should().Be("Calibri");
        ws.Cell("A1").Style.Font.FontSize.Should().Be(10);
        ws.Cell("A2").Style.Font.FontName.Should().Be("Calibri");
        ws.Cell("A2").Style.Font.FontSize.Should().Be(10);
        ws.Cell("D3").Style.Font.FontName.Should().Be("Calibri");
        ws.Cell("D3").Style.Font.FontSize.Should().Be(10);

        // Row heights and text wrapping
        ws.Row(1).Height.Should().BeApproximately(14.4, 1.0);
        ws.Row(2).Height.Should().Be(42);
        ws.Cell("B2").Style.Alignment.WrapText.Should().BeTrue();

        // Header fills (A1 is Gray, A2 is Yellow, B2 is Gray, F2 is Yellow)
        var lightGray = XLColor.FromArgb(217, 217, 217);
        var yellow = XLColor.FromArgb(255, 255, 0);

        ws.Cell("A1").Style.Fill.BackgroundColor.Should().Be(lightGray);
        ws.Cell("J1").Style.Fill.BackgroundColor.Should().Be(lightGray);
        ws.Cell("N1").Style.Fill.BackgroundColor.Should().Be(lightGray);

        ws.Cell("A2").Style.Fill.BackgroundColor.Should().Be(yellow);
        ws.Cell("B2").Style.Fill.BackgroundColor.Should().Be(lightGray);
        ws.Cell("D2").Style.Fill.BackgroundColor.Should().Be(lightGray);
        ws.Cell("F2").Style.Fill.BackgroundColor.Should().Be(yellow);
        ws.Cell("G2").Style.Fill.BackgroundColor.Should().Be(yellow);
        ws.Cell("H2").Style.Fill.BackgroundColor.Should().Be(yellow);
        ws.Cell("I2").Style.Fill.BackgroundColor.Should().Be(lightGray);

        // Column widths
        ws.Column(1).Width.Should().BeApproximately(12.5, 0.5);
        ws.Column(2).Width.Should().BeApproximately(41.5, 0.5);
        ws.Column(4).Width.Should().BeApproximately(33.5, 0.5);
    }
}
