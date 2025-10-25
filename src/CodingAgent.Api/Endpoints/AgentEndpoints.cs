using CodingAgent.Models;
using CodingAgent.Services;
using Microsoft.AspNetCore.Mvc;

namespace CodingAgent.Endpoints;

public class AgentEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api")
            .WithTags("Agent")
            .WithOpenApi();

        group.MapPost("/execute", ExecuteTaskAsync)
            .WithName("ExecuteTask")
            .WithDescription("Execute a task instruction using the agent orchestration");

        group.MapGet("/status", GetStatusAsync)
            .WithName("GetStatus")
            .WithDescription("Get current agent status");

        group.MapGet("/conversation", GetConversationAsync)
            .WithName("GetConversation")
            .WithDescription("Get conversation history");
    }

    private static async Task<IResult> ExecuteTaskAsync(
        [FromBody] ExecuteRequest request,
        [FromServices] IOrchestrationService orchestrationService,
        [FromServices] ILogger<AgentEndpoints> logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Instruction))
            {
                return Results.BadRequest(new { error = "Instruction is required" });
            }

            var result = await orchestrationService.ProcessInstructionAsync(request.Instruction);

            return Results.Ok(new
            {
                success = true,
                message = "Task completed",
                summary = result
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing task");
            return Results.Problem(ex.Message);
        }
    }

    private static async Task<IResult> GetStatusAsync(
        [FromServices] IOrchestrationService orchestrationService)
    {
        var status = await orchestrationService.GetStatusAsync();
        return Results.Ok(new { status = status.ToString() });
    }

    private static async Task<IResult> GetConversationAsync(
        [FromQuery] int pageSize,
        [FromQuery] int pageNumber,
        [FromServices] ISessionStore sessionStore)
    {
        pageSize = pageSize <= 0 ? 50 : Math.Min(pageSize, 100);
        pageNumber = Math.Max(pageNumber, 1);

        var messages = await sessionStore.GetConversationHistoryAsync(pageSize, pageNumber);
        var totalCount = messages.Count; // Simplified - in production would query total count

        return Results.Ok(new ConversationResponse(
            Messages: messages,
            TotalCount: totalCount,
            PageSize: pageSize,
            PageNumber: pageNumber));
    }
}
