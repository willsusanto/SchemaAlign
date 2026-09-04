using ClosedXML.Excel;
using SchemaAlign.Configuration;
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

    [Fact]
    public void Export_WithoutConfiguredColumnDefaults_PreservesCommentsAndLeavesSampleDataEmpty()
    {
        var mermaidContent = """
            erDiagram
                MockAuditedEntity {
                    nvarchar(36) IdMock PK
                    nvarchar(100) Remarks "Specific audit remark"
                    bit MockStatus
                    nvarchar(36) MockUser
                }
            """;

        var reader = new MermaidSchemaReader();
        var schema = reader.Read(mermaidContent);

        var options = new DictionaryExportOptions
        {
            DatabaseName = "AUDIT_DB",
            OutputPath = Path.Combine(_tempOutputDir, "output_default_empty.xlsx")
        };

        var exporter = new ExcelDataDictionaryExporter();
        exporter.Export(schema, options);

        using var workbook = new XLWorkbook(options.OutputPath);
        var ws = workbook.Worksheet("AUDIT_DB");

        // Row 3: IdMock -> Notes and Sample Data empty
        ws.Cell("N3").GetString().Should().Be(string.Empty);
        ws.Cell("O3").GetString().Should().Be(string.Empty);

        // Row 4: Remarks -> Preserves custom comment, Sample Data empty
        ws.Cell("E4").GetString().Should().Be("Remarks");
        ws.Cell("N4").GetString().Should().Be("Specific audit remark");
        ws.Cell("O4").GetString().Should().Be(string.Empty);

        // Row 5: MockStatus -> Without configured defaults, Notes and Sample Data are empty
        ws.Cell("E5").GetString().Should().Be("MockStatus");
        ws.Cell("N5").GetString().Should().Be(string.Empty);
        ws.Cell("O5").GetString().Should().Be(string.Empty);

        // Row 6: MockUser -> Without configured defaults, Notes and Sample Data are empty
        ws.Cell("E6").GetString().Should().Be("MockUser");
        ws.Cell("N6").GetString().Should().Be(string.Empty);
        ws.Cell("O6").GetString().Should().Be(string.Empty);
    }

    [Fact]
    public void Export_WithConfiguredColumnDefaults_PopulatesConfiguredNotesAndSampleData()
    {
        var mermaidContent = """
            erDiagram
                MockAuditedEntity {
                    nvarchar(36) IdMock PK
                    bit MockStatus
                    nvarchar(36) MockUser
                }
            """;

        var reader = new MermaidSchemaReader();
        var schema = reader.Read(mermaidContent);

        var options = new DictionaryExportOptions
        {
            DatabaseName = "AUDIT_DB",
            OutputPath = Path.Combine(_tempOutputDir, "output_configured.xlsx")
        };
        options.ColumnDefaults["MockStatus"] = new SchemaAlign.Configuration.DictionaryColumnDefault
        {
            Notes = "Custom status description",
            Sample = "0, 1"
        };
        options.ColumnDefaults["MockUser"] = new SchemaAlign.Configuration.DictionaryColumnDefault
        {
            Notes = "User identifier GUID",
            Sample = "USR-GUID-001"
        };

        var exporter = new ExcelDataDictionaryExporter();
        exporter.Export(schema, options);

        using var workbook = new XLWorkbook(options.OutputPath);
        var ws = workbook.Worksheet("AUDIT_DB");

        // Row 4: MockStatus -> Uses configured notes and sample
        ws.Cell("E4").GetString().Should().Be("MockStatus");
        ws.Cell("N4").GetString().Should().Be("Custom status description");
        ws.Cell("O4").GetString().Should().Be("0, 1");

        // Row 5: MockUser -> Uses configured notes and sample
        ws.Cell("E5").GetString().Should().Be("MockUser");
        ws.Cell("N5").GetString().Should().Be("User identifier GUID");
        ws.Cell("O5").GetString().Should().Be("USR-GUID-001");
    }

    [Fact]
    public void Export_GeneratesFullEvidenceReport_WhenEvidenceDirectoryExists()
    {
        var evidenceDir = @"C:\Users\william.susanto\.no-mistakes\evidence\01M1K15H603JE3PW8Y1BVPX8T1";
        if (!Directory.Exists(evidenceDir))
        {
            return;
        }

        var mermaidContent = """
            erDiagram
                Department {
                    nvarchar(36) IdDepartment PK
                    nvarchar(100) DepartmentName "Department display name"
                    nvarchar(50) DepartmentCode
                    bit Stsrc
                    nvarchar(36) UserIn
                    datetime DateIn
                    nvarchar(36) UserUp "NULL"
                    datetime DateUp "NULL"
                }
                Employee {
                    nvarchar(36) IdEmployee PK
                    nvarchar(36) IdDepartment FK
                    nvarchar(100) FullName "Employee full name"
                    bit Stsrc
                    nvarchar(36) UserIn
                    datetime DateIn
                    nvarchar(36) UserUp "NULL"
                    datetime DateUp "NULL"
                }
                Employee }o--|| Department : "IdDepartment"
            """;

        var reader = new MermaidSchemaReader();
        var schema = reader.Read(mermaidContent);

        var outputPath = Path.Combine(evidenceDir, "DataDictionary_AuditExport.xlsx");
        var options = new DictionaryExportOptions
        {
            Aid = "1191",
            IpDomain = "ssg5-hr-dev.internal.db",
            DatabaseName = "HR_MANAGEMENT_DB",
            SystemTitle = "HR Enterprise System",
            OutputPath = outputPath
        };
        options.ColumnDefaults["Stsrc"] = new DictionaryColumnDefault { Notes = "Status record data", Sample = "0.1" };
        options.ColumnDefaults["UserIn"] = new DictionaryColumnDefault { Notes = "User yang melakukan input data", Sample = "GUID" };
        options.ColumnDefaults["DateIn"] = new DictionaryColumnDefault { Notes = "Tanggal data di input", Sample = "2026-01-22 09:25:18.8933333" };
        options.ColumnDefaults["UserUp"] = new DictionaryColumnDefault { Notes = "User yang melakukan update data", Sample = "GUID" };
        options.ColumnDefaults["DateUp"] = new DictionaryColumnDefault { Notes = "Tanggal data di update", Sample = "2026-01-22 09:25:18.8933333" };

        var exporter = new ExcelDataDictionaryExporter();
        exporter.Export(schema, options);

        File.Exists(outputPath).Should().BeTrue();

        using var workbook = new XLWorkbook(outputPath);
        var ws = workbook.Worksheet("HR_MANAGEMENT_DB");

        // Validate headers
        ws.Cell("A1").GetString().Should().Be("HR Enterprise System");
        ws.Cell("J1").GetString().Should().Be("Source");
        ws.Cell("N1").GetString().Should().Be("Notes");
        ws.Cell("O1").GetString().Should().Be("Sample Data");

        // Validate Department rows (Rows 3-10)
        ws.Cell("E3").GetString().Should().Be("IdDepartment");
        ws.Cell("F3").GetString().Should().Be("YES"); // PK
        ws.Cell("N3").GetString().Should().Be(string.Empty);
        ws.Cell("O3").GetString().Should().Be(string.Empty);

        ws.Cell("E4").GetString().Should().Be("DepartmentName");
        ws.Cell("N4").GetString().Should().Be("Department display name"); // custom comment preserved
        ws.Cell("O4").GetString().Should().Be(string.Empty);

        ws.Cell("E5").GetString().Should().Be("DepartmentCode");
        ws.Cell("N5").GetString().Should().Be(string.Empty); // default empty
        ws.Cell("O5").GetString().Should().Be(string.Empty);

        ws.Cell("E6").GetString().Should().Be("Stsrc");
        ws.Cell("N6").GetString().Should().Be("Status record data");
        ws.Cell("O6").GetString().Should().Be("0.1");

        ws.Cell("E7").GetString().Should().Be("UserIn");
        ws.Cell("N7").GetString().Should().Be("User yang melakukan input data");
        ws.Cell("O7").GetString().Should().Be("GUID");

        ws.Cell("E8").GetString().Should().Be("DateIn");
        ws.Cell("N8").GetString().Should().Be("Tanggal data di input");
        ws.Cell("O8").GetString().Should().Be("2026-01-22 09:25:18.8933333");

        ws.Cell("E9").GetString().Should().Be("UserUp");
        ws.Cell("N9").GetString().Should().Be("User yang melakukan update data");
        ws.Cell("O9").GetString().Should().Be("GUID");

        ws.Cell("E10").GetString().Should().Be("DateUp");
        ws.Cell("N10").GetString().Should().Be("Tanggal data di update");
        ws.Cell("O10").GetString().Should().Be("2026-01-22 09:25:18.8933333");

        // Validate Employee rows (Rows 11-18)
        ws.Cell("E11").GetString().Should().Be("IdEmployee");
        ws.Cell("F11").GetString().Should().Be("YES"); // PK

        ws.Cell("E12").GetString().Should().Be("IdDepartment");
        ws.Cell("G12").GetString().Should().Be("YES"); // FK
        ws.Cell("L12").GetString().Should().Be("Department"); // Ref Table
        ws.Cell("M12").GetString().Should().Be("IdDepartment"); // Ref Field

        ws.Cell("E13").GetString().Should().Be("FullName");
        ws.Cell("N13").GetString().Should().Be("Employee full name"); // custom comment preserved

        ws.Cell("E14").GetString().Should().Be("Stsrc");
        ws.Cell("N14").GetString().Should().Be("Status record data");
        ws.Cell("O14").GetString().Should().Be("0.1");

        ws.Cell("E15").GetString().Should().Be("UserIn");
        ws.Cell("N15").GetString().Should().Be("User yang melakukan input data");
        ws.Cell("O15").GetString().Should().Be("GUID");

        ws.Cell("E16").GetString().Should().Be("DateIn");
        ws.Cell("N16").GetString().Should().Be("Tanggal data di input");
        ws.Cell("O16").GetString().Should().Be("2026-01-22 09:25:18.8933333");

        ws.Cell("E17").GetString().Should().Be("UserUp");
        ws.Cell("N17").GetString().Should().Be("User yang melakukan update data");
        ws.Cell("O17").GetString().Should().Be("GUID");

        ws.Cell("E18").GetString().Should().Be("DateUp");
        ws.Cell("N18").GetString().Should().Be("Tanggal data di update");
        ws.Cell("O18").GetString().Should().Be("2026-01-22 09:25:18.8933333");

        // Format and generate Markdown report in evidence directory
        var reportSb = new System.Text.StringBuilder();
        reportSb.AppendLine("# Excel Data Dictionary Export Verification Report");
        reportSb.AppendLine();
        reportSb.AppendLine($"- **Workbook**: `{Path.GetFileName(outputPath)}`");
        reportSb.AppendLine($"- **Sheet Name**: `{ws.Name}`");
        reportSb.AppendLine($"- **System Title**: `{ws.Cell(1, 1).GetString()}`");
        reportSb.AppendLine($"- **Source Header**: `{ws.Cell(1, 10).GetString()}`");
        reportSb.AppendLine($"- **Notes Header**: `{ws.Cell(1, 14).GetString()}`");
        reportSb.AppendLine($"- **Sample Data Header**: `{ws.Cell(1, 15).GetString()}`");
        reportSb.AppendLine();
        reportSb.AppendLine("## Exported Columns Matrix");
        reportSb.AppendLine();
        reportSb.AppendLine("| Row | Table | Field | PK | FK | Nullable | Datatype | Ref Table | Ref Field | Notes | Sample Data |");
        reportSb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 3; r <= lastRow; r++)
        {
            var tbl = ws.Cell(r, 4).GetString();
            var fld = ws.Cell(r, 5).GetString();
            var pk = ws.Cell(r, 6).GetString();
            var fk = ws.Cell(r, 7).GetString();
            var nul = ws.Cell(r, 8).GetString();
            var dt = ws.Cell(r, 9).GetString();
            var refTbl = ws.Cell(r, 12).GetString();
            var refFld = ws.Cell(r, 13).GetString();
            var notes = ws.Cell(r, 14).GetString();
            var sample = ws.Cell(r, 15).GetString();

            reportSb.AppendLine($"| {r} | {tbl} | {fld} | {pk} | {fk} | {nul} | {dt} | {refTbl} | {refFld} | {notes} | {sample} |");
        }

        reportSb.AppendLine();
        reportSb.AppendLine("## Verification Results");
        reportSb.AppendLine();
        reportSb.AppendLine("- Standard audit column `Stsrc`: Notes = `Status record data`, Sample Data = `0.1` -> [PASS]");
        reportSb.AppendLine("- Standard audit column `UserIn`: Notes = `User yang melakukan input data`, Sample Data = `GUID` -> [PASS]");
        reportSb.AppendLine("- Standard audit column `DateIn`: Notes = `Tanggal data di input`, Sample Data = `2026-01-22 09:25:18.8933333` -> [PASS]");
        reportSb.AppendLine("- Standard audit column `UserUp`: Notes = `User yang melakukan update data`, Sample Data = `GUID` -> [PASS]");
        reportSb.AppendLine("- Standard audit column `DateUp`: Notes = `Tanggal data di update`, Sample Data = `2026-01-22 09:25:18.8933333` -> [PASS]");
        reportSb.AppendLine("- Custom commented column `DepartmentName`: Notes = `Department display name`, Sample Data = `` -> [PASS]");
        reportSb.AppendLine("- Regular un-commented column `DepartmentCode`: Notes = ``, Sample Data = `` -> [PASS]");
        reportSb.AppendLine("- Custom commented column `FullName`: Notes = `Employee full name`, Sample Data = `` -> [PASS]");

        File.WriteAllText(Path.Combine(evidenceDir, "export_verification_report.md"), reportSb.ToString());
    }

    [Fact]
    public void Export_WhenTablesHaveClassification_AppliesHighlightOnlyToMatchingClasses()
    {
        var mermaidContent = """
            erDiagram
                classDef existingTbl fill:#c8e6c9,stroke:#2e7d32,color:#1b1b1b
                classDef newTbl fill:#bbdefb,stroke:#1565c0,color:#1b1b1b
                classDef updatedTbl fill:#FFE8CB,stroke:#E89C3D,color:#1b1b1b

                class TblAlpha existingTbl
                class TblBeta newTbl
                class TblGamma updatedTbl

                TblAlpha {
                    int ColA PK
                }
                TblBeta {
                    int ColB PK
                }
                TblGamma {
                    int ColC PK
                }
                TblDelta {
                    int ColD PK
                }
            """;

        var reader = new MermaidSchemaReader();
        var schema = reader.Read(mermaidContent);

        var options = new DictionaryExportOptions
        {
            DatabaseName = "HIGHLIGHT_DB",
            OutputPath = Path.Combine(_tempOutputDir, "output_highlight.xlsx")
        };

        var exporter = new ExcelDataDictionaryExporter();
        exporter.Export(schema, options);

        using var workbook = new XLWorkbook(options.OutputPath);
        var ws = workbook.Worksheet("HIGHLIGHT_DB");

        var expectedHighlight = XLColor.FromHtml("#ffcccc");

        // Row 3: TblAlpha (existingTbl) -> Default fill (no highlight)
        ws.Cell("D3").GetString().Should().Be("TblAlpha");
        ws.Cell("D3").Style.Fill.PatternType.Should().Be(XLFillPatternValues.None);

        // Row 4: TblBeta (newTbl) -> Highlighted (#ffcccc) across row
        ws.Cell("D4").GetString().Should().Be("TblBeta");
        ws.Cell("D4").Style.Fill.BackgroundColor.Should().Be(expectedHighlight);
        ws.Cell("A4").Style.Fill.BackgroundColor.Should().Be(expectedHighlight);
        ws.Cell("O4").Style.Fill.BackgroundColor.Should().Be(expectedHighlight);

        // Row 5: TblGamma (updatedTbl) -> Highlighted (#ffcccc) across row
        ws.Cell("D5").GetString().Should().Be("TblGamma");
        ws.Cell("D5").Style.Fill.BackgroundColor.Should().Be(expectedHighlight);
        ws.Cell("A5").Style.Fill.BackgroundColor.Should().Be(expectedHighlight);
        ws.Cell("O5").Style.Fill.BackgroundColor.Should().Be(expectedHighlight);

        // Row 6: TblDelta (unclassified) -> Default fill (no highlight)
        ws.Cell("D6").GetString().Should().Be("TblDelta");
        ws.Cell("D6").Style.Fill.PatternType.Should().Be(XLFillPatternValues.None);

        // Verify borders and fonts are preserved on highlighted rows
        ws.Cell("D4").Style.Font.FontName.Should().Be("Calibri");
        ws.Cell("D4").Style.Font.FontSize.Should().Be(10);
        ws.Cell("D4").Style.Border.TopBorder.Should().Be(XLBorderStyleValues.Medium);
        ws.Cell("D4").Style.Border.BottomBorder.Should().Be(XLBorderStyleValues.Medium);
        ws.Cell("A4").Style.Border.LeftBorder.Should().Be(XLBorderStyleValues.Medium);
        ws.Cell("O4").Style.Border.RightBorder.Should().Be(XLBorderStyleValues.Medium);
    }

    [Fact]
    public void Export_WithCustomHighlightOptions_AppliesCustomColorAndClasses()
    {
        var mermaidContent = """
            erDiagram
                class TblAlpha customHighlight
                class TblBeta newTbl

                TblAlpha {
                    int ColA PK
                }
                TblBeta {
                    int ColB PK
                }
            """;

        var reader = new MermaidSchemaReader();
        var schema = reader.Read(mermaidContent);

        var options = new DictionaryExportOptions
        {
            DatabaseName = "CUSTOM_HIGHLIGHT_DB",
            HighlightClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "customHighlight" },
            HighlightColor = "#e0f7fa",
            OutputPath = Path.Combine(_tempOutputDir, "output_custom_highlight.xlsx")
        };

        var exporter = new ExcelDataDictionaryExporter();
        exporter.Export(schema, options);

        using var workbook = new XLWorkbook(options.OutputPath);
        var ws = workbook.Worksheet("CUSTOM_HIGHLIGHT_DB");

        var customColor = XLColor.FromHtml("#e0f7fa");

        // Row 3: TblAlpha has "customHighlight" -> Should be highlighted with #e0f7fa
        ws.Cell("D3").GetString().Should().Be("TblAlpha");
        ws.Cell("D3").Style.Fill.BackgroundColor.Should().Be(customColor);

        // Row 4: TblBeta has "newTbl" (which was excluded from custom HighlightClasses) -> No highlight
        ws.Cell("D4").GetString().Should().Be("TblBeta");
        ws.Cell("D4").Style.Fill.PatternType.Should().Be(XLFillPatternValues.None);
    }
}
