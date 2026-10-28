# Choosing C# source paths

[← SchemaAlign](../README.md#documentation)

## Choosing C# paths when a project contains multiple databases or shared base classes

Choose the entity folder for the database you want to compare, together with the source files for its base classes:

```text
D:\Repo\MaskedApi\Model\
  MaskedPrimary\              Entities for the selected database
  MaskedSecondary\            Entities for another database
  Shared\MaskedBase.cs        Shared abstract base class
  Request\                    Request models
  Response\                   Response models
```

At the wizard's schema-path prompt, enter:

```text
D:\Repo\MaskedApi\Model\MaskedPrimary;D:\Repo\MaskedApi\Model\Shared\MaskedBase.cs
```

For a command, quote the combined input so the shell passes it as one argument:

```powershell
dotnet run --project SchemaAlign -- inspect --current "D:\Repo\MaskedApi\Model\MaskedPrimary;D:\Repo\MaskedApi\Model\Shared\MaskedBase.cs"
```

Both semicolons and commas separate C# file/directory paths. During synchronization, put the destination entity directory first; later paths provide source context.

- Scanning only MaskedPrimary misses inherited properties if their base-class source is elsewhere.
- Scanning the entire Model folder may also interpret another database's entities, request models, or response models as tables.
- Include the exact base-class source file where possible; a whole shared directory may contain unrelated concrete classes.

A using directive or `csharp.baseClass` value does not load extra source files. Include every required base-class source explicitly. Supported inherited properties and mapping annotations participate in schema comparison as ordinary columns.

The reader resolves base classes by short name. Qualified names, aliases, duplicate class names, and generic type arguments have resolution limitations. Properties available only in referenced assemblies are not inspected.
