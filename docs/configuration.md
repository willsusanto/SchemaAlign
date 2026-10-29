# Project configuration

[← SchemaAlign](../README.md#documentation)

No configuration file is required to start the wizard. To avoid entering the same locations repeatedly, create **.schemaalign.json** in your project:

```json
{
  "current": "./Masked.Entities",
  "target": "./masked-sprint.mmd"
}
```

That is enough for a basic setup. Add C# conventions only when your project needs them.

- **One file:** the wizard and diff/sync discover the nearest .schemaalign.json, searching upward from the working directory through the repository root. Parent files are not merged.
- **Predictable paths:** paths inside JSON are relative to that file. Paths entered in the wizard or CLI are relative to the working directory.
- **Explicit overrides:** diff/sync `--current` and `--target` override configured locations. Use `--config` to select a different JSON file.
- **Clear errors:** unsupported keys, duplicate keys, invalid types, and invalid values produce configuration errors. Omit unused settings rather than assigning null.

## All configuration keys, defaults, and effects

Every key is optional. current and target must eventually be supplied through the wizard, command, or configuration.

| Key | Value | Default / usage |
| --- | --- | --- |
| `current` | Non-empty string | Existing schema location; sync write destination. |
| `target` | Non-empty string | Desired schema location. |
| `tablePrefixes` | Array of non-empty strings | Empty. Prefixes used for Mermaid foreign-key matching and SQL migration resolution. |
| `csharp.namespace` | Non-empty string | Detect the destination namespace; fall back to Entities. Used for new entities. |
| `csharp.baseClass` | Non-empty string | No base class. Adds inheritance to new entities and enables inherited-property discovery from supplied source. |
| `csharp.usings` | Array of non-empty strings | Empty. Additional imports for new entities. Required standard and resolved reference imports are automatic. |
| `csharp.classAttributes` | Array of non-empty strings | Empty. Custom attributes on new entities. |
| `csharp.omitInheritedColumns` | Array of non-empty strings | Empty. Additional property/column names to omit from new entities. Matching ignores case. |
| `csharp.useFileScopedNamespaces` | true or false | true. Use file-scoped namespaces for new entities; false uses block namespaces. |
| `csharp.useDataAnnotations` | true or false | true. Generate mapping annotations for new entities and supported property rewrites. |
| `csharp.foreignKeyPlacement` | navigation or scalar | navigation. Choose where ForeignKey annotations appear in new entities. |

`csharp` is the grouping object for the eight C# settings. Generation conventions generally affect **new entities**, rather than restyling existing classes.

Keys are case-insensitive. JSON comments and trailing commas are accepted. Lists are not appended across configuration files because only one file is loaded. Duplicate imports and equivalent bracketed/unbracketed attributes are normalized.

For a custom configuration file:

```powershell
dotnet run --project SchemaAlign -- diff --config ./masked-project.json
```

A missing explicitly selected file returns an error; it does not fall back to another file. The inspect command requires its own `--current` input and does not independently discover JSON; inspection through the wizard can reuse the project's configured input.


## C# conventions: inheritance, imports, attributes, and foreign keys

A project with shared entity conventions might use:

```json
{
  "current": "./Masked.Entities;./Masked.Shared/MaskedBase.cs",
  "target": "./masked-sprint.mmd",
  "tablePrefixes": ["masked_"],
  "csharp": {
    "namespace": "Masked.Output",
    "baseClass": "MaskedBase",
    "usings": ["Masked.Shared"],
    "classAttributes": ["MaskedMarker"],
    "omitInheritedColumns": ["MaskedStamp"],
    "useFileScopedNamespaces": true,
    "useDataAnnotations": true,
    "foreignKeyPlacement": "navigation"
  }
}
```

**Inheritance:** new entities inherit from MaskedBase. SchemaAlign scans supplied source for its inheritance hierarchy, omits discovered inherited properties, and adds the base class's namespace import when it can resolve it. Use omitInheritedColumns as an additional manual omission list.

**Imports and attributes:** namespace names are the preferred usings format, such as Masked.Shared. Attribute names can be written as MaskedMarker or [MaskedMarker]. Equivalent entries are emitted once. A custom Table/TableAttribute mapping takes precedence over the automatic table annotation. Disabling useDataAnnotations suppresses automatic annotations, not explicitly requested custom attributes.

**Foreign-key placement:**

| Value | Generated placement |
| --- | --- |
| navigation | Put [ForeignKey("MaskedParentKey")] on the navigation property. |
| scalar | Put [ForeignKey("MaskedParent")] on the scalar foreign-key property. |

Root tablePrefixes helps match foreign keys when table names use conventions such as masked_. It is shared configuration, not a separate nested C# override.

For new entities, data annotations include `[Required]` on non-nullable, non-key strings, including foreign-key strings. This preserves required fields in projects with nullable reference types disabled.

Mermaid foreign-key matching recognizes role suffixes such as `IdMaskedActorOpen` referencing `IdMaskedActor`. Columns marked `FK` can produce references without a drawn relationship when their key name identifies exactly one principal table. Ambiguous or generic `Id` keys require explicit relationship lines labeled with the dependent column name.

**Existing files:** Roslyn-based property rewrites preserve surrounding methods, comments, and formatting. This is not a general class-restyling operation. C# CLI synchronization currently adds and updates properties; it does not apply every reported property or entity-file removal. Inspect the previews to see the operations supported for your destination.
