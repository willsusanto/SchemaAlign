using System.CommandLine;
using SchemaAlign.Cli.Commands;

namespace SchemaAlign.Cli;

/// <summary>
/// Configures System.CommandLine root command, subcommands, options, and handlers for SchemaAlign.
/// </summary>
public static class CommandLineConfiguration
{
    /// <summary>
    /// Creates and configures the root CLI command with diff, sync, inspect, export, and wizard commands.
    /// </summary>
    /// <param name="diffHandler">Optional custom handler for the diff command.</param>
    /// <param name="syncHandler">Optional custom handler for the sync command.</param>
    /// <param name="inspectHandler">Optional custom handler for the inspect command.</param>
    /// <param name="wizardHandler">Optional custom handler for the wizard command.</param>
    /// <param name="exportHandler">Optional custom handler for the export command.</param>
    /// <returns>A configured <see cref="RootCommand"/> ready for parsing and invocation.</returns>
    public static RootCommand CreateRootCommand(
        DiffCommandHandler? diffHandler = null,
        SyncCommandHandler? syncHandler = null,
        InspectCommandHandler? inspectHandler = null,
        WizardCommandHandler? wizardHandler = null,
        ExportCommandHandler? exportHandler = null)
    {
        var rootCommand = new RootCommand("SchemaAlign - Universal Database & Entity Schema Alignment Tool");

        // --- diff command ---
        var diffCurrentOpt = new Option<string>("--current")
        {
            Description = "Path to current/base schema (e.g. ./src/Entities or current database connection string)",
            Required = true
        };
        diffCurrentOpt.Aliases.Add("-c");

        var diffTargetOpt = new Option<string>("--target")
        {
            Description = "Path to desired/target schema to align toward (e.g. schema.mmd or new spec)",
            Required = true
        };
        diffTargetOpt.Aliases.Add("-t");

        var diffModeOpt = new Option<string>("--mode")
        {
            Description = "Diff mode: 'incremental' (default: sprint diagram, preserves unmentioned tables) or 'snapshot' (full replacement)",
            DefaultValueFactory = _ => "incremental"
        };
        diffModeOpt.Aliases.Add("-m");

        var diffOutputOpt = new Option<string>("--output")
        {
            Description = "Output format: 'console' (colorized table), 'json', or 'markdown'",
            DefaultValueFactory = _ => "console"
        };
        diffOutputOpt.Aliases.Add("-o");

        var diffDetailedOpt = new Option<bool>("--detailed")
        {
            Description = "Display detailed property-level change tree (types, nullability, lengths)",
            DefaultValueFactory = _ => false
        };

        var diffCommand = new Command("diff", "Compare current schema against desired target schema non-destructively")
        {
            diffCurrentOpt,
            diffTargetOpt,
            diffModeOpt,
            diffOutputOpt,
            diffDetailedOpt
        };

        diffCommand.SetAction(async parseResult =>
        {
            var handler = diffHandler ?? new DiffCommandHandler();
            var options = new DiffCommandOptions
            {
                Current = parseResult.GetValue(diffCurrentOpt) ?? string.Empty,
                Target = parseResult.GetValue(diffTargetOpt) ?? string.Empty,
                Mode = parseResult.GetValue(diffModeOpt) ?? "incremental",
                Output = parseResult.GetValue(diffOutputOpt) ?? "console",
                Detailed = parseResult.GetValue(diffDetailedOpt)
            };
            return await handler.RunAsync(options);
        });

        // --- sync command ---
        var syncCurrentOpt = new Option<string>("--current")
        {
            Description = "Path to current/base schema to be updated (e.g. ./src/Entities or SQL connection string)",
            Required = true
        };
        syncCurrentOpt.Aliases.Add("-c");

        var syncTargetOpt = new Option<string>("--target")
        {
            Description = "Path to desired/target schema to align toward (e.g. schema.mmd or new spec)",
            Required = true
        };
        syncTargetOpt.Aliases.Add("-t");

        var syncOutputFileOpt = new Option<string?>("--output-file")
        {
            Description = "Optional path to export generated .sql migration script to file without applying directly to live database",
            DefaultValueFactory = _ => null
        };
        syncOutputFileOpt.Aliases.Add("-o");

        var syncModeOpt = new Option<string>("--mode")
        {
            Description = "Diff mode: 'incremental' (default: sprint diagram, preserves unmentioned tables) or 'snapshot'",
            DefaultValueFactory = _ => "incremental"
        };
        syncModeOpt.Aliases.Add("-m");

        var syncAllowDropOpt = new Option<bool>("--allow-drop")
        {
            Description = "Allow destructive drops (DROP TABLE, DROP COLUMN) during synchronization",
            DefaultValueFactory = _ => false
        };

        var syncNoDropOpt = new Option<bool>("--no-drop")
        {
            Description = "Explicitly block destructive drops (default in automated mode)",
            DefaultValueFactory = _ => false
        };

        var syncDryRunOpt = new Option<bool>("--dry-run")
        {
            Description = "Generate and preview diffs without writing modifications to disk",
            DefaultValueFactory = _ => false
        };

        var syncYesOpt = new Option<bool>("--yes")
        {
            Description = "Apply changes non-interactively without confirmation prompt",
            DefaultValueFactory = _ => false
        };
        syncYesOpt.Aliases.Add("-y");

        var syncInteractiveOpt = new Option<bool>("--interactive")
        {
            Description = "Run interactive checklist prompt to toggle individual changes",
            DefaultValueFactory = _ => true
        };
        syncInteractiveOpt.Aliases.Add("-i");

        var syncNamespaceOpt = new Option<string?>("--namespace")
        {
            Description = "Target C# namespace for generated entities (defaults to auto-detection from source files or 'Entities')"
        };
        syncNamespaceOpt.Aliases.Add("--ns");

        var syncCommand = new Command("sync", "Synchronize current schema to match desired target schema")
        {
            syncCurrentOpt,
            syncTargetOpt,
            syncOutputFileOpt,
            syncModeOpt,
            syncAllowDropOpt,
            syncNoDropOpt,
            syncDryRunOpt,
            syncYesOpt,
            syncInteractiveOpt,
            syncNamespaceOpt
        };

        syncCommand.SetAction(async parseResult =>
        {
            var handler = syncHandler ?? new SyncCommandHandler();
            var allowDrop = parseResult.GetValue(syncAllowDropOpt);
            if (parseResult.GetValue(syncNoDropOpt))
            {
                allowDrop = false;
            }

            var options = new SyncCommandOptions
            {
                Current = parseResult.GetValue(syncCurrentOpt) ?? string.Empty,
                Target = parseResult.GetValue(syncTargetOpt) ?? string.Empty,
                OutputFile = parseResult.GetValue(syncOutputFileOpt),
                Mode = parseResult.GetValue(syncModeOpt) ?? "incremental",
                AllowDrop = allowDrop,
                DryRun = parseResult.GetValue(syncDryRunOpt),
                Yes = parseResult.GetValue(syncYesOpt),
                Interactive = parseResult.GetValue(syncInteractiveOpt) && !parseResult.GetValue(syncYesOpt),
                Namespace = parseResult.GetValue(syncNamespaceOpt)
            };
            return await handler.RunAsync(options);
        });

        // --- inspect command ---
        var inspectCurrentOpt = new Option<string>("--current")
        {
            Description = "Path to schema file or directory to inspect",
            Required = true
        };
        inspectCurrentOpt.Aliases.Add("-c");

        var inspectOutputOpt = new Option<string>("--output")
        {
            Description = "Output format: 'console' or 'json'",
            DefaultValueFactory = _ => "console"
        };
        inspectOutputOpt.Aliases.Add("-o");

        var inspectCommand = new Command("inspect", "Inspect and display parsed schema tables and columns")
        {
            inspectCurrentOpt,
            inspectOutputOpt
        };

        inspectCommand.SetAction(async parseResult =>
        {
            var handler = inspectHandler ?? new InspectCommandHandler();
            var options = new InspectCommandOptions
            {
                Current = parseResult.GetValue(inspectCurrentOpt) ?? string.Empty,
                Output = parseResult.GetValue(inspectOutputOpt) ?? "console"
            };
            return await handler.RunAsync(options);
        });

        // --- export command ---
        var exportTargetOpt = new Option<string>("--target")
        {
            Description = "Path to Mermaid schema file (.mmd, .mermaid)",
            Required = true
        };
        exportTargetOpt.Aliases.Add("-t");

        var exportOutputOpt = new Option<string>("--output")
        {
            Description = "Path to output Excel file (.xlsx)",
            Required = true
        };
        exportOutputOpt.Aliases.Add("-o");

        var exportConfigOpt = new Option<string?>("--config")
        {
            Description = "Optional path to schemaalign.json configuration file",
            DefaultValueFactory = _ => null
        };
        exportConfigOpt.Aliases.Add("-c");

        var exportAidOpt = new Option<string?>("--aid")
        {
            Description = "Application ID (AID) metadata value",
            DefaultValueFactory = _ => null
        };

        var exportIpOpt = new Option<string?>("--ip")
        {
            Description = "IP / Domain / Azure Cosmos host metadata value",
            DefaultValueFactory = _ => null
        };

        var exportDbOpt = new Option<string?>("--db")
        {
            Description = "SQL DB / Azure DB / Cosmos DB name metadata value",
            DefaultValueFactory = _ => null
        };

        var exportTitleOpt = new Option<string?>("--title")
        {
            Description = "System title header value",
            DefaultValueFactory = _ => null
        };

        var exportCommand = new Command("export", "Export Mermaid schema to Excel Data Dictionary (.xlsx)")
        {
            exportTargetOpt,
            exportOutputOpt,
            exportConfigOpt,
            exportAidOpt,
            exportIpOpt,
            exportDbOpt,
            exportTitleOpt
        };

        exportCommand.SetAction(async parseResult =>
        {
            var handler = exportHandler ?? new ExportCommandHandler();
            var options = new ExportCommandOptions
            {
                Target = parseResult.GetValue(exportTargetOpt) ?? string.Empty,
                Output = parseResult.GetValue(exportOutputOpt) ?? string.Empty,
                Config = parseResult.GetValue(exportConfigOpt),
                Aid = parseResult.GetValue(exportAidOpt),
                Ip = parseResult.GetValue(exportIpOpt),
                Db = parseResult.GetValue(exportDbOpt),
                Title = parseResult.GetValue(exportTitleOpt)
            };
            return await handler.RunAsync(options);
        });

        rootCommand.Add(diffCommand);
        rootCommand.Add(syncCommand);
        rootCommand.Add(inspectCommand);
        rootCommand.Add(exportCommand);

        rootCommand.SetAction(async parseResult =>
        {
            var wizard = wizardHandler ?? new WizardCommandHandler(
                diffHandler: diffHandler,
                syncHandler: syncHandler,
                inspectHandler: inspectHandler,
                exportHandler: exportHandler);
            return await wizard.RunAsync();
        });

        return rootCommand;
    }
}
