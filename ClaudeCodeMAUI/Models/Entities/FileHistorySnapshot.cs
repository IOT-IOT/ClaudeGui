using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClaudeCodeMAUI.Models.Entities;

/// <summary>
/// Entity per la tabella file_history_snapshots.
/// Memorizza gli snapshot della cronologia dei file tracciati da Claude durante le conversazioni.
/// Uno snapshot viene creato quando Claude riceve un messaggio con type="file-history-snapshot".
/// </summary>
[Table("file_history_snapshots")]
public class FileHistorySnapshot
{
    /// <summary>
    /// ID autoincrementale (chiave primaria)
    /// </summary>
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>
    /// ID della sessione di conversazione (FK verso sessions.session_id)
    /// </summary>
    [Required]
    [Column("session_id")]
    [MaxLength(255)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp di quando è stato creato lo snapshot
    /// Estratto da snapshot.timestamp nel JSON originale
    /// </summary>
    [Required]
    [Column("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Message ID univoco dello snapshot
    /// Esempio: "5cb496a6-3b3a-40ba-a0ba-0dfbfbb60a6c"
    /// </summary>
    [Required]
    [Column("message_id")]
    [MaxLength(255)]
    public string MessageId { get; set; } = string.Empty;

    /// <summary>
    /// Contenuto JSON del campo trackedFileBackups
    /// Memorizza i backup dei file tracciati (può essere vuoto: {})
    /// </summary>
    [Required]
    [Column("tracked_file_backups_json", TypeName = "TEXT")]
    public string TrackedFileBackupsJson { get; set; } = "{}";

    /// <summary>
    /// Flag che indica se questo è un aggiornamento di uno snapshot esistente
    /// </summary>
    [Required]
    [Column("is_snapshot_update")]
    public bool IsSnapshotUpdate { get; set; }

    /// <summary>
    /// Navigation property verso la sessione (opzionale, per query con Include)
    /// </summary>
    [ForeignKey(nameof(SessionId))]
    public Session? Session { get; set; }
}
