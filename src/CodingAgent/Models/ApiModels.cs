namespace CodingAgent.Models;

public record ExecuteRequest(string Instruction);

public record MessageRequest(string Message);

public record ExecuteResponse(bool Success, string Message, string SessionId);

public record ConversationResponse(
    List<ConversationMessage> Messages,
    int TotalCount,
    int PageSize,
    int PageNumber);

public record ConversationMessage(
    DateTimeOffset Timestamp,
    string Role, // user, assistant, tool
    string Content,
    string? ToolName = null);

public record HealthCheckResponse(
    string Status,
    Dictionary<string, string> Subsystems,
    TimeSpan Uptime,
    DateTimeOffset LastActivity);
