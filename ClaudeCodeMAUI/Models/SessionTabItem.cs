using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClaudeCodeMAUI.Services;
using ClaudeCodeMAUI.Models.Entities;

namespace ClaudeCodeMAUI.Models
{
    /// <summary>
    /// Rappresenta un singolo tab nella TabView multi-sessione.
    /// Ogni tab corrisponde a una sessione Claude Code attiva.
    /// Combina informazioni di sessione (entity Session), metriche runtime (SessionRuntimeMetrics), e stato processo.
    /// </summary>
    public class SessionTabItem : INotifyPropertyChanged
    {
        // ===== Informazioni Sessione =====

        /// <summary>
        /// Claude session UUID (dal database e dal nome file .jsonl)
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// Nome assegnato dall'utente a questa sessione (dal database)
        /// NULL se non ancora assegnato
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Working directory utilizzata dal processo Claude per questa sessione
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;


        // ===== Runtime State =====

        /// <summary>
        /// Metriche runtime della sessione (costo, token, tool utilizzati, ecc.)
        /// NON persistite nel database - solo per la sessione corrente in memoria
        /// </summary>
        public SessionRuntimeMetrics RuntimeMetrics { get; set; } = new SessionRuntimeMetrics();

        /// <summary>
        /// Process manager che gestisce il processo Claude per questa sessione
        /// </summary>
        public ClaudeProcessManager? ProcessManager { get; set; }

        /// <summary>
        /// Riferimento al contenuto del tab (SessionTabContent) che contiene la WebView
        /// </summary>
        public Views.SessionTabContent? TabContent { get; set; }

        /// <summary>
        /// Collezione HTML renderizzata per mostrare la conversazione
        /// Ogni elemento è un blocco HTML che rappresenta un messaggio
        /// </summary>
        public ObservableCollection<string> ConversationHtml { get; set; } = new ObservableCollection<string>();

        private bool _isRunning = false;

        /// <summary>
        /// Indica se il processo Claude per questa sessione è attualmente in esecuzione.
        /// Aggiornato in tempo reale dal ProcessManager.
        /// </summary>
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusIcon)); // Aggiorna anche l'icona di stato
                }
            }
        }


        // ===== Proprietà Helper per UI =====

        /// <summary>
        /// Titolo da mostrare nel tab:
        /// - Se ha un nome assegnato: mostra il nome
        /// - Altrimenti: mostra "Session [prime 8 caratteri UUID]..."
        /// </summary>
        public string TabTitle
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Name))
                    return Name;

                if (SessionId.Length >= 8)
                    return $"Session {SessionId.Substring(0, 8)}...";

                return "New Session";
            }
        }

        /// <summary>
        /// Icona di stato da mostrare nel tab:
        /// - ▶️ se il processo è in running
        /// - ⏸️ se il processo è fermo
        /// </summary>
        public string StatusIcon => IsRunning ? "▶️" : "⏸️";


        // ===== INotifyPropertyChanged =====

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


        // ===== Metodi Helper =====

        /// <summary>
        /// Aggiorna il titolo del tab quando il nome della sessione cambia.
        /// Deve essere chiamato dopo aver modificato la proprietà Name.
        /// </summary>
        public void RefreshTabTitle()
        {
            OnPropertyChanged(nameof(TabTitle));
        }
    }
}
