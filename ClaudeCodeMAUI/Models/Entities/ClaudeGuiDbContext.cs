using Microsoft.EntityFrameworkCore;

namespace ClaudeCodeMAUI.Models.Entities;

/// <summary>
/// DbContext per il database ClaudeGui.
/// Gestisce le entity Sessions, Messages e Conversations (legacy).
/// </summary>
public class ClaudeGuiDbContext : DbContext
{
    /// <summary>
    /// Tabella Sessions (nuova)
    /// </summary>
    public DbSet<Session> Sessions { get; set; }

    /// <summary>
    /// Tabella Messages
    /// </summary>
    public DbSet<Message> Messages { get; set; }

    /// <summary>
    /// Tabella Conversations (legacy, deprecata)
    /// </summary>
    public DbSet<Conversation> Conversations { get; set; }

    public ClaudeGuiDbContext(DbContextOptions<ClaudeGuiDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configurazione Session
        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.SessionId)
                .IsUnique()
                .HasDatabaseName("idx_session_id");

            entity.HasIndex(e => e.Status)
                .HasDatabaseName("idx_status");

            entity.HasIndex(e => e.WorkingDirectory)
                .HasDatabaseName("idx_working_directory");

            entity.HasIndex(e => new { e.Processed, e.Excluded })
                .HasDatabaseName("idx_sessions_processed_excluded");

            entity.Property(e => e.Status)
                .HasColumnType("varchar(20)")
                .HasDefaultValue("closed");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // Configurazione Message
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.Uuid)
                .IsUnique()
                .HasDatabaseName("idx_messages_uuid");

            entity.HasIndex(e => e.ConversationId)
                .HasDatabaseName("idx_conversation_id");

            // Foreign Key verso Sessions (usando session_id come chiave)
            entity.HasOne(m => m.Session)
                .WithMany(s => s.Messages)
                .HasForeignKey(m => m.ConversationId)
                .HasPrincipalKey(s => s.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Timestamp)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // Configurazione Conversation (legacy)
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.SessionId)
                .IsUnique()
                .HasDatabaseName("idx_conversations_session_id");

            entity.HasIndex(e => e.Status)
                .HasDatabaseName("idx_conversations_status");

            entity.HasIndex(e => new { e.Status, e.LastActivity })
                .HasDatabaseName("idx_recovery");

            entity.Property(e => e.Status)
                .HasColumnType("varchar(20)")
                .HasDefaultValue("closed");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");
        });
    }
}
