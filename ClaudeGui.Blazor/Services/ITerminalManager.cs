namespace ClaudeGui.Blazor.Services;

/// <summary>
/// Interfaccia per gestione dizionario sessioni attive.
/// Permette dependency injection e unit testing con mock.
/// </summary>
public interface ITerminalManager
{
    /// <summary>
    /// Crea una nuova sessione terminal Claude e aspetta il Session ID reale di Claude.
    /// </summary>
    /// <param name="workingDirectory">Working directory per il processo Claude</param>
    /// <param name="sessionId">SessionId esistente (null per nuova sessione)</param>
    /// <param name="connectionId">SignalR Connection ID per il routing dei messaggi</param>
    /// <param name="sessionName">Nome della sessione (opzionale, per nuove sessioni)</param>
    /// <returns>Session ID reale di Claude (dopo parsing)</returns>
    Task<string> CreateSession(string workingDirectory, string? sessionId, string connectionId, string? sessionName = null);

    /// <summary>
    /// Crea una nuova sessione terminal PowerShell interattiva.
    /// </summary>
    /// <param name="workingDirectory">Working directory per il processo PowerShell</param>
    /// <param name="connectionId">SignalR Connection ID per il routing dei messaggi</param>
    /// <param name="parentClaudeSessionId">Claude Session ID della sessione "parent" (per associazione)</param>
    /// <returns>Connection ID del terminale PowerShell creato</returns>
    Task<string> CreatePowerShellTerminal(string workingDirectory, string connectionId, string parentClaudeSessionId);

    /// <summary>
    /// Ottiene il ProcessManager per una sessione.
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <returns>ProcessManager se trovato, altrimenti null</returns>
    ClaudeProcessManager? GetSession(string sessionId);

    /// <summary>
    /// Invia input a una sessione terminal tramite ConnectionId.
    /// </summary>
    /// <param name="connectionId">SignalR Connection ID</param>
    /// <param name="data">Dati da inviare al processo Claude</param>
    Task SendInput(string connectionId, string data);

    /// <summary>
    /// Avvia il processo Claude per una sessione (lazy start).
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    void StartSession(string sessionId);

    /// <summary>
    /// Termina una sessione e rimuove dal dizionario.
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    void KillSession(string sessionId);

    /// <summary>
    /// Ottiene tutte le sessioni attive (solo ConnectionId).
    /// </summary>
    /// <returns>Elenco dei ConnectionId delle sessioni attive</returns>
    IEnumerable<string> GetActiveSessions();

    /// <summary>
    /// Ottiene informazioni complete su tutte le sessioni attive.
    /// Include ConnectionId, ClaudeSessionId, SessionName, WorkingDirectory, ecc.
    /// </summary>
    /// <returns>Lista di ActiveSessionInfo con metadata completo</returns>
    List<Models.ActiveSessionInfo> GetActiveSessionsInfo();

    /// <summary>
    /// Conta sessioni attive.
    /// </summary>
    int ActiveSessionCount { get; }

    /// <summary>
    /// Verifica se una sessione esiste ed è attiva.
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <returns>True se la sessione esiste</returns>
    bool SessionExists(string sessionId);

    /// <summary>
    /// Verifica se una sessione è in esecuzione.
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <returns>True se la sessione esiste ed è in esecuzione</returns>
    bool IsSessionRunning(string sessionId);

    /// <summary>
    /// Verifica se una sessione Claude è già attiva in memoria.
    /// </summary>
    /// <param name="claudeSessionId">Claude Session ID (UUID)</param>
    /// <returns>True se la sessione è già in memoria</returns>
    bool HasActiveSession(string claudeSessionId);

    /// <summary>
    /// Ottiene il ConnectionId associato a un ClaudeSessionId.
    /// </summary>
    /// <param name="claudeSessionId">Claude Session ID (UUID)</param>
    /// <returns>ConnectionId se trovato, altrimenti null</returns>
    string? GetConnectionIdByClaudeSessionId(string claudeSessionId);

    /// <summary>
    /// Invia comando "exit\r" per chiudere gracefully una sessione.
    /// </summary>
    /// <param name="connectionId">SignalR Connection ID</param>
    Task SendExit(string connectionId);
}
