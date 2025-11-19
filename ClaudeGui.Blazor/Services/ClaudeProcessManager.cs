using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ClaudeGui.Blazor.Models;
using ClaudeGui.Blazor.Services.ConPTY;
using Serilog;

namespace ClaudeGui.Blazor.Services
{
    /// <summary>
    /// Event args for raw output received from Claude stdout
    /// </summary>
    public class RawOutputReceivedEventArgs : EventArgs
    {
        public string RawOutput { get; set; } = string.Empty;
    }

    /// <summary>
    /// Event args for process completion
    /// </summary>
    public class ProcessCompletedEventArgs : EventArgs
    {
        public int ExitCode { get; set; }
        public bool WasKilled { get; set; }
    }

    /// <summary>
    /// Manages a persistent terminal process (Claude or PowerShell) for a single conversation.
    /// Handles process lifecycle via ConPTY, stdin/stdout communication, and termination.
    /// </summary>
    public class ClaudeProcessManager : IDisposable
    {
        private Terminal? _terminal;
        private bool _isRunning;
        private bool _wasKilled;
        private string? _sessionId; // Può essere passato al costruttore (resume) o rilevato da /status (nuova sessione)
        private readonly string _workingDirectory; // Working directory per il processo
        private readonly bool _isNewSession; // Flag per determinare se inviare /status o no (false per resume)
        private readonly TerminalType _terminalType; // Tipo di terminale (Claude o PowerShell)

        // Terminal dimensions (matching typical xterm.js defaults)
        private const int TERMINAL_ROWS = 24;
        private const int TERMINAL_COLS = 80;

        // Session ID detection
        private readonly StringBuilder _outputBuffer = new StringBuilder();

        // Dynamic /status detection flag
        private bool _statusCommandSent = false;

        // Timeout per Session ID detection (5 secondi)
        private CancellationTokenSource? _sessionIdTimeoutCts;

        // Events
        public event EventHandler<RawOutputReceivedEventArgs>? RawOutputReceived;
        public event EventHandler<string>? SessionIdDetected; // Evento quando Session ID viene rilevato
        public event EventHandler? SessionIdTimeout; // Evento quando Session ID non viene rilevato entro 5 secondi
        public event EventHandler<ProcessCompletedEventArgs>? ProcessCompleted;
        public event EventHandler<string>? ErrorReceived;
        public event EventHandler<bool>? IsRunningChanged;
        public event EventHandler? ResponseCompleted;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="terminalType">Tipo di terminale (Claude o PowerShell)</param>
        /// <param name="resumeSessionId">Optional session ID to resume (solo per Claude)</param>
        /// <param name="workingDirectory">Optional working directory. If null, uses AppConfig default.</param>
        /// <param name="isNewSession">True se nuova sessione (invia /status), False se resume (Session ID già noto)</param>
        public ClaudeProcessManager(TerminalType terminalType = TerminalType.Claude, string? resumeSessionId = null, string? workingDirectory = null, bool isNewSession = true)
        {
            _terminalType = terminalType;
            _sessionId = resumeSessionId;
            _workingDirectory = workingDirectory ?? AppConfig.ClaudeWorkingDirectory;
            _isNewSession = isNewSession;
            _isRunning = false;
            _wasKilled = false;

            Log.Information("ClaudeProcessManager created: Type={TerminalType}, WorkingDir={WorkingDir}, IsNewSession={IsNewSession}",
                _terminalType, _workingDirectory, _isNewSession);
        }

        /// <summary>
        /// Rimuove tutti i codici ANSI escape da una stringa.
        /// PTY output contiene codici per colori, grassetto, clear line, ecc.
        /// Esempi: [1m (bold), [22m (bold off), [K (clear line), [31m (red), [0m (reset)
        /// </summary>
        /// <param name="text">Testo contenente codici ANSI</param>
        /// <returns>Testo pulito senza codici ANSI</returns>
        private static string StripAnsiCodes(string text)
        {
            // Pattern regex: \x1b = ESC, \[ = bracket, [0-9;]* = parametri, [a-zA-Z] = comando finale
            return Regex.Replace(text, @"\x1b\[[0-9;]*[a-zA-Z]", string.Empty);
        }

