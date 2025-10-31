using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace ClaudeCodeMAUI.Services
{
    /// <summary>
    /// Servizio per eseguire comandi Claude one-shot (non persistenti).
    /// Utilizzato per comandi come /context che richiedono l'avvio di un processo
    /// separato, la cattura dell'output, e la terminazione del processo.
    /// </summary>
    public class ClaudeCommandRunner
    {
        /// <summary>
        /// Esegue un comando Claude in modalità interattiva.
        /// Avvia Claude con --resume, invia il comando via stdin, cattura l'output.
        /// Il processo viene automaticamente killato dopo la cattura dell'output.
        /// </summary>
        /// <param name="sessionId">ID della sessione da riprendere</param>
        /// <param name="command">Comando da eseguire (es. "/context")</param>
        /// <param name="workingDir">Directory di lavoro per il processo</param>
        /// <param name="timeoutMs">Timeout in millisecondi (default 10 secondi)</param>
        /// <returns>Output completo del comando</returns>
        /// <exception cref="TimeoutException">Se il comando non completa entro il timeout</exception>
        /// <exception cref="InvalidOperationException">Se il processo non può essere avviato</exception>
        public async Task<string> ExecuteCommandAsync(
            string sessionId,
            string command,
            string workingDir,
            int timeoutMs = 10000)
        {
            Process? process = null;

            try
            {
                Log.Information("Executing Claude command: {Command} for session {SessionId}", command, sessionId);

                // Crea ProcessStartInfo per Claude in modalità HEADLESS
                // Usa stream-json per comunicazione strutturata
                var startInfo = new ProcessStartInfo
                {
                    FileName = "claude",
                    Arguments = $"--resume {sessionId} -p --input-format stream-json --output-format stream-json --verbose",
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,  // Nasconde la console (debug completato)
                    StandardInputEncoding = new UTF8Encoding(false),  // UTF-8 senza BOM
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false)
                };

                Log.Information("Starting process with arguments: {Arguments} in directory: {WorkingDir}",
                    startInfo.Arguments, startInfo.WorkingDirectory);

                // Crea e avvia il processo
                process = new Process { StartInfo = startInfo };
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();
                var outputCompleted = new TaskCompletionSource<bool>();
                var errorCompleted = new TaskCompletionSource<bool>();

                // Handler per catturare stdout in modo asincrono
                var lineCount = 0;
                process.OutputDataReceived += (sender, e) =>
                {
                    lineCount++;
                    if (e.Data != null)
                    {
                        outputBuilder.AppendLine(e.Data);
                        Log.Debug("Claude stdout line #{LineNum}: {Line}", lineCount, e.Data);
                    }
                    else
                    {
                        // null significa che lo stream è terminato
                        Log.Information("Claude stdout stream ended after {LineCount} lines", lineCount - 1);
                        outputCompleted.TrySetResult(true);
                    }
                };

                // Handler per catturare stderr in modo asincrono
                var errorLineCount = 0;
                process.ErrorDataReceived += (sender, e) =>
                {
                    errorLineCount++;
                    if (e.Data != null)
                    {
                        errorBuilder.AppendLine(e.Data);
                        Log.Warning("Claude stderr line #{LineNum}: {Line}", errorLineCount, e.Data);

                        // IMPORTANTE: In modalità interattiva, l'output potrebbe venire su stderr!
                        // Cattura anche su stderr come possibile output
                        outputBuilder.AppendLine(e.Data);
                    }
                    else
                    {
                        // null significa che lo stream è terminato
                        Log.Information("Claude stderr stream ended after {LineCount} lines", errorLineCount - 1);
                        errorCompleted.TrySetResult(true);
                    }
                };

                // Avvia il processo
                if (!process.Start())
                {
                    throw new InvalidOperationException("Failed to start Claude process");
                }

                Log.Information("Process started with PID: {ProcessId}", process.Id);

                // Inizia la lettura asincrona degli stream
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Attendi un momento per permettere a Claude di inizializzarsi
                await Task.Delay(2000);

                // Invia il comando come messaggio JSON (formato stream-json)
                // Formato: {"type":"user","message":{"role":"user","content":"COMANDO"}}
                var jsonMessage = $@"{{""type"":""user"",""message"":{{""role"":""user"",""content"":""{EscapeJson(command)}""}}}}";

                Log.Information("Sending JSON message via stdin: {Message}", jsonMessage);
                await process.StandardInput.WriteLineAsync(jsonMessage);
                await process.StandardInput.FlushAsync();
                Log.Information("JSON message sent successfully");

                // Attendi con timeout che ci sia output OPPURE scada il timeout
                var delayTask = Task.Delay(timeoutMs);
                var completedTask = await Task.WhenAny(outputCompleted.Task, delayTask);

                // Se è scaduto il timeout invece di completare l'output, aspetta ancora un po'
                if (completedTask == delayTask)
                {
                    Log.Warning("Timeout reached ({TimeoutMs}ms), waiting 1 more second for output...", timeoutMs);
                    await Task.Delay(1000);
                }

                // Ora kill il processo
                if (!process.HasExited)
                {
                    Log.Information("Killing process (PID: {ProcessId})", process.Id);
                    process.Kill(entireProcessTree: true);

                    // Attendi un momento per permettere la terminazione e il flush degli stream
                    await Task.Delay(500);
                }

                var output = outputBuilder.ToString();
                var errors = errorBuilder.ToString();

                Log.Information("Command completed. Output length: {OutputLength} chars, Errors: {ErrorLength} chars",
                    output.Length, errors.Length);

                // Log dettagliato dell'output
                if (string.IsNullOrWhiteSpace(output))
                {
                    Log.Warning("Output is EMPTY!");
                }
                else
                {
                    Log.Information("Output preview (first 1000 chars): {OutputPreview}",
                        output.Length > 1000 ? output.Substring(0, 1000) : output);
                }

                // Se ci sono errori significativi, loggali
                if (!string.IsNullOrWhiteSpace(errors))
                {
                    Log.Warning("Command produced errors: {Errors}", errors);
                }

                // Ritorna l'output (anche se vuoto)
                return output;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to execute Claude command: {Command}", command);
                throw;
            }
            finally
            {
                // Cleanup: assicurati che il processo sia terminato
                if (process != null && !process.HasExited)
                {
                    try
                    {
                        Log.Warning("Process still running in finally block, forcing kill");
                        process.Kill(entireProcessTree: true);
                    }
                    catch (Exception killEx)
                    {
                        Log.Error(killEx, "Failed to kill process in cleanup");
                    }
                }

                process?.Dispose();
            }
        }

        /// <summary>
        /// Escapa una stringa per l'inserimento in JSON.
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
    }
}
