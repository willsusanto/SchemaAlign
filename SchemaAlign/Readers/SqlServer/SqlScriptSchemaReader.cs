using System.Text;
using System.Text.RegularExpressions;
using SchemaAlign.Models;
using SchemaAlign.Models.TypeMapping;

namespace SchemaAlign.Readers.SqlServer;

public class SqlScriptSchemaReader : ISchemaReader
{
    private static readonly Regex TableConstraintPkRegex = new(
        @"^(?:CONSTRAINT\s+([a-zA-Z0-9_\.\[\]""]+)\s+)?PRIMARY\s+KEY(?:\s+(?:CLUSTERED|NONCLUSTERED))?\s*\(([^)]+)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TableConstraintFkRegex = new(
        @"^(?:CONSTRAINT\s+([a-zA-Z0-9_\.\[\]""]+)\s+)?FOREIGN\s+KEY\s*\(([^)]+)\)\s*REFERENCES\s+([a-zA-Z0-9_\.\[\]""]+)\s*(?:\(([^)]+)\))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AlterTableRegex = new(
        @"ALTER\s+TABLE\s+([a-zA-Z0-9_\.\[\]""]+)(?:\s+WITH\s+(?:CHECK|NOCHECK))?\s+ADD\s+([\s\S]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExtendedPropertyRegex = new(
        @"EXEC(?:UTE)?\s+(?:sys\.)?sp_(?:add|update)extendedproperty\s+([\s\S]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InlineFkRegex = new(
        @"REFERENCES\s+([a-zA-Z0-9_\.\[\]""]+)\s*(?:\(([^)]+)\))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DefaultRegex = new(
        @"DEFAULT\s+((?:\((?:[^()]|\([^()]*\))*\)|'[^']*'|N'[^']*'|[a-zA-Z0-9_\(\)]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DatabaseSchema Read(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new DatabaseSchema();
        }

        var schema = new DatabaseSchema();
        var cleanSql = CleanSqlComments(content);

        // Split batches by GO statements
        var batches = Regex.Split(cleanSql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            ParseCreateTables(batch, schema);
            ParseAlterTables(batch, schema);
            ParseExtendedProperties(batch, schema);
        }

