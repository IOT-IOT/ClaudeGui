using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ClaudeCodeMAUI.Models;
using Serilog;

namespace ClaudeCodeMAUI.Services
{
    /// <summary>
    /// Service per unire i messaggi della sessione principale con quelli degli agent sub-process
    /// in un'unica timeline cronologica ordinata per timestamp.
    /// </summary>
    public class TimelineMerger
    {
        /// <summary>
        /// Crea una timeline unificata che include sia i messaggi della main session
        /// che tutti i messaggi degli agent, ordinati cronologicamente.
        /// </summary>
        /// <param name="sessionId">ID della sessione principale</param>
        /// <param name="workingDirectory">Working directory del progetto</param>
        /// <returns>Lista di UnifiedMessage ordinati per timestamp</returns>
        public static List<UnifiedMessage> MergeTimeline(string sessionId, string workingDirectory)
        {
            var timeline = new List<UnifiedMessage>();

            try
            {
                Log.Information("Starting timeline merge for session {SessionId}", sessionId);

                // 1. Carica i messaggi della main session
                var mainSessionPath = SessionFileReader.GetSessionFilePath(sessionId, workingDirectory);
                var mainMessages = SessionFileReader.ReadSessionMessages(mainSessionPath);

                Log.Information("Loaded {Count} main session messages", mainMessages.Count);

                // Aggiungi i messaggi main alla timeline
                for (int i = 0; i < mainMessages.Count; i++)
                {
                    var message = mainMessages[i];
                    var timestamp = ExtractTimestamp(message);

                    timeline.Add(new UnifiedMessage
                    {
                        RawMessage = message,
                        Timestamp = timestamp,
                        Source = MessageSource.MainSession,
                        AgentId = null,
                        AgentName = null,
                        OriginalIndex = i,
                        SessionId = sessionId
                    });
                }

                // 2. Trova e carica tutti i file agent
                var agentFiles = AgentFileReader.FindAgentFiles(sessionId, workingDirectory);
                Log.Information("Found {Count} agent files", agentFiles.Count);

                // 3. Processa ogni file agent
                foreach (var agentFilePath in agentFiles)
                {
                    try
                    {
                        var agentId = AgentFileReader.ExtractAgentIdFromFileName(agentFilePath);
                        var agentMessages = AgentFileReader.ReadAgentMessages(agentFilePath);

                        if (agentMessages.Count == 0)
                        {
                            continue;
                        }

                        // Estrai il nome dell'agent dal primo messaggio
                        var agentName = AgentFileReader.ExtractAgentName(agentMessages[0]);

                        Log.Information("Processing agent {AgentId} ({AgentName}) with {Count} messages",
                            agentId, agentName, agentMessages.Count);

                        // Aggiungi i messaggi dell'agent alla timeline
                        for (int i = 0; i < agentMessages.Count; i++)
                        {
                            var message = agentMessages[i];
                            var timestamp = ExtractTimestamp(message);

                            timeline.Add(new UnifiedMessage
                            {
                                RawMessage = message,
                                Timestamp = timestamp,
                                Source = MessageSource.Agent,
                                AgentId = agentId,
                                AgentName = agentName,
                                OriginalIndex = i,
                                SessionId = sessionId
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to process agent file {FilePath}", agentFilePath);
                        // Continua con gli altri agent files
                    }
                }

                // 4. Logging PRIMA dell'ordinamento per debug
                Log.Information("=== Timeline BEFORE sorting ===");
                Log.Information("Total messages: {Total}", timeline.Count);
                Log.Information("First 10 messages:");
                foreach (var msg in timeline.Take(10))
                {
                    Log.Information("  [{Source}] Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss.fff}, OrigIdx: {Index}, SessionId: {SessionId}",
                        msg.Source, msg.Timestamp, msg.OriginalIndex,
                        msg.Source == MessageSource.Agent ? msg.AgentId : "main");
                }

                // 5. Ordina la timeline per timestamp
                timeline = SortByTimestamp(timeline);

                // 6. Logging DOPO l'ordinamento per debug
                Log.Information("=== Timeline AFTER sorting ===");
                Log.Information("Total messages: {Total}", timeline.Count);
                Log.Information("First 10 messages:");
                foreach (var msg in timeline.Take(10))
                {
                    Log.Information("  [{Source}] Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss.fff}, OrigIdx: {Index}, SessionId: {SessionId}",
                        msg.Source, msg.Timestamp, msg.OriginalIndex,
                        msg.Source == MessageSource.Agent ? msg.AgentId : "main");
                }

                Log.Information("Timeline merge completed: {Total} total messages ({Main} main + {Agent} agent)",
                    timeline.Count,
                    timeline.Count(m => m.Source == MessageSource.MainSession),
                    timeline.Count(m => m.Source == MessageSource.Agent));

                return timeline;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to merge timeline for session {SessionId}", sessionId);
                return timeline;
            }
        }

        /// <summary>
        /// Estrae il timestamp da un messaggio JSON.
        /// Cerca in diverse posizioni: root.timestamp, message.timestamp, createdAt.
        /// </summary>
        /// <param name="message">JsonElement del messaggio</param>
        /// <returns>DateTime del timestamp, o DateTime.MinValue se non trovato</returns>
        private static DateTime ExtractTimestamp(JsonElement message)
        {
            try
            {
                // POSIZIONE 1: Root level "timestamp"
                if (message.TryGetProperty("timestamp", out var timestampElement))
                {
                    if (TryParseTimestamp(timestampElement, out var ts))
                    {
                        Log.Debug("Timestamp from root.timestamp: {Timestamp}", ts);
                        return ts;
                    }
                }

                // POSIZIONE 2: "message.timestamp" (nested)
                if (message.TryGetProperty("message", out var messageElement))
                {
                    if (messageElement.TryGetProperty("timestamp", out var nestedTimestamp))
                    {
                        if (TryParseTimestamp(nestedTimestamp, out var ts))
                        {
                            Log.Debug("Timestamp from message.timestamp: {Timestamp}", ts);
                            return ts;
                        }
                    }
                }

                // POSIZIONE 3: "createdAt" (alternativo)
                if (message.TryGetProperty("createdAt", out var createdAtElement))
                {
                    if (TryParseTimestamp(createdAtElement, out var ts))
                    {
                        Log.Debug("Timestamp from createdAt: {Timestamp}", ts);
                        return ts;
                    }
                }

                // POSIZIONE 4: "created_at" (snake_case alternativo)
                if (message.TryGetProperty("created_at", out var createdAtSnakeElement))
                {
                    if (TryParseTimestamp(createdAtSnakeElement, out var ts))
                    {
                        Log.Debug("Timestamp from created_at: {Timestamp}", ts);
                        return ts;
                    }
                }

                // Se non c'è timestamp, usa DateTime.MinValue
                Log.Warning("No valid timestamp found in message");
                return DateTime.MinValue;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to extract timestamp from message");
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// Helper per il parsing di un timestamp da JsonElement.
        /// Supporta diversi formati di data/ora.
        /// </summary>
        /// <param name="element">JsonElement contenente il timestamp</param>
        /// <param name="timestamp">DateTime estratto se ha successo</param>
        /// <returns>True se il parsing ha avuto successo</returns>
        private static bool TryParseTimestamp(JsonElement element, out DateTime timestamp)
        {
            timestamp = DateTime.MinValue;

            var timestampString = element.GetString();
            if (string.IsNullOrWhiteSpace(timestampString))
            {
                return false;
            }

            // Prova parsing con InvariantCulture e RoundtripKind
            // Questo supporta ISO 8601 e altri formati standard
            return DateTime.TryParse(timestampString,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out timestamp);
        }

        /// <summary>
        /// Ordina la timeline per timestamp crescente.
        /// Messaggi con lo stesso timestamp mantengono l'ordine relativo.
        /// </summary>
        /// <param name="messages">Lista di UnifiedMessage da ordinare</param>
        /// <returns>Lista ordinata per timestamp</returns>
        private static List<UnifiedMessage> SortByTimestamp(List<UnifiedMessage> messages)
        {
            // OrderBy è stabile, quindi messaggi con stesso timestamp mantengono l'ordine
            return messages.OrderBy(m => m.Timestamp).ToList();
        }

        /// <summary>
        /// Filtra la timeline per mostrare solo messaggi della main session.
        /// </summary>
        /// <param name="timeline">Timeline completa</param>
        /// <returns>Timeline filtrata con solo messaggi main</returns>
        public static List<UnifiedMessage> FilterMainOnly(List<UnifiedMessage> timeline)
        {
            return timeline.Where(m => m.Source == MessageSource.MainSession).ToList();
        }

        /// <summary>
        /// Filtra la timeline per mostrare solo messaggi degli agent.
        /// </summary>
        /// <param name="timeline">Timeline completa</param>
        /// <returns>Timeline filtrata con solo messaggi agent</returns>
        public static List<UnifiedMessage> FilterAgentsOnly(List<UnifiedMessage> timeline)
        {
            return timeline.Where(m => m.Source == MessageSource.Agent).ToList();
        }

        /// <summary>
        /// Filtra la timeline per mostrare solo messaggi di un agent specifico.
        /// </summary>
        /// <param name="timeline">Timeline completa</param>
        /// <param name="agentId">ID dell'agent da filtrare</param>
        /// <returns>Timeline filtrata con solo messaggi dell'agent specificato</returns>
        public static List<UnifiedMessage> FilterByAgent(List<UnifiedMessage> timeline, string agentId)
        {
            return timeline.Where(m => m.Source == MessageSource.Agent && m.AgentId == agentId).ToList();
        }

        /// <summary>
        /// Crea timeline unificata di TUTTE le sessioni nella working directory.
        /// Ordina per timestamp e inserisce gli agent subito dopo il tool_use parent.
        /// </summary>
        /// <param name="workingDirectory">Working directory del progetto</param>
        /// <returns>Lista di UnifiedMessage ordinati cronologicamente con agent inseriti gerarchicamente</returns>
        public static List<UnifiedMessage> MergeAllSessions(string workingDirectory)
        {
            var timeline = new List<UnifiedMessage>();

            try
            {
                Log.Information("Merging all sessions for working directory: {WorkingDir}", workingDirectory);

                // 1. Trova tutte le sessioni
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var escapedPath = EscapePathForClaudeProjects(workingDirectory);
                var projectDir = System.IO.Path.Combine(userProfile, ".claude", "projects", escapedPath);

                if (!System.IO.Directory.Exists(projectDir))
                {
                    Log.Warning("Project directory not found: {ProjectDir}", projectDir);
                    return timeline;
                }

                var sessionFiles = System.IO.Directory.GetFiles(projectDir, "*.jsonl")
                    .Where(f => !System.IO.Path.GetFileName(f).StartsWith("agent-"))
                    .ToList();

                Log.Information("Found {Count} session files", sessionFiles.Count);

                // 2. Per ogni sessione, carica main + agents
                foreach (var sessionFile in sessionFiles)
                {
                    var sessionId = System.IO.Path.GetFileNameWithoutExtension(sessionFile);
                    Log.Information("Loading session {SessionId}", sessionId);

                    var sessionTimeline = MergeTimeline(sessionId, workingDirectory);
                    timeline.AddRange(sessionTimeline);
                }

                // 3. Ordina tutto per timestamp
                Log.Information("Sorting {Count} messages by timestamp", timeline.Count);
                timeline = timeline.OrderBy(m => m.Timestamp)
                                  .ThenBy(m => m.Source) // Main prima di Agent a parità timestamp
                                  .ToList();

                // 4. Riorganizza inserendo gli agent dopo i loro tool_use parent
                Log.Information("Building hierarchical timeline");
                timeline = BuildHierarchicalTimeline(timeline);

                Log.Information("Merged {Count} messages from {Sessions} sessions",
                    timeline.Count, sessionFiles.Count);

                return timeline;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to merge all sessions");
                return timeline;
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
            return path.Replace(":", "-")
                       .Replace("\\", "-")
                       .Replace("/", "-");
        }

        /// <summary>
        /// Riorganizza la timeline inserendo gli agent subito dopo i tool_use che li invocano.
        /// Mantiene l'ordine cronologico ma raggruppa agent con i loro messaggi parent.
        /// </summary>
        /// <param name="sortedTimeline">Timeline già ordinata per timestamp</param>
        /// <returns>Timeline gerarchica con agent inseriti dopo i tool_use</returns>
        private static List<UnifiedMessage> BuildHierarchicalTimeline(List<UnifiedMessage> sortedTimeline)
        {
            try
            {
                // 1. Separa main e agent messages
                var mainMessages = sortedTimeline
                    .Where(m => m.Source == MessageSource.MainSession)
                    .ToList();

                var agentsByAgentId = sortedTimeline
                    .Where(m => m.Source == MessageSource.Agent)
                    .GroupBy(m => m.AgentId)
                    .ToDictionary(g => g.Key!, g => g.OrderBy(m => m.Timestamp).ToList());

                var hierarchical = new List<UnifiedMessage>();
                var insertedAgents = new HashSet<string>();

                // 2. Per ogni messaggio main
                foreach (var mainMsg in mainMessages)
                {
                    hierarchical.Add(mainMsg);

                    // 3. Cerca se questo messaggio invoca un agent
                    var agentId = ExtractAgentIdFromMessage(mainMsg);

                    if (!string.IsNullOrEmpty(agentId) &&
                        agentsByAgentId.ContainsKey(agentId) &&
                        !insertedAgents.Contains(agentId))
                    {
                        // 4. Inserisci tutti i messaggi di questo agent subito dopo
                        var agentMessages = agentsByAgentId[agentId];
                        hierarchical.AddRange(agentMessages);
                        insertedAgents.Add(agentId);

                        Log.Debug("Inserted {Count} agent messages after tool_use for agent {AgentId}",
                            agentMessages.Count, agentId);
                    }
                }

                // 5. Aggiungi eventuali agent orfani alla fine (non dovrebbe accadere normalmente)
                foreach (var kvp in agentsByAgentId)
                {
                    if (!insertedAgents.Contains(kvp.Key))
                    {
                        Log.Warning("Found {Count} orphan agent messages for agent {AgentId}",
                            kvp.Value.Count, kvp.Key);
                        hierarchical.AddRange(kvp.Value);
                    }
                }

                return hierarchical;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to build hierarchical timeline, returning flat timeline");
                return sortedTimeline;
            }
        }

        /// <summary>
        /// Estrae l'agent ID da un messaggio che potrebbe contenere riferimenti ad agent.
        /// Cerca nel messaggio stesso o nei metadati.
        /// </summary>
        /// <param name="unifiedMessage">Messaggio da cui estrarre l'agent ID</param>
        /// <returns>Agent ID se trovato, null altrimenti</returns>
        private static string? ExtractAgentIdFromMessage(UnifiedMessage unifiedMessage)
        {
            try
            {
                var message = unifiedMessage.RawMessage;

                // Strategia 1: Cerca direttamente il campo agentId
                if (message.TryGetProperty("agentId", out var agentIdElement))
                {
                    var agentId = agentIdElement.GetString();
                    if (!string.IsNullOrWhiteSpace(agentId))
                    {
                        Log.Debug("Found agentId in message: {AgentId}", agentId);
                        return agentId;
                    }
                }

                // Strategia 2: Cerca nel message.content per tool_use Task
                if (!message.TryGetProperty("message", out var messageObj))
                    return null;

                if (!messageObj.TryGetProperty("content", out var content))
                    return null;

                if (content.ValueKind != JsonValueKind.Array)
                    return null;

                // Cerca tool_use con name="Task"
                foreach (var item in content.EnumerateArray())
                {
                    if (!item.TryGetProperty("type", out var typeElement))
                        continue;

                    if (typeElement.GetString() != "tool_use")
                        continue;

                    if (!item.TryGetProperty("name", out var nameElement))
                        continue;

                    if (nameElement.GetString() != "Task")
                        continue;

                    // Trovato tool_use Task - l'agent verrà creato subito dopo questo messaggio
                    // Non possiamo sapere l'agent ID finché non viene creato, quindi torniamo null
                    Log.Debug("Found Task tool_use but agent ID not yet available in this message");
                    return null;
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to extract agent ID from message");
                return null;
            }
        }
    }
}
