using CodingAgent.Models;
using CodingAgent.Models.Orchestration;
using CodingAgent.Services;
using Microsoft.AspNetCore.Mvc;

namespace CodingAgent.Endpoints;

public class OrchestrationEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/v2/orchestration")
            .WithTags("Orchestration")
            .WithOpenApi();

        group.MapPost("/execute", ExecuteAsync)
            .WithName("OrchestrationExecute")
            .WithDescription("Execute request with multi-agent orchestration");

        group.MapGet("/state/{sessionId}", GetStateAsync)
            .WithName("GetOrchestrationState")
            .WithDescription("Get orchestration state for a session");
    }

    private static async Task<IResult> ExecuteAsync(
        [FromBody] ExecuteRequest request,
        [FromServices] IOrchestrationService orchestrationService,
        [FromServices] ILogger<OrchestrationEndpoints> logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Instruction))
            {
                return Results.BadRequest(new { error = "Instruction is required" });
            }

            var sessionId = Guid.NewGuid().ToString();
            var summary = await orchestrationService.ProcessInstructionAsync(request.Instruction);
            var status = await orchestrationService.GetStatusAsync();

            return Results.Ok(new OrchestrationResponse(
                SessionId: sessionId,
                Response: summary.Summary,
                Status: status.ToString(),
                Intent: "Completed",
                Metrics: new Dictionary<string, object>(),
                Summary: summary,
                Plan: null,
                StepResults: null));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in orchestration");
            return Results.Problem(ex.Message);
        }
    }

    private static async Task<IResult> GetStateAsync(
        string sessionId,
        [FromServices] IOrchestrationService orchestrationService)
    {
        var status = await orchestrationService.GetStatusAsync();

        return Results.Ok(new { sessionId, status = status.ToString() });
    }
}

public record OrchestrationResponse(
    string SessionId,
    string Response,
    string Status,
    string Intent,
    Dictionary<string, object> Metrics,
    SummaryResult? Summary,
    ExecutionPlan? Plan,
    List<StepResult>? StepResults);
