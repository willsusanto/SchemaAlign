using System.CommandLine;
using SchemaAlign.Cli.Commands;

namespace SchemaAlign.Cli;

/// <summary>
/// Configures System.CommandLine root command, subcommands, options, and handlers for SchemaAlign.
/// </summary>
public static class CommandLineConfiguration
{
    /// <summary>
    /// Creates and configures the root CLI command with diff, sync, inspect, and wizard commands.
    /// </summary>
    /// <param name="diffHandler">Optional custom handler for the diff command.</param>
    /// <param name="syncHandler">Optional custom handler for the sync command.</param>
    /// <param name="inspectHandler">Optional custom handler for the inspect command.</param>
    /// <param name="wizardHandler">Optional custom handler for the wizard command.</param>
    /// <returns>A configured <see cref="RootCommand"/> ready for parsing and invocation.</returns>
    public static RootCommand CreateRootCommand(
        DiffCommandHandler? diffHandler = null,
        SyncCommandHandler? syncHandler = null,
        InspectCommandHandler? inspectHandler = null,
        WizardCommandHandler? wizardHandler = null)
    {
        var rootCommand = new RootCommand("SchemaAlign - Universal Database & Entity Schema Alignment Tool");

        // --- diff command ---
        var diffCurrentOpt = new Option<string>("--current")
        {
            Description = "Path to current/base schema (e.g. ./src/Entities or current database connection string)"
        };
        diffCurrentOpt.Aliases.Add("-c");

        var diffTargetOpt = new Option<string?>("--target")
        {
            Description = "Path to desired/target schema to align toward (e.g. schema.mmd or new spec)"
        };
        diffTargetOpt.Aliases.Add("-t");

        var diffModeOpt = new Option<string>("--mode")
        {
            Description = "Diff mode: 'incremental' (default: sprint tables; include all desired columns) or 'snapshot' (complete system)",
            DefaultValueFactory = _ => "incremental"
        };
        diffModeOpt.Aliases.Add("-m");
        diffModeOpt.AcceptOnlyFromAmong("incremental", "snapshot");

        var diffOutputOpt = new Option<string>("--output")
        {
            Description = "Output format: 'console' (colorized table), 'json', or 'markdown'",
            DefaultValueFactory = _ => "console"
        };
        diffOutputOpt.Aliases.Add("-o");
        diffOutputOpt.AcceptOnlyFromAmong("console", "json", "markdown", "md");

        var diffDetailedOpt = new Option<bool>("--detailed")
        {
            Description = "Display detailed property-level change tree (types, nullability, lengths)",
            DefaultValueFactory = _ => false
        };

        var diffConfigOpt = new Option<string?>("--config")
        {
            Description = "Path to .schemaalign.json configuration file"
        };

        var diffReportOpt = new Option<string?>("--report")
        {
            Description = "File path to write a detailed markdown report"
        };
        diffReportOpt.Aliases.Add("-r");

        var diffCommand = new Command("diff", "Compare current schema against desired target schema non-destructively")
        {
            diffCurrentOpt,
            diffTargetOpt,
            diffModeOpt,
            diffOutputOpt,
            diffDetailedOpt,
            diffConfigOpt,
            diffReportOpt
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
                Detailed = parseResult.GetValue(diffDetailedOpt),
                ConfigFile = parseResult.GetValue(diffConfigOpt),
                ReportPath = parseResult.GetValue(diffReportOpt)
            };
            return await handler.RunAsync(options);
        });

        // --- sync command ---
        var syncCurrentOpt = new Option<string>("--current")
        {
            Description = "Path to current/base schema to be updated (e.g. ./src/Entities or SQL connection string)"
        };
        syncCurrentOpt.Aliases.Add("-c");

        var syncTargetOpt = new Option<string?>("--target")
        {
            Description = "Path to desired/target schema to align toward (e.g. schema.mmd or new spec)"
        };
        syncTargetOpt.Aliases.Add("-t");

        var syncOutputFileOpt = new Option<string?>("--output-file")
        {
            Description = "Path to write the generated SQL Server migration script (e.g. ./migrations/migration.sql)"
        };
        syncOutputFileOpt.Aliases.Add("-o");

        var syncModeOpt = new Option<string>("--mode")
        {
            Description = "Diff mode: 'incremental' (default: sprint tables; include all desired columns) or 'snapshot' (complete system)",
            DefaultValueFactory = _ => "incremental"
        };
        syncModeOpt.Aliases.Add("-m");
        syncModeOpt.AcceptOnlyFromAmong("incremental", "snapshot");

        var syncAllowDropOpt = new Option<bool>("--allow-drop")
        {
            Description = "Allow destructive drops (DROP TABLE, DROP COLUMN) during synchronization",
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

        var syncConfigOpt = new Option<string?>("--config")
        {
            Description = "Path to .schemaalign.json configuration file"
        };

        var syncReportOpt = new Option<string?>("--report")
        {
            Description = "File path to write a detailed markdown report"
        };
        syncReportOpt.Aliases.Add("-r");

        var syncCommand = new Command("sync", "Synchronize current schema to match desired target schema")
        {
            syncCurrentOpt,
            syncTargetOpt,
            syncOutputFileOpt,
            syncModeOpt,
            syncAllowDropOpt,
            syncDryRunOpt,
            syncYesOpt,
            syncConfigOpt,
            syncReportOpt
        };

        syncCommand.SetAction(async parseResult =>
        {
            var handler = syncHandler ?? new SyncCommandHandler();
            var allowDrop = parseResult.GetValue(syncAllowDropOpt);

            var options = new SyncCommandOptions
            {
                Current = parseResult.GetValue(syncCurrentOpt) ?? string.Empty,
                Target = parseResult.GetValue(syncTargetOpt) ?? string.Empty,
                OutputFile = parseResult.GetValue(syncOutputFileOpt),
                Mode = parseResult.GetValue(syncModeOpt) ?? "incremental",
                AllowDrop = allowDrop,
                DryRun = parseResult.GetValue(syncDryRunOpt),
                Yes = parseResult.GetValue(syncYesOpt),
                Interactive = !parseResult.GetValue(syncYesOpt),
                Guided = !parseResult.GetValue(syncYesOpt),
                ConfigFile = parseResult.GetValue(syncConfigOpt),
                ReportPath = parseResult.GetValue(syncReportOpt)
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
        inspectOutputOpt.AcceptOnlyFromAmong("console", "json");

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

        rootCommand.Add(diffCommand);
        rootCommand.Add(syncCommand);
        rootCommand.Add(inspectCommand);

        rootCommand.SetAction(async parseResult =>
        {
            var wizard = wizardHandler ?? new WizardCommandHandler(
                diffHandler: diffHandler,
                syncHandler: syncHandler,
                inspectHandler: inspectHandler);
            return await wizard.RunAsync();
        });

        return rootCommand;
    }
}
