using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SchemaAlign.Models;
using SchemaAlign.Models.TypeMapping;

namespace SchemaAlign.Readers.CSharp;

public class CSharpEntityReader : ISchemaReader
{
    private static readonly Regex XmlDocSummaryRegex = new(@"<summary>\s*([\s\S]*?)\s*</summary>", RegexOptions.Compiled);
    private static readonly Regex XmlDocCleanLineRegex = new(@"^\s*(?:///|\*)\s?", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly HashSet<string> CollectionTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ICollection", "IList", "List", "IEnumerable", "HashSet", "ISet",
        "Collection", "ObservableCollection", "ReadOnlyCollection", "IReadOnlyCollection", "IReadOnlyList"
    };

    public DatabaseSchema Read(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new DatabaseSchema();
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(content);
        return ReadSyntaxTrees(new[] { syntaxTree });
    }

    public DatabaseSchema ReadFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var sourceCode = File.ReadAllText(filePath);
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
        return ReadSyntaxTrees(new[] { syntaxTree });
    }

    public DatabaseSchema ReadFiles(IEnumerable<string> filePaths)
    {
        var syntaxTrees = filePaths
            .Where(File.Exists)
            .Select(fp => CSharpSyntaxTree.ParseText(File.ReadAllText(fp), path: fp))
            .ToList();

        return ReadSyntaxTrees(syntaxTrees);
    }

    public DatabaseSchema ReadDirectory(string directoryPath, string searchPattern = "*.cs", SearchOption searchOption = SearchOption.AllDirectories)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        var csFiles = Directory.GetFiles(directoryPath, searchPattern, searchOption)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}"))
            .ToList();

        return ReadFiles(csFiles);
    }

    public DatabaseSchema ReadSyntaxTrees(IEnumerable<SyntaxTree> syntaxTrees)
    {
        var schema = new DatabaseSchema();
        var rawClasses = new Dictionary<string, RawClassInfo>(StringComparer.OrdinalIgnoreCase);

        // Phase 1: Collect all raw class definitions
        foreach (var tree in syntaxTrees)
        {
            var root = tree.GetRoot();
            var classDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                .Where(t => t is ClassDeclarationSyntax or RecordDeclarationSyntax);

            foreach (var classDecl in classDeclarations)
            {
                var classInfo = ParseClassDeclaration(classDecl);
                if (classInfo != null && !classInfo.IsNotMapped)
                {
                    rawClasses[classInfo.ClassName] = classInfo;
                }
            }
        }

        // Phase 2: Flatten inheritance (resolve base class properties)
        var resolvedClasses = new Dictionary<string, ResolvedClassInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (className, rawClass) in rawClasses)
        {
            resolvedClasses[className] = ResolveClassHierarchy(rawClass, rawClasses);
        }

        // Phase 3: Create TableSchema and ColumnSchema for entity tables
        var entityTables = new Dictionary<string, (TableSchema Table, ResolvedClassInfo ClassInfo)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (className, resolved) in resolvedClasses)
        {
            // Skip abstract base classes unless explicitly marked with [Table]
            if (resolved.IsAbstract && !resolved.HasExplicitTableAttribute)
            {
                continue;
            }

            var table = schema.GetOrCreateTable(resolved.TableName, resolved.Schema);
            if (!string.IsNullOrWhiteSpace(resolved.Comment))
            {
                table.Comment = resolved.Comment;
            }

            // Check if any property has explicit IsKey
            var hasExplicitKey = resolved.Properties.Any(p => p.IsKey && !p.IsNotMapped);

            foreach (var prop in resolved.Properties)
            {
                if (prop.IsNotMapped)
                {
                    continue;
                }

                if (IsNavigationProperty(prop, rawClasses))
                {
                    continue;
                }

                var isConventionKey = !hasExplicitKey && (prop.PropertyName.Equals("Id", StringComparison.OrdinalIgnoreCase) || prop.PropertyName.Equals($"{className}Id", StringComparison.OrdinalIgnoreCase));
                var column = CreateColumnSchema(prop, isConventionKey);
                table.AddColumn(column);
            }

            entityTables[className] = (table, resolved);
        }

        // Phase 4: Resolve Navigation Properties & Foreign Keys
        foreach (var (className, (table, classInfo)) in entityTables)
        {
            ResolveForeignKeys(table, classInfo, entityTables, rawClasses);
        }

        return schema;
    }

    private static RawClassInfo? ParseClassDeclaration(TypeDeclarationSyntax classDecl)
    {
        var className = classDecl.Identifier.Text;
        var isAbstract = classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword);
        var isNotMapped = false;
        var hasExplicitTable = false;
        string tableName = className;
        string schemaName = "dbo";

        foreach (var attrList in classDecl.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var attrName = GetNormalizedAttributeName(attr);
                if (attrName.Equals("NotMapped", StringComparison.OrdinalIgnoreCase))
                {
                    isNotMapped = true;
                }
                else if (attrName.Equals("Table", StringComparison.OrdinalIgnoreCase))
                {
                    hasExplicitTable = true;
                    if (attr.ArgumentList != null && attr.ArgumentList.Arguments.Count > 0)
                    {
                        var firstArg = attr.ArgumentList.Arguments[0];
                        if (firstArg.NameEquals == null)
                        {
                            tableName = GetStringLiteralValue(firstArg.Expression) ?? tableName;
                        }

                        foreach (var arg in attr.ArgumentList.Arguments)
                        {
                            if (arg.NameEquals != null)
                            {
                                var paramName = arg.NameEquals.Name.Identifier.Text;
                                if (paramName.Equals("Schema", StringComparison.OrdinalIgnoreCase))
                                {
                                    schemaName = GetStringLiteralValue(arg.Expression) ?? schemaName;
                                }
                                else if (paramName.Equals("Name", StringComparison.OrdinalIgnoreCase))
                                {
                                    tableName = GetStringLiteralValue(arg.Expression) ?? tableName;
                                }
                            }
                        }
                    }
                }
            }
        }

        var baseTypes = new List<string>();
        if (classDecl.BaseList != null)
        {
            foreach (var baseType in classDecl.BaseList.Types)
            {
                var typeName = baseType.Type.ToString().Trim();
                var genericIdx = typeName.IndexOf('<');
                if (genericIdx > 0)
                {
                    typeName = typeName.Substring(0, genericIdx).Trim();
                }
                baseTypes.Add(typeName);
            }
        }

        var comment = ExtractXmlDocSummary(classDecl);

        var properties = new List<RawPropertyInfo>();
        var propDecls = classDecl.Members.OfType<PropertyDeclarationSyntax>();
        foreach (var propDecl in propDecls)
        {
            var propInfo = ParsePropertyDeclaration(propDecl);
            if (propInfo != null)
            {
                properties.Add(propInfo);
            }
        }

        return new RawClassInfo
        {
            ClassName = className,
            TableName = tableName,
            Schema = schemaName,
            IsAbstract = isAbstract,
            IsNotMapped = isNotMapped,
            HasExplicitTableAttribute = hasExplicitTable,
            BaseTypes = baseTypes,
            Comment = comment,
            Properties = properties
        };
    }

    private static RawPropertyInfo ParsePropertyDeclaration(PropertyDeclarationSyntax propDecl)
    {
        var propName = propDecl.Identifier.Text;
        var propType = propDecl.Type.ToString().Trim();
        var isVirtual = propDecl.Modifiers.Any(SyntaxKind.VirtualKeyword);
        var isNotMapped = false;
        var isKey = false;
        var isRequired = false;
        var isIdentity = false;
        string? customColumnName = null;
        string? customTypeName = null;
        int? maxLength = null;
        int? stringLength = null;
        string? foreignKeyName = null;

        foreach (var attrList in propDecl.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var attrName = GetNormalizedAttributeName(attr);
                if (attrName.Equals("NotMapped", StringComparison.OrdinalIgnoreCase))
                {
                    isNotMapped = true;
                }
                else if (attrName.Equals("Key", StringComparison.OrdinalIgnoreCase))
                {
                    isKey = true;
                }
                else if (attrName.Equals("Required", StringComparison.OrdinalIgnoreCase))
                {
                    isRequired = true;
                }
                else if (attrName.Equals("MaxLength", StringComparison.OrdinalIgnoreCase))
                {
                    if (attr.ArgumentList != null && attr.ArgumentList.Arguments.Count > 0)
                    {
                        var arg = attr.ArgumentList.Arguments[0];
                        if (int.TryParse(arg.Expression.ToString(), out var len))
                        {
                            maxLength = len;
                        }
                    }
                }
                else if (attrName.Equals("StringLength", StringComparison.OrdinalIgnoreCase))
                {
                    if (attr.ArgumentList != null && attr.ArgumentList.Arguments.Count > 0)
                    {
                        var arg = attr.ArgumentList.Arguments[0];
                        if (int.TryParse(arg.Expression.ToString(), out var len))
                        {
                            stringLength = len;
                        }
                    }
                }
                else if (attrName.Equals("Column", StringComparison.OrdinalIgnoreCase))
                {
                    if (attr.ArgumentList != null)
                    {
                        foreach (var arg in attr.ArgumentList.Arguments)
                        {
                            if (arg.NameEquals == null && customColumnName == null)
                            {
                                customColumnName = GetStringLiteralValue(arg.Expression);
                            }
                            else if (arg.NameEquals != null)
                            {
                                var paramName = arg.NameEquals.Name.Identifier.Text;
                                if (paramName.Equals("Name", StringComparison.OrdinalIgnoreCase))
                                {
                                    customColumnName = GetStringLiteralValue(arg.Expression);
                                }
                                else if (paramName.Equals("TypeName", StringComparison.OrdinalIgnoreCase))
                                {
                                    customTypeName = GetStringLiteralValue(arg.Expression);
                                }
                            }
                        }
                    }
                }
                else if (attrName.Equals("ForeignKey", StringComparison.OrdinalIgnoreCase))
                {
                    if (attr.ArgumentList != null && attr.ArgumentList.Arguments.Count > 0)
                    {
                        foreignKeyName = GetStringLiteralValue(attr.ArgumentList.Arguments[0].Expression);
                    }
                }
                else if (attrName.Equals("DatabaseGenerated", StringComparison.OrdinalIgnoreCase))
                {
                    var expr = attr.ArgumentList?.Arguments.FirstOrDefault()?.Expression.ToString() ?? string.Empty;
                    if (expr.Contains("Identity", StringComparison.OrdinalIgnoreCase))
                    {
                        isIdentity = true;
                    }
                }
            }
        }

        var comment = ExtractXmlDocSummary(propDecl);

        return new RawPropertyInfo
        {
            PropertyName = propName,
            TypeString = propType,
            IsVirtual = isVirtual,
            IsNotMapped = isNotMapped,
            IsKey = isKey,
            IsRequired = isRequired,
            IsIdentity = isIdentity,
            CustomColumnName = customColumnName,
            CustomTypeName = customTypeName,
            MaxLength = maxLength,
            StringLength = stringLength,
            ForeignKeyName = foreignKeyName,
            Comment = comment
        };
    }

    private static ResolvedClassInfo ResolveClassHierarchy(RawClassInfo rawClass, Dictionary<string, RawClassInfo> rawClasses)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rawClass.ClassName };
        var properties = new List<RawPropertyInfo>(rawClass.Properties);

        void CollectBaseProperties(string baseTypeName)
        {
            if (!visited.Add(baseTypeName) || !rawClasses.TryGetValue(baseTypeName, out var baseClass))
            {
                return;
            }

            foreach (var prop in baseClass.Properties)
            {
                if (!properties.Any(p => p.PropertyName.Equals(prop.PropertyName, StringComparison.OrdinalIgnoreCase)))
                {
                    properties.Add(prop);
                }
            }

            foreach (var parentBase in baseClass.BaseTypes)
            {
                CollectBaseProperties(parentBase);
            }
        }

        foreach (var baseType in rawClass.BaseTypes)
        {
            CollectBaseProperties(baseType);
        }

        return new ResolvedClassInfo
        {
            ClassName = rawClass.ClassName,
            TableName = rawClass.TableName,
            Schema = rawClass.Schema,
            IsAbstract = rawClass.IsAbstract,
            HasExplicitTableAttribute = rawClass.HasExplicitTableAttribute,
            Comment = rawClass.Comment,
            Properties = properties
        };
    }

    private static bool IsNavigationProperty(RawPropertyInfo prop, Dictionary<string, RawClassInfo> rawClasses)
    {
        var typeStr = prop.TypeString.TrimEnd('?');

        var genericMatch = Regex.Match(typeStr, @"^([a-zA-Z0-9_]+)<(.+)>$");
        if (genericMatch.Success)
        {
            var collName = genericMatch.Groups[1].Value;
            if (CollectionTypeNames.Contains(collName))
            {
                return true;
            }
        }

        if (rawClasses.ContainsKey(typeStr))
        {
            return true;
        }

        if (prop.IsVirtual)
        {
            var std = TypeMapper.FromCSharpType(typeStr, out _);
            if (std == StandardType.Unknown)
            {
                return true;
            }
        }

        return false;
    }

    private static ColumnSchema CreateColumnSchema(RawPropertyInfo prop, bool isConventionKey = false)
    {
        var columnName = prop.CustomColumnName ?? prop.PropertyName;
        int? length = prop.MaxLength ?? prop.StringLength;
        int? precision = null;
        int? scale = null;
        StandardType standardType;
        bool isNullable;

        if (!string.IsNullOrWhiteSpace(prop.CustomTypeName))
        {
            standardType = TypeMapper.FromSqlServerType(
                prop.CustomTypeName, length, precision, scale,
                out length, out precision, out scale);

            isNullable = prop.TypeString.EndsWith("?") ||
                         prop.TypeString.StartsWith("Nullable<", StringComparison.OrdinalIgnoreCase) ||
                         prop.TypeString.StartsWith("System.Nullable<", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            standardType = TypeMapper.FromCSharpType(prop.TypeString, out isNullable);
        }

        var isPrimaryKey = prop.IsKey || isConventionKey;
        if (isPrimaryKey || prop.IsRequired)
        {
            isNullable = false;
        }

        var isIdentity = prop.IsIdentity;
        if (!isIdentity && isPrimaryKey && (standardType is StandardType.Int or StandardType.BigInt or StandardType.SmallInt or StandardType.TinyInt))
        {
            isIdentity = true;
        }

        return new ColumnSchema
        {
            Name = columnName,
            Type = standardType,
            RawType = prop.CustomTypeName ?? prop.TypeString,
            Length = length,
            Precision = precision,
            Scale = scale,
            IsPrimaryKey = isPrimaryKey,
            IsNullable = isNullable,
            IsIdentity = isIdentity,
            Comment = prop.Comment
        };
    }

    private static void ResolveForeignKeys(
        TableSchema table,
        ResolvedClassInfo classInfo,
        Dictionary<string, (TableSchema Table, ResolvedClassInfo ClassInfo)> entityTables,
        Dictionary<string, RawClassInfo> rawClasses)
    {
        foreach (var prop in classInfo.Properties)
        {
            if (prop.IsNotMapped)
            {
                continue;
            }

            // Case 1: [ForeignKey("NavigationProperty")] on FK scalar property
            if (!string.IsNullOrWhiteSpace(prop.ForeignKeyName) && !IsNavigationProperty(prop, rawClasses))
            {
                var navPropName = prop.ForeignKeyName;
                var navProp = classInfo.Properties.FirstOrDefault(p => p.PropertyName.Equals(navPropName, StringComparison.OrdinalIgnoreCase));
                var targetTypeName = navProp != null ? navProp.TypeString.TrimEnd('?') : navPropName;

                if (entityTables.TryGetValue(targetTypeName, out var targetEntity))
                {
                    var fkCol = prop.CustomColumnName ?? prop.PropertyName;
                    var pkCol = targetEntity.Table.PrimaryKeys.FirstOrDefault() ?? "Id";

                    AddForeignKeyIfNotExists(table, new ForeignKeySchema
                    {
                        ConstraintName = $"FK_{table.Name}_{targetEntity.Table.Name}_{fkCol}",
                        DependentTable = table.Name,
                        DependentColumn = fkCol,
                        PrincipalTable = targetEntity.Table.Name,
                        PrincipalColumn = pkCol,
                        Cardinality = ForeignKeyCardinality.ManyToOne
                    });
                }
            }

            // Case 2: [ForeignKey("ForeignKeyProperty")] on navigation property
            if (IsNavigationProperty(prop, rawClasses))
            {
                var targetTypeName = GetTargetTypeName(prop.TypeString);
                if (entityTables.TryGetValue(targetTypeName, out var targetEntity))
                {
                    var isCollection = IsCollectionType(prop.TypeString);
                    if (!isCollection)
                    {
                        string? fkColumnName = null;
                        if (!string.IsNullOrWhiteSpace(prop.ForeignKeyName))
                        {
                            var fkProp = classInfo.Properties.FirstOrDefault(p => p.PropertyName.Equals(prop.ForeignKeyName, StringComparison.OrdinalIgnoreCase));
                            fkColumnName = fkProp?.CustomColumnName ?? prop.ForeignKeyName;
                        }
                        else
                        {
                            fkColumnName = FindMatchingFkColumn(table, prop.PropertyName, targetEntity.Table.Name);
                        }

                        if (!string.IsNullOrWhiteSpace(fkColumnName) && table.FindColumn(fkColumnName) != null)
                        {
                            var pkCol = targetEntity.Table.PrimaryKeys.FirstOrDefault() ?? "Id";
                            AddForeignKeyIfNotExists(table, new ForeignKeySchema
                            {
                                ConstraintName = $"FK_{table.Name}_{targetEntity.Table.Name}_{fkColumnName}",
                                DependentTable = table.Name,
                                DependentColumn = fkColumnName,
                                PrincipalTable = targetEntity.Table.Name,
                                PrincipalColumn = pkCol,
                                Cardinality = ForeignKeyCardinality.ManyToOne
                            });
                        }
                    }
                }
            }
        }
    }

    private static string? FindMatchingFkColumn(TableSchema table, string navPropName, string targetTableName)
    {
        var candidates = new[]
        {
            $"Id{navPropName}",
            $"{navPropName}Id",
            $"{navPropName}_Id",
            $"Id{targetTableName}",
            $"{targetTableName}Id",
            $"{targetTableName}_Id"
        };

        foreach (var candidate in candidates)
        {
            var col = table.FindColumn(candidate);
            if (col != null)
            {
                return col.Name;
            }
        }

        return null;
    }

    private static void AddForeignKeyIfNotExists(TableSchema table, ForeignKeySchema foreignKey)
    {
        if (!table.ForeignKeys.Any(k => k.PrincipalTable.Equals(foreignKey.PrincipalTable, StringComparison.OrdinalIgnoreCase)
                                     && k.DependentColumn.Equals(foreignKey.DependentColumn, StringComparison.OrdinalIgnoreCase)))
        {
            table.AddForeignKey(foreignKey);
        }
    }

    private static string GetTargetTypeName(string typeString)
    {
        var clean = typeString.Trim().TrimEnd('?');
        var genericMatch = Regex.Match(clean, @"^[a-zA-Z0-9_]+<(.+)>$");
        if (genericMatch.Success)
        {
            return genericMatch.Groups[1].Value.Trim().TrimEnd('?');
        }
        return clean;
    }

    private static bool IsCollectionType(string typeString)
    {
        var clean = typeString.Trim().TrimEnd('?');
        var genericMatch = Regex.Match(clean, @"^([a-zA-Z0-9_]+)<(.+)>$");
        if (genericMatch.Success)
        {
            return CollectionTypeNames.Contains(genericMatch.Groups[1].Value);
        }
        return false;
    }

    private static string GetNormalizedAttributeName(AttributeSyntax attr)
    {
        var name = attr.Name.ToString().Trim();
        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0)
        {
            name = name.Substring(lastDot + 1);
        }

        if (name.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(0, name.Length - 9);
        }

        return name;
    }

    private static string? GetStringLiteralValue(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax lit)
        {
            return lit.Token.ValueText;
        }

        var str = expr.ToString().Trim();
        if (str.StartsWith("\"") && str.EndsWith("\"") && str.Length >= 2)
        {
            return str.Substring(1, str.Length - 2);
        }

        return str;
    }

    private static string? ExtractXmlDocSummary(SyntaxNode node)
    {
        var trivia = node.GetLeadingTrivia();
        var docText = trivia.ToFullString();
        if (string.IsNullOrWhiteSpace(docText))
        {
            return null;
        }

        var match = XmlDocSummaryRegex.Match(docText);
        if (match.Success)
        {
            var summaryContent = match.Groups[1].Value;
            var cleaned = XmlDocCleanLineRegex.Replace(summaryContent, "").Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
        }

        return null;
    }

    private class RawClassInfo
    {
        public string ClassName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string Schema { get; set; } = "dbo";
        public bool IsAbstract { get; set; }
        public bool IsNotMapped { get; set; }
        public bool HasExplicitTableAttribute { get; set; }
        public List<string> BaseTypes { get; set; } = new();
        public string? Comment { get; set; }
        public List<RawPropertyInfo> Properties { get; set; } = new();
    }

    private class RawPropertyInfo
    {
        public string PropertyName { get; set; } = string.Empty;
        public string TypeString { get; set; } = string.Empty;
        public bool IsVirtual { get; set; }
        public bool IsNotMapped { get; set; }
        public bool IsKey { get; set; }
        public bool IsRequired { get; set; }
        public bool IsIdentity { get; set; }
        public string? CustomColumnName { get; set; }
        public string? CustomTypeName { get; set; }
        public int? MaxLength { get; set; }
        public int? StringLength { get; set; }
        public string? ForeignKeyName { get; set; }
        public string? Comment { get; set; }
    }

    private class ResolvedClassInfo
    {
        public string ClassName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string Schema { get; set; } = "dbo";
        public bool IsAbstract { get; set; }
        public bool HasExplicitTableAttribute { get; set; }
        public string? Comment { get; set; }
        public List<RawPropertyInfo> Properties { get; set; } = new();
    }
}
