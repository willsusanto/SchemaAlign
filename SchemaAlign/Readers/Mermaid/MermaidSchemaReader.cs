using System.Text.RegularExpressions;
using SchemaAlign.Models;
using SchemaAlign.Models.TypeMapping;

namespace SchemaAlign.Readers.Mermaid;

public class MermaidSchemaReader : ISchemaReader
{
    private static readonly Regex FrontmatterRegex = new(@"^---\s*$", RegexOptions.Compiled);
    private static readonly Regex EntityBlockStartRegex = new(@"^([a-zA-Z0-9_\.\[\]]+)(?:\[""([^""]*)""\])?\s*\{", RegexOptions.Compiled);
    private static readonly Regex SingleLineEntityRegex = new(@"^([a-zA-Z0-9_\.\[\]]+)(?:\[""([^""]*)""\])?\s*\{([^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex AttributeLineRegex = new(@"^([a-zA-Z0-9_]+(?:\([^)]*\))?\??)\s+([a-zA-Z0-9_\[\]]+)(.*)$", RegexOptions.Compiled);
    private static readonly Regex CommentRegex = new(@"[""']([^""']*)[""']\s*$", RegexOptions.Compiled);
    private static readonly Regex PrecScaleCommentRegex = new(@"^(\d+)\s*,\s*(\d+)(?:\s*,\s*(.*))?$", RegexOptions.Compiled);
    private static readonly Regex LengthCommentRegex = new(@"^(max|\d+)(?:\s*,\s*(.*))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RelationshipRegex = new(
        @"^([a-zA-Z0-9_\.\[\]]+)\s*([\|\}o][\|o\{])\s*(--|\.\.)\s*([\|o\{][\|\{o])\s*([a-zA-Z0-9_\.\[\]]+)(?:\s*:\s*(?:""([^""]*)""|'([^']*)'|(\S*)))?",
        RegexOptions.Compiled);

