using ClaudeGui.Blazor.Services;
using Microsoft.AspNetCore.SignalR;
using Serilog;

namespace ClaudeGui.Blazor.Hubs;

/// <summary>
/// SignalR Hub per comunicazione bidirezionale terminal ↔ server.
/// Gestisce creazione sessioni, input/output, e terminazione.
/// </summary>
public class ClaudeHub : Hub
{
    private readonly ITerminalManager _terminalManager;
    private readonly Serilog.ILogger _logger = Log.ForContext<ClaudeHub>();

    public ClaudeHub(ITerminalManager terminalManager)
    {
        _terminalManager = terminalManager;
    }

    /// <summary>
    /// Client crea una nuova sessione terminal e aspetta il Session ID reale di Claude.
    /// Event handlers sono gestiti automaticamente da TerminalManager con IHubContext.
    /// </summary>
    /// <param name="workingDirectory">Working directory per Claude</param>
    /// <param name="existingSessionId">SessionId esistente per resume (opzionale)</param>
    /// <param name="sessionName">Nome della sessione (opzionale, per nuove sessioni)</param>
    /// <param name="customConnectionId">ID univoco generato lato client per identificare questo terminal (opzionale)</param>
    /// <param name="runAsAdmin">True per eseguire il processo claude.exe con privilegi amministratore (UAC)</param>
    /// <returns>Connection ID univoco per questo terminal</returns>
    public async Task<string> CreateSession(string workingDirectory, string? existingSessionId = null, string? sessionName = null, string? customConnectionId = null, bool runAsAdmin = false)
    {
        // Usa customConnectionId se fornito, altrimenti fallback a Context.ConnectionId
        // ⚠️ IMPORTANTE: Context.ConnectionId è lo stesso per tutti i component in Blazor Server!
        // Per multi-terminal support, DEVI passare customConnectionId univoco da JavaScript.
        var connectionId = customConnectionId ?? Context.ConnectionId;

        _logger.Information("Client {HubConnectionId} creating session with CustomId: {ConnectionId}, Name: {Name}, WorkingDir: {WorkingDir}, ExistingSessionId: {ExistingSessionId}",
            Context.ConnectionId, connectionId, sessionName ?? "unnamed", workingDirectory, existingSessionId);

        // Aggiungi a SignalR group per routing messaggi
        await Groups.AddToGroupAsync(Context.ConnectionId, connectionId);

        // Crea sessione - ASPETTA il Session ID reale di Claude
        // TerminalManager gestisce la registrazione degli event handlers
        var realSessionId = await _terminalManager.CreateSession(workingDirectory, existingSessionId, connectionId, sessionName, runAsAdmin);

        _logger.Information("✅ Session {SessionId} created successfully (Name: {Name}, ConnectionId: {ConnectionId})",
            realSessionId, sessionName ?? "unnamed", connectionId);

        // Ritorna il Connection ID univoco per questo terminal
        return connectionId;
    }

    /// <summary>
    /// Client invia input al terminal (es. comando, Ctrl+C, ecc.).
    /// </summary>
    /// <param name="connectionId">Connection ID univoco del terminal (generato lato client)</param>
    /// <param name="input">Input text/data</param>
    public async Task SendInput(string connectionId, string input)
    {
        // ⚡ Usa Trace.WriteLine con Flush per output immediato (non bufferizzato)
        //System.Diagnostics.Trace.WriteLine($"⚡ [ClaudeHub] RICEVUTO da SignalR: '{input}' (ConnectionId: {connectionId})");
        //System.Diagnostics.Trace.Flush();

        _logger.Information("🔵 ClaudeHub.SendInput called - ConnectionId: {ConnectionId}, InputLength: {Length}",
            connectionId, input.Length);

        try
        {
            // TerminalManager usa connectionId per trovare la sessione
            await _terminalManager.SendInput(connectionId, input);
            _logger.Information("✅ ClaudeHub.SendInput completed successfully for connectionId {ConnectionId}", connectionId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.Error(ex, "❌ Error sending input for connectionId {ConnectionId}", connectionId);
            await Clients.Caller.SendAsync("ReceiveError", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ Unexpected error sending input for connectionId {ConnectionId}", connectionId);
            await Clients.Caller.SendAsync("ReceiveError", $"Error sending input: {ex.Message}");
        }
    }

    /// <summary>
    /// Client richiede terminazione sessione.
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    public async Task KillSession(string sessionId)
    {
        _logger.Information("Client {ConnectionId} killing session {SessionId}", Context.ConnectionId, sessionId);

        _terminalManager.KillSession(sessionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);

        // Notifica i client che la sessione è terminata
        await Clients.Group(sessionId).SendAsync("SessionTerminated", sessionId);
    }

    /// <summary>
    /// Client richiede informazioni su una sessione.
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <returns>Informazioni sullo stato della sessione</returns>
    public Task<object> GetSessionInfo(string sessionId)
    {
        var exists = _terminalManager.SessionExists(sessionId);
        var isRunning = _terminalManager.IsSessionRunning(sessionId);

        return Task.FromResult<object>(new
        {
            SessionId = sessionId,
            Exists = exists,
            IsRunning = isRunning
        });
    }

    /// <summary>
    /// Client richiede lista di tutte le sessioni attive.
    /// </summary>
    /// <returns>Lista di session ID attivi</returns>
    public Task<IEnumerable<string>> GetActiveSessions()
    {
        var activeSessions = _terminalManager.GetActiveSessions();
        _logger.Debug("GetActiveSessions: {Count} active sessions", _terminalManager.ActiveSessionCount);
        return Task.FromResult(activeSessions);
    }

    /// <summary>
    /// Client richiede il ridimensionamento del terminal PTY.
    /// Chiamato quando l'utente ridimensiona xterm.js nel browser.
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <param name="cols">Nuova larghezza in colonne</param>
    /// <param name="rows">Nuova altezza in righe</param>
    public async Task ResizeTerminal(string sessionId, int cols, int rows)
    {
        var processManager = _terminalManager.GetSession(sessionId);
        if (processManager == null)
        {
            _logger.Warning("ResizeTerminal failed: session {SessionId} not found", sessionId);
            await Clients.Caller.SendAsync("ReceiveError", $"Session {sessionId} not found");
            return;
        }

        _logger.Debug("Resizing terminal for session {SessionId} to {Cols}x{Rows}", sessionId, cols, rows);
        processManager.ResizeTerminal(cols, rows);
    }

    /// <summary>
    /// Cleanup quando client disconnette.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
        {
            _logger.Warning(exception, "Client {ConnectionId} disconnected with error", Context.ConnectionId);
        }
        else
        {
            _logger.Information("Client {ConnectionId} disconnected", Context.ConnectionId);
        }

        // Opzionale: kill sessioni associate a questo connection
        // Per ora lasciamo attive per supportare reconnect
        // In produzione si potrebbe aggiungere un timeout di inattività

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Handler quando client si connette.
    /// </summary>
    public override Task OnConnectedAsync()
    {
        _logger.Information("Client {ConnectionId} connected", Context.ConnectionId);
        return base.OnConnectedAsync();
    }
}
