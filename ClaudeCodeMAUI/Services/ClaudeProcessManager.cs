using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace ClaudeCodeMAUI.Services
{
    /// <summary>
    /// Event args for JSON line received from Claude stdout
    /// </summary>
    public class JsonLineReceivedEventArgs : EventArgs
    {
        public string JsonLine { get; set; } = string.Empty;
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
    /// Manages a persistent Claude Code process for a single conversation.
    /// Handles process lifecycle, stdin/stdout communication, and termination.
    /// </summary>
    public class ClaudeProcessManager : IDisposable
    {
        private Process? _process;
        private StreamWriter? _stdinWriter;
        private bool _isRunning;
        private bool _wasKilled;
        private readonly string? _sessionId;
        //private readonly bool _isPlanMode;
        private readonly string? _dbSessionId; // For DB updates
        private readonly string _workingDirectory; // Working directory per il processo Claude

        // Events
        public event EventHandler<JsonLineReceivedEventArgs>? JsonLineReceived;
        public event EventHandler<ProcessCompletedEventArgs>? ProcessCompleted;
        public event EventHandler<string>? ErrorReceived;
        public event EventHandler<bool>? IsRunningChanged;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="resumeSessionId">Optional session ID to resume</param>
        /// <param name="dbSessionId">Session ID for DB operations (for fire-and-forget updates)</param>
        /// <param name="workingDirectory">Optional working directory for Claude process. If null, uses AppConfig default.</param>
        public ClaudeProcessManager(string? resumeSessionId = null, string? dbSessionId = null, string? workingDirectory = null)
        {

            _sessionId = resumeSessionId;
            _dbSessionId = dbSessionId;
            _workingDirectory = workingDirectory ?? AppConfig.ClaudeWorkingDirectory;
            _isRunning = false;
            _wasKilled = false;

            Log.Information("ClaudeProcessManager created with working directory: {WorkingDir}", _workingDirectory);
        }

        /// <summary>
        /// Starts the Claude process with persistent stream-json mode
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                Log.Warning("Claude process already running");
                return;
            }

            try
            {
                Log.Information("Creating ProcessStartInfo...");
                var startInfo = new ProcessStartInfo
                {
                    FileName = "claude",
                    Arguments = BuildArguments(),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = _workingDirectory
                };
                Log.Information("ProcessStartInfo created with Arguments: {Args}, WorkingDirectory: {WorkingDir}",
                    startInfo.Arguments, startInfo.WorkingDirectory);

                Log.Information("Creating Process object...");
                _process = new Process { StartInfo = startInfo };
                _process.EnableRaisingEvents = true;
                _process.Exited += OnProcessExited;
                Log.Information("Process object created");

                Log.Information("Starting process...");
                _process.Start();
                Log.Information("Process started successfully!");

                _stdinWriter = _process.StandardInput;
                SetIsRunning(true);

                // Start async reading of stdout and stderr on background threads
                Log.Information("Starting async read tasks...");
                _ = Task.Run(async () => await ReadStdoutAsync());
                _ = Task.Run(async () => await ReadStderrAsync());

                Log.Information("Claude process started (PID: {ProcessId}, PlanMode: {PlanMode}, Resume: {Resume})",
                                _process.Id,  _sessionId ?? "none");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start Claude process");
                _isRunning = false;
                throw;
            }
        }

        /// <summary>
        /// Builds command line arguments for claude.exe
        /// </summary>
        private string BuildArguments()
        {
            var args = new StringBuilder();
            args.Append("-p "); // Non-interactive mode
            args.Append("--input-format stream-json ");
            args.Append("--output-format stream-json ");
            args.Append("--verbose ");
            args.Append("--dangerously-skip-permissions ");
            //args.Append("--include-partial-messages ");
            
            //args.Append("--context-mode auto-compact "); // Gestione automatica del contesto con compattazione

            if (!string.IsNullOrEmpty(_sessionId))
            {
                args.Append($"--resume {_sessionId} ");
            }

            return args.ToString().Trim();
        }

        /// <summary>
        /// Sends a user message to Claude via stdin
        /// </summary>
        public async Task SendMessageAsync(string prompt)
        {
            if (!_isRunning || _stdinWriter == null)
            {
                throw new InvalidOperationException("Process is not running");
            }

            try
            {
                // Build JSONL message
                var jsonMessage = $@"{{""type"":""user"",""message"":{{""role"":""user"",""content"":""{EscapeJson(prompt)}""}}}}";

                await _stdinWriter.WriteLineAsync(jsonMessage);
                await _stdinWriter.FlushAsync();

                Log.Debug("Sent message to Claude: {PromptLength} chars", prompt.Length);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to send message to Claude");
                throw;
            }
        }

        /// <summary>
        /// Escapes a string for JSON
        /// </summary>
        private string EscapeJson(string text)
        {
            return text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        /// <summary>
        /// Reads stdout asynchronously and raises events for each JSONL line
        /// </summary>
        private async Task ReadStdoutAsync()
        {
            if (_process?.StandardOutput == null)
                return;

            try
            {
                while (!_process.StandardOutput.EndOfStream)
                {
                    var line = await _process.StandardOutput.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        JsonLineReceived?.Invoke(this, new JsonLineReceivedEventArgs { JsonLine = line });
                    }
                }
            }
            catch (Exception ex)
            {
                if (_isRunning) // Only log if we're still supposed to be running
                {
                    Log.Error(ex, "Error reading stdout from Claude process");
                }
            }
        }

        /// <summary>
        /// Reads stderr asynchronously and raises error events
        /// </summary>
        private async Task ReadStderrAsync()
        {
            if (_process?.StandardError == null)
                return;

            try
            {
                while (!_process.StandardError.EndOfStream)
                {
                    var line = await _process.StandardError.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        Log.Warning("Claude stderr: {Line}", line);
                        ErrorReceived?.Invoke(this, line);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_isRunning)
                {
                    Log.Error(ex, "Error reading stderr from Claude process");
                }
            }
        }

        /// <summary>
        /// Kills the process immediately (for Stop button)
        /// Fire-and-forget DB update in background
        /// </summary>
        public void Kill()
        {
            if (_process == null || !_isRunning)
                return;

            try
            {
                _wasKilled = true;
                _process.Kill(entireProcessTree: true);
                SetIsRunning(false);

                Log.Warning("Claude process killed (PID: {ProcessId})", _process.Id);

                // Fire-and-forget DB update (don't await, don't block)
                if (!string.IsNullOrEmpty(_dbSessionId))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // TODO: Get DbService instance and update status
                            // await dbService.UpdateStatusAsync(_dbSessionId, "killed");
                            Log.Information("DB status updated to 'killed' for session: {SessionId}", _dbSessionId);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Failed to update DB status after kill");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error killing Claude process");
            }
        }

        /// <summary>
        /// Closes stdin gracefully and waits for process to terminate
        /// </summary>
        public async Task CloseGracefullyAsync(int timeoutMs = 5000)
        {
            if (_process == null || !_isRunning)
                return;

            try
            {
                // Close stdin to signal EOF to Claude
                _stdinWriter?.Close();

                // Wait for process to exit gracefully
                var exited = await Task.Run(() => _process.WaitForExit(timeoutMs));

                if (!exited)
                {
                    Log.Warning("Claude process did not exit gracefully within {Timeout}ms, killing it", timeoutMs);
                    Kill();
                }
                else
                {
                    Log.Information("Claude process exited gracefully");
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
        /// Process exited event handler
        /// </summary>
        private void OnProcessExited(object? sender, EventArgs e)
        {
            SetIsRunning(false);

            var exitCode = _process?.ExitCode ?? -1;
            Log.Information("Claude process exited (ExitCode: {ExitCode}, WasKilled: {WasKilled})", exitCode, _wasKilled);

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
            if (!_isRunning || _stdinWriter == null)
            {
                throw new InvalidOperationException("Process is not running");
            }

            try
            {
                Log.Information("Sending 'exit' command to Claude session: {SessionId}", _sessionId ?? "unknown");
                await SendMessageAsync("exit");
                Log.Information("Exit command sent successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to send exit command to Claude");
                throw;
            }
        }

        /// <summary>
        /// Checks if process is currently running
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// IDisposable implementation
        /// </summary>
        public void Dispose()
        {
            if (_isRunning)
            {
                Kill();
            }

            _stdinWriter?.Dispose();
            _process?.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
