using System.Text;
using System.Text.RegularExpressions;

namespace SchemaAlign.Appliers.CSharp;

public static class NamingHelper
{
    public static string ToPascalCase(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        // If contains underscores, hyphens, or spaces, split by them
        var words = Regex.Split(name, @"[_\-\s]+", RegexOptions.Compiled)
            .Where(w => !string.IsNullOrEmpty(w))
            .ToArray();

        if (words.Length > 1)
        {
            var sb = new StringBuilder();
            foreach (var word in words)
            {
                if (word.Length == 0) continue;
                sb.Append(char.ToUpperInvariant(word[0]));
                if (word.Length > 1)
                {
                    sb.Append(word.Substring(1));
                }
            }
            return sb.ToString();
        }

        // If single word, capitalize first letter
        var single = words.Length == 1 ? words[0] : name;
        if (char.IsUpper(single[0]))
        {
            return single;
        }

        return char.ToUpperInvariant(single[0]) + (single.Length > 1 ? single.Substring(1) : string.Empty);
    }

    public static string ToEntityClassName(string tableName)
    {
        var pascal = ToPascalCase(tableName);
        return Singularize(pascal);
    }

    public static string Singularize(string word)
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

    public static string ToNavigationPropertyName(string fkColumnName, string? principalTable = null)
    {
        if (string.IsNullOrWhiteSpace(fkColumnName))
        {
            return !string.IsNullOrWhiteSpace(principalTable)
                ? ToEntityClassName(principalTable)
                : string.Empty;
        }

        var name = ToPascalCase(fkColumnName);

        if (name.Equals("Id", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(principalTable)
                ? ToEntityClassName(principalTable)
                : name;
        }

        if (name.StartsWith("Id", StringComparison.Ordinal) && name.Length > 2 && char.IsUpper(name[2]))
        {
            name = name.Substring(2);
        }
        else if (name.EndsWith("Id", StringComparison.Ordinal) && name.Length > 2)
        {
            name = name.Substring(0, name.Length - 2);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return !string.IsNullOrWhiteSpace(principalTable)
                ? ToEntityClassName(principalTable)
                : ToPascalCase(fkColumnName);
        }

        return name;
    }
}

