using System;
using System.Collections.Generic;

namespace ClaudeCodeMAUI.Models
{
    /// <summary>
    /// Contiene metriche runtime di una sessione Claude Code che non vengono persistite nel database.
    /// Questi dati vengono raccolti durante l'esecuzione e persi alla chiusura dell'applicazione.
    /// </summary>
    public class SessionRuntimeMetrics
    {
        /// <summary>
        /// Costo totale in USD accumulato durante questa sessione
        /// </summary>
        public decimal TotalCostUsd { get; set; } = 0;

        /// <summary>
        /// Token totali di input utilizzati
        /// </summary>
        public int TotalInputTokens { get; set; } = 0;

        /// <summary>
        /// Token totali di output generati
        /// </summary>
        public int TotalOutputTokens { get; set; } = 0;

        /// <summary>
        /// Token totali letti dalla cache
        /// </summary>
        public int TotalCacheReadTokens { get; set; } = 0;

        /// <summary>
        /// Token totali per creazione cache
        /// </summary>
        public int TotalCacheCreationTokens { get; set; } = 0;

        /// <summary>
        /// Numero di turni di conversazione (incrementato da Claude)
        /// </summary>
        public int NumTurns { get; set; } = 0;

        /// <summary>
        /// Lista di tool utilizzati durante la sessione (Bash, Read, Edit, ecc.)
        /// Key: nome del tool, Value: conteggio utilizzi
        /// </summary>
        public Dictionary<string, int> ToolsUsed { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Modello corrente in uso (es. claude-sonnet-4-5-20250929)
        /// </summary>
        public string CurrentModel { get; set; } = string.Empty;

        /// <summary>
        /// Durata totale in millisecondi (somma di tutte le risposte)
        /// </summary>
        public long TotalDurationMs { get; set; } = 0;

        /// <summary>
        /// Durata dell'ultima risposta in millisecondi
        /// </summary>
        public long LastDurationMs { get; set; } = 0;

        /// <summary>
        /// Aggiorna le metriche runtime da un messaggio "result" di Claude
        /// </summary>
        /// <param name="costUsd">Costo della richiesta in USD</param>
        /// <param name="inputTokens">Token di input utilizzati</param>
        /// <param name="outputTokens">Token di output generati</param>
        /// <param name="cacheReadTokens">Token letti dalla cache</param>
        /// <param name="cacheCreationTokens">Token per creazione cache</param>
        /// <param name="numTurns">Numero totale di turni</param>
        /// <param name="durationMs">Durata della richiesta in millisecondi</param>
        /// <param name="model">Modello utilizzato</param>
        public void UpdateFromResult(decimal costUsd, int inputTokens, int outputTokens,
                                      int cacheReadTokens, int cacheCreationTokens,
                                      int numTurns, long durationMs, string model)
        {
            TotalCostUsd += costUsd;
            TotalInputTokens += inputTokens;
            TotalOutputTokens += outputTokens;
            TotalCacheReadTokens += cacheReadTokens;
            TotalCacheCreationTokens += cacheCreationTokens;
            NumTurns = numTurns;
            TotalDurationMs += durationMs;
            LastDurationMs = durationMs;
            CurrentModel = model;
        }

        /// <summary>
        /// Registra l'utilizzo di un tool
        /// </summary>
        /// <param name="toolName">Nome del tool utilizzato</param>
        public void RecordToolUsage(string toolName)
        {
            if (ToolsUsed.ContainsKey(toolName))
            {
                ToolsUsed[toolName]++;
            }
            else
            {
                ToolsUsed[toolName] = 1;
            }
        }

        /// <summary>
        /// Restituisce una stringa formattata con i tool utilizzati.
        /// Esempio: "Bash (3x), Read (5x), Edit (2x)"
        /// </summary>
        /// <returns>Stringa con il riepilogo dei tool, oppure "None" se nessun tool è stato utilizzato</returns>
        public string GetToolsSummary()
        {
            if (ToolsUsed.Count == 0)
                return "None";

            var tools = new List<string>();
            foreach (var kvp in ToolsUsed)
            {
                tools.Add($"{kvp.Key} ({kvp.Value}x)");
            }
            return string.Join(", ", tools);
        }
    }
}
