using System.Collections.Concurrent;
using ClaudeGui.Blazor.Hubs;
using ClaudeGui.Blazor.Models;
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
    private readonly ConcurrentDictionary<string, ActiveSessionInfo> _activeSessions = new();
    private readonly IHubContext<ClaudeHub> _hubContext;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly SessionEventService _sessionEventService;
    private readonly Serilog.ILogger _logger = Log.ForContext<TerminalManager>();

    public TerminalManager(IHubContext<ClaudeHub> hubContext, IServiceScopeFactory serviceScopeFactory, SessionEventService sessionEventService)
    {
        _hubContext = hubContext;
        _serviceScopeFactory = serviceScopeFactory;
        _sessionEventService = sessionEventService;
    }

    /// <summary>
    /// Crea una nuova sessione terminal e ritorna immediatamente.
    /// Il Session ID di Claude viene rilevato in background quando arriva.
    /// </summary>
    /// <param name="workingDirectory">Working directory per il processo Claude</param>
    /// <param name="sessionId">SessionId di Claude esistente per resume (null per nuova sessione)</param>
    /// <param name="connectionId">SignalR Connection ID per il routing dei messaggi</param>
    /// <param name="sessionName">Nome della sessione (opzionale, per nuove sessioni)</param>
    /// <returns>ConnectionId per il routing SignalR</returns>
    public async Task<string> CreateSession(string workingDirectory, string? sessionId, string connectionId, string? sessionName = null)
    {
        // isNewSession=true se sessionId è null (nuova sessione), altrimenti false (resume)
        bool isNewSession = string.IsNullOrEmpty(sessionId);

        var processManager = new ClaudeProcessManager(
            resumeSessionId: sessionId,
            workingDirectory: workingDirectory,
            isNewSession: isNewSession
        );

        // Crea ActiveSessionInfo con metadata
        var sessionInfo = new ActiveSessionInfo
        {
            ConnectionId = connectionId,
            ClaudeSessionId = sessionId, // Se resume, già noto; se new session, null (verrà rilevato)
            ProcessManager = processManager,
            SessionName = sessionName,
            WorkingDirectory = workingDirectory,
            CreatedAt = DateTime.Now
        };

        // Usa sempre connectionId come chiave nel dictionary
        if (_activeSessions.TryAdd(connectionId, sessionInfo))
        {
            _logger.Information("✅ Created session - ConnectionId: {ConnectionId}, Name: {Name}, WorkingDir: {WorkingDir}, Resume: {Resume}, IsNewSession: {IsNewSession}",
                connectionId, sessionName ?? "unnamed", workingDirectory, sessionId ?? "none", isNewSession);

            // Registra event handlers usando IHubContext (passa isNewSession per evitare ESC su resume)
            RegisterEventHandlers(processManager, connectionId, workingDirectory, sessionName, isNewSession);

            // Se resume (sessionId già noto), aggiorna lo status DB a 'open' e notifica NavMenu
            if (!isNewSession && !string.IsNullOrEmpty(sessionId))
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var dbService = scope.ServiceProvider.GetRequiredService<DbService>();
                    await dbService.UpdateSessionStatusAsync(sessionId, "open");
                    _logger.Information("💾 Updated DB status to 'open' for resumed session: {SessionId}", sessionId);

                    // Notifica NavMenu che la lista sessioni è cambiata (dopo che DB è aggiornato)
                    _sessionEventService.NotifySessionListChanged();
                    _logger.Information("📢 Notified session list changed for resumed session");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "❌ Failed to update DB status for resumed session: {SessionId}", sessionId);
                }
            }

            // ✅ AVVIA IMMEDIATAMENTE il processo Claude
            processManager.Start();
            _logger.Information("🚀 Started Claude process for ConnectionId: {ConnectionId}", connectionId);

            // Ritorna il connectionId
            // Il Session ID di Claude arriverà in background via SessionIdDetected event (per nuove sessioni)
            return connectionId;
        }

         throw new InvalidOperationException($"Session {connectionId} already exists");
    }

    /// <summary>
    /// Crea una nuova sessione terminal PowerShell interattiva.
    /// PowerShell non ha SessionId, quindi usa solo connectionId per routing.
    /// </summary>
    /// <param name="workingDirectory">Working directory per il processo PowerShell</param>
    /// <param name="connectionId">SignalR Connection ID per il routing dei messaggi</param>
    /// <param name="parentClaudeSessionId">Claude Session ID della sessione "parent" (per associazione)</param>
    /// <returns>Connection ID del terminale PowerShell creato</returns>
    public Task<string> CreatePowerShellTerminal(string workingDirectory, string connectionId, string parentClaudeSessionId)
    {
        // PowerShell: nessun resume, nessun sessionId, sempre nuova istanza
        var processManager = new ClaudeProcessManager(
            terminalType: TerminalType.PowerShell,
            resumeSessionId: null,
            workingDirectory: workingDirectory,
            isNewSession: true // Non rilevante per PowerShell, ma serve per coerenza
        );

        // Crea ActiveSessionInfo con metadata
        var sessionInfo = new ActiveSessionInfo
        {
            Type = TerminalType.PowerShell,
            ConnectionId = connectionId,
            ClaudeSessionId = parentClaudeSessionId, // Per associare PowerShell a sessione Claude parent
            ProcessManager = processManager,
            SessionName = $"PowerShell-{parentClaudeSessionId.Substring(0, 8)}",
            WorkingDirectory = workingDirectory,
            CreatedAt = DateTime.Now
        };

        // Usa connectionId come chiave nel dictionary
        if (_activeSessions.TryAdd(connectionId, sessionInfo))
        {
            _logger.Information("✅ Created PowerShell terminal - ConnectionId: {ConnectionId}, WorkingDir: {WorkingDir}, Parent: {Parent}",
                connectionId, workingDirectory, parentClaudeSessionId);

            // Registra event handlers semplificati per PowerShell (no Session ID detection)
            RegisterPowerShellEventHandlers(processManager, connectionId);

            // Avvia immediatamente il processo PowerShell
            processManager.Start();
            _logger.Information("🚀 Started PowerShell process for ConnectionId: {ConnectionId}", connectionId);

            // Ritorna subito il connectionId
            return Task.FromResult(connectionId);
        }

        throw new InvalidOperationException($"PowerShell terminal {connectionId} already exists");
    }

    /// <summary>
    /// Registra gli event handlers del ProcessManager per PowerShell (semplificati, no Session ID detection).
    /// </summary>
    /// <param name="processManager">Process manager PowerShell</param>
    /// <param name="connectionId">SignalR Connection ID per routing messaggi</param>
    private void RegisterPowerShellEventHandlers(ClaudeProcessManager processManager, string connectionId)
    {
        // Handler per raw output da PowerShell PTY
        processManager.RawOutputReceived += async (sender, e) =>
        {
            try
            {
                await _hubContext.Clients.All.SendAsync("ReceiveOutput", connectionId, e.RawOutput);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error sending PowerShell output to connection {ConnectionId}", connectionId);
            }
        };

        // Handler per completamento processo PowerShell
        processManager.ProcessCompleted += async (sender, e) =>
        {
            try
            {
                _logger.Information("PowerShell process completed for ConnectionId {ConnectionId}, ExitCode: {ExitCode}",
                    connectionId, e.ExitCode);

                // Rimuovi sessione da _activeSessions (no DB update per PowerShell)
                _activeSessions.TryRemove(connectionId, out _);
                _logger.Information("🗑️ Removed PowerShell terminal from memory: ConnectionId={ConnectionId}", connectionId);

                // Notifica client via SignalR
                await _hubContext.Clients.All.SendAsync("ProcessCompleted", connectionId, e.ExitCode, e.WasKilled);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error sending PowerShell ProcessCompleted to connection {ConnectionId}", connectionId);
            }
        };
    }

    /// <summary>
    /// Registra gli event handlers del ProcessManager per inviare eventi a SignalR.
    /// Usa IHubContext invece di Hub instance per evitare ObjectDisposedException.
    /// </summary>
    /// <param name="processManager">Process manager di cui registrare gli eventi</param>
    /// <param name="connectionId">SignalR Connection ID per routing messaggi</param>
    /// <param name="workingDirectory">Working directory della sessione (per salvataggio DB)</param>
    /// <param name="sessionName">Nome della sessione (opzionale, per salvataggio DB)</param>
    /// <param name="isNewSession">True se nuova sessione (invia ESC dopo /status), False se resume (NON inviare ESC)</param>
    private void RegisterEventHandlers(ClaudeProcessManager processManager, string connectionId, string workingDirectory, string? sessionName, bool isNewSession)
    {
        // Handler per Session ID rilevato (in background, opzionale)
        processManager.SessionIdDetected += async (sender, claudeSessionId) =>
        {
            _logger.Information("✅ Claude Session ID detected for ConnectionId {ConnectionId}: {SessionId}",
                connectionId, claudeSessionId);

            try
            {
                // ✅ Aggiorna ActiveSessionInfo con il ClaudeSessionId rilevato
                if (_activeSessions.TryGetValue(connectionId, out var sessionInfo))
                {
                    sessionInfo.ClaudeSessionId = claudeSessionId;
                    _logger.Information("📝 Updated ActiveSessionInfo with ClaudeSessionId: {SessionId}", claudeSessionId);
                }

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

                // Notifica NavMenu che la lista sessioni è cambiata (dopo inserimento/update DB)
                _sessionEventService.NotifySessionListChanged();
                _logger.Information("📢 Notified session list changed for new session");

                // Notifica il client via SignalR con il Session ID di Claude (con connectionId per routing)
                await _hubContext.Clients.All.SendAsync("SessionIdDetected", connectionId, claudeSessionId);

                // ⌨️ Invia ESC al terminal per chiudere la finestra /status (SOLO per nuove sessioni)
                // Se resume (isNewSession=false), NON inviare ESC perché /status non è stato aperto
                if (isNewSession)
                {
                    _logger.Information("⌨️ Sending ESC key to close /status window for SessionId: {SessionId} (isNewSession=true)", claudeSessionId);
                    await processManager.SendRawInputAsync("\x1b"); // ESC key (ASCII 27)
                }
                else
                {
                    _logger.Information("⏭️ Skipping ESC key for SessionId: {SessionId} (isNewSession=false, resume mode)", claudeSessionId);
                }

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
                // ⚠️ IMPORTANTE: In Blazor Server, tutti i component condividono lo stesso Context.ConnectionId!
                // Quindi NON possiamo usare gruppi per routing. Inviamo a TUTTI i client e lasciamo che
                // JavaScript faccia il routing basato sul connectionId univoco del terminal.
                await _hubContext.Clients.All.SendAsync("ReceiveOutput", connectionId, e.RawOutput);
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
                _logger.Information("🔔 Process completed for ConnectionId {ConnectionId}, ExitCode: {ExitCode}", connectionId, e.ExitCode);

                // DEBUG: Verifica se la sessione esiste in _activeSessions
                var sessionExists = _activeSessions.TryGetValue(connectionId, out var sessionInfo);
                _logger.Information("🔍 Session lookup: ConnectionId={ConnectionId}, Exists={Exists}, ClaudeSessionId={ClaudeSessionId}",
                    connectionId, sessionExists, sessionInfo?.ClaudeSessionId ?? "NULL");

                // 1. Aggiorna DB status='closed' se la sessione ha ClaudeSessionId (AWAIT per assicurarsi che completi)
                if (sessionExists && !string.IsNullOrEmpty(sessionInfo.ClaudeSessionId))
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var dbService = scope.ServiceProvider.GetRequiredService<DbService>();
                        await dbService.UpdateSessionStatusAsync(sessionInfo.ClaudeSessionId, "closed");
                        _logger.Information("💾 Updated DB status to 'closed' for session: {SessionId}", sessionInfo.ClaudeSessionId);
                    }
                    catch (Exception dbEx)
                    {
                        _logger.Error(dbEx, "❌ Failed to update DB status for session: {SessionId}", sessionInfo.ClaudeSessionId);
                    }

                    // 2. Rimuovi sessione da _activeSessions
                    _activeSessions.TryRemove(connectionId, out _);
                    _logger.Information("🗑️ Removed session from memory: ConnectionId={ConnectionId}, ClaudeSessionId={ClaudeSessionId}",
                        connectionId, sessionInfo.ClaudeSessionId);

                    // 4. Notifica NavMenu che la lista sessioni è cambiata (dopo che DB è aggiornato)
                    _sessionEventService.NotifySessionListChanged();
                    _logger.Information("📢 Notified session list changed for NavMenu refresh");
                }
                else
                {
                    // ⚠️ Sessione non trovata o ClaudeSessionId mancante
                    _logger.Warning("⚠️ SKIPPED cleanup: Session not found in _activeSessions or ClaudeSessionId is null. ConnectionId={ConnectionId}", connectionId);
                }

                // 3. Notifica client via SignalR con connectionId per routing corretto
                _logger.Information("📡 Sending ProcessCompleted to client via SignalR: ConnectionId={ConnectionId}", connectionId);
                await _hubContext.Clients.All.SendAsync("ProcessCompleted", connectionId, e.ExitCode, e.WasKilled);
                _logger.Information("✅ ProcessCompleted sent successfully");

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
    /// <param name="sessionId">Session ID (ConnectionId)</param>
    /// <returns>ProcessManager se trovato, altrimenti null</returns>
    public ClaudeProcessManager? GetSession(string sessionId)
    {
        if (_activeSessions.TryGetValue(sessionId, out var sessionInfo))
        {
            return sessionInfo.ProcessManager;
        }
        return null;
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
    /// Aggiorna anche lo status nel DB a "closed" se disponibile il ClaudeSessionId.
    /// </summary>
    /// <param name="sessionId">Session ID (ConnectionId)</param>
    public void KillSession(string sessionId)
    {
        _logger.Warning("💀 [KillSession] CALLED for session: {SessionId}", sessionId);
        if (_activeSessions.TryRemove(sessionId, out var sessionInfo))
        {
            // 💾 Aggiorna DB status a "closed" se disponibile ClaudeSessionId
            if (!string.IsNullOrEmpty(sessionInfo.ClaudeSessionId))
            {
                Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var dbService = scope.ServiceProvider.GetRequiredService<DbService>();

                        await dbService.UpdateSessionStatusAsync(sessionInfo.ClaudeSessionId, "closed");
                        _logger.Information("📝 Session {SessionId} marked as closed in DB", sessionInfo.ClaudeSessionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to update DB status for session {SessionId}", sessionInfo.ClaudeSessionId);
                    }
                });
            }

            sessionInfo.ProcessManager.Dispose();
            _logger.Information("Killed session: {SessionId} (ClaudeSessionId: {ClaudeSessionId})",
                sessionId, sessionInfo.ClaudeSessionId ?? "not detected");
        }
        else
        {
            _logger.Warning("Cannot kill session {SessionId}: not found", sessionId);
        }
    }

    /// <summary>
    /// Termina SOLO il processo della sessione (claude.exe/powershell)
    /// SENZA aggiornare lo status nel database.
    /// Usato quando si chiude l'app Blazor per permettere il resume delle sessioni.
    /// </summary>
    /// <param name="sessionId">Session ID (ConnectionId)</param>
    public void KillProcessOnly(string sessionId)
    {
        _logger.Warning("💀 [KillProcessOnly] CALLED for session: {SessionId}", sessionId);
        if (_activeSessions.TryRemove(sessionId, out var sessionInfo))
        {
            _logger.Information("💀 [KillProcessOnly] Session found, disposing ProcessManager");
            // ❌ NON aggiorna DB - le sessioni rimangono "open" per il resume
            // ✅ Termina solo il processo fisico (claude.exe/powershell)
            sessionInfo.ProcessManager.Dispose();

            _logger.Information("🛑 Process terminated for session: {SessionId} (ClaudeSessionId: {ClaudeSessionId}) - DB status NOT updated",
                sessionId, sessionInfo.ClaudeSessionId ?? "not detected");
        }
        else
        {
            _logger.Warning("Cannot terminate process for session {SessionId}: not found", sessionId);
        }
    }

    /// <summary>
    /// Marca una sessione come "closed" nel database.
    /// Helper method per aggiornare lo status DB in modo asincrono.
    /// </summary>
    /// <param name="connectionId">SignalR Connection ID</param>
    private async Task MarkSessionAsClosed(string connectionId)
    {
        if (_activeSessions.TryGetValue(connectionId, out var sessionInfo) && !string.IsNullOrEmpty(sessionInfo.ClaudeSessionId))
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var dbService = scope.ServiceProvider.GetRequiredService<DbService>();
                await dbService.UpdateSessionStatusAsync(sessionInfo.ClaudeSessionId, "closed");
                _logger.Information("💾 Session {SessionId} marked as closed in DB", sessionInfo.ClaudeSessionId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ Failed to update DB status for session {SessionId}", sessionInfo.ClaudeSessionId);
            }
        }
    }

    /// <summary>
    /// Chiude gracefully una sessione con timeout intelligente.
    /// 1. Invia comando "exit\r" a Claude
    /// 2. Monitora ogni 100ms se il processo si è chiuso
    /// 3. Se chiuso entro 10 secondi → successo
    /// 4. Se timeout → force kill
    /// </summary>
    /// <param name="connectionId">SignalR Connection ID</param>
    /// <param name="updateDatabase">Se true, marca la sessione come "closed" nel DB</param>
    public async Task CloseSessionGracefully(string connectionId, bool updateDatabase)
    {
        _logger.Information("🔄 CloseSessionGracefully called - ConnectionId: {ConnectionId}, UpdateDB: {UpdateDB}",
            connectionId, updateDatabase);

        // 1. Invia comando "exit\r" gracefully
        try
        {
            await SendExit(connectionId);
            _logger.Information("⌨️ Sent 'exit\\r' command to session: {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "⚠️ Failed to send exit command, will force kill - ConnectionId: {ConnectionId}", connectionId);

            // Forza terminazione immediata se SendExit fallisce
            if (updateDatabase)
            {
                KillSession(connectionId);
            }
            else
            {
                KillProcessOnly(connectionId);
            }
            return;
        }

        // 2. Monitora ogni 100ms per max 10 secondi (100 tentativi)
        var maxAttempts = 100;
        var delayMs = 100;
        var startTime = DateTime.Now;

        for (int i = 0; i < maxAttempts; i++)
        {
            await Task.Delay(delayMs);

            // Verifica se il processo si è già chiuso
            if (!IsSessionRunning(connectionId))
            {
                var elapsedMs = (DateTime.Now - startTime).TotalMilliseconds;
                _logger.Information("✅ Process closed gracefully after {Ms}ms for ConnectionId: {ConnectionId}",
                    elapsedMs, connectionId);

                // Se richiesto, aggiorna DB status a "closed"
                if (updateDatabase)
                {
                    await MarkSessionAsClosed(connectionId);
                }

                // Rimuovi da _activeSessions
                _activeSessions.TryRemove(connectionId, out _);
                _logger.Information("🗑️ Session removed from memory: {ConnectionId}", connectionId);
                return;
            }
        }

        // 3. Timeout raggiunto (10 secondi) → force kill
        _logger.Warning("⚠️ Graceful shutdown timeout (10s) for ConnectionId: {ConnectionId}, forcing termination",
            connectionId);

        if (updateDatabase)
        {
            KillSession(connectionId); // Force kill + update DB
        }
        else
        {
            KillProcessOnly(connectionId); // Force kill senza DB update
        }
    }

    /// <summary>
    /// Ottiene tutte le sessioni attive (solo ConnectionId).
    /// </summary>
    /// <returns>Elenco dei ConnectionId delle sessioni attive</returns>
    public IEnumerable<string> GetActiveSessions()
    {
        return _activeSessions.Keys;
    }

    /// <summary>
    /// Ottiene informazioni complete su tutte le sessioni attive.
    /// Include ConnectionId, ClaudeSessionId, SessionName, WorkingDirectory, ecc.
    /// </summary>
    /// <returns>Lista di ActiveSessionInfo con metadata completo</returns>
    public List<ActiveSessionInfo> GetActiveSessionsInfo()
    {
        return _activeSessions.Values.ToList();
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
        _logger.Debug("🔍 [IsSessionRunning] Checking session: {SessionId}", sessionId);
        var exists = _activeSessions.TryGetValue(sessionId, out var sessionInfo);
        _logger.Debug("🔍 [IsSessionRunning] Session exists in dictionary: {Exists}", exists);

        if (!exists)
        {
            _logger.Debug("🔍 [IsSessionRunning] Result: FALSE (not in _activeSessions)");
            return false;
        }

        var isRunning = sessionInfo.ProcessManager.IsRunning;
        _logger.Debug("🔍 [IsSessionRunning] ProcessManager.IsRunning: {IsRunning}", isRunning);
        _logger.Debug("🔍 [IsSessionRunning] Final Result: {Result}", isRunning);
        return isRunning;
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

    /// <summary>
    /// Verifica se una sessione Claude è già attiva in memoria.
    /// </summary>
    /// <param name="claudeSessionId">Claude Session ID (UUID)</param>
    /// <returns>True se la sessione è già in memoria</returns>
    public bool HasActiveSession(string claudeSessionId)
    {
        return _activeSessions.Values.Any(s => s.ClaudeSessionId == claudeSessionId);
    }

    /// <summary>
    /// Ottiene il ConnectionId associato a un ClaudeSessionId.
    /// </summary>
    /// <param name="claudeSessionId">Claude Session ID (UUID)</param>
    /// <returns>ConnectionId se trovato, altrimenti null</returns>
    public string? GetConnectionIdByClaudeSessionId(string claudeSessionId)
    {
        // Filtra SOLO i Claude terminals, non i PowerShell
        var session = _activeSessions.Values.FirstOrDefault(s =>
            s.ClaudeSessionId == claudeSessionId &&
            s.ProcessManager.TerminalType == TerminalType.Claude);
        return session?.ConnectionId;
    }

    /// <summary>
    /// Invia comando "exit\r" per chiudere gracefully una sessione.
    /// </summary>
    /// <param name="connectionId">SignalR Connection ID</param>
    public async Task SendExit(string connectionId)
    {
        //_logger.Information("📤 SendExit called for ConnectionId: {ConnectionId}", connectionId);

        var manager = GetSession(connectionId);
        if (manager == null)
        {
            //_logger.Error("❌ Session not found for connectionId: {ConnectionId}", connectionId);
            throw new InvalidOperationException($"Session not found for connection {connectionId}");
        }

        if (!manager.IsRunning)
        {
            //_logger.Warning("⚠️ Session not running for connectionId: {ConnectionId}, skipping exit command", connectionId);
            return;
        }

        //_logger.Information("⌨️ Sending 'exit\\r' command to close Claude process gracefully...");
        await manager.SendRawInputAsync("exit");
        await Task.Delay(100);
        await manager.SendRawInputAsync("\r");
        //_logger.Information("✅ Exit command sent successfully to session for connectionId: {ConnectionId}", connectionId);
    }
}
