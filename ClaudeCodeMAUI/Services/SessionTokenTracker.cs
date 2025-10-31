using System;
using System.IO;
using System.Text.Json;
using Serilog;

namespace ClaudeCodeMAUI.Services
{
    /// <summary>
    /// Traccia l'utilizzo dei token leggendo il file JSONL della sessione corrente di Claude Code.
    /// Claude Code salva ogni sessione in ~/.claude/projects/[directory-encoded]/[session-uuid].jsonl
    /// </summary>
    public class SessionTokenTracker
    {
        private readonly string? _sessionId;
        private readonly string _claudeProjectsPath;
        private string? _sessionFilePath;

        /// <summary>
        /// Costruttore
        /// </summary>
        /// <param name="sessionId">ID della sessione corrente (UUID)</param>
        /// <param name="workingDirectory">Directory di lavoro corrente (es. C:\Sources\ClaudeGui)</param>
        public SessionTokenTracker(string? sessionId, string workingDirectory)
        {
            _sessionId = sessionId;

            // Path alla directory dei progetti Claude: ~/.claude/projects
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _claudeProjectsPath = Path.Combine(userProfile, ".claude", "projects");

            // Codifica la directory di lavoro: C:\Sources\ClaudeGui -> C--Sources-ClaudeGui
            var encodedDir = EncodeDirectoryPath(workingDirectory);

            // Path completo al file JSONL della sessione
            if (!string.IsNullOrEmpty(sessionId))
            {
                _sessionFilePath = Path.Combine(_claudeProjectsPath, encodedDir, $"{sessionId}.jsonl");
                Log.Information("SessionTokenTracker initialized with path: {Path}", _sessionFilePath);
            }
            else
            {
                Log.Warning("SessionTokenTracker initialized without session ID");
            }
        }

        /// <summary>
        /// Codifica il path della directory nel formato usato da Claude Code
        /// Es: C:\Sources\ClaudeGui -> C--Sources-ClaudeGui
        ///     /home/user/project -> -home-user-project
        /// </summary>
        private string EncodeDirectoryPath(string path)
        {
            // Normalizza separatori
            var normalized = path.Replace('\\', '-').Replace('/', '-');

            // Rimuovi ":" dai drive letters (C: -> C)
            normalized = normalized.Replace(":", "");

            return normalized;
        }

        /// <summary>
        /// Calcola l'utilizzo totale dei token dalla sessione corrente leggendo il file JSONL
        /// </summary>
        /// <returns>Oggetto TokenUsage con il totale dei token utilizzati</returns>
        public TokenUsage CalculateUsage()
        {
            var usage = new TokenUsage
            {
                TotalTokens = 0,
                InputTokens = 0,
                OutputTokens = 0,
                CacheCreationTokens = 0,
                CacheReadTokens = 0,
                TotalBudget = 200000, // Budget standard di Claude Sonnet 4.5
                IsValid = false
            };

            // Verifica che il file esista
            if (string.IsNullOrEmpty(_sessionFilePath) || !File.Exists(_sessionFilePath))
            {
                Log.Debug("Session file not found: {Path}", _sessionFilePath ?? "null");
                return usage;
            }

            try
            {
                // Leggi il file JSONL riga per riga
                using var reader = new StreamReader(_sessionFilePath);
                string? line;

                while ((line = reader.ReadLine()) != null)
                {
                    try
                    {
                        // Parse della riga JSON
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        // Cerca il campo "message.usage"
                        if (root.TryGetProperty("message", out var message) &&
                            message.TryGetProperty("usage", out var usageObj))
                        {
                            // Somma i token
                            if (usageObj.TryGetProperty("input_tokens", out var inputTokens))
                                usage.InputTokens += inputTokens.GetInt32();

                            if (usageObj.TryGetProperty("output_tokens", out var outputTokens))
                                usage.OutputTokens += outputTokens.GetInt32();

                            if (usageObj.TryGetProperty("cache_creation_input_tokens", out var cacheCreation))
                                usage.CacheCreationTokens += cacheCreation.GetInt32();

                            if (usageObj.TryGetProperty("cache_read_input_tokens", out var cacheRead))
                                usage.CacheReadTokens += cacheRead.GetInt32();
                        }
                    }
                    catch (JsonException ex)
                    {
                        Log.Debug(ex, "Failed to parse JSONL line (skipping)");
                        // Ignora righe malformate e continua
                    }
                }

                // Calcola il totale
                usage.TotalTokens = usage.InputTokens + usage.OutputTokens +
                                   usage.CacheCreationTokens + usage.CacheReadTokens;
                usage.IsValid = true;

                Log.Information("Token usage calculated: {Total} / {Budget} ({Percentage:F1}%)",
                               usage.TotalTokens, usage.TotalBudget, usage.PercentageUsed);

                return usage;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to calculate token usage from session file");
                return usage;
            }
        }

        /// <summary>
        /// Verifica se il file della sessione esiste
        /// </summary>
        public bool SessionFileExists()
        {
            return !string.IsNullOrEmpty(_sessionFilePath) && File.Exists(_sessionFilePath);
        }
    }

    /// <summary>
    /// Rappresenta l'utilizzo dei token in una sessione
    /// </summary>
    public class TokenUsage
    {
        /// <summary>
        /// Totale token utilizzati (input + output + cache creation + cache read)
        /// </summary>
        public int TotalTokens { get; set; }

        /// <summary>
        /// Token di input (prompt dell'utente)
        /// </summary>
        public int InputTokens { get; set; }

        /// <summary>
        /// Token di output (risposta di Claude)
        /// </summary>
        public int OutputTokens { get; set; }

        /// <summary>
        /// Token usati per creare la cache
        /// </summary>
        public int CacheCreationTokens { get; set; }

        /// <summary>
        /// Token letti dalla cache
        /// </summary>
        public int CacheReadTokens { get; set; }

        /// <summary>
        /// Budget totale disponibile
        /// </summary>
        public int TotalBudget { get; set; }

        /// <summary>
        /// Token rimanenti
        /// </summary>
        public int RemainingTokens => TotalBudget - TotalTokens;

        /// <summary>
        /// Percentuale di utilizzo (0-100)
        /// </summary>
        public double PercentageUsed => TotalBudget > 0 ? (double)TotalTokens / TotalBudget * 100 : 0;

        /// <summary>
        /// Indica se i dati sono validi (file trovato e letto correttamente)
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Livello di warning: None (0-70%), Low (70-85%), Medium (85-95%), High (95-100%)
        /// </summary>
        public WarningLevel GetWarningLevel()
        {
            if (PercentageUsed < 70) return WarningLevel.None;
            if (PercentageUsed < 85) return WarningLevel.Low;
            if (PercentageUsed < 95) return WarningLevel.Medium;
            return WarningLevel.High;
        }
    }

    /// <summary>
    /// Livello di warning per l'utilizzo del contesto
    /// </summary>
    public enum WarningLevel
    {
        None,   // 0-70% - Verde
        Low,    // 70-85% - Giallo chiaro
        Medium, // 85-95% - Arancione
        High    // 95-100% - Rosso
    }
}
