using System;

namespace ClaudeGui.Blazor.Services
{
    /// <summary>
    /// Configurazione condivisa dell'applicazione.
    /// Contiene impostazioni globali utilizzate da più componenti.
    /// </summary>
    public static class AppConfig
    {
        /// <summary>
        /// Working directory utilizzata per i processi Claude.
        /// Questa directory viene impostata come WorkingDirectory per il processo Claude
        /// e viene utilizzata quando si lancia un terminale esterno.
        /// </summary>
        public static string ClaudeWorkingDirectory { get; set; } = @"C:\Sources\ClaudeGui";
    }
}
