using CodingAgent.Configuration;
using CodingAgent.Core.Workflow;
using CodingAgent.Extensions;
using CodingAgent.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog();
builder.Configuration.AddUserSecrets<Program>();

// Register all CodingAgent services
builder.Services.AddCodingAgent(builder.Configuration);

var app = builder.Build();



// Create a scope for the workflow
using var scope = app.Services.CreateScope();
var initService = scope.ServiceProvider.GetRequiredService<IInitializationService>();

// Initialize the workspace
await initService.InitializeAsync();

var workflowService = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
var agentSettings = scope.ServiceProvider.GetRequiredService<IOptions<AgentSettings>>().Value;
var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

Console.WriteLine("=============================================");
Console.WriteLine("   Coding Agent Terminal");
Console.WriteLine("=============================================");
Console.WriteLine($"Session ID: {agentSettings.Session.SessionId}");
Console.WriteLine();
Console.WriteLine("Type your instructions and press Enter.");
Console.WriteLine("Type 'exit' to quit.");
Console.WriteLine("=============================================");
Console.WriteLine();

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;

    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Goodbye!");
        break;
    }

    try
    {
        Console.WriteLine("Processing...");
        var result = await workflowService.ProcessInstructionAsync(input);

        Console.WriteLine($"Agent: {result.Summary}");

        if (result.KeyFindings?.Any() == true)
        {
            foreach (var finding in result.KeyFindings)
                Console.WriteLine(finding);
        }

        if (result.Metrics != null)
            Console.WriteLine($"[{result.Metrics.StepsCompleted}/{result.Metrics.StepsTotal} steps]");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        logger.LogError(ex, "Error processing instruction");
    }

    Console.WriteLine();
}
