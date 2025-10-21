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
            var result = await orchestrationService.ProcessRequestAsync(request.Instruction, sessionId);

            return Results.Ok(new OrchestrationResponse(
                SessionId: sessionId,
                Response: result.Response,
                Status: result.Status.ToString(),
                Intent: result.Summary != null ? "Completed" : "Unknown",
                Metrics: result.Metrics,
                Summary: result.Summary,
                Plan: result.Plan,
                StepResults: result.StepResults));
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
        var state = await orchestrationService.GetStateAsync(sessionId);

        if (state == null)
        {
            return Results.NotFound(new { error = "Session not found" });
        }

        return Results.Ok(state);
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
