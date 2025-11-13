using System.Collections.Concurrent;
using ClaudeGui.Blazor.Hubs;
using Microsoft.AspNetCore.SignalR;
using Serilog;

namespace ClaudeGui.Blazor.Services;

/// <summary>
/// Gestisce il dizionario di sessioni attive e i loro ProcessManager.
/// Singleton shared tra tutte le SignalR connections.
/// Thread-safe tramite ConcurrentDictionary.
/// </summary>
public class TerminalManager : ITerminalManager
{
    private readonly ConcurrentDictionary<string, ClaudeProcessManager> _activeSessions = new();
    private readonly IHubContext<ClaudeHub> _hubContext;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly Serilog.ILogger _logger = Log.ForContext<TerminalManager>();

    public TerminalManager(IHubContext<ClaudeHub> hubContext, IServiceScopeFactory serviceScopeFactory)
    {
        _hubContext = hubContext;
        _serviceScopeFactory = serviceScopeFactory;
    }

    /// <summary>
    /// Crea una nuova sessione terminal e ritorna immediatamente.
    /// Il Session ID di Claude viene rilevato in background quando arriva.
    /// </summary>
    /// <param name="workingDirectory">Working directory per il processo Claude</param>
    /// <param name="sessionId">SessionId di Claude esistente per resume (null per nuova sessione)</param>
    /// <param name="connectionId">SignalR Connection ID per il routing dei messaggi</param>
    /// <param name="sessionName">Nome della sessione (opzionale, per nuove sessioni)</param>
    /// <returns>ConnectionId per il routing SignalR (ritorna immediatamente)</returns>
    public Task<string> CreateSession(string workingDirectory, string? sessionId, string connectionId, string? sessionName = null)
    {
        var processManager = new ClaudeProcessManager(
            resumeSessionId: sessionId,
            workingDirectory: workingDirectory
        );

        // Usa sempre connectionId come chiave nel dictionary
        if (_activeSessions.TryAdd(connectionId, processManager))
        {
            _logger.Information("✅ Created session - ConnectionId: {ConnectionId}, Name: {Name}, WorkingDir: {WorkingDir}, Resume: {Resume}",
                connectionId, sessionName ?? "unnamed", workingDirectory, sessionId ?? "none");

            // Registra event handlers usando IHubContext (passa workingDirectory e sessionName per salvataggio DB)
            RegisterEventHandlers(processManager, connectionId, workingDirectory, sessionName);

            // ✅ AVVIA IMMEDIATAMENTE il processo Claude
            processManager.Start();
            _logger.Information("🚀 Started Claude process for ConnectionId: {ConnectionId}", connectionId);

            // Ritorna SUBITO il connectionId (no await, no timeout)
            // Il Session ID di Claude arriverà in background via SessionIdDetected event
            return Task.FromResult(connectionId);
        }

        throw new InvalidOperationException($"Session {connectionId} already exists");
    }

