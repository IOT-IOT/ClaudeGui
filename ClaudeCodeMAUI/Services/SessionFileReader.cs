using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Serilog;

namespace ClaudeCodeMAUI.Services
{
    /// <summary>
    /// Service per leggere e parsare i file JSONL delle sessioni Claude Code.
    /// I file si trovano in: C:\Users\{user}\.claude\projects\{escaped-path}\{session-id}.jsonl
    /// </summary>
    public class SessionFileReader
    {
        /// <summary>
        /// Costruisce il path completo del file JSONL della sessione.
        /// Esempio: C:\Users\enric\.claude\projects\C--Sources-ClaudeGui\c20736e3-cc02-4cdb-85ab-726f2a0041fa.jsonl
        /// </summary>
        /// <param name="sessionId">ID della sessione (UUID)</param>
        /// <param name="workingDirectory">Working directory del progetto (es: C:\Sources\ClaudeGui)</param>
        /// <returns>Path completo del file JSONL</returns>
        public static string GetSessionFilePath(string sessionId, string workingDirectory)
        {
            try
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var escapedPath = EscapePathForClaudeProjects(workingDirectory);
                var filePath = Path.Combine(userProfile, ".claude", "projects", escapedPath, $"{sessionId}.jsonl");

                Log.Information("Session file path: {FilePath}", filePath);
                return filePath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to construct session file path for session {SessionId}", sessionId);
                throw;
            }
        }

        /// <summary>
        /// Legge il file JSONL e restituisce una lista di JsonElement (uno per ogni messaggio).
        /// Ogni riga del file è un oggetto JSON completo.
        /// </summary>
        /// <param name="filePath">Path del file JSONL</param>
        /// <returns>Lista di messaggi come JsonElement</returns>
        public static List<JsonElement> ReadSessionMessages(string filePath)
        {
            var messages = new List<JsonElement>();

            try
            {
                if (!File.Exists(filePath))
                {
                    Log.Warning("Session file not found: {FilePath}", filePath);
                    throw new FileNotFoundException($"Session file not found: {filePath}");
                }

                Log.Information("Reading session file: {FilePath}", filePath);

                // IMPORTANTE: Leggi il file con encoding UTF-8 per gestire caratteri accentati
                var lines = File.ReadAllLines(filePath, System.Text.Encoding.UTF8);
                Log.Information("Found {Count} lines in session file", lines.Length);

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue; // Salta righe vuote
                    }

                    try
                    {
                        // Deserializza il JSON - .NET decodifica automaticamente le sequenze escape Unicode
                        var jsonElement = JsonSerializer.Deserialize<JsonElement>(line);
                        messages.Add(jsonElement);
                    }
                    catch (JsonException ex)
                    {
                        Log.Warning(ex, "Failed to parse JSON line: {Line}", line.Substring(0, Math.Min(100, line.Length)));
                        // Continua con le altre righe invece di fallire completamente
                    }
                }

                Log.Information("Successfully parsed {Count} messages from session file", messages.Count);
                return messages;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to read session file: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// Converte un path Windows in formato escaped per la directory progetti di Claude.
        /// Esempio: C:\Sources\ClaudeGui → C--Sources-ClaudeGui
        /// </summary>
        /// <param name="path">Path originale</param>
        /// <returns>Path escaped</returns>
        private static string EscapePathForClaudeProjects(string path)
        {
            // Sostituisce i ":" e \ e / con -
            // IMPORTANTE: C:\ diventa C-- perché sia : che \ diventano -
            return path.Replace(":", "-")
                       .Replace("\\", "-")
                       .Replace("/", "-");
        }
    }
}
