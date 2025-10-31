using System;

namespace ClaudeCodeMAUI.Models
{
    /// <summary>
    /// Rappresenta le informazioni sull'utilizzo del contesto di una sessione Claude.
    /// Contiene metriche dettagliate sulla distribuzione dei token nel contesto.
    /// </summary>
    public class ContextInfo
    {
        /// <summary>
        /// Nome del modello Claude utilizzato (es. claude-sonnet-4-5-20250929)
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Token totali utilizzati nel contesto
        /// </summary>
        public int UsedTokens { get; set; }

        /// <summary>
        /// Token totali disponibili (budget massimo)
        /// </summary>
        public int TotalTokens { get; set; }

        /// <summary>
        /// Percentuale di utilizzo del contesto (0-100)
        /// </summary>
        public double UsagePercentage { get; set; }

        /// <summary>
        /// Token utilizzati dal system prompt
        /// </summary>
        public int SystemPromptTokens { get; set; }

        /// <summary>
        /// Percentuale del system prompt sul totale (0-100)
        /// </summary>
        public double SystemPromptPercentage { get; set; }

        /// <summary>
        /// Token utilizzati dai system tools
        /// </summary>
        public int SystemToolsTokens { get; set; }

        /// <summary>
        /// Percentuale dei system tools sul totale (0-100)
        /// </summary>
        public double SystemToolsPercentage { get; set; }

        /// <summary>
        /// Token utilizzati dai memory files
        /// </summary>
        public int MemoryFilesTokens { get; set; }

        /// <summary>
        /// Percentuale dei memory files sul totale (0-100)
        /// </summary>
        public double MemoryFilesPercentage { get; set; }

        /// <summary>
        /// Token utilizzati dai messaggi della conversazione
        /// </summary>
        public int MessagesTokens { get; set; }

        /// <summary>
        /// Percentuale dei messaggi sul totale (0-100)
        /// </summary>
        public double MessagesPercentage { get; set; }

        /// <summary>
        /// Token liberi disponibili
        /// </summary>
        public int FreeSpaceTokens { get; set; }

        /// <summary>
        /// Percentuale dello spazio libero sul totale (0-100)
        /// </summary>
        public double FreeSpacePercentage { get; set; }

        /// <summary>
        /// Token riservati per il buffer di autocompact
        /// </summary>
        public int AutocompactBufferTokens { get; set; }

        /// <summary>
        /// Percentuale del buffer di autocompact sul totale (0-100)
        /// </summary>
        public double AutocompactBufferPercentage { get; set; }

        /// <summary>
        /// Output raw completo del comando /context (per debug e copia)
        /// </summary>
        public string RawOutput { get; set; } = string.Empty;
    }
}
