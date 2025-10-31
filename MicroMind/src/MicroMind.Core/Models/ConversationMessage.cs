namespace MicroMind.Core.Models;

public class ConversationMessage
{
    public required MessageRole Role { get; init; }

    public required string Content { get; init; }
}

public enum MessageRole
{
    System,

    User,

    Assistant
}
