using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Serilog;

namespace ClaudeCodeMAUI.Services
{
    /// <summary>
    /// Service per leggere e gestire i file JSONL degli agent sub-process di Claude Code.
    /// Gli agent files hanno il formato: agent-{shortId}.jsonl
    /// Es: agent-066fa22f.jsonl
    /// </summary>
    public class AgentFileReader
    {
        /// <summary>
        /// Trova tutti i file agent relativi a una sessione specifica.
        /// I file agent sono nella stessa directory della main session e iniziano con "agent-".
        /// </summary>
        /// <param name="sessionId">ID della sessione principale</param>
        /// <param name="workingDirectory">Working directory del progetto</param>
        /// <returns>Lista di path completi dei file agent trovati</returns>
        public static List<string> FindAgentFiles(string sessionId, string workingDirectory)
        {
            try
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var escapedPath = EscapePathForClaudeProjects(workingDirectory);
                var projectDir = Path.Combine(userProfile, ".claude", "projects", escapedPath);

                if (!Directory.Exists(projectDir))
                {
                    Log.Warning("Project directory not found: {ProjectDir}", projectDir);
                    return new List<string>();
                }

                // Trova tutti i file che iniziano con "agent-" e finiscono con ".jsonl"
                var agentFiles = Directory.GetFiles(projectDir, "agent-*.jsonl")
                    .Where(file => IsAgentFileForSession(file, sessionId))
                    .ToList();

                Log.Information("Found {Count} agent files for session {SessionId}", agentFiles.Count, sessionId);
                return agentFiles;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to find agent files for session {SessionId}", sessionId);
                return new List<string>();
            }
        }

        /// <summary>
        /// Verifica se un file agent appartiene alla sessione specificata.
        /// Legge il primo messaggio del file e controlla il campo "sessionId".
        /// </summary>
        /// <param name="agentFilePath">Path del file agent</param>
        /// <param name="sessionId">Session ID da verificare</param>
        /// <returns>True se il file appartiene alla sessione</returns>
        private static bool IsAgentFileForSession(string agentFilePath, string sessionId)
        {
            try
            {
                // Leggi solo la prima riga per verificare il sessionId
                var firstLine = File.ReadLines(agentFilePath, System.Text.Encoding.UTF8).FirstOrDefault();

                if (string.IsNullOrWhiteSpace(firstLine))
                {
                    return false;
                }

                var jsonElement = JsonSerializer.Deserialize<JsonElement>(firstLine);

                if (jsonElement.TryGetProperty("sessionId", out var sessionIdElement))
                {
                    var fileSessionId = sessionIdElement.GetString();
                    return fileSessionId == sessionId;
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to check agent file {FilePath}", agentFilePath);
                return false;
            }
        }

        /// <summary>
        /// Legge tutti i messaggi da un singolo file agent.
        /// Simile a SessionFileReader.ReadSessionMessages ma per i file agent.
        /// </summary>
        /// <param name="agentFilePath">Path completo del file agent</param>
        /// <returns>Lista di messaggi come JsonElement</returns>
        public static List<JsonElement> ReadAgentMessages(string agentFilePath)
        {
            var messages = new List<JsonElement>();

            try
            {
                if (!File.Exists(agentFilePath))
                {
                    Log.Warning("Agent file not found: {FilePath}", agentFilePath);
                    return messages;
                }

                Log.Information("Reading agent file: {FilePath}", agentFilePath);

                // Leggi il file con encoding UTF-8
                var lines = File.ReadAllLines(agentFilePath, System.Text.Encoding.UTF8);
                Log.Information("Found {Count} lines in agent file", lines.Length);

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        var jsonElement = JsonSerializer.Deserialize<JsonElement>(line);
                        messages.Add(jsonElement);
                    }
                    catch (JsonException ex)
                    {
                        Log.Warning(ex, "Failed to parse JSON line in agent file: {Line}",
                            line.Substring(0, Math.Min(100, line.Length)));
                    }
                }

                Log.Information("Successfully parsed {Count} messages from agent file", messages.Count);
                return messages;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to read agent file: {FilePath}", agentFilePath);
                return messages;
            }
        }

        /// <summary>
        /// Estrae l'agent ID dal nome del file.
        /// Es: "agent-066fa22f.jsonl" → "066fa22f"
        /// </summary>
        /// <param name="agentFilePath">Path completo del file agent</param>
        /// <returns>Agent ID (short UUID) o null se il formato è invalido</returns>
        public static string? ExtractAgentIdFromFileName(string agentFilePath)
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(agentFilePath);

                // Formato atteso: agent-{agentId}
                if (fileName.StartsWith("agent-"))
                {
                    return fileName.Substring("agent-".Length);
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to extract agent ID from file path: {FilePath}", agentFilePath);
                return null;
            }
        }

        /// <summary>
        /// Estrae il nome/tipo dell'agent dal JSON del messaggio.
        /// Cerca nel campo "prompt" o "description" per identificare il tipo di agent.
        /// Es: "Explore", "Plan", "general-purpose"
        /// </summary>
        /// <param name="agentMessage">JsonElement rappresentante un messaggio dell'agent</param>
        /// <returns>Nome dell'agent o "Unknown" se non trovato</returns>
        public static string ExtractAgentName(JsonElement agentMessage)
        {
            try
            {
                // Prova a estrarre da vari campi possibili

                // 1. Campo "agentType" (se esiste)
                if (agentMessage.TryGetProperty("agentType", out var agentTypeElement))
                {
                    var agentType = agentTypeElement.GetString();
                    if (!string.IsNullOrWhiteSpace(agentType))
                    {
                        return agentType;
                    }
                }

                // 2. Campo "subagent_type" nell'input dei tool calls
                if (agentMessage.TryGetProperty("message", out var message))
                {
                    if (message.TryGetProperty("content", out var content) &&
                        content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in content.EnumerateArray())
                        {
                            if (item.TryGetProperty("type", out var typeElement) &&
                                typeElement.GetString() == "tool_use" &&
                                item.TryGetProperty("name", out var nameElement) &&
                                nameElement.GetString() == "Task")
                            {
                                if (item.TryGetProperty("input", out var input) &&
                                    input.TryGetProperty("subagent_type", out var subagentType))
                                {
                                    var type = subagentType.GetString();
                                    if (!string.IsNullOrWhiteSpace(type))
                                    {
                                        return type;
                                    }
                                }
                            }
                        }
                    }
                }

                // 3. Fallback: "Agent" generico
                return "Agent";
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to extract agent name from message");
                return "Unknown";
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
    }
}
