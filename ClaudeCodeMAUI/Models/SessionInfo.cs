using System;

namespace ClaudeCodeMAUI.Models
{
    /// <summary>
    /// Rappresenta le informazioni di una sessione Claude Code.
    /// Combina dati dal filesystem (.jsonl files) e dal database.
    /// Utilizzato per popolare la lista sessioni e per aprire sessioni in tab.
    /// </summary>
    public class SessionInfo
    {
        /// <summary>
        /// Claude session UUID (dal nome file .jsonl o dal DB)
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// Nome assegnato dall'utente alla sessione (dal DB)
        /// NULL se non ancora assegnato
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Working directory della sessione (dal primo messaggio nel .jsonl o dal DB)
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Data/ora di creazione della sessione (dal primo messaggio nel .jsonl)
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Status della sessione dal DB: 'open' o 'closed'
        /// </summary>
        public string Status { get; set; } = "open";

        /// <summary>
        /// Ultima attività registrata (dal DB)
        /// </summary>
        public DateTime LastActivity { get; set; }

        /// <summary>
        /// Path completo al file .jsonl di questa sessione
        /// </summary>
        public string JsonlFilePath { get; set; } = string.Empty;

        /// <summary>
        /// Indica se la sessione ha un nome assegnato
        /// </summary>
        public bool HasName => !string.IsNullOrWhiteSpace(Name);

        /// <summary>
        /// Nome da visualizzare: il nome assegnato o un placeholder
        /// </summary>
        public string DisplayName => HasName ? Name! : $"(Session {SessionId.Substring(0, 8)}...)";

        /// <summary>
        /// Data formattata per display nella UI
        /// </summary>
        public string FormattedCreatedAt => CreatedAt.ToString("yyyy-MM-dd HH:mm");

        /// <summary>
        /// Icona da mostrare nella lista (➕ per placeholder, ✅ se ha nome, ❌ se manca)
        /// </summary>
        public string Icon
        {
            get
            {
                if (IsNewSessionPlaceholder) return "➕";
                return HasName ? "✅" : "❌";
            }
        }

        /// <summary>
        /// Indica se questa è una "nuova sessione" (usato nella UI)
        /// </summary>
        public bool IsNewSessionPlaceholder { get; set; } = false;
    }
}
