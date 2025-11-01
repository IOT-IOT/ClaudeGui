using System;
using System.Text.Json;

namespace ClaudeCodeMAUI.Models
{
    /// <summary>
    /// Rappresenta un messaggio unificato che può provenire sia dalla sessione principale
    /// che da un agent sub-process. Utilizzato per creare una timeline cronologica completa.
    /// </summary>
    public class UnifiedMessage
    {
        /// <summary>
        /// Il messaggio JSON raw completo come letto dal file JSONL.
        /// </summary>
        public JsonElement RawMessage { get; set; }

        /// <summary>
        /// Timestamp del messaggio, estratto dal campo "timestamp" del JSON.
        /// Utilizzato per l'ordinamento cronologico nella timeline.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Indica se il messaggio proviene dalla sessione principale o da un agent.
        /// </summary>
        public MessageSource Source { get; set; }

        /// <summary>
        /// ID dell'agent (es: "066fa22f"). Null se il messaggio proviene dalla main session.
        /// </summary>
        public string? AgentId { get; set; }

        /// <summary>
        /// Nome/tipo dell'agent (es: "Explore", "Plan", "general-purpose").
        /// Null se il messaggio proviene dalla main session.
        /// </summary>
        public string? AgentName { get; set; }

        /// <summary>
        /// Indice del messaggio nel file originale (0-based).
        /// Utile per il debug e per mostrare la posizione originale.
        /// </summary>
        public int OriginalIndex { get; set; }

        /// <summary>
        /// Session ID a cui appartiene questo messaggio.
        /// Per i messaggi main è il session ID principale,
        /// per gli agent è il session ID del parent.
        /// </summary>
        public string SessionId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Enum che identifica la provenienza di un messaggio nella timeline.
    /// </summary>
    public enum MessageSource
    {
        /// <summary>
        /// Messaggio dalla sessione principale (file {sessionId}.jsonl)
        /// </summary>
        MainSession,

        /// <summary>
        /// Messaggio da un agent sub-process (file agent-{agentId}.jsonl)
        /// </summary>
        Agent
    }
}