        /// <summary>
        /// Starts the terminal process (Claude or PowerShell) via ConPTY (Windows Pseudo Console).
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                Log.Warning("{TerminalType} PTY terminal already running", _terminalType);
                return;
            }

            try
            {
                Log.Information("🚀 Starting {TerminalType} process via ConPTY...", _terminalType);

                // Nota: L'applicazione ClaudeGui.Blazor è configurata per richiedere sempre privilegi amministratore
                // tramite app.manifest (requestedExecutionLevel="requireAdministrator").
                // Tutti i processi lanciati ereditano automaticamente i privilegi admin.

                // Crea istanza Terminal
                _terminal = new Terminal();

                // Registra handler per process exit
                _terminal.ProcessExited += OnProcessExited;

                // Build command line in base al tipo di terminale
                var command = _terminalType == TerminalType.Claude
                    ? BuildClaudeCommand()
                    : BuildPowerShellCommand();

                Log.Information("Command: {Command}", command);
                Log.Information("WorkingDirectory: {WorkingDir}", _workingDirectory);

                // Avvia processo tramite ConPTY
                _terminal.Start(command, _workingDirectory, TERMINAL_ROWS, TERMINAL_COLS);

                Log.Information("✅ {TerminalType} PTY process started! PID: {ProcessId}", _terminalType, _terminal.Pid);

                SetIsRunning(true);

                // Avvia task asincrono per leggere output PTY
                Log.Information("📖 Starting async read task for PTY output...");
                _ = Task.Run(async () => await ReadPtyOutputAsync());

                Log.Information("✅ {TerminalType} PTY terminal started (PID: {ProcessId}, Resume: {Resume}, WorkingDir: {WorkingDir})",
                                _terminalType, _terminal.Pid, _sessionId ?? "none", _workingDirectory);

                // Per nuove sessioni Claude: /status verrà inviato quando viene rilevato il marker $$Ready$$ nell'output
                // Per resume: Session ID già noto
                // Per PowerShell: nessun /status da inviare
                if (_terminalType == TerminalType.Claude)
                {
                    if (string.IsNullOrEmpty(_sessionId))
                    {
                        Log.Information("📝 /status command will be sent when $$Ready$$ marker is detected in output");
                    }
                    else
                    {
                        Log.Information("🔄 Resume mode - Session ID already known: {SessionId}", _sessionId);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start {TerminalType} PTY process", _terminalType);
                _isRunning = false;
                throw;
            }
        }

        /// <summary>
        /// Builds full command line for claude.exe in interactive mode via ConPTY.
        /// In modalità interactive, Claude gestisce autonomamente il proprio stato
        /// e comunica tramite PTY con pieno supporto ANSI escape codes.
        /// </summary>
        private string BuildClaudeCommand()
        {
            var command = new StringBuilder();

            // Comando base
            command.Append("claude ");

            // Modalità interactive (no -p flag)
            // Claude gestisce TTY e session state autonomamente
            command.Append("--dangerously-skip-permissions ");

            if (!string.IsNullOrEmpty(_sessionId))
            {
                command.Append($"--resume {_sessionId} ");
            }

            return command.ToString().Trim();
        }

        /// <summary>
        /// Builds full command line for pwsh.exe (PowerShell 7) in interactive mode via ConPTY.
        /// PowerShell viene lanciato con prompt interattivo e supporto ANSI colors.
        /// </summary>
        private string BuildPowerShellCommand()
        {
            // pwsh.exe è PowerShell 7 (installato in "C:\Program Files\PowerShell\7\pwsh.exe")
            // -NoLogo: non mostra banner iniziale
            // -NoExit: mantiene la shell aperta dopo aver eseguito comandi
            // Prompt interattivo: PowerShell gestisce autonomamente il proprio stato
            return @"C:\Program Files\PowerShell\7\pwsh.exe -NoLogo -NoExit";
        }

        /// <summary>
        /// Sends raw input to Claude via PTY terminal.
        /// In modalità interactive via PTY, l'input viene inviato direttamente al processo.
        /// </summary>
        public async Task SendRawInputAsync(string input)
        {

            if (!_isRunning || _terminal == null)
            {
                Log.Error("❌ SendRawInputAsync failed: IsRunning={IsRunning}, Terminal={Terminal}",
                    _isRunning, _terminal != null ? "available" : "NULL");
                throw new InvalidOperationException("PTY terminal is not running");
            }

            try
            {
                // ✅ Usa Console.WriteLine invece di Debug.WriteLine (non bufferizzato)
               var txt = input.Replace("\n", "\\n");

                //if (input.Length == 1)
                //{
                //    Debugger.Break();
                //}

                //Debug.WriteLine($"📤 [ClaudeProcessManager] Sending to PTY: '{txt}' ({txt.Length} chars)");

                // Invia input al PTY terminal
                await _terminal.WriteInputAsync(input);
                

            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ Failed to send input to Claude PTY");
                throw;
            }
        }

        /// <summary>
        /// Reads PTY output asynchronously and raises events for raw output.
        /// ConPTY output include ANSI escape codes, colori, cursor positioning, ecc.
        /// Fa anche parsing per rilevare il Session ID da /status output.
        /// </summary>
        private async Task ReadPtyOutputAsync()
        {
            if (_terminal == null)
            {
                Log.Error("❌ ReadPtyOutputAsync: Terminal is NULL!");
                return;
            }

            Log.Information("📖 ReadPtyOutputAsync: Started reading PTY output...");

            try
            {
                var buffer = new byte[4096];
                int totalBytesRead = 0;

                while (_isRunning && _terminal != null)
                {
                    var bytesRead = await _terminal.ReadOutputAsync(buffer);

                    if (bytesRead == 0)
                    {
                        // End of stream, processo terminato
                        Log.Information("📖 ReadPtyOutputAsync: PTY stream ended (process exited)");
                        break;
                    }

                    totalBytesRead += bytesRead;
                    var output = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    // ✅ DEBUG: Stampa TUTTO l'output in Visual Studio Output window
                    //Debug.WriteLine($"[CLAUDE PTY OUTPUT] {output}");
                    Log.Debug("📤 PTY Output ({BytesRead} bytes): {Output}", bytesRead, output);

                    // Inoltra output a SignalR per display nel terminal
                    RawOutputReceived?.Invoke(this, new RawOutputReceivedEventArgs { RawOutput = output });
                    Log.Debug("✅ RawOutputReceived event raised with {BytesRead} bytes", bytesRead);

                    // Accumula output nel buffer (serve per rilevare $$Ready$$ e Session ID)
                    // Solo per nuove sessioni Claude (quando _sessionId è ancora null)
                    if (_terminalType == TerminalType.Claude && string.IsNullOrEmpty(_sessionId))
                    {
                        _outputBuffer.Append(output);
                    }

                    // Rilevamento marker $$Ready$$: invia /status quando prompt è pronto (solo per nuove sessioni Claude)
                    // Se _isNewSession=false (resume), NON inviare /status perché Session ID è già noto
                    // PowerShell: nessun /status da inviare
                    if (_terminalType == TerminalType.Claude && !_statusCommandSent && _isNewSession && string.IsNullOrEmpty(_sessionId))
                    {
                        var bufferContent = _outputBuffer.ToString();
                        if (bufferContent.Contains("$$Ready$$"))
                        {
                            _statusCommandSent = true;
                            Log.Information("✅ Detected $$Ready$$ marker in output!");
                            Log.Warning("⚠️ Automatic /status command is DISABLED for manual testing");

                             
                            
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await Task.Delay(100);
                                    Log.Information("📤 Sending /status command now...");
                                    await SendRawInputAsync("/status");
                                    await Task.Delay(100);
                                    await SendRawInputAsync("\r");
                                    Log.Information("✅ /status command sent successfully");

                                    // ⏱️ Avvia timer di 5 secondi per timeout Session ID (cancellabile)
                                    _sessionIdTimeoutCts = new CancellationTokenSource();
                                    Log.Information("⏱️ Starting 5-second timeout for Session ID detection...");

                                    try
                                    {
                                        await Task.Delay(5000, _sessionIdTimeoutCts.Token);

                                        // Se arriviamo qui, il timeout è scaduto senza cancellazione
                                        if (string.IsNullOrEmpty(_sessionId))
                                        {
                                            Log.Error("❌ TIMEOUT: Session ID not detected within 5 seconds!");
                                            SessionIdTimeout?.Invoke(this, EventArgs.Empty);
                                        }
                                    }
                                    catch (TaskCanceledException)
                                    {
                                        // Timeout cancellato perché Session ID è stato rilevato
                                       // Log.Information("✅ Session ID timeout cancelled - Session ID detected: {SessionId}", _sessionId);
                                    }
                                    finally
                                    {
                                        _sessionIdTimeoutCts?.Dispose();
                                        _sessionIdTimeoutCts = null;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, "❌ Failed to send /status command");
                                }
                            });

                        }

                    }

                    // Se non abbiamo ancora Session ID (nuova sessione Claude), prova a estrarlo dall'output
                    // PowerShell: nessun Session ID da rilevare
                    if (_terminalType == TerminalType.Claude && string.IsNullOrEmpty(_sessionId))
                    {
                        TryExtractSessionId();
                    }

                }

