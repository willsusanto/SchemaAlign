using System.CommandLine;
using SchemaAlign.Cli;

var rootCommand = CommandLineConfiguration.CreateRootCommand();
return await rootCommand.Parse(args).InvokeAsync();
