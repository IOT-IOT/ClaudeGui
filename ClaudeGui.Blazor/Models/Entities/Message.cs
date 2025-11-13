using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClaudeGui.Blazor.Models.Entities;

/// <summary>
/// Entity rappresentante la tabella 'messages_from_stdout' del database.
/// Contiene i messaggi ricevuti in tempo reale dallo stdout del processo Claude.
/// </summary>
[Table("messages_from_stdout")]
public class Message
{
    /// <summary>
    /// Primary key auto-incrementale
    /// </summary>
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>
    /// Foreign key: UUID della sessione di appartenenza
    /// </summary>
    [Required]
    [StringLength(36)]
    [Column("conversation_id")]
    public string ConversationId { get; set; } = null!;

    /// <summary>
    /// Contenuto testuale del messaggio (supporta fino a 16MB con MEDIUMTEXT)
    /// </summary>
    [Required]
    [Column("content", TypeName = "MEDIUMTEXT")]
    public string Content { get; set; } = null!;

    /// <summary>
    /// Timestamp del messaggio
    /// </summary>
    [Column("timestamp")]
    public DateTime Timestamp { get; set; }
    /// <summary>
    /// UUID univoco del messaggio
    /// </summary>
    [Required]
    [StringLength(36)]
    [Column("uuid")]
    public string Uuid { get; set; } = null!;

    /// <summary>
    /// Versione di Claude utilizzata
    /// </summary>
    [StringLength(20)]
    [Column("version")]
    public string? Version { get; set; }

    /// <summary>
    /// Current Working Directory al momento del messaggio
    /// </summary>
    [StringLength(500)]
    [Column("cwd")]
    public string? Cwd { get; set; }

    /// <summary>
    /// Modello Claude utilizzato (es. 'claude-3-opus-20240229')
    /// </summary>
    [StringLength(100)]
    [Column("model")]
    public string? Model { get; set; }

    /// <summary>
    /// JSON con statistiche usage (token input/output, cache, ecc.)
    /// </summary>
    [Column("usage_json", TypeName = "TEXT")]
    public string? UsageJson { get; set; }

    /// <summary>
    /// Tipo di messaggio: 'user', 'assistant', 'system', ecc.
    /// </summary>
    [Required]
    [StringLength(50)]
    [Column("message_type")]
    public string MessageType { get; set; } = null!;

    /// <summary>
    /// Navigation property: sessione di appartenenza
    /// </summary>
    [ForeignKey(nameof(ConversationId))]
    public virtual Session? Session { get; set; }
}