                Log.Information("📖 ReadPtyOutputAsync: Finished reading PTY output. Total bytes read: {TotalBytes}", totalBytesRead);
            }
            catch (Exception ex)
            {
                if (_isRunning) // Only log if we're still supposed to be running
                {
                    Log.Error(ex, "❌ Error reading PTY output from Claude process");
                }
            }
        }

        /// <summary>
        /// Prova a estrarre il Session ID dall'output buffer usando regex.
        /// Pattern: "Session ID: eb772253-4548-4ae4-b3cd-24ddbe509379"
        /// </summary>
        private void TryExtractSessionId()
        {
            // ✅ Rimuovi codici ANSI prima del parsing (PTY output contiene [1m, [22m, [K, ecc.)
            var text = StripAnsiCodes(_outputBuffer.ToString());
            var match = Regex.Match(text, @"Session ID:\s+([a-f0-9-]{36})", RegexOptions.IgnoreCase);

            if (match.Success)
            {
                _sessionId = match.Groups[1].Value;
                Log.Information("✅ Detected Session ID from Claude output: {SessionId}", _sessionId);

                // ⏱️ Cancella il timeout se è in corso (Session ID rilevato prima dei 5 secondi)
                if (_sessionIdTimeoutCts != null && !_sessionIdTimeoutCts.IsCancellationRequested)
                {
                    Log.Information("⏱️ Cancelling Session ID timeout (detected before 5 seconds)");
                    _sessionIdTimeoutCts.Cancel();
                }

                // Raise evento per notificare subscribers (ClaudeHub)
                SessionIdDetected?.Invoke(this, _sessionId);

                // Pulisci buffer (non serve più accumulare output)
                _outputBuffer.Clear();
            }

            // Limita dimensione buffer per evitare memory leak (mantieni solo ultimi 10KB)
            if (_outputBuffer.Length > 10000)
            {
                _outputBuffer.Remove(0, _outputBuffer.Length - 10000);
            }
        }


        /// <summary>
        /// Kills the PTY terminal process immediately (for Stop button).
        /// Fire-and-forget DB update in background.
        /// </summary>
        public void Kill()
        {
            if (_terminal == null || !_isRunning)
                return;

            try
            {
                _wasKilled = true;
                var pid = _terminal.Pid;
                _terminal.Kill();
                SetIsRunning(false);

                Log.Warning("Claude PTY process killed (PID: {ProcessId})", pid);

                // TODO: Implementare aggiornamento DB status quando disponibile
                // Usare _sessionId (rilevato o da resume) per identificare la sessione nel DB
                // _ = Task.Run(() => dbService.UpdateStatusAsync(_sessionId, "killed"));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error killing Claude PTY process");
            }
        }

        /// <summary>
        /// Waits for PTY terminal process to terminate gracefully.
        /// </summary>
        public async Task CloseGracefullyAsync(int timeoutMs = 5000)
        {
            if (_terminal == null || !_isRunning)
                return;

            try
            {
                // Wait for process to exit gracefully
                var exited = await Task.Run(() => _terminal.WaitForExit(timeoutMs));

                if (!exited)
                {
                    Log.Warning("Claude PTY process did not exit gracefully within {Timeout}ms, killing it", timeoutMs);
                    Kill();
                }
                else
                {
                    Log.Information("Claude PTY process exited gracefully");
                    SetIsRunning(false);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during graceful close");
                Kill(); // Fallback to kill
            }
        }

        /// <summary>
        /// PTY terminal process exited event handler.
        /// </summary>
        private void OnProcessExited(object? sender, int exitCode)
        {
            SetIsRunning(false);

            Log.Information("Claude PTY process exited (ExitCode: {ExitCode}, WasKilled: {WasKilled})", exitCode, _wasKilled);

            ProcessCompleted?.Invoke(this, new ProcessCompletedEventArgs
            {
                ExitCode = exitCode,
                WasKilled = _wasKilled
            });
        }

        /// <summary>
        /// Imposta lo stato IsRunning e notifica i subscriber dell'evento IsRunningChanged.
        /// Questo metodo centralizza tutti i cambiamenti di stato per garantire
        /// che l'evento venga sempre sollevato quando lo stato cambia.
        /// </summary>
        private void SetIsRunning(bool value)
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                IsRunningChanged?.Invoke(this, value);
                Log.Debug("IsRunning changed to: {IsRunning}", value);
            }
        }

        /// <summary>
        /// Invia il comando "exit" a Claude per chiudere la sessione in modo pulito.
        /// Questo comando:
        /// - Chiude la sessione corrente
        /// - Aggiorna lo status nel database a 'closed'
        /// - Causa la terminazione del processo Claude
        /// - Dovrebbe chiudere il tab nell'UI
        /// </summary>
        public async Task SendExitCommandAsync()
        {
            if (!_isRunning || _terminal == null)
            {
                throw new InvalidOperationException("PTY terminal is not running");
            }

            try
            {
                Log.Information("Sending 'exit' command to Claude PTY session: {SessionId}", _sessionId ?? "unknown");
                await SendRawInputAsync("exit\r");
                Log.Information("Exit command sent successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to send exit command to Claude PTY");
                throw;
            }
        }

        /// <summary>
        /// Checks if process is currently running
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Ottiene il Session ID (rilevato da /status per nuove sessioni, o passato al costruttore per resume).
        /// </summary>
        public string? DetectedSessionId => _sessionId;

        /// <summary>
        /// Ridimensiona il terminal PTY quando l'utente ridimensiona xterm.js nel browser.
        /// Questo metodo viene chiamato dal ClaudeHub via SignalR.
        /// </summary>
        /// <param name="cols">Nuova larghezza in colonne</param>
        /// <param name="rows">Nuova altezza in righe</param>
        public void ResizeTerminal(int cols, int rows)
        {
            if (_terminal == null || !_isRunning)
            {
                Log.Warning("Cannot resize terminal: not running");
                return;
            }

            try
            {
                _terminal.Resize(cols, rows);
                Log.Information("Terminal resized to {Cols}x{Rows}", cols, rows);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to resize terminal");
            }
        }

        /// <summary>
        /// IDisposable implementation.
        /// Rilascia tutte le risorse, termina il processo PTY se necessario,
        /// e rimuove tutti gli event subscribers per prevenire memory leak.
        /// </summary>
        public void Dispose()
        {
            if (_isRunning)
            {
                Kill();
            }

            // Pulisci timeout Session ID se in corso
            if (_sessionIdTimeoutCts != null)
            {
                _sessionIdTimeoutCts.Cancel();
                _sessionIdTimeoutCts.Dispose();
                _sessionIdTimeoutCts = null;
            }

            // Pulisci tutti gli event subscribers per prevenire memory leak
            // e chiamate a handler dopo il dispose
            RawOutputReceived = null;
            SessionIdDetected = null;
            SessionIdTimeout = null;
            ProcessCompleted = null;
            ErrorReceived = null;
            IsRunningChanged = null;
            ResponseCompleted = null;

            _terminal?.Dispose();

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Plays a simple beep sound (cross-platform).
        /// </summary>
        private void PlayBeep()
        {
            try
            {
                Console.Beep();
            }
            catch
            {
                // On platforms where Console.Beep is not supported, ignore errors.
            }
        }
    }
}