        return schema;
    }

    public DatabaseSchema ReadFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        return Read(File.ReadAllText(filePath));
    }

    public DatabaseSchema ReadFiles(IEnumerable<string> filePaths)
    {
        var schema = new DatabaseSchema();
        foreach (var file in filePaths.Where(File.Exists))
        {
            var fileSchema = Read(File.ReadAllText(file));
            MergeSchemas(schema, fileSchema);
        }
        return schema;
    }

    public DatabaseSchema ReadDirectory(string directoryPath, string searchPattern = "*.sql", SearchOption searchOption = SearchOption.AllDirectories)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        var sqlFiles = Directory.GetFiles(directoryPath, searchPattern, searchOption)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}"))
            .ToList();

        return ReadFiles(sqlFiles);
    }

    private static void ParseCreateTables(string sql, DatabaseSchema schema)
    {
        var createTablePattern = new Regex(@"CREATE\s+TABLE\s+([a-zA-Z0-9_\.\[\]""]+)\s*\(", RegexOptions.IgnoreCase);
        var matches = createTablePattern.Matches(sql);

        foreach (Match match in matches)
        {
            var rawTableName = match.Groups[1].Value;
            var (schemaName, tableName) = SplitSchemaAndTableName(rawTableName);
            var openParenIndex = match.Index + match.Length - 1;

            var body = ExtractParenthesizedBody(sql, openParenIndex, out _);
            if (body == null)
            {
                continue;
            }

            var table = schema.GetOrCreateTable(tableName, schemaName);
            var items = SplitSqlItems(body);

            foreach (var rawItem in items)
            {
                var item = rawItem.Trim();
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                // Check table constraint: PRIMARY KEY
                var pkMatch = TableConstraintPkRegex.Match(item);
                if (pkMatch.Success)
                {
                    var cols = pkMatch.Groups[2].Value.Split(',');
                    foreach (var col in cols)
                    {
                        var colName = CleanIdentifier(CleanOrderSuffix(col));
                        var existingCol = table.FindColumn(colName);
                        if (existingCol != null)
                        {
                            existingCol.IsPrimaryKey = true;
                            existingCol.IsNullable = false;
                        }
                    }
                    continue;
                }

                // Check table constraint: FOREIGN KEY
                var fkMatch = TableConstraintFkRegex.Match(item);
                if (fkMatch.Success)
                {
                    var constraintName = fkMatch.Groups[1].Success ? CleanIdentifier(fkMatch.Groups[1].Value) : null;
                    var depCol = CleanIdentifier(fkMatch.Groups[2].Value.Split(',')[0]);
                    var (pSchema, pTable) = SplitSchemaAndTableName(fkMatch.Groups[3].Value);
                    var pCol = fkMatch.Groups[4].Success ? CleanIdentifier(fkMatch.Groups[4].Value.Split(',')[0]) : "Id";

                    var fk = new ForeignKeySchema
                    {
                        ConstraintName = constraintName ?? $"FK_{table.Name}_{pTable}_{depCol}",
                        DependentTable = table.Name,
                        DependentColumn = depCol,
                        PrincipalTable = pTable,
                        PrincipalColumn = pCol,
                        Cardinality = ForeignKeyCardinality.ManyToOne
                    };
                    table.AddForeignKey(fk);
                    continue;
                }

                // Check other table constraints (CHECK, UNIQUE)
                if (item.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase) ||
                    item.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase) ||
                    item.StartsWith("UNIQUE", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Parse Column Definition
                ParseColumnDefinition(item, table);
            }
        }
    }

    private static void ParseColumnDefinition(string columnDef, TableSchema table)
    {
        var tokens = TokenizeColumnDefinition(columnDef);
        if (tokens.Count < 2)
        {
            return;
        }

        var columnName = CleanIdentifier(tokens[0]);
        var rawType = tokens[1];
        var rest = string.Join(" ", tokens.Skip(2));

        var isPrimaryKey = Regex.IsMatch(rest, @"\bPRIMARY\s+KEY\b", RegexOptions.IgnoreCase);
        var isIdentity = Regex.IsMatch(rest, @"\bIDENTITY\b", RegexOptions.IgnoreCase);
        var hasNotNull = Regex.IsMatch(rest, @"\bNOT\s+NULL\b", RegexOptions.IgnoreCase);
        var hasNull = !hasNotNull && Regex.IsMatch(rest, @"\bNULL\b", RegexOptions.IgnoreCase);

        var isNullable = !isPrimaryKey && !hasNotNull;
        if (isPrimaryKey || hasNotNull)
        {
            isNullable = false;
        }
        else if (hasNull)
        {
            isNullable = true;
        }

        string? defaultValue = null;
        var defMatch = DefaultRegex.Match(rest);
        if (defMatch.Success)
        {
            defaultValue = defMatch.Groups[1].Value.Trim();
        }

        var standardType = TypeMapper.FromSqlServerType(
            rawType, null, null, null,
            out var length, out var precision, out var scale);

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
            IsIdentity = isIdentity,
            DefaultValue = defaultValue
        };

        table.AddColumn(column);

        // Check inline REFERENCES
        var inlineFkMatch = InlineFkRegex.Match(rest);
        if (inlineFkMatch.Success)
        {
            var (pSchema, pTable) = SplitSchemaAndTableName(inlineFkMatch.Groups[1].Value);
            var pCol = inlineFkMatch.Groups[2].Success ? CleanIdentifier(inlineFkMatch.Groups[2].Value) : "Id";

            var fk = new ForeignKeySchema
            {
                ConstraintName = $"FK_{table.Name}_{pTable}_{columnName}",
                DependentTable = table.Name,
                DependentColumn = columnName,
                PrincipalTable = pTable,
                PrincipalColumn = pCol,
                Cardinality = ForeignKeyCardinality.ManyToOne
            };
            table.AddForeignKey(fk);
        }
    }

    private static void ParseAlterTables(string sql, DatabaseSchema schema)
    {
        var matches = AlterTableRegex.Matches(sql);
        foreach (Match match in matches)
        {
            var rawTableName = match.Groups[1].Value;
            var (schemaName, tableName) = SplitSchemaAndTableName(rawTableName);
            var actionBody = match.Groups[2].Value;

            var semicolonIdx = actionBody.IndexOf(';');
            if (semicolonIdx >= 0)
            {
                actionBody = actionBody.Substring(0, semicolonIdx);
            }

            var table = schema.GetOrCreateTable(tableName, schemaName);
            var items = SplitSqlItems(actionBody);

            foreach (var item in items)
            {
                var trimmedItem = item.Trim();
                if (string.IsNullOrWhiteSpace(trimmedItem))
                {
                    continue;
                }

                // Check Foreign Key
                var fkMatch = TableConstraintFkRegex.Match(trimmedItem);
                if (fkMatch.Success)
                {
                    var constraintName = fkMatch.Groups[1].Success ? CleanIdentifier(fkMatch.Groups[1].Value) : null;
                    var depCol = CleanIdentifier(fkMatch.Groups[2].Value.Split(',')[0]);
                    var (pSchema, pTable) = SplitSchemaAndTableName(fkMatch.Groups[3].Value);
                    var pCol = fkMatch.Groups[4].Success ? CleanIdentifier(fkMatch.Groups[4].Value.Split(',')[0]) : "Id";

                    var fk = new ForeignKeySchema
                    {
                        ConstraintName = constraintName ?? $"FK_{table.Name}_{pTable}_{depCol}",
                        DependentTable = table.Name,
                        DependentColumn = depCol,
                        PrincipalTable = pTable,
                        PrincipalColumn = pCol,
                        Cardinality = ForeignKeyCardinality.ManyToOne
                    };
                    table.AddForeignKey(fk);
                    continue;
                }

                // Check Primary Key
                var pkMatch = TableConstraintPkRegex.Match(trimmedItem);
                if (pkMatch.Success)
                {
                    var cols = pkMatch.Groups[2].Value.Split(',');
                    foreach (var col in cols)
                    {
                        var colName = CleanIdentifier(CleanOrderSuffix(col));
                        var existingCol = table.FindColumn(colName);
                        if (existingCol != null)
                        {
                            existingCol.IsPrimaryKey = true;
                            existingCol.IsNullable = false;
                        }
                    }
                    continue;
                }

                // Add Column
                if (!trimmedItem.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase) &&
                    !trimmedItem.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase) &&
                    !trimmedItem.StartsWith("UNIQUE", StringComparison.OrdinalIgnoreCase))
                {
                    ParseColumnDefinition(trimmedItem, table);
                }
            }
        }
    }

    private static void ParseExtendedProperties(string sql, DatabaseSchema schema)
    {
        var matches = ExtendedPropertyRegex.Matches(sql);
        foreach (Match match in matches)
        {
            var body = match.Groups[1].Value;
            var semicolonIdx = body.IndexOf(';');
            if (semicolonIdx >= 0)
            {
                body = body.Substring(0, semicolonIdx);
            }

            var name = ExtractParamValue(body, "name");
            var value = ExtractParamValue(body, "value");
            var level1Type = ExtractParamValue(body, "level1type");
            var level1Name = ExtractParamValue(body, "level1name");
            var level2Type = ExtractParamValue(body, "level2type");
            var level2Name = ExtractParamValue(body, "level2name");

            if (string.IsNullOrWhiteSpace(value) || !string.Equals(level1Type, "TABLE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(level1Name))
            {
                continue;
            }

            var table = schema.FindTable(level1Name);
            if (table == null)
            {
                continue;
            }

            if (string.Equals(level2Type, "COLUMN", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(level2Name))
            {
                var column = table.FindColumn(level2Name);
                if (column != null)
                {
                    column.Comment = value;
                }
            }
            else
            {
                table.Comment = value;
            }
        }
    }

    private static string? ExtractParamValue(string body, string paramName)
    {
        var regex = new Regex($@"@{paramName}\s*=\s*(?:N)?'([^']*)'", RegexOptions.IgnoreCase);
        var match = regex.Match(body);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static List<string> TokenizeColumnDefinition(string columnDef)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        var i = 0;
        var len = columnDef.Length;
        var inBracket = false;
        var inString = false;
        var parenDepth = 0;

        while (i < len)
        {
            var ch = columnDef[i];

            if (inString)
            {
                sb.Append(ch);
                if (ch == '\'')
                {
                    inString = false;
                }
                i++;
                continue;
            }

            if (inBracket)
            {
                sb.Append(ch);
                if (ch == ']')
                {
                    inBracket = false;
                }
                i++;
                continue;
            }

            if (ch == '\'')
            {
                inString = true;
                sb.Append(ch);
                i++;
                continue;
            }

            if (ch == '[')
            {
                inBracket = true;
                sb.Append(ch);
                i++;
                continue;
            }

            if (ch == '(')
            {
                parenDepth++;
                sb.Append(ch);
                i++;
                continue;
            }

            if (ch == ')')
            {
                if (parenDepth > 0) parenDepth--;
                sb.Append(ch);
                i++;
                continue;
            }

            if (char.IsWhiteSpace(ch) && parenDepth == 0)
            {
                if (sb.Length > 0)
                {
                    tokens.Add(sb.ToString().Trim());
                    sb.Clear();
                }
                i++;
                continue;
            }

            sb.Append(ch);
            i++;
        }

        if (sb.Length > 0)
        {
            tokens.Add(sb.ToString().Trim());
        }

        return tokens;
    }

    private static List<string> SplitSqlItems(string sql)
    {
        var items = new List<string>();
        var sb = new StringBuilder();
        var i = 0;
        var len = sql.Length;
        var inString = false;
        var inBracket = false;
        var parenDepth = 0;

        while (i < len)
        {
            var ch = sql[i];

            if (inString)
            {
                sb.Append(ch);
                if (ch == '\'')
                {
                    if (i + 1 < len && sql[i + 1] == '\'')
                    {
                        sb.Append('\'');
                        i += 2;
                        continue;
                    }
                    inString = false;
                }
                i++;
                continue;
            }

            if (inBracket)
            {
                sb.Append(ch);
                if (ch == ']')
                {
                    inBracket = false;
                }
                i++;
                continue;
            }

            if (ch == '\'')
            {
                inString = true;
                sb.Append(ch);
                i++;
                continue;
            }

            if (ch == '[')
            {
                inBracket = true;
                sb.Append(ch);
                i++;
                continue;
            }

            if (ch == '(')
            {
                parenDepth++;
                sb.Append(ch);
                i++;
                continue;
            }

            if (ch == ')')
            {
                if (parenDepth > 0) parenDepth--;
                sb.Append(ch);
                i++;
                continue;
            }

            if (ch == ',' && parenDepth == 0)
            {
                items.Add(sb.ToString().Trim());
                sb.Clear();
                i++;
                continue;
            }

            sb.Append(ch);
            i++;
        }

        if (sb.Length > 0)
        {
            var remaining = sb.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(remaining))
            {
                items.Add(remaining);
            }
        }

        return items;
    }

    private static string? ExtractParenthesizedBody(string sql, int openParenIndex, out int closeParenIndex)
    {
        closeParenIndex = -1;
        if (openParenIndex < 0 || openParenIndex >= sql.Length || sql[openParenIndex] != '(')
        {
            return null;
        }

        var depth = 1;
        var inString = false;
        var inBracket = false;

        for (var i = openParenIndex + 1; i < sql.Length; i++)
        {
            var ch = sql[i];

            if (inString)
            {
                if (ch == '\'')
                {
                    if (i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        i++;
                        continue;
                    }
                    inString = false;
                }
                continue;
            }

            if (inBracket)
            {
                if (ch == ']')
                {
                    inBracket = false;
                }
                continue;
            }

            if (ch == '\'')
            {
                inString = true;
                continue;
            }

            if (ch == '[')
            {
                inBracket = true;
                continue;
            }

            if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
            {
                depth--;
                if (depth == 0)
                {
                    closeParenIndex = i;
                    return sql.Substring(openParenIndex + 1, i - openParenIndex - 1);
                }
            }
        }

        return null;
    }

    private static string CleanSqlComments(string sql)
    {
        var sb = new StringBuilder();
        var i = 0;
        var len = sql.Length;
        var inString = false;

        while (i < len)
        {
            if (inString)
            {
                sb.Append(sql[i]);
                if (sql[i] == '\'')
                {
                    if (i + 1 < len && sql[i + 1] == '\'')
                    {
                        sb.Append(sql[i + 1]);
                        i += 2;
                        continue;
                    }
                    inString = false;
                }
                i++;
                continue;
            }

            if (sql[i] == '\'')
            {
                inString = true;
                sb.Append('\'');
                i++;
                continue;
            }

            // Line comment --
            if (i + 1 < len && sql[i] == '-' && sql[i + 1] == '-')
            {
                i += 2;
                while (i < len && sql[i] != '\r' && sql[i] != '\n')
                {
                    i++;
                }
                continue;
            }

            // Block comment /* ... */
            if (i + 1 < len && sql[i] == '/' && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < len && !(sql[i] == '*' && sql[i + 1] == '/'))
                {
                    i++;
                }
                if (i + 1 < len)
                {
                    i += 2;
                }
                continue;
            }

            sb.Append(sql[i]);
            i++;
        }

        return sb.ToString();
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

    private static string CleanOrderSuffix(string identifier)
    {
        var trimmed = identifier.Trim();
        if (trimmed.EndsWith(" ASC", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Substring(0, trimmed.Length - 4).Trim();
        }
        if (trimmed.EndsWith(" DESC", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Substring(0, trimmed.Length - 5).Trim();
        }
        return trimmed;
    }

    private static void MergeSchemas(DatabaseSchema target, DatabaseSchema source)
    {
        foreach (var (tableName, sourceTable) in source.Tables)
        {
            var targetTable = target.GetOrCreateTable(sourceTable.Name, sourceTable.Schema);
            if (!string.IsNullOrWhiteSpace(sourceTable.Comment))
            {
                targetTable.Comment = sourceTable.Comment;
            }

            foreach (var (colName, sourceCol) in sourceTable.Columns)
            {
                targetTable.AddColumn(sourceCol);
            }

            foreach (var fk in sourceTable.ForeignKeys)
            {
                targetTable.AddForeignKey(fk);
            }
        }
    }
}
