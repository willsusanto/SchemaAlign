using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SchemaAlign.Appliers.CSharp;
using SchemaAlign.Diff;
using SchemaAlign.Models;
using SchemaAlign.Models.TypeMapping;

namespace SchemaAlign.Appliers.CSharp;

/// <summary>
/// Roslyn C# syntax rewriter that updates existing entity classes in-place based on <see cref="TableDiff"/>, preserving comments, methods, and formatting.
/// </summary>
public class CSharpEntityRewriter : CSharpSyntaxRewriter
{
    private readonly TableDiff _tableDiff;
    private readonly CSharpApplierOptions _options;
    private bool _needsDataAnnotations;
    private bool _needsDataAnnotationsSchema;

    /// <summary>
    /// Indicates whether any modifications were made to the syntax tree during rewriting.
    /// </summary>
    public bool HasModifications { get; private set; }

    public CSharpEntityRewriter(TableDiff tableDiff, CSharpApplierOptions? options = null)
    {
        _tableDiff = tableDiff;
        _options = options ?? new CSharpApplierOptions();
    }

    /// <summary>
    /// Rewrites source code in-place by applying property additions, modifications, and attribute changes matching the table diff.
    /// </summary>
    public static string Rewrite(string sourceCode, TableDiff tableDiff, CSharpApplierOptions? options = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = syntaxTree.GetCompilationUnitRoot();

        var rewriter = new CSharpEntityRewriter(tableDiff, options);
        var newRoot = (CompilationUnitSyntax)rewriter.Visit(root);

        if (rewriter._options.AutoAddMissingUsings && (rewriter._needsDataAnnotations || rewriter._needsDataAnnotationsSchema))
        {
            newRoot = EnsureUsings(newRoot, rewriter._needsDataAnnotations, rewriter._needsDataAnnotationsSchema);
        }

        return newRoot.ToFullString();
    }

    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        if (!IsMatchingClass(node, _tableDiff.TableName))
        {
            return base.VisitClassDeclaration(node);
        }

        HasModifications = true;
        var updatedClass = node;

        // Detect member indentation
        var memberIndent = DetectMemberIndentation(node);
        var newline = DetectNewline(node);

