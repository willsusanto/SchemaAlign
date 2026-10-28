# Getting started

[← SchemaAlign](../README.md#documentation)

You need the **.NET 10 SDK**. From the cloned repository's root directory, run:

```powershell
dotnet run --project SchemaAlign
```

The wizard guides you through:

1. **Choose an action:** compare schemas, review and synchronize changes, or inspect one schema.
2. **Choose your inputs:** reuse configured locations or enter file paths, C# entity directories, or SQL Server connection strings. Invalid local paths can be corrected immediately.
3. **Choose the scope:** this sprint's tables or the complete system.
4. **Review the result:** inspect differences and, for synchronization, select changes and preview their output before choosing an action.

For C# and SQL script workflows, finishing the preview without applying is the default final choice. Live SQL Server workflows offer script export, direct execution with confirmation, or cancellation.

> **Which input gets updated?**
> **Current** is your existing schema and the destination synchronization updates. **Target** describes the desired schema. For example, to update C# entities from a Mermaid diagram, choose the entity directory as current and the diagram as target.

### What can I use as an input?

| Input | Accepted form | Can synchronization write to it? |
| --- | --- | --- |
| Mermaid ER diagram | .mmd or .mermaid file | Read only; use it as a desired definition. |
| C# entities | .cs source files or entity directories; multiple paths supported | Yes, through entity generation and supported property rewrites. |
| SQL Server script | .sql file | Yes, through migration script generation. |
| SQL Server database | Connection string | Yes, through migration export or direct execution. |

All examples below use placeholder paths and synthetic schema names. Replace them with your project's locations.
