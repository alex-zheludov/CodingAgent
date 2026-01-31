using CodingAgent.Core.Data;
using CodingAgent.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CodingAgent.Core.Services;

public interface ISessionStore
{
    Task InitializeAsync();
    Task AddConversationMessageAsync(string role, string content, string? toolName = null);
    Task AddThinkingAsync(string thought);
    Task AddStatusUpdateAsync(AgentState state, string activity);
    Task<List<ConversationMessage>> GetConversationHistoryAsync(int pageSize = 50, int pageNumber = 1);
    Task<List<string>> GetRecentThinkingAsync(int count = 10);
    Task SetMetadataAsync(string key, string value);
    Task<string?> GetMetadataAsync(string key);
}

public class SessionStore : ISessionStore
{
    private readonly SessionDbContext _dbContext;
    private readonly ILogger<SessionStore> _logger;

    public SessionStore(SessionDbContext dbContext, ILogger<SessionStore> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing session database");
        await _dbContext.Database.EnsureCreatedAsync();
    }

    public async Task AddConversationMessageAsync(string role, string content, string? toolName = null)
    {
        var entry = new ConversationEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Role = role,
            Content = content,
            ToolName = toolName
        };

        _dbContext.Conversations.Add(entry);
        await _dbContext.SaveChangesAsync();
    }

    public async Task AddThinkingAsync(string thought)
    {
        var entry = new ThinkingEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Thought = thought
        };

        _dbContext.ThinkingLog.Add(entry);
        await _dbContext.SaveChangesAsync();
    }

    public async Task AddStatusUpdateAsync(AgentState state, string activity)
    {
        var entry = new StatusEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            State = state.ToString(),
            Activity = activity
        };

        _dbContext.StatusHistory.Add(entry);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<List<ConversationMessage>> GetConversationHistoryAsync(int pageSize = 50, int pageNumber = 1)
    {
        // Load all conversations and order on client side (SQLite doesn't support DateTimeOffset in ORDER BY)
        var allConversations = await _dbContext.Conversations.ToListAsync();

        var messages = allConversations
            .OrderBy(c => c.Timestamp)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ConversationMessage(
                c.Timestamp,
                c.Role,
                c.Content,
                c.ToolName))
            .ToList();

        return messages;
    }

    public async Task<List<string>> GetRecentThinkingAsync(int count = 10)
    {
        // Load all thinking entries and order on client side (SQLite doesn't support DateTimeOffset in ORDER BY)
        var allThoughts = await _dbContext.ThinkingLog.ToListAsync();

        var thoughts = allThoughts
            .OrderByDescending(t => t.Timestamp)
            .Take(count)
            .Select(t => t.Thought)
            .ToList();

        thoughts.Reverse(); // Return in chronological order
        return thoughts;
    }

    public async Task SetMetadataAsync(string key, string value)
    {
        var metadata = await _dbContext.Metadata.FindAsync(key);
        if (metadata == null)
        {
            metadata = new SessionMetadata { Key = key, Value = value };
            _dbContext.Metadata.Add(metadata);
        }
        else
        {
            metadata.Value = value;
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task<string?> GetMetadataAsync(string key)
    {
        var metadata = await _dbContext.Metadata.FindAsync(key);
        return metadata?.Value;
    }
}
