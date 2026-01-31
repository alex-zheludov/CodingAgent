using Microsoft.EntityFrameworkCore;

namespace CodingAgent.Core.Data;

public class SessionDbContext : DbContext
{
    public DbSet<ConversationEntry> Conversations { get; set; } = null!;
    public DbSet<ThinkingEntry> ThinkingLog { get; set; } = null!;
    public DbSet<StatusEntry> StatusHistory { get; set; } = null!;
    public DbSet<SessionMetadata> Metadata { get; set; } = null!;

    public SessionDbContext(DbContextOptions<SessionDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConversationEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.Role).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Content).IsRequired();
            entity.HasIndex(e => e.Timestamp);
        });

        modelBuilder.Entity<ThinkingEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.Thought).IsRequired();
            entity.HasIndex(e => e.Timestamp);
        });

        modelBuilder.Entity<StatusEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.State).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.Timestamp);
        });

        modelBuilder.Entity<SessionMetadata>(entity =>
        {
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Value).IsRequired();
        });
    }
}

public class ConversationEntry
{
    public int Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ToolName { get; set; }
}

public class ThinkingEntry
{
    public int Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Thought { get; set; } = string.Empty;
}

public class StatusEntry
{
    public int Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string State { get; set; } = string.Empty;
    public string Activity { get; set; } = string.Empty;
}

public class SessionMetadata
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
