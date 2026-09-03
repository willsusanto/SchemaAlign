using ClosedXML.Excel;
using SchemaAlign.Models;

namespace SchemaAlign.Exporters.Excel;

/// <summary>
/// Generates an Excel data dictionary (.xlsx) from a parsed database schema.
/// </summary>
public class ExcelDataDictionaryExporter
{
    public void Export(DatabaseSchema schema, DictionaryExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            throw new ArgumentException("Output path cannot be empty.", nameof(options));
        }

        var dir = Path.GetDirectoryName(options.OutputPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var workbook = new XLWorkbook();
        var sheetName = SanitizeSheetName(options.DatabaseName);
        var ws = workbook.Worksheets.Add(sheetName);

        BuildHeaders(ws, options);
        PopulateRows(ws, schema, options);

        workbook.SaveAs(options.OutputPath);
    }

    private static void BuildHeaders(IXLWorksheet ws, DictionaryExportOptions options)
    {
        // Row 1 merged sections
        var titleRange = ws.Range("A1:I1").Merge();
        titleRange.Value = options.SystemTitle;
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var sourceRange = ws.Range("J1:M1").Merge();
        sourceRange.Value = "Source";
        sourceRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var notesRange = ws.Range("N1:N2").Merge();
        notesRange.Value = "Notes";
        notesRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var sampleRange = ws.Range("O1:O2").Merge();
        sampleRange.Value = "Sample Data";
        sampleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // Row 2 column headers
        ws.Cell(2, 1).Value = "AID";
        ws.Cell(2, 2).Value = "IP / Domain / Azure Cosmos";
        ws.Cell(2, 3).Value = "SQL DB / Azure DB / Cosmos DB";
        ws.Cell(2, 4).Value = "Table / Container";
        ws.Cell(2, 5).Value = "Field / Attribute";
        ws.Cell(2, 6).Value = "Is Primary Key";
        ws.Cell(2, 7).Value = "Is Foreign Key";
        ws.Cell(2, 8).Value = "Nullable";
        ws.Cell(2, 9).Value = "Datatype (Datalength)";
        ws.Cell(2, 10).Value = "IP / Domain / Azure Cosmos (References)";
        ws.Cell(2, 11).Value = "SQL DB / Azure DB / Cosmos DB (References)";
        ws.Cell(2, 12).Value = "Table / Container (References)";
        ws.Cell(2, 13).Value = "Field / Attribute (References)";

        // Header styles
        ws.Row(1).Style.Font.Bold = true;
        ws.Row(2).Style.Font.Bold = true;

        ws.Range("A1:O2").Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        // Header borders
        ws.Range("A1:I1").Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        ws.Range("J1:M1").Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        ws.Range("N1:N2").Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        ws.Range("O1:O2").Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

        var r2 = ws.Range("A2:O2");
        r2.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
        r2.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    }

    private static void PopulateRows(IXLWorksheet ws, DatabaseSchema schema, DictionaryExportOptions options)
    {
        var row = 3;

        foreach (var table in schema.Tables.Values)
        {
            var startRow = row;

            foreach (var column in table.Columns.Values)
            {
                var isPk = column.IsPrimaryKey;
                var (isFk, refTable, refColumn) = ResolveForeignKey(table, column, schema);

                var formattedType = FormatDataType(column);

                ws.Cell(row, 1).Value = options.Aid;
                ws.Cell(row, 2).Value = options.IpDomain;
                ws.Cell(row, 3).Value = options.DatabaseName;
                ws.Cell(row, 4).Value = table.Name;
                ws.Cell(row, 5).Value = column.Name;
                ws.Cell(row, 6).Value = isPk ? "YES" : "NO";
                ws.Cell(row, 7).Value = isFk ? "YES" : "NO";
                ws.Cell(row, 8).Value = column.IsNullable ? "YES" : "NO";
                ws.Cell(row, 9).Value = formattedType;

                if (isFk && !string.IsNullOrWhiteSpace(refTable) && !refTable.Equals("-", StringComparison.Ordinal))
                {
                    ws.Cell(row, 10).Value = options.IpDomain;
                    ws.Cell(row, 11).Value = options.DatabaseName;
                    ws.Cell(row, 12).Value = refTable;
                    ws.Cell(row, 13).Value = refColumn ?? "-";
                }
                else
                {
                    ws.Cell(row, 10).Value = "-";
                    ws.Cell(row, 11).Value = "-";
                    ws.Cell(row, 12).Value = "-";
                    ws.Cell(row, 13).Value = "-";
                }

                ws.Cell(row, 14).Value = column.Comment ?? string.Empty;
                ws.Cell(row, 15).Value = string.Empty;

                row++;
            }

            var endRow = row - 1;
            if (endRow >= startRow)
            {
                // Table boundary styling: medium separator lines between tables, and medium outer table edges
                var tableRange = ws.Range(startRow, 1, endRow, 15);
                tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                tableRange.FirstRow().Style.Border.TopBorder = XLBorderStyleValues.Medium;
                tableRange.LastRow().Style.Border.BottomBorder = XLBorderStyleValues.Medium;
                tableRange.FirstColumn().Style.Border.LeftBorder = XLBorderStyleValues.Medium;
                tableRange.LastColumn().Style.Border.RightBorder = XLBorderStyleValues.Medium;
            }
        }

        // Auto-fit columns for clean readability
        ws.Columns().AdjustToContents();
    }

