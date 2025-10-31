using System;

namespace ClaudeCodeMAUI.Models
{
    /// <summary>
    /// Rappresenta un singolo messaggio nella conversazione.
    /// Può essere un messaggio dell'utente ("user") o dell'assistant ("assistant").
    /// Viene salvato nel database per permettere la visualizzazione della storia
    /// quando si riprende una sessione.
    /// </summary>
    public class ConversationMessage
    {
        /// <summary>
        /// ID univoco del messaggio (auto-incrementato dal database).
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// ID della conversazione a cui appartiene questo messaggio.
        /// Foreign key verso conversations.session_id.
        /// </summary>
        public string ConversationId { get; set; } = string.Empty;

        /// <summary>
        /// Ruolo del mittente: "user" o "assistant".
        /// </summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>
        /// Contenuto testuale del messaggio (può includere markdown).
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp del messaggio in formato ISO 8601 (UTC).
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Numero di sequenza del messaggio nella conversazione (1, 2, 3...).
        /// Usato per ordinare i messaggi cronologicamente.
        /// </summary>
        public int Sequence { get; set; }
    }
}
