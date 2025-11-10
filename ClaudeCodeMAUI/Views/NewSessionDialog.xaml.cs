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

            // Monitora i cambiamenti nei campi per abilitare/disabilitare il pulsante Create
            NameEntry.TextChanged += OnFieldChanged;
            WorkingDirectoryEntry.TextChanged += OnFieldChanged;
        }

        /// <summary>
        /// Handler per il pulsante "Sfoglia".
        /// Apre un folder picker nativo per selezionare la working directory.
        /// </summary>
        private async void OnBrowseClicked(object? sender, EventArgs e)
        {
            try
            {
                Log.Information("Browse button clicked for working directory selection");

#if WINDOWS
                // Usa il FolderPicker nativo di Windows
                var folderPicker = new Windows.Storage.Pickers.FolderPicker();

                // Ottieni la window handle per il picker
                var hwnd = ((MauiWinUIWindow)Application.Current.Windows[0].Handler.PlatformView).WindowHandle;
                WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

                folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
                folderPicker.FileTypeFilter.Add("*");

                var folder = await folderPicker.PickSingleFolderAsync();

                if (folder != null)
                {
                    WorkingDirectoryEntry.Text = folder.Path;
                    Log.Information("Working directory selected: {Path}", folder.Path);
                }
                else
                {
                    Log.Information("Folder picker cancelled by user");
                }
#else
                // Fallback per altre piattaforme: usa prompt manuale
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
#endif
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open folder picker");
                await this.DisplaySelectableAlert("Errore", $"Impossibile aprire il selettore di cartelle:\n{ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Handler per il cambio di testo nei campi Nome e Working Directory.
        /// Abilita il pulsante "Crea Sessione" solo se entrambi i campi sono compilati.
        /// </summary>
        private void OnFieldChanged(object? sender, TextChangedEventArgs e)
        {
            // Abilita il pulsante Create solo se ENTRAMBI i campi sono compilati
            CreateButton.IsEnabled = !string.IsNullOrWhiteSpace(NameEntry.Text) &&
                                     !string.IsNullOrWhiteSpace(WorkingDirectoryEntry.Text);
        }

        /// <summary>
        /// Handler per il pulsante "Crea Sessione".
        /// Valida i dati e crea una nuova SessionInfo da restituire.
        /// </summary>
        private async void OnCreateClicked(object? sender, EventArgs e)
        {
            try
            {
                var name = NameEntry.Text?.Trim();
                var workingDirectory = WorkingDirectoryEntry.Text?.Trim();

                // Validazione: nome obbligatorio
                if (string.IsNullOrWhiteSpace(name))
                {
                    await this.DisplaySelectableAlert("Errore", "Devi specificare un nome per la nuova sessione.", "OK");
                    return;
                }

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

                Log.Information("Creating new session: Name={Name}, WorkingDirectory={WorkingDirectory}", name, workingDirectory);

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
