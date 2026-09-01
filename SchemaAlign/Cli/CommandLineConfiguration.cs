using System.CommandLine;
using SchemaAlign.Cli.Commands;

namespace SchemaAlign.Cli;

public static class CommandLineConfiguration
{
    public static RootCommand CreateRootCommand(
        DiffCommandHandler? diffHandler = null,
        SyncCommandHandler? syncHandler = null,
        InspectCommandHandler? inspectHandler = null,
        WizardCommandHandler? wizardHandler = null)
    {
        var rootCommand = new RootCommand("SchemaAlign - Universal Database & Entity Schema Alignment Tool");

        // --- diff command ---
        var diffSourceOpt = new Option<string>("--source") { Description = "Path to source schema (desired state)", Required = true };
        diffSourceOpt.Aliases.Add("-s");
        var diffTargetOpt = new Option<string>("--target") { Description = "Path to target schema (existing state)", Required = true };
        diffTargetOpt.Aliases.Add("-t");
        var diffModeOpt = new Option<string>("--mode") { Description = "Diff mode: 'incremental' (default) or 'snapshot'", DefaultValueFactory = _ => "incremental" };
        diffModeOpt.Aliases.Add("-m");
        var diffOutputOpt = new Option<string>("--output") { Description = "Output format: 'console', 'json', or 'markdown'", DefaultValueFactory = _ => "console" };
        diffOutputOpt.Aliases.Add("-o");
        var diffDetailedOpt = new Option<bool>("--detailed") { Description = "Display detailed property-level change tree", DefaultValueFactory = _ => false };

        var diffCommand = new Command("diff", "Compare source and target schemas non-destructively")
        {
            diffSourceOpt,
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
                Source = parseResult.GetValue(diffSourceOpt) ?? string.Empty,
                Target = parseResult.GetValue(diffTargetOpt) ?? string.Empty,
                Mode = parseResult.GetValue(diffModeOpt) ?? "incremental",
                Output = parseResult.GetValue(diffOutputOpt) ?? "console",
                Detailed = parseResult.GetValue(diffDetailedOpt)
            };
            return await handler.RunAsync(options);
        });

        // --- sync command ---
        var syncSourceOpt = new Option<string>("--source") { Description = "Path to source schema (desired state)", Required = true };
        syncSourceOpt.Aliases.Add("-s");
        var syncTargetOpt = new Option<string>("--target") { Description = "Path to target schema (existing state)", Required = true };
        syncTargetOpt.Aliases.Add("-t");
        var syncModeOpt = new Option<string>("--mode") { Description = "Diff mode: 'incremental' (default) or 'snapshot'", DefaultValueFactory = _ => "incremental" };
        syncModeOpt.Aliases.Add("-m");
        var syncAllowDropOpt = new Option<bool>("--allow-drop") { Description = "Allow destructive drops (DROP TABLE, DROP COLUMN)", DefaultValueFactory = _ => false };
        var syncNoDropOpt = new Option<bool>("--no-drop") { Description = "Explicitly block destructive drops (default in automated mode)", DefaultValueFactory = _ => false };
        var syncDryRunOpt = new Option<bool>("--dry-run") { Description = "Generate and preview diffs without modifying target files", DefaultValueFactory = _ => false };
        var syncYesOpt = new Option<bool>("--yes") { Description = "Apply changes non-interactively without confirmation prompt", DefaultValueFactory = _ => false };
        syncYesOpt.Aliases.Add("-y");
        var syncInteractiveOpt = new Option<bool>("--interactive") { Description = "Run interactive checklist prompt", DefaultValueFactory = _ => true };
        syncInteractiveOpt.Aliases.Add("-i");

        var syncCommand = new Command("sync", "Synchronize target schema with source schema")
        {
            syncSourceOpt,
            syncTargetOpt,
            syncModeOpt,
            syncAllowDropOpt,
            syncNoDropOpt,
            syncDryRunOpt,
            syncYesOpt,
            syncInteractiveOpt
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
                Source = parseResult.GetValue(syncSourceOpt) ?? string.Empty,
                Target = parseResult.GetValue(syncTargetOpt) ?? string.Empty,
                Mode = parseResult.GetValue(syncModeOpt) ?? "incremental",
                AllowDrop = allowDrop,
                DryRun = parseResult.GetValue(syncDryRunOpt),
                Yes = parseResult.GetValue(syncYesOpt),
                Interactive = parseResult.GetValue(syncInteractiveOpt) && !parseResult.GetValue(syncYesOpt)
            };
            return await handler.RunAsync(options);
        });

        // --- inspect command ---
        var inspectSourceOpt = new Option<string>("--source") { Description = "Path to schema file or directory", Required = true };
        inspectSourceOpt.Aliases.Add("-s");
        var inspectOutputOpt = new Option<string>("--output") { Description = "Output format: 'console' or 'json'", DefaultValueFactory = _ => "console" };
        inspectOutputOpt.Aliases.Add("-o");

        var inspectCommand = new Command("inspect", "Inspect and display parsed schema tables and columns")
        {
            inspectSourceOpt,
            inspectOutputOpt
        };

        inspectCommand.SetAction(async parseResult =>
        {
            var handler = inspectHandler ?? new InspectCommandHandler();
            var options = new InspectCommandOptions
            {
                Source = parseResult.GetValue(inspectSourceOpt) ?? string.Empty,
                Output = parseResult.GetValue(inspectOutputOpt) ?? "console"
            };
            return await handler.RunAsync(options);
        });

        rootCommand.Add(diffCommand);
        rootCommand.Add(syncCommand);
        rootCommand.Add(inspectCommand);

        rootCommand.SetAction(async parseResult =>
        {
            var wizard = wizardHandler ?? new WizardCommandHandler();
            return await wizard.RunAsync();
        });

        return rootCommand;
    }
}
