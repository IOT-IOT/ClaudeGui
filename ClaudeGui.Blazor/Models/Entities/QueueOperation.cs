using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClaudeGui.Blazor.Models.Entities;

/// <summary>
/// Entity per la tabella queue_operations.
/// Memorizza le operazioni di accodamento messaggi di Claude durante le conversazioni.
/// Un'operazione viene registrata quando Claude riceve un messaggio con type="queue-operation".
/// </summary>
[Table("queue_operations")]
public class QueueOperation
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
    /// Timestamp dell'operazione di accodamento
    /// </summary>
    [Required]
    [Column("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Tipo di operazione (es. "enqueue", "dequeue", ecc.)
    /// </summary>
    [Required]
    [Column("operation")]
    [MaxLength(100)]
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// Contenuto del messaggio accodato
    /// Può contenere riferimenti a file, codice, domande dell'utente, ecc.
    /// </summary>
    [Required]
    [Column("content", TypeName = "TEXT")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Navigation property verso la sessione (opzionale, per query con Include)
    /// </summary>
    [ForeignKey(nameof(SessionId))]
    public Session? Session { get; set; }
}
