namespace ClaudeGui.Blazor.Models;

/// <summary>
/// Informazioni su una sessione terminal attiva in memoria.
/// Contiene sia il ConnectionId (per routing SignalR) che il ClaudeSessionId (UUID).
/// </summary>
public class ActiveSessionInfo
{
    /// <summary>
    /// SignalR Connection ID - usato come chiave per routing messaggi.
    /// Viene generato immediatamente quando si crea la sessione.
    /// </summary>
    public string ConnectionId { get; set; } = null!;

    /// <summary>
    /// Session ID di Claude (UUID) - rilevato in background dopo ~2 secondi.
    /// Null finché non viene estratto dall'output di /status.
    /// </summary>
    public string? ClaudeSessionId { get; set; }

    /// <summary>
    /// Process manager che controlla il processo Claude via ConPTY.
    /// </summary>
    public Services.ClaudeProcessManager ProcessManager { get; set; } = null!;

    /// <summary>
    /// Nome della sessione fornito dall'utente.
    /// </summary>
    public string? SessionName { get; set; }

    /// <summary>
    /// Working directory del processo Claude.
    /// </summary>
    public string WorkingDirectory { get; set; } = null!;

    /// <summary>
    /// Timestamp di creazione della sessione.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
