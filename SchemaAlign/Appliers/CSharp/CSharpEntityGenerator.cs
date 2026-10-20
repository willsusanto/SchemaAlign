using System.Text;
using SchemaAlign.Models;
using SchemaAlign.Models.TypeMapping;

namespace SchemaAlign.Appliers.CSharp;

/// <summary>
/// Generates C# entity class source code from a <see cref="TableSchema"/>.
/// </summary>
public static class CSharpEntityGenerator
{
    /// <summary>
    /// Generates full C# source code for an entity class based on the given table schema and options.
    /// </summary>
    public static string Generate(TableSchema table, CSharpApplierOptions? options = null)
    {
        options ??= new CSharpApplierOptions();
        var sb = new StringBuilder();
        var ns = options.DefaultNamespace ?? "Entities";
        var className = NamingHelper.ToEntityClassName(table.Name);

        // Usings
        var usings = new HashSet<string>(StringComparer.Ordinal)
        {
            "using System;"
        };

        if (options.UseDataAnnotations)
        {
            usings.Add("using System.ComponentModel.DataAnnotations;");
            usings.Add("using System.ComponentModel.DataAnnotations.Schema;");
        }

        // Add usings for foreign key principal types
        foreach (var fk in table.ForeignKeys)
        {
            var principalClassName = NamingHelper.ToEntityClassName(fk.PrincipalTable);
            if (options.EntityNamespaces.TryGetValue(principalClassName, out var principalNs) ||
                options.EntityNamespaces.TryGetValue(fk.PrincipalTable, out principalNs))
            {
                if (!string.IsNullOrWhiteSpace(principalNs) && !string.Equals(principalNs, ns, StringComparison.OrdinalIgnoreCase))
                {
                    usings.Add($"using {principalNs};");
                }
            }
        }

        // Add using for base class if resolved to another namespace
        if (!string.IsNullOrWhiteSpace(options.BaseClass))
        {
            var baseClassName = options.BaseClass.Trim();
            if (options.EntityNamespaces.TryGetValue(baseClassName, out var baseNs))
            {
                if (!string.IsNullOrWhiteSpace(baseNs) && !string.Equals(baseNs, ns, StringComparison.OrdinalIgnoreCase))
                {
                    usings.Add($"using {baseNs};");
                }
            }
        }

        // Add any explicit additional usings
        foreach (var extra in options.AdditionalUsings)
        {
            if (string.IsNullOrWhiteSpace(extra)) continue;
            var clean = extra.Trim();
            var usingStmt = clean.StartsWith("using ", StringComparison.Ordinal)
                ? (clean.EndsWith(";", StringComparison.Ordinal) ? clean : $"{clean};")
                : $"using {clean};";
            var nsPart = clean.StartsWith("using ", StringComparison.Ordinal)
                ? clean[6..].TrimEnd(';').Trim()
                : clean.TrimEnd(';');
            if (!string.Equals(nsPart, ns, StringComparison.OrdinalIgnoreCase))
            {
                usings.Add(usingStmt);
            }
        }

        // Sort usings: System first, then System.*, then alphabetically
        var sortedUsings = usings
            .OrderBy(u => u == "using System;" ? 0 : (u.StartsWith("using System", StringComparison.Ordinal) ? 1 : 2))
            .ThenBy(u => u, StringComparer.Ordinal)
            .ToList();

        foreach (var u in sortedUsings)
        {
            sb.AppendLine(u);
        }
        sb.AppendLine();

        // Namespace
        if (options.UseFileScopedNamespaces)
        {
            sb.AppendLine($"namespace {ns};");
            sb.AppendLine();
            GenerateClassBody(sb, table, className, options, indent: "");
        }
        else
        {
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            GenerateClassBody(sb, table, className, options, indent: "    ");
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static void GenerateClassBody(
        StringBuilder sb,
        TableSchema table,
        string className,
        CSharpApplierOptions options,
        string indent)
    {
        // Table comment
        if (!string.IsNullOrWhiteSpace(table.Comment))
        {
            sb.AppendLine($"{indent}/// <summary>");
            sb.AppendLine($"{indent}/// {table.Comment}");
            sb.AppendLine($"{indent}/// </summary>");
        }

        // Custom class attributes (e.g. [DatabaseName("...")] or [CustomMarker])
        foreach (var attr in options.ClassAttributes)
        {
            if (string.IsNullOrWhiteSpace(attr)) continue;
            var trimmed = attr.Trim();
            var formattedAttr = trimmed.StartsWith("[") && trimmed.EndsWith("]") ? trimmed : $"[{trimmed}]";
            sb.AppendLine($"{indent}{formattedAttr}");
        }

        // [Table] attribute
        if (options.UseDataAnnotations)
        {
            if (options.AddSchemaToTableAttribute || (!string.IsNullOrEmpty(table.Schema) && !string.Equals(table.Schema, "dbo", StringComparison.OrdinalIgnoreCase)))
            {
                sb.AppendLine($"{indent}[Table(\"{table.Name}\", Schema = \"{table.Schema}\")]");
            }
            else
            {
                sb.AppendLine($"{indent}[Table(\"{table.Name}\")]");
            }
        }

        var baseClassSuffix = !string.IsNullOrWhiteSpace(options.BaseClass)
            ? $" : {options.BaseClass.Trim()}"
            : string.Empty;

        sb.AppendLine($"{indent}public class {className}{baseClassSuffix}");
        sb.AppendLine($"{indent}{{");

        var memberIndent = indent + "    ";
        var first = true;

        // Foreign keys / Navigation properties pre-calculation
        var usedPropNames = new HashSet<string>(table.Columns.Values.Select(c => NamingHelper.ToPascalCase(c.Name)), StringComparer.OrdinalIgnoreCase);
        var fkNavPropMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var navProperties = new List<(ForeignKeySchema fk, string principalClassName, string navPropName, string fkPropName)>();

        foreach (var fk in table.ForeignKeys)
        {
            var principalClassName = NamingHelper.ToEntityClassName(fk.PrincipalTable);
            var navPropName = NamingHelper.ToNavigationPropertyName(fk.DependentColumn, fk.PrincipalTable);

            if (usedPropNames.Contains(navPropName))
            {
                navPropName += "Entity";
            }

            var baseNavName = navPropName;
            int counter = 1;
            while (usedPropNames.Contains(navPropName))
            {
                navPropName = $"{baseNavName}{++counter}";
            }
            usedPropNames.Add(navPropName);

            var fkPropName = NamingHelper.ToPascalCase(fk.DependentColumn);
            navProperties.Add((fk, principalClassName, navPropName, fkPropName));

            if (!fkNavPropMap.TryGetValue(fk.DependentColumn, out var list))
            {
                list = new List<string>();
                fkNavPropMap[fk.DependentColumn] = list;
            }
            list.Add(navPropName);

            if (!fkNavPropMap.TryGetValue(fkPropName, out var propList))
            {
                propList = new List<string>();
                fkNavPropMap[fkPropName] = propList;
            }
            if (!propList.Contains(navPropName))
            {
                propList.Add(navPropName);
            }
        }

        // Columns
        foreach (var col in table.Columns.Values)
        {
            var propName = NamingHelper.ToPascalCase(col.Name);

            // Skip columns that are inherited from the base class
            if (options.OmitInheritedColumns.Contains(col.Name) || options.OmitInheritedColumns.Contains(propName))
            {
                continue;
            }

            if (!first)
            {
                sb.AppendLine();
            }
            first = false;
            var typeStr = TypeMapper.ToCSharpType(col.Type, col.IsNullable);

            // Property comment
            if (!string.IsNullOrWhiteSpace(col.Comment))
            {
                sb.AppendLine($"{memberIndent}/// <summary>");
                sb.AppendLine($"{memberIndent}/// {col.Comment}");
                sb.AppendLine($"{memberIndent}/// </summary>");
            }

            // Attributes
            if (options.UseDataAnnotations)
            {
                if (col.IsPrimaryKey)
                {
                    sb.AppendLine($"{memberIndent}[Key]");
                }

                if (col.IsIdentity)
                {
                    sb.AppendLine($"{memberIndent}[DatabaseGenerated(DatabaseGeneratedOption.Identity)]");
                }

                if (!string.Equals(col.Name, propName, StringComparison.Ordinal))
                {
                    sb.AppendLine($"{memberIndent}[Column(\"{col.Name}\")]");
                }

                if (options.ForeignKeyPlacement == ForeignKeyPlacement.Scalar &&
                    (fkNavPropMap.TryGetValue(col.Name, out var navNames) || fkNavPropMap.TryGetValue(propName, out navNames)))
                {
                    foreach (var navName in navNames.Distinct())
                    {
                        sb.AppendLine($"{memberIndent}[ForeignKey(\"{navName}\")]");
                    }
                }

                if (col.Length.HasValue && col.Length > 0 &&
                    (col.Type == StandardType.String || col.Type == StandardType.ByteArray))
                {
                    sb.AppendLine($"{memberIndent}[MaxLength({col.Length.Value})]");
                }

                if (col.Precision.HasValue && col.Scale.HasValue && col.Type == StandardType.Decimal)
                {
                    sb.AppendLine($"{memberIndent}[Precision({col.Precision.Value}, {col.Scale.Value})]");
                }
            }

            // Initializer for non-nullable strings
            var initializer = "";
            if (options.UseNullableReferenceTypes && col.Type == StandardType.String && !col.IsNullable)
            {
                initializer = " = string.Empty;";
            }

            sb.AppendLine($"{memberIndent}public {typeStr} {propName} {{ get; set; }}{initializer}");
        }

        // Foreign keys / Navigation properties
        foreach (var nav in navProperties)
        {
            if (!first)
            {
                sb.AppendLine();
            }
            first = false;

            if (options.UseDataAnnotations && options.ForeignKeyPlacement == ForeignKeyPlacement.Navigation)
            {
                sb.AppendLine($"{memberIndent}[ForeignKey(\"{nav.fkPropName}\")]");
            }

            sb.AppendLine($"{memberIndent}public virtual {nav.principalClassName}? {nav.navPropName} {{ get; set; }}");
        }

        sb.AppendLine($"{indent}}}");
    }
}
