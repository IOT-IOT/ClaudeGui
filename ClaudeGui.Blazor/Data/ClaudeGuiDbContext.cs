using Microsoft.EntityFrameworkCore;
using ClaudeGui.Blazor.Models.Entities;
using Message = ClaudeGui.Blazor.Models.Entities.Message;

namespace ClaudeGui.Blazor.Data;

/// <summary>
/// DbContext per il database ClaudeGui.
/// Gestisce le entity Sessions, Messages, Summaries e Conversations (legacy).
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
    /// Tabella Summaries (riepiloghi generati da Claude)
    /// </summary>
    public DbSet<Summary> Summaries { get; set; }

    /// <summary>
    /// Tabella FileHistorySnapshots (snapshot cronologia file tracciati)
    /// </summary>
    public DbSet<FileHistorySnapshot> FileHistorySnapshots { get; set; }

    /// <summary>
    /// Tabella QueueOperations (operazioni di accodamento messaggi)
    /// </summary>
    public DbSet<QueueOperation> QueueOperations { get; set; }

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

        // Configurazione Summary
        modelBuilder.Entity<Summary>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.SessionId)
                .HasDatabaseName("idx_summaries_session_id");

            entity.HasIndex(e => e.LeafUuid)
                .HasDatabaseName("idx_summaries_leaf_uuid");

            entity.HasIndex(e => e.Timestamp)
                .HasDatabaseName("idx_summaries_timestamp");

            // Foreign Key verso Sessions (usando session_id come chiave)
            entity.HasOne(s => s.Session)
                .WithMany()
                .HasForeignKey(s => s.SessionId)
                .HasPrincipalKey(s => s.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configurazione FileHistorySnapshot
        modelBuilder.Entity<FileHistorySnapshot>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.SessionId)
                .HasDatabaseName("idx_file_history_snapshots_session_id");

            entity.HasIndex(e => e.MessageId)
                .HasDatabaseName("idx_file_history_snapshots_message_id");

            entity.HasIndex(e => e.Timestamp)
                .HasDatabaseName("idx_file_history_snapshots_timestamp");

            // Foreign Key verso Sessions (usando session_id come chiave)
            entity.HasOne(f => f.Session)
                .WithMany()
                .HasForeignKey(f => f.SessionId)
                .HasPrincipalKey(s => s.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configurazione QueueOperation
        modelBuilder.Entity<QueueOperation>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.SessionId)
                .HasDatabaseName("idx_queue_operations_session_id");

            entity.HasIndex(e => e.Operation)
                .HasDatabaseName("idx_queue_operations_operation");

            entity.HasIndex(e => e.Timestamp)
                .HasDatabaseName("idx_queue_operations_timestamp");

            // Foreign Key verso Sessions (usando session_id come chiave)
            entity.HasOne(q => q.Session)
                .WithMany()
                .HasForeignKey(q => q.SessionId)
                .HasPrincipalKey(s => s.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
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
