using System.CommandLine;
using CodeVectorization.Cli.Commands;
using CodeVectorization.Core.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables();

builder.Services.AddCodeVectorization(builder.Configuration);

using var host = builder.Build();

var rootCommand = new RootCommand("Code Vectorization CLI - Semantic code search across repositories");
rootCommand.AddCommand(IndexCommand.Create());
rootCommand.AddCommand(SearchCommand.Create());
rootCommand.AddCommand(ListReposCommand.Create());
rootCommand.AddCommand(DeleteRepoCommand.Create());

return await rootCommand.InvokeAsync(args);

// Required for user secrets binding
public partial class Program;
