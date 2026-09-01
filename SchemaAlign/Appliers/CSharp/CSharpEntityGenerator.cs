using System.Text;
using SchemaAlign.Models;
using SchemaAlign.Models.TypeMapping;

namespace SchemaAlign.Appliers.CSharp;

public static class CSharpEntityGenerator
{
    public static string Generate(TableSchema table, CSharpApplierOptions? options = null)
    {
        options ??= new CSharpApplierOptions();
        var sb = new StringBuilder();
        var ns = options.DefaultNamespace ?? "Entities";
        var className = NamingHelper.ToEntityClassName(table.Name);

        // Usings
        sb.AppendLine("using System;");
        if (options.UseDataAnnotations)
        {
            sb.AppendLine("using System.ComponentModel.DataAnnotations;");
            sb.AppendLine("using System.ComponentModel.DataAnnotations.Schema;");
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

        sb.AppendLine($"{indent}public class {className}");
        sb.AppendLine($"{indent}{{");

        var memberIndent = indent + "    ";
        var first = true;

        // Columns
        foreach (var col in table.Columns.Values)
        {
            if (!first)
            {
                sb.AppendLine();
            }
            first = false;

            var propName = NamingHelper.ToPascalCase(col.Name);
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
        foreach (var fk in table.ForeignKeys)
        {
            if (!first)
            {
                sb.AppendLine();
            }
            first = false;

            var principalClassName = NamingHelper.ToEntityClassName(fk.PrincipalTable);
            var navPropName = principalClassName;

            // If navPropName is same as any column propName, avoid collision
            if (table.Columns.Values.Any(c => string.Equals(NamingHelper.ToPascalCase(c.Name), navPropName, StringComparison.OrdinalIgnoreCase)))
            {
                navPropName += "Entity";
            }

            var fkPropName = NamingHelper.ToPascalCase(fk.DependentColumn);

            if (options.UseDataAnnotations)
            {
                sb.AppendLine($"{memberIndent}[ForeignKey(\"{fkPropName}\")]");
            }

            sb.AppendLine($"{memberIndent}public virtual {principalClassName}? {navPropName} {{ get; set; }}");
        }

        sb.AppendLine($"{indent}}}");
    }
}
