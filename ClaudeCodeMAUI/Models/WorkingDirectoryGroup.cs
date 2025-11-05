using System;
using System.Collections.Generic;
using ClaudeCodeMAUI.Services;

namespace ClaudeCodeMAUI.Models
{
    /// <summary>
    /// Rappresenta un gruppo di sessioni raggruppate per working directory.
    /// Utilizzato per organizzare la SessionSelectorPage con tabs per working directory.
    /// </summary>
    public class WorkingDirectoryGroup
    {
        /// <summary>
        /// Path della working directory (es: "C:\Sources\MyProject")
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Data/ora dell'ultima attività tra tutte le sessioni di questa working directory.
        /// Calcolato come MAX(last_activity) delle sessioni del gruppo.
        /// Utilizzato per ordinare le tabs (più recente = più a sinistra).
        /// </summary>
        public DateTime MostRecentActivity { get; set; }

        /// <summary>
        /// Numero di sessioni in questa working directory
        /// </summary>
        public int SessionCount { get; set; }

        /// <summary>
        /// Lista di tutte le sessioni appartenenti a questa working directory
        /// </summary>
        public List<DbService.SessionDbRow> Sessions { get; set; } = new List<DbService.SessionDbRow>();

        /// <summary>
        /// Testo da visualizzare nella tab/dropdown: "C:\Sources\MyProject (5)"
        /// </summary>
        public string DisplayText => $"{WorkingDirectory} ({SessionCount})";

        /// <summary>
        /// Testo abbreviato per la tab (solo ultima parte del path + count)
        /// Es: "MyProject (5)" da "C:\Sources\MyProject"
        /// </summary>
        public string ShortDisplayText
        {
            get
            {
                try
                {
                    var lastPart = System.IO.Path.GetFileName(WorkingDirectory);
                    return string.IsNullOrWhiteSpace(lastPart)
                        ? $"{WorkingDirectory} ({SessionCount})"
                        : $"{lastPart} ({SessionCount})";
                }
                catch
                {
                    return DisplayText;
                }
            }
        }
    }
}
