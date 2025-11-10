using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClaudeCodeMAUI.Models.Entities;

/// <summary>
/// Entity per la tabella summaries.
/// Memorizza i summary generati da Claude durante le conversazioni.
/// Un summary viene creato quando Claude riceve un messaggio con type="summary".
/// </summary>
[Table("summaries")]
public class Summary
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
    /// Timestamp di quando è stato generato il summary
    /// </summary>
    [Required]
    [Column("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Testo del summary generato da Claude
    /// Esempio: "Italian UI Font Size Reduction Completed Successfully"
    /// </summary>
    [Required]
    [Column("summary")]
    [MaxLength(1000)]
    public string SummaryText { get; set; } = string.Empty;

    /// <summary>
    /// UUID del "leaf" message (opzionale)
    /// Riferimento al messaggio finale della conversazione che ha generato questo summary
    /// </summary>
    [Column("leaf_uuid")]
    [MaxLength(255)]
    public string? LeafUuid { get; set; }

    /// <summary>
    /// Navigation property verso la sessione (opzionale, per query con Include)
    /// </summary>
    [ForeignKey(nameof(SessionId))]
    public Session? Session { get; set; }
}
