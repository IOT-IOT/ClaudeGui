using ClaudeCodeMAUI.Extensions;
using ClaudeCodeMAUI.Models;
using ClaudeCodeMAUI.Models.Entities;
using Serilog;

namespace ClaudeCodeMAUI.Views
{
    /// <summary>
    /// Dialog per la creazione di una nuova sessione Claude Code.
    /// Richiede all'utente di specificare:
    /// - Nome sessione (obbligatorio)
    /// - Working directory (obbligatoria)
    ///
    /// Una nuova sessione viene creata SENZA --resume (conversazione da zero).
    /// </summary>
    public partial class NewSessionDialog : ContentPage
    {
        private readonly TaskCompletionSource<bool> _completionSource = new TaskCompletionSource<bool>();
        private readonly Action<Session>? _onSessionCreated;

        /// <summary>
        /// Task che completa quando il dialog viene chiuso (con o senza creazione sessione)
        /// </summary>
        public Task<bool> CompletionTask => _completionSource.Task;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="onSessionCreated">Callback invocato quando viene creata una nuova sessione</param>
        public NewSessionDialog(Action<Session>? onSessionCreated = null)
        {
            InitializeComponent();
            _onSessionCreated = onSessionCreated;

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



                Log.Information("Creating new session: Name={Name}, WorkingDirectory={WorkingDirectory}", name, workingDirectory);

                // Crea un oggetto Session per rappresentare la nuova sessione
                // NOTA: Il SessionId verrà generato dal processo Claude quando viene avviato
                var newSession = new Session
                {
                    SessionId = string.Empty, // Verrà popolato dal sistema quando il processo Claude viene avviato
                    Name = name,
                    WorkingDirectory = workingDirectory,
                    CreatedAt = DateTime.Now,
                    Status = "open",
                    LastActivity = DateTime.Now,
                    Processed = false,
                    Excluded = false
                };

                // Notifica al chiamante che la sessione è stata creata
                _onSessionCreated?.Invoke(newSession);
                await Navigation.PopModalAsync();
                _completionSource.TrySetResult(true);
                
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
            _completionSource.TrySetResult(false);
            await Navigation.PopModalAsync();
        }
    }
}