    private static (bool IsFk, string? RefTable, string? RefColumn) ResolveForeignKey(
        TableSchema currentTable,
        ColumnSchema column,
        DatabaseSchema schema)
    {
        // 1. Check explicit ForeignKeys defined on the table matching this column
        var directFk = currentTable.ForeignKeys.FirstOrDefault(f =>
            string.Equals(f.DependentColumn, column.Name, StringComparison.OrdinalIgnoreCase));

        if (directFk != null)
        {
            return (true, directFk.PrincipalTable, directFk.PrincipalColumn);
        }

        // 2. If column was tagged FK in attributes, attempt inferencing from schema
        if (column.IsForeignKey)
        {
            // Try finding a principal table that has a primary key matching column.Name
            foreach (var candidateTable in schema.Tables.Values)
            {
                if (string.Equals(candidateTable.Name, currentTable.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var matchingPk = candidateTable.PrimaryKeys.FirstOrDefault(pk =>
                    string.Equals(pk, column.Name, StringComparison.OrdinalIgnoreCase));

                if (matchingPk != null)
                {
                    return (true, candidateTable.Name, matchingPk);
                }
            }

            // Try matching by table entity naming prefixes/suffixes
            var stem = column.Name;
            if (stem.StartsWith("Id", StringComparison.OrdinalIgnoreCase))
            {
                stem = stem.Substring(2);
            }
            else if (stem.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
            {
                stem = stem.Substring(0, stem.Length - 2);
            }

            if (!string.IsNullOrWhiteSpace(stem))
            {
                foreach (var candidateTable in schema.Tables.Values)
                {
                    if (string.Equals(candidateTable.Name, currentTable.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (candidateTable.Name.EndsWith(stem, StringComparison.OrdinalIgnoreCase) ||
                        candidateTable.Name.Equals(stem, StringComparison.OrdinalIgnoreCase))
                    {
                        var pk = candidateTable.PrimaryKeys.FirstOrDefault() ?? column.Name;
                        return (true, candidateTable.Name, pk);
                    }
                }
            }

            return (true, "-", "-");
        }

        return (false, "-", "-");
    }

    private static string FormatDataType(ColumnSchema column)
    {
        if (!string.IsNullOrWhiteSpace(column.RawType))
        {
            var raw = column.RawType.Trim();
            if (raw.Contains('('))
            {
                return raw;
            }

            if (column.Length.HasValue)
            {
                return column.Length.Value == -1 ? $"{raw}(max)" : $"{raw}({column.Length.Value})";
            }

            if (column.Precision.HasValue && column.Scale.HasValue)
            {
                return $"{raw}({column.Precision.Value},{column.Scale.Value})";
            }

            return raw;
        }

        if (column.Type != StandardType.Unknown)
        {
            var typeName = column.Type.ToString().ToLowerInvariant();
            if (column.Length.HasValue)
            {
                return column.Length.Value == -1 ? $"{typeName}(max)" : $"{typeName}({column.Length.Value})";
            }
            return typeName;
        }

        return "nvarchar(max)";
    }

    private static string SanitizeSheetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "DataDictionary";
        }

        var invalidChars = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var sanitized = new string(name.Where(c => !invalidChars.Contains(c)).ToArray());
        return sanitized.Length > 31 ? sanitized.Substring(0, 31) : sanitized;
    }
}
