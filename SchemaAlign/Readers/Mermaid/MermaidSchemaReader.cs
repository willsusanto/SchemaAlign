using System.Text.RegularExpressions;
using SchemaAlign.Models;
using SchemaAlign.Models.TypeMapping;

namespace SchemaAlign.Readers.Mermaid;

/// <summary>
/// Reads database schemas from Mermaid ER diagrams (erDiagram syntax).
/// Supports entity definitions, column attributes, data types, nullability (via '?' suffix or 'NULL'/'nullable' comments),
/// dimensions (length, precision, scale), PK/FK markers, relationship cardinality, and table classification (class statements and inline :::class notation).
/// </summary>
public class MermaidSchemaReader : ISchemaReader
{
    private static readonly Regex FrontmatterRegex = new(@"^---\s*$", RegexOptions.Compiled);
    private static readonly Regex EntityBlockStartRegex = new(@"^([a-zA-Z0-9_\.\[\]]+)(?::::([a-zA-Z0-9_-]+))?(?:\[""([^""]*)""\])?(?::::([a-zA-Z0-9_-]+))?\s*\{", RegexOptions.Compiled);
    private static readonly Regex SingleLineEntityRegex = new(@"^([a-zA-Z0-9_\.\[\]]+)(?::::([a-zA-Z0-9_-]+))?(?:\[""([^""]*)""\])?(?::::([a-zA-Z0-9_-]+))?\s*\{([^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex StandaloneEntityRegex = new(@"^([a-zA-Z0-9_\.\[\]]+):::([a-zA-Z0-9_-]+)(?:\[""([^""]*)""\])?$", RegexOptions.Compiled);
    private static readonly Regex ClassStatementRegex = new(@"^class\s+(.+?)\s+([a-zA-Z0-9_-]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AttributeLineRegex = new(@"^([a-zA-Z0-9_]+(?:\([^)]*\))?\??)\s+([a-zA-Z0-9_\[\]]+)(.*)$", RegexOptions.Compiled);
    private static readonly Regex CommentRegex = new(@"[""']([^""']*)[""']\s*$", RegexOptions.Compiled);
    private static readonly Regex PrecScaleCommentRegex = new(@"^(\d+)\s*,\s*(\d+)(?:\s*,\s*(.*))?$", RegexOptions.Compiled);
    private static readonly Regex LengthCommentRegex = new(@"^(max|\d+)(?:\s*,\s*(.*))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RelationshipRegex = new(
        @"^([a-zA-Z0-9_\.\[\]]+)\s*([\|\}o][\|o\{])\s*(--|\.\.)\s*([\|o\{][\|\{o])\s*([a-zA-Z0-9_\.\[\]]+)(?:\s*:\s*(?:""([^""]*)""|'([^']*)'|(\S*)))?",
        RegexOptions.Compiled);

    /// <summary>
    /// Gets the configuration options for this reader.
    /// </summary>
    public MermaidReaderOptions Options { get; }

    public MermaidSchemaReader(MermaidReaderOptions? options = null)
    {
        Options = options ?? new MermaidReaderOptions();
    }

    /// <summary>
    /// Reads and parses a database schema from Mermaid ER diagram text content.
    /// </summary>
    /// <param name="content">The Mermaid ER diagram text content.</param>
    /// <returns>A <see cref="DatabaseSchema"/> representing the parsed schema.</returns>
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
        var relationshipMatches = new List<Match>();

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
                rawLine.Equals("erDiagram", StringComparison.OrdinalIgnoreCase) ||
                (rawLine.StartsWith("classDef", StringComparison.OrdinalIgnoreCase) &&
                 (rawLine.Length == 8 || char.IsWhiteSpace(rawLine[8]))))
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