    public DatabaseSchema Read(string content)
    {
        var schema = new DatabaseSchema();
        if (string.IsNullOrWhiteSpace(content))
        {
            return schema;
        }

        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var inFrontmatter = false;
        TableSchema? currentTable = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var rawLine = lines[i].Trim();

            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            // Handle YAML frontmatter (e.g. --- title: ... ---)
            if (FrontmatterRegex.IsMatch(rawLine))
            {
                inFrontmatter = !inFrontmatter;
                continue;
            }

            if (inFrontmatter)
            {
                continue;
            }

            // Skip comments and diagram directives
            if (rawLine.StartsWith("%%", StringComparison.Ordinal) ||
                rawLine.StartsWith("title:", StringComparison.OrdinalIgnoreCase) ||
                rawLine.StartsWith("accTitle:", StringComparison.OrdinalIgnoreCase) ||
                rawLine.StartsWith("accDescr:", StringComparison.OrdinalIgnoreCase) ||
                rawLine.StartsWith("direction", StringComparison.OrdinalIgnoreCase) ||
                rawLine.Equals("erDiagram", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }


            // Inside an entity block
            if (currentTable != null)
            {
                if (rawLine.StartsWith("}", StringComparison.Ordinal))
                {
                    currentTable = null;
                    continue;
                }

                ParseAttributeLine(rawLine, currentTable);
                continue;
            }

            // Single line entity e.g. Customer { int id PK }
            var singleLineMatch = SingleLineEntityRegex.Match(rawLine);
            if (singleLineMatch.Success)
            {
                var fullTableName = singleLineMatch.Groups[1].Value;
                var tableComment = singleLineMatch.Groups[2].Success ? singleLineMatch.Groups[2].Value : null;
                var (schemaName, tableName) = SplitSchemaAndTableName(fullTableName);

                var table = schema.GetOrCreateTable(tableName, schemaName);
                if (!string.IsNullOrWhiteSpace(tableComment))
                {
                    table.Comment = tableComment;
                }

                var attributesContent = singleLineMatch.Groups[3].Value;
                if (!string.IsNullOrWhiteSpace(attributesContent))
                {
                    var attrLines = attributesContent.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var attr in attrLines)
                    {
                        var trimmedAttr = attr.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmedAttr))
                        {
                            ParseAttributeLine(trimmedAttr, table);
                        }
                    }
                }
                continue;
            }

            // Multi-line entity block start e.g. Customer {
            var entityMatch = EntityBlockStartRegex.Match(rawLine);
            if (entityMatch.Success)
            {
                var fullTableName = entityMatch.Groups[1].Value;
                var tableComment = entityMatch.Groups[2].Success ? entityMatch.Groups[2].Value : null;
                var (schemaName, tableName) = SplitSchemaAndTableName(fullTableName);

                currentTable = schema.GetOrCreateTable(tableName, schemaName);
                if (!string.IsNullOrWhiteSpace(tableComment))
                {
                    currentTable.Comment = tableComment;
                }
                continue;
            }

            // Relationship line e.g. Customer ||--o{ Order : places
            var relMatch = RelationshipRegex.Match(rawLine);
            if (relMatch.Success)
            {
                ParseRelationshipLine(relMatch, schema);
                continue;
            }
        }

        return schema;
    }

    public DatabaseSchema ReadFile(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return Read(File.ReadAllText(filePath));
    }

    private static void ParseAttributeLine(string line, TableSchema table)
    {
        string? rawComment = null;
        var commentMatch = CommentRegex.Match(line);
        if (commentMatch.Success)
        {
            rawComment = commentMatch.Groups[1].Value;
            line = line.Substring(0, commentMatch.Index).Trim();
        }

        var attrMatch = AttributeLineRegex.Match(line);
        if (!attrMatch.Success)
        {
            return;
        }

        var rawType = attrMatch.Groups[1].Value;
        var columnName = CleanIdentifier(attrMatch.Groups[2].Value);
        var rest = attrMatch.Groups[3].Value;

        var isNullable = rawType.EndsWith("?");
        if (isNullable)
        {
            rawType = rawType.Substring(0, rawType.Length - 1).Trim();
        }

        var isPrimaryKey = Regex.IsMatch(rest, @"\bPK\b", RegexOptions.IgnoreCase);
        var isForeignKey = Regex.IsMatch(rest, @"\bFK\b", RegexOptions.IgnoreCase);

        var standardType = TypeMapper.FromMermaidType(rawType, out var length, out var precision, out var scale);

        ParseCommentAndDimensions(rawComment, ref length, ref precision, ref scale, ref isNullable, out var comment);

        if (isPrimaryKey)
        {
            isNullable = false;
        }

        var column = new ColumnSchema
        {
            Name = columnName,
            Type = standardType,
            RawType = rawType,
            Length = length,
            Precision = precision,
            Scale = scale,
            IsPrimaryKey = isPrimaryKey,
            IsNullable = isNullable,
            Comment = comment
        };

        table.AddColumn(column);
    }

    private static void ParseCommentAndDimensions(
        string? rawComment,
        ref int? length,
        ref int? precision,
        ref int? scale,
        ref bool isNullable,
        out string? comment)
    {
        comment = rawComment;
        if (string.IsNullOrWhiteSpace(rawComment))
        {
            comment = null;
            return;
        }

        var trimmed = rawComment.Trim();
        if (trimmed.Equals("nullable", StringComparison.OrdinalIgnoreCase))
        {
            isNullable = true;
            comment = null;
            return;
        }

        // Check for "precision,scale, comment" or "precision,scale" (e.g. "18,2" or "18,2, Unit price")
        var precScaleMatch = PrecScaleCommentRegex.Match(trimmed);
        if (precScaleMatch.Success)
        {
            if (!precision.HasValue) precision = int.Parse(precScaleMatch.Groups[1].Value);
            if (!scale.HasValue) scale = int.Parse(precScaleMatch.Groups[2].Value);
            comment = precScaleMatch.Groups[3].Success && !string.IsNullOrWhiteSpace(precScaleMatch.Groups[3].Value)
                ? precScaleMatch.Groups[3].Value.Trim()
                : null;
            return;
        }

        // Check for "length, comment" or "length" (e.g. "50", "100, Title", "max, Description")
        var lenMatch = LengthCommentRegex.Match(trimmed);
        if (lenMatch.Success)
        {
            if (!length.HasValue)
            {
                var lenStr = lenMatch.Groups[1].Value;
                length = lenStr.Equals("max", StringComparison.OrdinalIgnoreCase) ? -1 : int.Parse(lenStr);
            }
            comment = lenMatch.Groups[2].Success && !string.IsNullOrWhiteSpace(lenMatch.Groups[2].Value)
                ? lenMatch.Groups[2].Value.Trim()
                : null;
            return;
        }
    }

    private static void ParseRelationshipLine(Match relMatch, DatabaseSchema schema)
    {
        var entity1Raw = relMatch.Groups[1].Value;
        var card1 = relMatch.Groups[2].Value;
        var lineStyle = relMatch.Groups[3].Value;
        var card2 = relMatch.Groups[4].Value;
        var entity2Raw = relMatch.Groups[5].Value;

        var label = relMatch.Groups[6].Success ? relMatch.Groups[6].Value :
                    relMatch.Groups[7].Success ? relMatch.Groups[7].Value :
                    relMatch.Groups[8].Success ? relMatch.Groups[8].Value : null;

        var (schema1, table1Name) = SplitSchemaAndTableName(entity1Raw);
        var (schema2, table2Name) = SplitSchemaAndTableName(entity2Raw);

        var table1 = schema.GetOrCreateTable(table1Name, schema1);
        var table2 = schema.GetOrCreateTable(table2Name, schema2);

        var isOne1 = IsOneCardinality(card1);
        var isMany1 = IsManyCardinality(card1);
        var isOne2 = IsOneCardinality(card2);
        var isMany2 = IsManyCardinality(card2);

        TableSchema principalTable;
        TableSchema dependentTable;
        ForeignKeyCardinality cardinality;

        if (isOne1 && isMany2)
        {
            // Table1 ||--o{ Table2 (1 to Many)
            principalTable = table1;
            dependentTable = table2;
            cardinality = ForeignKeyCardinality.OneToMany;
        }
        else if (isMany1 && isOne2)
        {
            // Table1 }o--|| Table2 (Many to 1)
            principalTable = table2;
            dependentTable = table1;
            cardinality = ForeignKeyCardinality.ManyToOne;
        }
        else if (isOne1 && isOne2)
        {
            // Table1 ||--|| Table2 (1 to 1)
            principalTable = table1;
            dependentTable = table2;
            cardinality = ForeignKeyCardinality.OneToOne;
        }
        else
        {
            // Many to Many
            principalTable = table1;
            dependentTable = table2;
            cardinality = ForeignKeyCardinality.ManyToMany;
        }

        var principalCol = principalTable.PrimaryKeys.FirstOrDefault() ?? "Id";
        var dependentCol = FindDependentColumn(dependentTable, principalTable);

        var constraintName = !string.IsNullOrWhiteSpace(label) && label.StartsWith("FK_", StringComparison.OrdinalIgnoreCase)
            ? label
            : $"FK_{dependentTable.Name}_{principalTable.Name}_{dependentCol}";

        var fk = new ForeignKeySchema
        {
            ConstraintName = constraintName,
            PrincipalTable = principalTable.Name,
            PrincipalColumn = principalCol,
            DependentTable = dependentTable.Name,
            DependentColumn = dependentCol,
            Cardinality = cardinality
        };

        dependentTable.AddForeignKey(fk);
    }

    private static string FindDependentColumn(TableSchema dependentTable, TableSchema principalTable)
    {
        // 1. Look for column ending in Id with principal table name
        var singularPrincipal = principalTable.Name.TrimEnd('s', 'S');
        var candidates = new[]
        {
            $"{principalTable.Name}Id",
            $"{singularPrincipal}Id",
            $"{principalTable.Name}_Id",
            $"{singularPrincipal}_id",
            $"{principalTable.Name}_id",
            $"{principalTable.Name.ToLowerInvariant()}_id",
            $"{singularPrincipal.ToLowerInvariant()}_id",
            $"{principalTable.Name.ToLowerInvariant()}id"
        };

        foreach (var candidate in candidates)
        {
            var col = dependentTable.FindColumn(candidate);
            if (col != null)
            {
                return col.Name;
            }
        }

        // 2. Look for any column marked as FK or with FK-like name
        var fkCol = dependentTable.Columns.Values.FirstOrDefault(c =>
            c.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && !c.IsPrimaryKey);
        if (fkCol != null)
        {
            return fkCol.Name;
        }

        return $"{singularPrincipal}Id";
    }

    private static bool IsOneCardinality(string card)
    {
        return card is "||" or "|o" or "o|";
    }

    private static bool IsManyCardinality(string card)
    {
        return card is "}|" or "|{" or "}o" or "o{";
    }

    private static (string Schema, string TableName) SplitSchemaAndTableName(string rawIdentifier)
    {
        var cleaned = CleanIdentifier(rawIdentifier);
        var parts = cleaned.Split('.');
        if (parts.Length > 1)
        {
            return (CleanIdentifier(parts[0]), CleanIdentifier(parts[1]));
        }
        return ("dbo", cleaned);
    }

    private static string CleanIdentifier(string identifier)
    {
        return identifier.Trim().Trim('[', ']', '"', '\'');
    }
}