        // 1. Update existing properties
        var updatedMembers = new List<MemberDeclarationSyntax>();
        var existingPropNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in updatedClass.Members)
        {
            if (member is PropertyDeclarationSyntax prop)
            {
                var propName = prop.Identifier.Text;
                existingPropNames.Add(propName);

                // Find matching column diff
                var colDiff = FindMatchingColumnDiff(prop, _tableDiff);
                if (colDiff != null && colDiff.Kind == DiffKind.Modified && (colDiff.Target ?? colDiff.Source) != null)
                {
                    var updatedProp = UpdateProperty(prop, colDiff.Target ?? colDiff.Source!, memberIndent, newline);
                    updatedMembers.Add(updatedProp);
                    continue;
                }
            }

            updatedMembers.Add(member);
        }

        // 2. Add new properties for added columns
        foreach (var colDiff in _tableDiff.AddedColumns)
        {
            var col = colDiff.Target ?? colDiff.Source;
            if (col == null) continue;

            var propName = NamingHelper.ToPascalCase(col.Name);
            if (existingPropNames.Contains(propName) || existingPropNames.Contains(col.Name))
            {
                continue;
            }

            var newProp = CreateProperty(col, propName, memberIndent, newline);
            updatedMembers.Add(newProp);
            existingPropNames.Add(propName);
        }

        updatedClass = updatedClass.WithMembers(SyntaxFactory.List(updatedMembers));
        return updatedClass;
    }

    private bool IsMatchingClass(ClassDeclarationSyntax classDecl, string tableName)
    {
        var className = classDecl.Identifier.Text;
        if (string.Equals(className, tableName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(className, NamingHelper.ToEntityClassName(tableName), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(className, NamingHelper.ToPascalCase(tableName), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check [Table("tableName")] attribute
        foreach (var attrList in classDecl.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = attr.Name.ToString();
                if (name == "Table" || name == "TableAttribute")
                {
                    var firstArg = attr.ArgumentList?.Arguments.FirstOrDefault();
                    if (firstArg != null)
                    {
                        var argVal = firstArg.Expression.ToString().Trim('"', '\'');
                        if (string.Equals(argVal, tableName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private ColumnDiff? FindMatchingColumnDiff(PropertyDeclarationSyntax prop, TableDiff tableDiff)
    {
        var propName = prop.Identifier.Text;

        var exact = tableDiff.Columns.FirstOrDefault(c => string.Equals(c.ColumnName, propName, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return exact;
        }

        // Check if matching PascalCase
        foreach (var cDiff in tableDiff.Columns)
        {
            if (string.Equals(NamingHelper.ToPascalCase(cDiff.ColumnName), propName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cDiff.ColumnName, propName, StringComparison.OrdinalIgnoreCase))
            {
                return cDiff;
            }
        }

        // Check [Column("colName")] attribute
        foreach (var attrList in prop.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = attr.Name.ToString();
                if (name == "Column" || name == "ColumnAttribute")
                {
                    var firstArg = attr.ArgumentList?.Arguments.FirstOrDefault();
                    if (firstArg != null)
                    {
                        var argVal = firstArg.Expression.ToString().Trim('"', '\'');
                        var match = tableDiff.Columns.FirstOrDefault(c => string.Equals(c.ColumnName, argVal, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            return match;
                        }
                    }
                }
            }
        }

        return null;
    }

    private PropertyDeclarationSyntax UpdateProperty(
        PropertyDeclarationSyntax prop,
        ColumnSchema srcCol,
        string memberIndent,
        string newline)
    {
        var updated = prop;

        // 1. Update type
        var expectedTypeStr = TypeMapper.ToCSharpType(srcCol.Type, srcCol.IsNullable);
        var oldTypeStr = prop.Type.ToString();

        if (expectedTypeStr != oldTypeStr)
        {
            var newTypeSyntax = SyntaxFactory.ParseTypeName(expectedTypeStr)
                .WithLeadingTrivia(prop.Type.GetLeadingTrivia())
                .WithTrailingTrivia(prop.Type.GetTrailingTrivia());
            updated = updated.WithType(newTypeSyntax);
        }

        // 2. Attributes
        var attrs = new List<AttributeListSyntax>(updated.AttributeLists);

        if (_options.UseDataAnnotations)
        {
            if (srcCol.IsPrimaryKey && !HasAttribute(attrs, "Key"))
            {
                _needsDataAnnotations = true;
                attrs.Insert(0, CreateAttributeList("Key", memberIndent, newline));
            }

            if (srcCol.IsIdentity && !HasAttribute(attrs, "DatabaseGenerated"))
            {
                _needsDataAnnotationsSchema = true;
                attrs.Add(CreateAttributeList("DatabaseGenerated(DatabaseGeneratedOption.Identity)", memberIndent, newline));
            }

            if (srcCol.Length.HasValue && srcCol.Length > 0 &&
                (srcCol.Type == StandardType.String || srcCol.Type == StandardType.ByteArray))
            {
                if (!HasAttribute(attrs, "MaxLength") && !HasAttribute(attrs, "StringLength"))
                {
                    _needsDataAnnotations = true;
                    attrs.Add(CreateAttributeList($"MaxLength({srcCol.Length.Value})", memberIndent, newline));
                }
            }

            if (srcCol.Precision.HasValue && srcCol.Scale.HasValue && srcCol.Type == StandardType.Decimal)
            {
                if (!HasAttribute(attrs, "Precision"))
                {
                    _needsDataAnnotations = true;
                    attrs.Add(CreateAttributeList($"Precision({srcCol.Precision.Value}, {srcCol.Scale.Value})", memberIndent, newline));
                }
            }
        }

        if (attrs.Count != updated.AttributeLists.Count)
        {
            updated = FixAttributeLeadingTrivia(updated, attrs, memberIndent, newline);
        }

        return updated;
    }

    private PropertyDeclarationSyntax CreateProperty(
        ColumnSchema col,
        string propName,
        string memberIndent,
        string newline)
    {
        var typeStr = TypeMapper.ToCSharpType(col.Type, col.IsNullable);
        var initializer = (_options.UseNullableReferenceTypes && col.Type == StandardType.String && !col.IsNullable)
            ? " = string.Empty;"
            : "";
        var parsed = (PropertyDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration($"public {typeStr} {propName} {{ get; set; }}{initializer}")!;

        var attrLists = new List<AttributeListSyntax>();

        // Custom column name attribute if casing differs
        if (!string.Equals(col.Name, propName, StringComparison.Ordinal))
        {
            _needsDataAnnotationsSchema = true;
            attrLists.Add(CreateAttributeList($"Column(\"{col.Name}\")", memberIndent, newline));
        }

        if (_options.UseDataAnnotations)
        {
            if (col.IsPrimaryKey)
            {
                _needsDataAnnotations = true;
                attrLists.Add(CreateAttributeList("Key", memberIndent, newline));
            }

            if (col.IsIdentity)
            {
                _needsDataAnnotationsSchema = true;
                attrLists.Add(CreateAttributeList("DatabaseGenerated(DatabaseGeneratedOption.Identity)", memberIndent, newline));
            }

            if (col.Length.HasValue && col.Length > 0 &&
                (col.Type == StandardType.String || col.Type == StandardType.ByteArray))
            {
                _needsDataAnnotations = true;
                attrLists.Add(CreateAttributeList($"MaxLength({col.Length.Value})", memberIndent, newline));
            }

            if (col.Precision.HasValue && col.Scale.HasValue && col.Type == StandardType.Decimal)
            {
                _needsDataAnnotations = true;
                attrLists.Add(CreateAttributeList($"Precision({col.Precision.Value}, {col.Scale.Value})", memberIndent, newline));
            }
        }

        // Leading trivia for comments and indentation
        var leadingTrivia = new List<SyntaxTrivia>();
        leadingTrivia.Add(SyntaxFactory.Whitespace(newline));

        if (!string.IsNullOrWhiteSpace(col.Comment))
        {
            leadingTrivia.Add(SyntaxFactory.Whitespace(memberIndent));
            leadingTrivia.Add(SyntaxFactory.Comment($"/// <summary>{newline}"));
            leadingTrivia.Add(SyntaxFactory.Whitespace(memberIndent));
            leadingTrivia.Add(SyntaxFactory.Comment($"/// {col.Comment}{newline}"));
            leadingTrivia.Add(SyntaxFactory.Whitespace(memberIndent));
            leadingTrivia.Add(SyntaxFactory.Comment($"/// </summary>{newline}"));
        }

        PropertyDeclarationSyntax prop;

        if (attrLists.Count > 0)
        {
            attrLists[0] = attrLists[0].WithLeadingTrivia(leadingTrivia.Concat(attrLists[0].GetLeadingTrivia()));
            for (int i = 1; i < attrLists.Count; i++)
            {
                attrLists[i] = attrLists[i]
                    .WithLeadingTrivia(SyntaxFactory.Whitespace(memberIndent))
                    .WithTrailingTrivia(SyntaxFactory.Whitespace(newline));
            }

            prop = parsed.WithAttributeLists(SyntaxFactory.List(attrLists));
            var firstMod = parsed.Modifiers[0].WithLeadingTrivia(SyntaxFactory.Whitespace(memberIndent));
            var mods = new List<SyntaxToken> { firstMod };
            mods.AddRange(parsed.Modifiers.Skip(1));
            prop = prop.WithModifiers(SyntaxFactory.TokenList(mods));
        }
        else
        {
            leadingTrivia.Add(SyntaxFactory.Whitespace(memberIndent));
            var firstMod = parsed.Modifiers[0].WithLeadingTrivia(leadingTrivia);
            var mods = new List<SyntaxToken> { firstMod };
            mods.AddRange(parsed.Modifiers.Skip(1));
            prop = parsed.WithModifiers(SyntaxFactory.TokenList(mods));
        }

        prop = prop.WithTrailingTrivia(SyntaxFactory.Whitespace(newline));
        return prop;
    }

    private static AttributeListSyntax CreateAttributeList(string attributeText, string indent, string newline)
    {
        var dummy = $"[{attributeText}]\nclass Dummy {{}}";
        var parsed = (ClassDeclarationSyntax)SyntaxFactory.ParseCompilationUnit(dummy).Members[0];
        var attrList = parsed.AttributeLists[0];

        return attrList
            .WithLeadingTrivia(SyntaxFactory.Whitespace(indent))
            .WithTrailingTrivia(SyntaxFactory.Whitespace(newline));
    }

    private static bool HasAttribute(IEnumerable<AttributeListSyntax> attributeLists, string attributeName)
    {
        return attributeLists.SelectMany(l => l.Attributes).Any(a =>
        {
            var name = a.Name.ToString();
            return name == attributeName || name == attributeName + "Attribute" || name.StartsWith(attributeName + "(");
        });
    }

    private static PropertyDeclarationSyntax FixAttributeLeadingTrivia(
        PropertyDeclarationSyntax prop,
        List<AttributeListSyntax> attrs,
        string memberIndent,
        string newline)
    {
        var leading = prop.GetLeadingTrivia();
        if (attrs.Count > 0)
        {
            attrs[0] = attrs[0].WithLeadingTrivia(leading);
            for (int i = 1; i < attrs.Count; i++)
            {
                attrs[i] = attrs[i].WithLeadingTrivia(SyntaxFactory.Whitespace(memberIndent))
                    .WithTrailingTrivia(SyntaxFactory.Whitespace(newline));
            }

            var updatedModifiers = prop.Modifiers;
            if (updatedModifiers.Count > 0)
            {
                var firstMod = updatedModifiers[0].WithLeadingTrivia(SyntaxFactory.Whitespace(memberIndent));
                var list = new List<SyntaxToken> { firstMod };
                list.AddRange(updatedModifiers.Skip(1));
                updatedModifiers = SyntaxFactory.TokenList(list);
            }

            return prop.WithAttributeLists(SyntaxFactory.List(attrs)).WithModifiers(updatedModifiers);
        }

        return prop.WithAttributeLists(SyntaxFactory.List(attrs));
    }

    private static string DetectMemberIndentation(ClassDeclarationSyntax classDecl)
    {
        var firstMember = classDecl.Members.FirstOrDefault();
        if (firstMember != null)
        {
            var leadingTrivia = firstMember.GetLeadingTrivia();
            var lastWhitespace = leadingTrivia.LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
            if (!string.IsNullOrEmpty(lastWhitespace.ToString()))
            {
                return lastWhitespace.ToString();
            }
        }

        // If block scoped, usually 8 spaces, else 4 spaces
        return "    ";
    }

    private static string DetectNewline(SyntaxNode node)
    {
        var fullText = node.SyntaxTree.GetText().ToString();
        return fullText.Contains("\r\n") ? "\r\n" : "\n";
    }

    private static CompilationUnitSyntax EnsureUsings(
        CompilationUnitSyntax root,
        bool needDataAnn,
        bool needDataAnnSchema)
    {
        var existingUsings = root.Usings.Select(u => u.Name?.ToString() ?? string.Empty).ToHashSet(StringComparer.Ordinal);
        var usingsToAdd = new List<string>();
        if (needDataAnn && !existingUsings.Contains("System.ComponentModel.DataAnnotations"))
        {
            usingsToAdd.Add("System.ComponentModel.DataAnnotations");
        }
        if (needDataAnnSchema && !existingUsings.Contains("System.ComponentModel.DataAnnotations.Schema"))
        {
            usingsToAdd.Add("System.ComponentModel.DataAnnotations.Schema");
        }

        if (usingsToAdd.Count == 0)
        {
            return root;
        }

        var newline = root.ToFullString().Contains("\r\n") ? "\r\n" : "\n";

        var newUsings = new List<UsingDirectiveSyntax>(root.Usings);

        foreach (var u in usingsToAdd)
        {
            var directive = ((UsingDirectiveSyntax)SyntaxFactory.ParseCompilationUnit($"using {u};\n").Usings[0])
                .WithTrailingTrivia(SyntaxFactory.Whitespace(newline));

            newUsings.Add(directive);
        }

        return root.WithUsings(SyntaxFactory.List(newUsings));
    }
}
