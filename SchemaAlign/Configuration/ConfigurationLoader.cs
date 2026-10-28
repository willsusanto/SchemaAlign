using System.Text.Json;
using SchemaAlign.Cli.Services;

namespace SchemaAlign.Configuration;

/// <summary>Loads one project configuration; relative schema paths belong to that file.</summary>
public static class ConfigurationLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static SchemaAlignConfiguration Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
        ValidateObject(document.RootElement, false);
        var config = JsonSerializer.Deserialize<SchemaAlignConfiguration>(json, JsonOptions)!;
        config.TablePrefixes = config.TablePrefixes.Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        config.CSharp.Usings = config.CSharp.Usings.Select(NormalizeUsing).Distinct(StringComparer.Ordinal).ToList();
        config.CSharp.ClassAttributes = config.CSharp.ClassAttributes.Select(a =>
        {
            var value = a.Trim();
            return value.StartsWith("[") && value.EndsWith("]") ? value : $"[{value}]";
        }).Distinct(StringComparer.Ordinal).ToList();
        config.CSharp.OmitInheritedColumns = config.CSharp.OmitInheritedColumns
            .Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return config;
    }

    private static string NormalizeUsing(string value)
    {
        value = value.Trim();
        if (value.StartsWith("using ", StringComparison.Ordinal)) value = value[6..].Trim();
        return value.TrimEnd(';').Trim();
    }

    private static void ValidateObject(JsonElement element, bool csharp)
    {
        var strings = csharp
            ? new[] { "namespace", "baseClass", "foreignKeyPlacement" }
            : new[] { "current", "target" };
        var lists = csharp
            ? new[] { "usings", "classAttributes", "omitInheritedColumns" }
            : new[] { "tablePrefixes" };
        var booleans = csharp
            ? new[] { "useFileScopedNamespaces", "useDataAnnotations" }
            : Array.Empty<string>();
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException($"{(csharp ? "csharp" : "configuration")} must be an object.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            var name = property.Name;
            var value = property.Value;
            var path = csharp ? $"csharp.{name}" : name;
            if (!seen.Add(name)) throw new JsonException($"Duplicate setting '{path}'. Specify it once.");
            if (!csharp && name.Equals("csharp", StringComparison.OrdinalIgnoreCase))
            {
                ValidateObject(value, true);
                continue;
            }
            if (strings.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                    throw new JsonException($"'{path}' must be a non-empty string; omit it to use the default.");
                if (name.Equals("foreignKeyPlacement", StringComparison.OrdinalIgnoreCase) &&
                    !new[] { "scalar", "navigation" }.Contains(value.GetString(), StringComparer.OrdinalIgnoreCase))
                    throw new JsonException("'csharp.foreignKeyPlacement' must be 'scalar' or 'navigation'.");
                continue;
            }
            if (lists.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                if (value.ValueKind != JsonValueKind.Array ||
                    value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())))
                    throw new JsonException($"'{path}' must be an array of non-empty strings.");
                continue;
            }
            if (booleans.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                    throw new JsonException($"'{path}' must be true or false.");
                continue;
            }
            var guidance = name.ToLowerInvariant() switch
            {
                "source" => "Use 'current' instead.",
                "mode" => "Choose incremental or snapshot in the wizard or with --mode.",
                "allowdrop" => "Confirm removals in the wizard or use --allow-drop.",
                "output" => "Use --output on diff or inspect.",
                "detailed" => "Use --detailed on diff.",
                "tableprefixes" when csharp => "Move this setting to root 'tablePrefixes'.",
                _ => "Supported settings: " + string.Join(", ", strings.Concat(lists).Concat(booleans).Concat(csharp ? Array.Empty<string>() : new[] { "csharp" })) + "."
            };
            throw new JsonException($"Unsupported setting '{path}'. {guidance}");
        }
    }

    public static string? FindConfigFile(string? startDirectory = null)
    {
        var current = Path.GetFullPath(startDirectory ?? Directory.GetCurrentDirectory());
        while (Directory.Exists(current))
        {
            var path = Path.Combine(current, ".schemaalign.json");
            if (File.Exists(path)) return path;
            var legacy = Path.Combine(current, "schemaalign.json");
            if (File.Exists(legacy))
                throw new JsonException($"Rename '{legacy}' to '.schemaalign.json'.");
            if (Directory.Exists(Path.Combine(current, ".git")) || File.Exists(Path.Combine(current, ".git"))) break;
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return null;
    }

    public static async Task<SchemaAlignConfiguration?> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(filePath);
        var config = Parse(await File.ReadAllTextAsync(path, cancellationToken));
        var directory = Path.GetDirectoryName(path)!;
        config.Current = ResolveInput(config.Current, directory);
        config.Target = ResolveInput(config.Target, directory);
        return config;
    }

    private static string? ResolveInput(string? input, string directory)
    {
        if (input == null || SchemaDetectionService.IsConnectionString(input)) return input;
        return string.Join(";", input.Split(new[] { ';', ',' }, StringSplitOptions.TrimEntries)
            .Select(part => Path.GetFullPath(part.Trim('"', '\''), directory)));
    }

    public static async Task<SchemaAlignConfiguration?> FindAndLoadAsync(string? startDirectory = null, CancellationToken cancellationToken = default)
    {
        var path = FindConfigFile(startDirectory);
        return path == null ? null : await LoadAsync(path, cancellationToken);
    }
}
