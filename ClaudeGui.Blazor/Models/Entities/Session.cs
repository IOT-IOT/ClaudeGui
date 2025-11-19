using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ClaudeGui.Blazor.Models.Entities;

/// <summary>
/// Entity rappresentante la tabella 'Sessions' del database.
/// Traccia le sessioni di conversazione con Claude.
/// </summary>
[Table("Sessions")]
[Index(nameof(Name), IsUnique = true, Name = "IX_Sessions_Name")]
public class Session
{
    /// <summary>
    /// Primary key auto-incrementale
    /// </summary>
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>
    /// UUID univoco della sessione (formato: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)
    /// </summary>
    [Required]
    [StringLength(36)]
    [Column("session_id")]
    public string SessionId { get; set; } = null!;

    /// <summary>
    /// Nome assegnato dall'utente alla sessione (nullable)
    /// </summary>
    [StringLength(255)]
    [Column("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Directory di lavoro della sessione
    /// </summary>
    [StringLength(500)]
    [Column("working_directory")]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Data/ora ultimo accesso/modifica
    /// </summary>
    [Column("last_activity")]
    public DateTime LastActivity { get; set; }

    /// <summary>
    /// Stato sessione: 'open' o 'closed'
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
    /// Flag per indicare se la sessione è stata processata dallo scanner filesystem
    /// </summary>
    [Column("processed")]
    public bool Processed { get; set; }

    /// <summary>
    /// Flag per escludere la sessione dalle visualizzazioni (es. sessioni di sistema)
    /// </summary>
    [Column("excluded")]
    public bool Excluded { get; set; }

    /// <summary>
    /// Motivo dell'esclusione (nullable)
    /// </summary>
    [StringLength(255)]
    [Column("excluded_reason")]
    public string? ExcludedReason { get; set; }

    /// <summary>
    /// Flag per indicare se la sessione è stata avviata con privilegi amministratore
    /// </summary>
    [Column("run_as_admin")]
    public bool RunAsAdmin { get; set; } = false;

    /// <summary>
    /// Navigation property: messaggi associati a questa sessione
    /// </summary>
    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