    /// <summary>
    /// Registra gli event handlers del ProcessManager per inviare eventi a SignalR.
    /// Usa IHubContext invece di Hub instance per evitare ObjectDisposedException.
    /// </summary>
    /// <param name="processManager">Process manager di cui registrare gli eventi</param>
    /// <param name="connectionId">SignalR Connection ID per routing messaggi</param>
    /// <param name="workingDirectory">Working directory della sessione (per salvataggio DB)</param>
    /// <param name="sessionName">Nome della sessione (opzionale, per salvataggio DB)</param>
    private void RegisterEventHandlers(ClaudeProcessManager processManager, string connectionId, string workingDirectory, string? sessionName = null)
    {
        // Handler per Session ID rilevato (in background, opzionale)
        processManager.SessionIdDetected += async (sender, claudeSessionId) =>
        {
            _logger.Information("✅ Claude Session ID detected for ConnectionId {ConnectionId}: {SessionId}",
                connectionId, claudeSessionId);

            try
            {
                // 🔧 Crea scope per ottenere DbService (Scoped) da Singleton (TerminalManager)
                using var scope = _serviceScopeFactory.CreateScope();
                var dbService = scope.ServiceProvider.GetRequiredService<DbService>();

                // 💾 Salva sessione nel DB con status='open' (sessione attiva)
                var inserted = await dbService.InsertSessionAsync(
                    sessionId: claudeSessionId,
                    name: sessionName, // Nome fornito dall'utente al momento della creazione
                    workingDirectory: workingDirectory,
                    lastActivity: DateTime.Now,
                    status: "open" // Sessione attiva (non "closed")
                );

                if (inserted)
                {
                    _logger.Information("💾 Session saved to DB: {SessionId} (Name: {Name}, WorkingDir: {WorkingDir}, Status: open)",
                        claudeSessionId, sessionName ?? "unnamed", workingDirectory);
                }
                else
                {
                    _logger.Warning("⚠️ Session {SessionId} already exists in DB", claudeSessionId);
                }

                // Notifica il client via SignalR con il Session ID di Claude
                await _hubContext.Clients.Group(connectionId).SendAsync("SessionIdDetected", claudeSessionId);

                // ⌨️ Invia ESC al terminal per chiudere la finestra /status di Claude Code
                _logger.Information("⌨️ Sending ESC key to close /status window for SessionId: {SessionId}", claudeSessionId);
                await processManager.SendRawInputAsync("\x1b"); // ESC key (ASCII 27)

                _logger.Information("✅ Session ID detection complete for {SessionId}", claudeSessionId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ CRITICAL: Failed to save session to DB for connection {ConnectionId}", connectionId);

                // ⚠️ CRITICO: Non può funzionare senza DB - chiudi terminal
                try
                {
                    // Notifica client dell'errore critico fatale (chiude il terminal automaticamente)
                    await _hubContext.Clients.Group(connectionId).SendAsync(
                        "FatalError",
                        "Impossibile salvare la sessione nel database. Il terminal verrà chiuso.",
                        connectionId // ✅ Passa ConnectionId (chiave corretta nella mappa terminals)
                    );

                    _logger.Information("📤 Sent FatalError to client - ConnectionId: {ConnectionId}, ClaudeSessionId: {ClaudeSessionId}",
                        connectionId, claudeSessionId);

                    // Attendi che il client chiuda il terminal (3 secondi timeout)
                    await Task.Delay(3500);

                    // Chiudi processo Claude
                    processManager.Kill();

                    // Rimuovi dal dizionario sessioni attive
                    _activeSessions.TryRemove(connectionId, out _);

                    _logger.Warning("⚠️ Terminal closed for ConnectionId {ConnectionId} due to DB failure", connectionId);
                }
                catch (Exception cleanupEx)
                {
                    _logger.Error(cleanupEx, "Error during cleanup after DB failure for connection {ConnectionId}", connectionId);
                }
            }
        };

        // Handler per timeout Session ID (5 secondi dopo /status senza risposta)
        processManager.SessionIdTimeout += async (sender, e) =>
        {
            _logger.Error("❌ TIMEOUT: Session ID not detected within 5 seconds for ConnectionId {ConnectionId}", connectionId);

            try
            {
                // Notifica client dell'errore critico fatale (chiude il terminal automaticamente)
                await _hubContext.Clients.Group(connectionId).SendAsync(
                    "FatalError",
                    "Timeout: Session ID non rilevato entro 5 secondi. Il terminal verrà chiuso.",
                    connectionId // ✅ Passa ConnectionId (chiave corretta nella mappa terminals)
                );

                _logger.Information("📤 Sent FatalError (timeout) to client - ConnectionId: {ConnectionId}", connectionId);

                // Attendi che il client chiuda il terminal (3 secondi timeout)
                await Task.Delay(3500);

                // Chiudi processo Claude
                processManager.Kill();

                // Rimuovi dal dizionario sessioni attive
                _activeSessions.TryRemove(connectionId, out _);

                _logger.Warning("⚠️ Terminal closed for ConnectionId {ConnectionId} due to Session ID timeout", connectionId);
            }
            catch (Exception cleanupEx)
            {
                _logger.Error(cleanupEx, "Error during cleanup after Session ID timeout for connection {ConnectionId}", connectionId);
            }
        };

        // Handler per raw output da Claude PTY
        processManager.RawOutputReceived += async (sender, e) =>
        {
            try
            {
                // Invia raw output a tutti i client in questo connection group
                await _hubContext.Clients.Group(connectionId).SendAsync("ReceiveOutput", e.RawOutput);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error sending RawOutput to connection {ConnectionId}", connectionId);
            }
        };

        // Handler per errori
        processManager.ErrorReceived += async (sender, error) =>
        {
            try
            {
                _logger.Error("Error in ConnectionId {ConnectionId}: {Error}", connectionId, error);
                await _hubContext.Clients.Group(connectionId).SendAsync("ReceiveError", error);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error sending error message to connection {ConnectionId}", connectionId);
            }
        };

        // Handler per completamento processo
        processManager.ProcessCompleted += async (sender, e) =>
        {
            try
            {
                _logger.Information("Process completed for ConnectionId {ConnectionId}, ExitCode: {ExitCode}", connectionId, e.ExitCode);
                await _hubContext.Clients.Group(connectionId).SendAsync("ProcessCompleted", e.ExitCode, e.WasKilled);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error sending ProcessCompleted to connection {ConnectionId}", connectionId);
            }
        };
    }

    /// <summary>
    /// Ottiene il ProcessManager per una sessione.
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <returns>ProcessManager se trovato, altrimenti null</returns>
    public ClaudeProcessManager? GetSession(string sessionId)
    {
        _activeSessions.TryGetValue(sessionId, out var manager);
        return manager;
    }

    /// <summary>
    /// Avvia il processo Claude per una sessione (lazy start).
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    public void StartSession(string sessionId)
    {
        var manager = GetSession(sessionId);
        if (manager == null)
        {
            _logger.Warning("Cannot start session {SessionId}: not found", sessionId);
            return;
        }

        if (!manager.IsRunning)
        {
            manager.Start();
            _logger.Information("Started Claude process for session: {SessionId}", sessionId);
        }
        else
        {
            _logger.Debug("Session {SessionId} is already running", sessionId);
        }
    }

    /// <summary>
    /// Termina una sessione e rimuove dal dizionario.
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    public void KillSession(string sessionId)
    {
        if (_activeSessions.TryRemove(sessionId, out var manager))
        {
            manager.Dispose();
            _logger.Information("Killed session: {SessionId}", sessionId);
        }
        else
        {
            _logger.Warning("Cannot kill session {SessionId}: not found", sessionId);
        }
    }

    /// <summary>
    /// Ottiene tutte le sessioni attive.
    /// </summary>
    /// <returns>Elenco degli ID di sessione attive</returns>
    public IEnumerable<string> GetActiveSessions()
    {
        return _activeSessions.Keys;
    }

    /// <summary>
    /// Conta sessioni attive.
    /// </summary>
    public int ActiveSessionCount => _activeSessions.Count;

    /// <summary>
    /// Verifica se una sessione esiste ed è attiva.
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <returns>True se la sessione esiste</returns>
    public bool SessionExists(string sessionId)
    {
        return _activeSessions.ContainsKey(sessionId);
    }

    /// <summary>
    /// Verifica se una sessione è in esecuzione.
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <returns>True se la sessione esiste ed è in esecuzione</returns>
    public bool IsSessionRunning(string sessionId)
    {
        var manager = GetSession(sessionId);
        return manager?.IsRunning ?? false;
    }

    /// <summary>
    /// Invia input a una sessione terminal tramite ConnectionId.
    /// Durante l'attesa del Session ID reale, usa ConnectionId come chiave.
    /// Dopo il parsing, usa Session ID reale come chiave.
    /// </summary>
    /// <param name="connectionId">SignalR Connection ID</param>
    /// <param name="data">Dati da inviare al processo Claude</param>
    public async Task SendInput(string connectionId, string data)
    {
        _logger.Information("📥 SendInput called - ConnectionId: {ConnectionId}, InputLength: {Length}, ActiveSessions: {@Keys}",
            connectionId, data.Length, _activeSessions.Keys);

        // Prova prima con connectionId (per sessioni in attesa di Session ID reale)
        var manager = GetSession(connectionId);

        if (manager == null)
        {
            // Se non trovato, potrebbe essere una sessione già rimappata con Session ID reale
            // In questo caso, SignalR dovrebbe comunque usare connectionId come chiave
            _logger.Error("❌ Session not found for connectionId: {ConnectionId}. Active sessions: {@Keys}",
                connectionId, _activeSessions.Keys);
            throw new InvalidOperationException($"Session not found for connection {connectionId}");
        }

        if (!manager.IsRunning)
        {
            _logger.Error("❌ Session not running for connectionId: {ConnectionId}", connectionId);
            throw new InvalidOperationException($"Session not running for connection {connectionId}");
        }

        _logger.Information("✅ Sending input to ProcessManager...");
        await manager.SendRawInputAsync(data);
        _logger.Information("✅ Input sent successfully to session for connectionId: {ConnectionId}", connectionId);
    }
}