            // Class assignment statement e.g. class TableA, TableB existingTbl
            var classMatch = ClassStatementRegex.Match(rawLine);
            if (classMatch.Success && !rawLine.EndsWith("{"))
            {
                var tableListRaw = classMatch.Groups[1].Value;
                var className = classMatch.Groups[2].Value;

                var tableNames = tableListRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var rawTable in tableNames)
                {
                    var (schemaName, tableName) = SplitSchemaAndTableName(rawTable);
                    var table = schema.GetOrCreateTable(tableName, schemaName);
                    table.Classes.Add(className);
                }
                continue;
            }

            // Single line entity e.g. Customer { int id PK } or Customer:::newTbl { int id PK }
            var singleLineMatch = SingleLineEntityRegex.Match(rawLine);
            if (singleLineMatch.Success)
            {
                var fullTableName = singleLineMatch.Groups[1].Value;
                var inlineClass = singleLineMatch.Groups[2].Success ? singleLineMatch.Groups[2].Value :
                                  singleLineMatch.Groups[4].Success ? singleLineMatch.Groups[4].Value : null;
                var tableComment = singleLineMatch.Groups[3].Success ? singleLineMatch.Groups[3].Value : null;
                var (schemaName, tableName) = SplitSchemaAndTableName(fullTableName);

                var table = schema.GetOrCreateTable(tableName, schemaName);
                if (!string.IsNullOrWhiteSpace(tableComment))
                {
                    table.Comment = tableComment;
                }
                if (!string.IsNullOrWhiteSpace(inlineClass))
                {
                    table.Classes.Add(inlineClass);
                }

                var attributesContent = singleLineMatch.Groups[5].Value;
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

            // Multi-line entity block start e.g. Customer { or Customer:::newTbl {
            var entityMatch = EntityBlockStartRegex.Match(rawLine);
            if (entityMatch.Success)
            {
                var fullTableName = entityMatch.Groups[1].Value;
                var inlineClass = entityMatch.Groups[2].Success ? entityMatch.Groups[2].Value :
                                  entityMatch.Groups[4].Success ? entityMatch.Groups[4].Value : null;
                var tableComment = entityMatch.Groups[3].Success ? entityMatch.Groups[3].Value : null;
                var (schemaName, tableName) = SplitSchemaAndTableName(fullTableName);

                currentTable = schema.GetOrCreateTable(tableName, schemaName);
                if (!string.IsNullOrWhiteSpace(tableComment))
                {
                    currentTable.Comment = tableComment;
                }
                if (!string.IsNullOrWhiteSpace(inlineClass))
                {
                    currentTable.Classes.Add(inlineClass);
                }
                continue;
            }

            // Standalone entity with class e.g. Customer:::newTbl
            var standaloneMatch = StandaloneEntityRegex.Match(rawLine);
            if (standaloneMatch.Success)
            {
                var fullTableName = standaloneMatch.Groups[1].Value;
                var inlineClass = standaloneMatch.Groups[2].Value;
                var tableComment = standaloneMatch.Groups[3].Success ? standaloneMatch.Groups[3].Value : null;
                var (schemaName, tableName) = SplitSchemaAndTableName(fullTableName);

                var table = schema.GetOrCreateTable(tableName, schemaName);
                if (!string.IsNullOrWhiteSpace(tableComment))
                {
                    table.Comment = tableComment;
                }
                table.Classes.Add(inlineClass);
                continue;
            }

            // Relationship line e.g. Customer ||--o{ Order : places
            var relMatch = RelationshipRegex.Match(rawLine);
            if (relMatch.Success)
            {
                relationshipMatches.Add(relMatch);
                continue;
            }
        }

        foreach (var match in relationshipMatches)
        {
            ParseRelationshipLine(match, schema);
        }

        return schema;
    }

    /// <summary>
    /// Reads and parses a database schema from a Mermaid ER diagram file.
    /// </summary>
    /// <param name="filePath">Path to the Mermaid diagram file (.mmd, .mermaid).</param>
    /// <returns>A <see cref="DatabaseSchema"/> representing the parsed schema.</returns>
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
            IsForeignKey = isForeignKey,
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
        if (trimmed.Equals("nullable", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            isNullable = true;
            comment = null;
            return;
        }

        if (trimmed.StartsWith("nullable,", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("NULL,", StringComparison.OrdinalIgnoreCase))
        {
            isNullable = true;
            trimmed = trimmed.Substring(trimmed.IndexOf(',') + 1).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                comment = null;
                return;
            }
        }

        // Check for "precision,scale, comment" or "precision,scale" (e.g. "18,2" or "18,2, NULL" or "18,2, Unit price")
        var precScaleMatch = PrecScaleCommentRegex.Match(trimmed);
        if (precScaleMatch.Success)
        {
            if (!precision.HasValue) precision = int.Parse(precScaleMatch.Groups[1].Value);
            if (!scale.HasValue) scale = int.Parse(precScaleMatch.Groups[2].Value);
            var rest = precScaleMatch.Groups[3].Success && !string.IsNullOrWhiteSpace(precScaleMatch.Groups[3].Value)
                ? precScaleMatch.Groups[3].Value.Trim()
                : null;

            if (rest != null)
            {
                if (rest.Equals("nullable", StringComparison.OrdinalIgnoreCase) || rest.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                {
                    isNullable = true;
                    comment = null;
                    return;
                }

                if (rest.StartsWith("nullable,", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("NULL,", StringComparison.OrdinalIgnoreCase))
                {
                    isNullable = true;
                    rest = rest.Substring(rest.IndexOf(',') + 1).Trim();
                }
            }

            comment = string.IsNullOrWhiteSpace(rest) ? null : rest;
            return;
        }

        // Check for "length, comment" or "length" (e.g. "50", "50, NULL", "100, Title", "max, Description")
        var lenMatch = LengthCommentRegex.Match(trimmed);
        if (lenMatch.Success)
        {
            if (!length.HasValue)
            {
                var lenStr = lenMatch.Groups[1].Value;
                length = lenStr.Equals("max", StringComparison.OrdinalIgnoreCase) ? -1 : int.Parse(lenStr);
            }
            var rest = lenMatch.Groups[2].Success && !string.IsNullOrWhiteSpace(lenMatch.Groups[2].Value)
                ? lenMatch.Groups[2].Value.Trim()
                : null;

            if (rest != null)
            {
                if (rest.Equals("nullable", StringComparison.OrdinalIgnoreCase) || rest.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                {
                    isNullable = true;
                    comment = null;
                    return;
                }

                if (rest.StartsWith("nullable,", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("NULL,", StringComparison.OrdinalIgnoreCase))
                {
                    isNullable = true;
                    rest = rest.Substring(rest.IndexOf(',') + 1).Trim();
                }
            }

            comment = string.IsNullOrWhiteSpace(rest) ? null : rest;
            return;
        }

        comment = string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private void ParseRelationshipLine(Match relMatch, DatabaseSchema schema)
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
        var dependentCol = FindDependentColumn(dependentTable, principalTable, label, Options.TablePrefixes);

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

    private static string FindDependentColumn(
        TableSchema dependentTable,
        TableSchema principalTable,
        string? label = null,
        IReadOnlyList<string>? tablePrefixes = null)
    {
        // 1. Explicit relationship label matching column name in dependent table
        if (!string.IsNullOrWhiteSpace(label))
        {
            var trimmedLabel = label.Trim().Trim('"', '\'');
            var colByLabel = dependentTable.FindColumn(trimmedLabel);
            if (colByLabel != null)
            {
                return colByLabel.Name;
            }

            if (trimmedLabel.StartsWith("FK_", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmedLabel.Split('_');
                if (parts.Length > 0)
                {
                    var colByFkSuffix = dependentTable.FindColumn(parts[^1]);
                    if (colByFkSuffix != null)
                    {
                        return colByFkSuffix.Name;
                    }
                }
            }
        }

        // 2. Matching principal primary key column name in dependent table (if non-generic "Id")
        var principalPk = principalTable.PrimaryKeys.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(principalPk) && !string.Equals(principalPk, "Id", StringComparison.OrdinalIgnoreCase))
        {
            var colByPk = dependentTable.FindColumn(principalPk);
            if (colByPk != null && !colByPk.IsPrimaryKey)
            {
                return colByPk.Name;
            }
        }

        // 3. Suffix & Prefix Candidates based on Principal Table Name
        var singularPrincipal = Singularize(principalTable.Name);
        var candidates = new List<string>
        {
            // Suffix
            $"{principalTable.Name}Id",
            $"{singularPrincipal}Id",
            $"{principalTable.Name}_Id",
            $"{singularPrincipal}_id",
            $"{principalTable.Name}_id",
            $"{principalTable.Name.ToLowerInvariant()}_id",
            $"{singularPrincipal.ToLowerInvariant()}_id",
            $"{principalTable.Name.ToLowerInvariant()}id",
            // Prefix
            $"Id{principalTable.Name}",
            $"Id{singularPrincipal}",
            $"Id_{principalTable.Name}",
            $"Id_{singularPrincipal}",
            $"id_{principalTable.Name.ToLowerInvariant()}",
            $"id_{singularPrincipal.ToLowerInvariant()}",
            $"id{principalTable.Name.ToLowerInvariant()}"
        };

        // Table prefix stripping
        var prefixesToCheck = new List<string>();
        if (tablePrefixes != null)
        {
            prefixesToCheck.AddRange(tablePrefixes);
        }
        if (principalTable.Name.Length > 3 &&
            char.IsUpper(principalTable.Name[0]) &&
            char.IsLower(principalTable.Name[1]) &&
            char.IsUpper(principalTable.Name[2]))
        {
            var autoPrefix = principalTable.Name.Substring(0, 2);
            if (!prefixesToCheck.Contains(autoPrefix, StringComparer.OrdinalIgnoreCase))
            {
                prefixesToCheck.Add(autoPrefix);
            }
        }

        foreach (var prefix in prefixesToCheck)
        {
            if (principalTable.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                principalTable.Name.Length > prefix.Length)
            {
                var stripped = principalTable.Name.Substring(prefix.Length).TrimStart('_');
                var singularStripped = Singularize(stripped);

                candidates.Add($"Id{stripped}");
                candidates.Add($"Id{singularStripped}");
                candidates.Add($"Id_{stripped}");
                candidates.Add($"Id_{singularStripped}");
                candidates.Add($"{stripped}Id");
                candidates.Add($"{singularStripped}Id");
                candidates.Add($"{stripped}_Id");
                candidates.Add($"{singularStripped}_Id");
                candidates.Add($"id_{stripped.ToLowerInvariant()}");
                candidates.Add($"id_{singularStripped.ToLowerInvariant()}");
                candidates.Add($"{stripped.ToLowerInvariant()}_id");
                candidates.Add($"{singularStripped.ToLowerInvariant()}_id");
                candidates.Add($"id{stripped.ToLowerInvariant()}");
                candidates.Add($"{stripped.ToLowerInvariant()}id");
            }
        }

        foreach (var candidate in candidates)
        {
            var col = dependentTable.FindColumn(candidate);
            if (col != null)
            {
                return col.Name;
            }
        }

        // 4. Look for unused column explicitly marked as FK in Mermaid definition
        var fkMarkedCol = dependentTable.Columns.Values.FirstOrDefault(c =>
            c.IsForeignKey && !c.IsPrimaryKey && !dependentTable.ForeignKeys.Any(f => f.DependentColumn.Equals(c.Name, StringComparison.OrdinalIgnoreCase)));
        if (fkMarkedCol != null)
        {
            return fkMarkedCol.Name;
        }

        // 5. Look for any other column with FK-like name (ending in Id or starting with Id)
        var fkCol = dependentTable.Columns.Values.FirstOrDefault(c =>
            (c.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) || c.Name.StartsWith("Id", StringComparison.OrdinalIgnoreCase)) && !c.IsPrimaryKey);
        if (fkCol != null)
        {
            return fkCol.Name;
        }

        return $"{singularPrincipal}Id";
    }

    private static string Singularize(string word)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Length <= 2)
        {
            return word;
        }

        var lower = word.ToLowerInvariant();
        if (lower.EndsWith("status") || lower.EndsWith("series") || lower.EndsWith("species") || lower.EndsWith("news"))
        {
            return word;
        }

        if (word.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && word.Length > 3)
        {
            return word.Substring(0, word.Length - 3) + "y";
        }

        if ((word.EndsWith("sses", StringComparison.OrdinalIgnoreCase) ||
             word.EndsWith("shes", StringComparison.OrdinalIgnoreCase) ||
             word.EndsWith("ches", StringComparison.OrdinalIgnoreCase) ||
             word.EndsWith("xes", StringComparison.OrdinalIgnoreCase) ||
             word.EndsWith("zes", StringComparison.OrdinalIgnoreCase)) && word.Length > 4)
        {
            return word.Substring(0, word.Length - 2);
        }

        if (word.EndsWith("s", StringComparison.OrdinalIgnoreCase) &&
            !word.EndsWith("ss", StringComparison.OrdinalIgnoreCase) &&
            !word.EndsWith("us", StringComparison.OrdinalIgnoreCase) &&
            !word.EndsWith("is", StringComparison.OrdinalIgnoreCase) &&
            !word.EndsWith("as", StringComparison.OrdinalIgnoreCase))
        {
            return word.Substring(0, word.Length - 1);
        }

        return word;
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
