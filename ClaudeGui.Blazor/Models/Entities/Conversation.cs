using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClaudeGui.Blazor.Models.Entities;

/// <summary>
/// Entity rappresentante la tabella 'conversations' del database (legacy).
/// Questa tabella è deprecata e verrà sostituita dalla tabella 'Sessions'.
/// Mantenuta per compatibilità con vecchie sessioni.
/// </summary>
[Table("conversations")]
public class Conversation
{
    /// <summary>
    /// Primary key auto-incrementale
    /// </summary>
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>
    /// UUID univoco della conversazione
    /// </summary>
    [Required]
    [StringLength(36)]
    [Column("session_id")]
    public string SessionId { get; set; } = null!;

    /// <summary>
    /// Titolo/nome della tab (nullable)
    /// </summary>
    [StringLength(255)]
    [Column("tab_title")]
    public string? TabTitle { get; set; }

    /// <summary>
    /// Flag indicante se la sessione è in plan mode
    /// </summary>
    [Column("is_plan_mode")]
    public bool IsPlanMode { get; set; }

    /// <summary>
    /// Data/ora ultimo accesso
    /// </summary>
    [Column("last_activity")]
    public DateTime LastActivity { get; set; }

    /// <summary>
    /// Stato conversazione: 'active', 'closed', 'killed'
    /// </summary>
    [Required]
    [StringLength(20)]
    [Column("status")]
    public string Status { get; set; } = "closed";

    /// <summary>
    /// Data/ora creazione
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Data/ora ultimo aggiornamento
    /// </summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Directory di lavoro
    /// </summary>
    [StringLength(500)]
    [Column("working_directory")]
    public string? WorkingDirectory { get; set; }
}
