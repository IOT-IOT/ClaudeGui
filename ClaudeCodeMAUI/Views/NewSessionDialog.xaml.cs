using ClaudeCodeMAUI.Extensions;
using ClaudeCodeMAUI.Models;
using Serilog;

namespace ClaudeCodeMAUI.Views
{
    /// <summary>
    /// Dialog per la creazione di una nuova sessione Claude Code.
    /// Richiede all'utente di specificare:
    /// - Nome sessione (opzionale)
    /// - Working directory (obbligatoria)
    ///
    /// Una nuova sessione viene creata SENZA --resume (conversazione da zero).
    /// </summary>
    public partial class NewSessionDialog : ContentPage
    {
        /// <summary>
        /// Indica se l'utente ha creato una nuova sessione (true) o ha cancellato (false)
        /// </summary>
        public bool WasSessionCreated { get; private set; } = false;

        /// <summary>
        /// Informazioni della sessione creata (null se l'utente ha cancellato)
        /// </summary>
        public SessionInfo? CreatedSession { get; private set; }

        public NewSessionDialog()
        {
            InitializeComponent();

            // Monitora i cambiamenti nel campo Working Directory per abilitare/disabilitare il pulsante Create
            WorkingDirectoryEntry.TextChanged += OnWorkingDirectoryChanged;
        }

        /// <summary>
        /// Handler per il pulsante "Sfoglia".
        /// Apre un folder picker per selezionare la working directory.
        /// </summary>
        private async void OnBrowseClicked(object? sender, EventArgs e)
        {
            try
            {
                Log.Information("Browse button clicked for working directory selection");

                // MAUI non ha FolderPicker nativo, usa un prompt per inserire il path manualmente
                var path = await DisplayPromptAsync(
                    "Working Directory",
                    "Inserisci il percorso completo della working directory:",
                    placeholder: @"C:\Sources\MyProject",
                    maxLength: 500
                );

                if (!string.IsNullOrWhiteSpace(path))
                {
                    WorkingDirectoryEntry.Text = path.Trim();
                    Log.Information("Working directory entered: {Path}", path);
                }
                else
                {
                    Log.Information("Folder path entry cancelled by user");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open folder picker");
                await this.DisplaySelectableAlert("Errore", $"Impossibile aprire il selettore di cartelle:\n{ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Handler per il cambio di testo nel campo Working Directory.
        /// Abilita il pulsante "Crea Sessione" solo se la working directory è specificata.
        /// </summary>
        private void OnWorkingDirectoryChanged(object? sender, TextChangedEventArgs e)
        {
            // Abilita il pulsante Create solo se la working directory è specificata
            CreateButton.IsEnabled = !string.IsNullOrWhiteSpace(WorkingDirectoryEntry.Text);
        }

        /// <summary>
        /// Handler per il pulsante "Crea Sessione".
        /// Valida i dati e crea una nuova SessionInfo da restituire.
        /// </summary>
        private async void OnCreateClicked(object? sender, EventArgs e)
        {
            try
            {
                var workingDirectory = WorkingDirectoryEntry.Text?.Trim();

                // Validazione: working directory obbligatoria
                if (string.IsNullOrWhiteSpace(workingDirectory))
                {
                    await this.DisplaySelectableAlert("Errore", "Devi selezionare una working directory per la nuova sessione.", "OK");
                    return;
                }

                // Validazione: verifica che la directory esista
                if (!Directory.Exists(workingDirectory))
                {
                    var createIt = await this.DisplaySelectableAlert(
                        "Directory non trovata",
                        $"La directory '{workingDirectory}' non esiste.\n\nVuoi crearla?",
                        "Sì, crea",
                        "No, annulla");

                    if (createIt)
                    {
                        try
                        {
                            Directory.CreateDirectory(workingDirectory);
                            Log.Information("Created working directory: {Path}", workingDirectory);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Failed to create working directory: {Path}", workingDirectory);
                            await this.DisplaySelectableAlert("Errore", $"Impossibile creare la directory:\n{ex.Message}", "OK");
                            return;
                        }
                    }
                    else
                    {
                        return; // L'utente ha annullato
                    }
                }

                var name = NameEntry.Text?.Trim();

                // Se il nome non è specificato, lascia NULL (verrà assegnato automaticamente dal sistema)
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = null;
                }

                Log.Information("Creating new session: Name={Name}, WorkingDirectory={WorkingDirectory}", name ?? "(unnamed)", workingDirectory);

                // Crea un oggetto SessionInfo per rappresentare la nuova sessione
                // NOTA: Il SessionId verrà generato dal processo Claude quando viene avviato
                CreatedSession = new SessionInfo
                {
                    SessionId = string.Empty, // Verrà popolato dal sistema quando il processo Claude viene avviato
                    Name = name,
                    WorkingDirectory = workingDirectory,
                    CreatedAt = DateTime.Now,
                    Status = "open",
                    LastActivity = DateTime.Now,
                    JsonlFilePath = string.Empty // Verrà popolato quando il processo Claude crea il file .jsonl
                };

                WasSessionCreated = true;
                await Navigation.PopModalAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to create new session");
                await this.DisplaySelectableAlert("Errore", $"Impossibile creare la sessione:\n{ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Handler per il pulsante "Annulla".
        /// Chiude il dialog senza creare alcuna sessione.
        /// </summary>
        private async void OnCancelClicked(object? sender, EventArgs e)
        {
            Log.Information("New session dialog cancelled by user");
            WasSessionCreated = false;
            CreatedSession = null;
            await Navigation.PopModalAsync();
        }
    }
}
