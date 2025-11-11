using ClaudeCodeMAUI.Extensions;
using ClaudeCodeMAUI.Models;
using ClaudeCodeMAUI.Models.Entities;
using ClaudeCodeMAUI.Services;
using Serilog;
using System.Collections.ObjectModel;

namespace ClaudeCodeMAUI.Views
{
    /// <summary>
    /// Pagina per la selezione di una sessione esistente.
    /// Mostra tutte le sessioni disponibili, con opzione per:
    /// - Selezionare una sessione esistente
    /// - Creare una nuova sessione (prima opzione nella lista)
    /// - Assegnare un nome a sessioni senza nome
    /// </summary>
    public partial class SessionSelectorPage : ContentPage
    {
        private readonly SessionScannerService _sessionScanner;
        private readonly DbService _dbService;

        // Collections per il binding
        public ObservableCollection<WorkingDirectoryGroup> AllWorkingDirectories { get; set; } = new ObservableCollection<WorkingDirectoryGroup>();
        public ObservableCollection<WorkingDirectoryGroup> TopWorkingDirectories { get; set; } = new ObservableCollection<WorkingDirectoryGroup>();
        private ObservableCollection<SessionDisplayItem> _currentSessions = new ObservableCollection<SessionDisplayItem>();

        private WorkingDirectoryGroup? _selectedWorkingDir;
        public WorkingDirectoryGroup? SelectedWorkingDir
        {
            get => _selectedWorkingDir;
            set
            {
                _selectedWorkingDir = value;
                UpdateCurrentSessions();
            }
        }

        /// <summary>
        /// Sessione selezionata dall'utente (entity Session)
        /// </summary>
        public Session? SelectedSession { get; private set; }

        public bool isNewSession  { get; private set; }

        /// <summary>
        /// TaskCompletionSource per gestire l'attesa della selezione
        /// </summary>
        private TaskCompletionSource<Session?> _selectionCompletionSource = new TaskCompletionSource<Session?>();

        /// <summary>
        /// Task che completa quando l'utente seleziona una sessione o annulla
        /// </summary>
        public Task<Session?> SelectionTask => _selectionCompletionSource.Task;

        /// <summary>
        /// Classe helper per visualizzare sessioni nella lista
        /// </summary>
        public class SessionDisplayItem
        {
            public string SessionId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string WorkingDirectory { get; set; } = string.Empty;
            public string FormattedDate { get; set; } = string.Empty;
            public string FormattedFileSize { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public bool IsNewSessionPlaceholder { get; set; } = false;
            public Session? SessionData { get; set; }
        }

        public SessionSelectorPage(SessionScannerService sessionScanner, DbService dbService)
        {
            InitializeComponent();
            _sessionScanner = sessionScanner;
            _dbService = dbService;

            // Set BindingContext per data binding
            BindingContext = this;

            // Bind CollectionView alla lista sessioni correnti
            SessionsCollectionView.ItemsSource = _currentSessions;

            // Carica le sessioni all'apertura della pagina
            _ = LoadSessionsAsync();
        }

        /// <summary>
        /// Carica tutte le sessioni closed dal database e le raggruppa per working directory.
        /// Ordina i gruppi per ultima attività decrescente.
        /// </summary>
        private async Task LoadSessionsAsync()
        {
            try
            {
                Log.Information("SessionSelectorPage.LoadSessionsAsync() started");

                if (_sessionScanner == null)
                {
                    Log.Error("SessionScanner is null!");
                    await this.DisplaySelectableAlert("Error", "SessionScanner is null - cannot load sessions", "OK");
                    return;
                }

                // Carica tutte le sessioni CLOSED dal database
                Log.Information("Loading closed sessions from database...");
                var closedSessions = await _sessionScanner.GetClosedSessionsAsync();
                Log.Information("Found {Count} closed sessions", closedSessions?.Count ?? 0);

                if (closedSessions == null || closedSessions.Count == 0)
                {
                    Log.Warning("No closed sessions found");
                    return;
                }

                // Raggruppa per working directory
                var grouped = closedSessions
                    .GroupBy(s => s.WorkingDirectory)
                    .Select(g => new WorkingDirectoryGroup
                    {
                        WorkingDirectory = g.Key,
                        Sessions = g.ToList(),
                        SessionCount = g.Count(),
                        MostRecentActivity = g.Max(s => s.LastActivity)
                    })
                    .OrderByDescending(g => g.MostRecentActivity)  // Ordina per ultima attività decrescente
                    .ToList();

                Log.Information("Grouped into {Count} working directories", grouped.Count);

                // Popola AllWorkingDirectories (per dropdown)
                AllWorkingDirectories.Clear();
                foreach (var group in grouped)
                {
                    AllWorkingDirectories.Add(group);
                }

                // Popola TopWorkingDirectories (top 10 per carousel)
                TopWorkingDirectories.Clear();
                foreach (var group in grouped.Take(10))
                {
                    TopWorkingDirectories.Add(group);
                }

                // Seleziona automaticamente il primo gruppo (più recente)
                if (AllWorkingDirectories.Count > 0)
                {
                    SelectedWorkingDir = AllWorkingDirectories[0];
                    WorkingDirectoryPicker.SelectedItem = SelectedWorkingDir;
                }

                // Forza l'aggiornamento della vista corrente (anche se SelectedWorkingDir non è cambiato)
                UpdateCurrentSessions();

                Log.Information("Loaded {AllCount} working directories, showing top {TopCount} in carousel",
                    AllWorkingDirectories.Count, TopWorkingDirectories.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load sessions");
                await this.DisplaySelectableAlert("Errore", $"Impossibile caricare le sessioni:\n{ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Codifica una working directory nel formato Claude Code.
        /// Es: "C:\Sources\MyProject" → "C--Sources-MyProject"
        /// Regola inversa di DecodeWorkingDirectory in SessionScannerService
        /// </summary>
        private string EncodeWorkingDirectory(string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
                return string.Empty;

            // Rimuovi ":\\" dopo la lettera del drive e sostituisci con "--"
            var result = workingDirectory.Replace(":\\", "--");

            // Sostituisci tutti i "\" con "-"
            result = result.Replace("\\", "-");

            return result;
        }

        /// <summary>
        /// Ottiene il path completo del file .jsonl di una sessione
        /// </summary>
        private string GetSessionFilePath(string sessionId, string workingDirectory)
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var claudeProjectsPath = Path.Combine(userProfile, ".claude", "projects");
            var encodedDir = EncodeWorkingDirectory(workingDirectory);
            return Path.Combine(claudeProjectsPath, encodedDir, $"{sessionId}.jsonl");
        }

        /// <summary>
        /// Formatta una dimensione in bytes in formato leggibile (B, KB, MB, GB)
        /// </summary>
        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            else if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F1} KB";
            else if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0):F1} MB";
            else
                return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        /// <summary>
        /// Aggiorna la lista delle sessioni correnti quando viene selezionata una working directory
        /// </summary>
        private void UpdateCurrentSessions()
        {
            _currentSessions.Clear();

            if (SelectedWorkingDir == null)
                return;

            Log.Information("Updating current sessions for: {WorkingDir}", SelectedWorkingDir.WorkingDirectory);

            foreach (var session in SelectedWorkingDir.Sessions.OrderByDescending(s => s.LastActivity))
            {
                var displayName = session.Name ?? "";

                // Ottieni la dimensione del file dal filesystem (dato dinamico)
                string formattedSize = "N/A";
                try
                {
                    var filePath = GetSessionFilePath(session.SessionId, session.WorkingDirectory);
                    if (File.Exists(filePath))
                    {
                        var fileInfo = new FileInfo(filePath);
                        formattedSize = FormatFileSize(fileInfo.Length);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to read file size for session {SessionId}", session.SessionId);
                }

                _currentSessions.Add(new SessionDisplayItem
                {
                    SessionId = session.SessionId,
                    DisplayName = displayName,
                    WorkingDirectory = session.WorkingDirectory,
                    FormattedDate = session.LastActivity.ToString("yyyy-MM-dd HH:mm"),
                    FormattedFileSize = formattedSize,
                    Icon = "",
                    IsNewSessionPlaceholder = false,
                    SessionData = session
                });
            }

            Log.Information("Showing {Count} sessions for selected working directory", _currentSessions.Count);
        }

        /// <summary>
        /// Handler per la selezione dal dropdown
        /// </summary>
        private void OnWorkingDirSelected(object? sender, EventArgs e)
        {
            if (WorkingDirectoryPicker.SelectedItem is WorkingDirectoryGroup selected)
            {
                Log.Information("Working directory selected from dropdown: {WorkingDir}", selected.WorkingDirectory);
                SelectedWorkingDir = selected;
            }
        }

        /// <summary>
        /// Handler per la selezione dal carousel
        /// </summary>
        private void OnCarouselSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is WorkingDirectoryGroup selected)
            {
                Log.Information("Working directory selected from carousel: {WorkingDir}", selected.WorkingDirectory);
                SelectedWorkingDir = selected;
                WorkingDirectoryPicker.SelectedItem = selected;
            }
        }

        /// <summary>
        /// Scroll carousel verso sinistra
        /// </summary>
        private void OnScrollCarouselLeft(object? sender, EventArgs e)
        {
            // TODO: Implementare scroll programmatico del carousel
            Log.Information("Scroll carousel left requested");
        }

        /// <summary>
        /// Scroll carousel verso destra
        /// </summary>
        private void OnScrollCarouselRight(object? sender, EventArgs e)
        {
            // TODO: Implementare scroll programmatico del carousel
            Log.Information("Scroll carousel right requested");
        }

        /// <summary>
        /// Handler per il doppio click su una sessione dalla tabella.
        /// </summary>
        private async void OnSessionDoubleTapped(object? sender, TappedEventArgs e)
        {
            // Recupera il SessionDisplayItem dal BindingContext del Grid
            if (sender is Grid grid && grid.BindingContext is SessionDisplayItem selectedItem)
            {
                Log.Information("Session double-tapped: {SessionId}, Name: {DisplayName}",
                    selectedItem.SessionId, selectedItem.DisplayName);

                // VERIFICA: Se il nome è null, chiedi di assegnarlo PRIMA di procedere
                if (string.IsNullOrWhiteSpace(selectedItem.SessionData.Name))
                {
                    Log.Information("Session {SessionId} has no name, showing AssignNameDialog", selectedItem.SessionId);

                    // Mostra il dialog per assegnare un nome
                    var assignNameDialog = new AssignNameDialog(selectedItem.SessionId, _dbService);
                    await Navigation.PushModalAsync(new NavigationPage(assignNameDialog));

                    // Aspetta che il dialog completi (utente preme Salva o Annulla)
                    await assignNameDialog.CompletionTask;

                    // Se il nome NON è stato assegnato, interrompi (non chiudere la finestra di selezione)
                    if (!assignNameDialog.WasNameAssigned)
                    {
                        Log.Information("Name not assigned for session {SessionId}, keeping selector open", selectedItem.SessionId);
                        return; // Rimani nella finestra di selezione
                    }

                    // Nome assegnato con successo, ricarica i dati della sessione dal database
                    var sessionIdToReload = selectedItem.SessionId;
                    Log.Information("Name assigned for session {SessionId}, reloading session data from DB", sessionIdToReload);

                    // Ricarica la sessione specifica dal database
                    var updatedSessionData = await _dbService.GetSessionByIdAsync(sessionIdToReload);
                    if (updatedSessionData == null)
                    {
                        Log.Error("Session {SessionId} not found in database after name assignment", sessionIdToReload);
                        await this.DisplaySelectableAlert("Errore", "Impossibile ricaricare i dati della sessione dal database.", "OK");
                        return; // Rimani nel selector
                    }

                    // Aggiorna i dati della sessione nell'oggetto selectedItem
                    selectedItem.SessionData = updatedSessionData;
                    selectedItem.DisplayName = updatedSessionData.Name ?? "";
                    Log.Information("Session data reloaded with name: {Name}", selectedItem.DisplayName);

                    // Ricarica anche la lista UI in background per mantenerla aggiornata
                    _ = LoadSessionsAsync();
                }

                // Procedi con la selezione della sessione
                SelectedSession = selectedItem.SessionData;

                // Completa il Task con la sessione selezionata
                _selectionCompletionSource.TrySetResult(selectedItem.SessionData);

                Log.Information("Session {SessionId} selected, closing selector", selectedItem.SessionId);

                // Chiudi questa pagina
                await Navigation.PopModalAsync();
                Log.Information("SessionSelectorPage closed itself after selection");
            }
        }

        /// <summary>
        /// Handler per il menu contestuale "Apri con Notepad++".
        /// Click destro sulla riga → menu → apre il file .jsonl con Notepad++
        /// </summary>
        private async void OnOpenWithNotepadClicked(object? sender, EventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.BindingContext is SessionDisplayItem selectedItem)
            {
                Log.Information("Open with Notepad++ clicked for session: {SessionId}", selectedItem.SessionId);

                try
                {
                    // Costruisci il path del file .jsonl
                    var filePath = GetSessionFilePath(selectedItem.SessionId, selectedItem.WorkingDirectory);

                    if (!File.Exists(filePath))
                    {
                        Log.Warning("File not found: {FilePath}", filePath);
                        await this.DisplaySelectableAlert("File non trovato", $"Il file non esiste:\n{filePath}", "OK");
                        return;
                    }

                    // Apri con Notepad++
                    var notepadPath = @"C:\Program Files\Notepad++\notepad++.exe";

                    if (!File.Exists(notepadPath))
                    {
                        // Prova path alternativo
                        notepadPath = @"C:\Program Files (x86)\Notepad++\notepad++.exe";
                    }

                    if (!File.Exists(notepadPath))
                    {
                        Log.Warning("Notepad++ not found");
                        await this.DisplaySelectableAlert("Notepad++ non trovato",
                            "Notepad++ non è installato o non è stato trovato nel percorso predefinito.",
                            "OK");
                        return;
                    }

                    // Avvia Notepad++ con il file
                    var processStartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = notepadPath,
                        Arguments = $"\"{filePath}\"",
                        UseShellExecute = true
                    };

                    System.Diagnostics.Process.Start(processStartInfo);
                    Log.Information("Opened file with Notepad++: {FilePath}", filePath);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to open file with Notepad++");
                    await this.DisplaySelectableAlert("Errore", $"Impossibile aprire il file:\n{ex.Message}", "OK");
                }
            }
        }

        /// <summary>
        /// Variabile per tracciare la sessione attualmente selezionata nella CollectionView
        /// </summary>
        private SessionDisplayItem? _selectedSessionItem;

        /// <summary>
        /// Handler per la selezione di una riga nella CollectionView.
        /// Abilita il pulsante "Apri File .jsonl" quando una riga è selezionata.
        /// </summary>
        private void OnSessionSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.Count > 0 && e.CurrentSelection[0] is SessionDisplayItem selectedItem)
            {
                _selectedSessionItem = selectedItem;
                OpenFileButton.IsEnabled = true;
                Log.Debug("Session selected: {SessionId}", selectedItem.SessionId);
            }
            else
            {
                _selectedSessionItem = null;
                OpenFileButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// Handler per il pulsante "Apri File .jsonl".
        /// Apre il file .jsonl della sessione selezionata con Notepad++.
        /// </summary>
        private async void OnOpenFileClicked(object? sender, EventArgs e)
        {
            Log.Information("OnOpenFileClicked triggered");

            if (_selectedSessionItem == null)
            {
                Log.Warning("No session selected");
                await this.DisplaySelectableAlert("Nessuna selezione", "Seleziona prima una sessione dalla lista.", "OK");
                return;
            }

            try
            {
                Log.Information("Opening file for session: {SessionId}", _selectedSessionItem.SessionId);
                var filePath = GetSessionFilePath(_selectedSessionItem.SessionId, _selectedSessionItem.WorkingDirectory);
                Log.Information("File path: {FilePath}", filePath);

                if (!File.Exists(filePath))
                {
                    Log.Warning("File does not exist: {FilePath}", filePath);
                    await this.DisplaySelectableAlert("File non trovato", $"Il file non esiste:\n{filePath}", "OK");
                    return;
                }

                // Cerca Notepad++
                var notepadPath = @"C:\Program Files\Notepad++\notepad++.exe";
                if (!File.Exists(notepadPath))
                {
                    Log.Debug("Notepad++ not found in Program Files, trying Program Files (x86)");
                    notepadPath = @"C:\Program Files (x86)\Notepad++\notepad++.exe";
                }

                if (!File.Exists(notepadPath))
                {
                    Log.Warning("Notepad++ not found in standard locations");
                    await this.DisplaySelectableAlert("Notepad++ non trovato", "Notepad++ non è installato nel percorso standard.", "OK");
                    return;
                }

                Log.Information("Launching Notepad++: {NotepadPath} with file: {FilePath}", notepadPath, filePath);

                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = notepadPath,
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = true
                };

                System.Diagnostics.Process.Start(processStartInfo);
                Log.Information("Successfully opened file with Notepad++: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open file with Notepad++");
                await this.DisplaySelectableAlert("Errore", $"Impossibile aprire il file:\n{ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Handler per il pulsante "Nuova Sessione"
        /// </summary>
        private async void OnNewSessionClicked(object? sender, EventArgs e)
        {
            await ShowNewSessionDialogAsync();
        }

        /// <summary>
        /// Mostra il dialog per creare una nuova sessione.
        /// </summary>
        private async Task ShowNewSessionDialogAsync()
        {
            try
            {
                Log.Information("Opening NewSessionDialog...");

                // Crea il dialog passando un callback che assegna SelectedSession
                var newSessionDialog = new NewSessionDialog(session =>
                {
                    Log.Information("New session created via callback: Name={Name}, WorkingDirectory={WorkingDirectory}",
                        session.Name, session.WorkingDirectory);
                    SelectedSession = session;
                });

                // WORKAROUND: NO NavigationPage wrapper
                await Navigation.PushModalAsync(newSessionDialog);
                Log.Information("NewSessionDialog shown, waiting for CompletionTask...");

                // Aspetta che il dialog venga chiuso (CompletionTask)
                var completed = await newSessionDialog.CompletionTask;
                Log.Information("NewSessionDialog CompletionTask completed: {Completed}", completed);

                // Se una sessione è stata creata (callback invocato), notifica MainPage
                if (SelectedSession != null)
                {
                    Log.Information("Setting SelectionTask result with new session...");
                    Log.Information("New session: Name={Name}, WorkingDirectory={WorkingDirectory}",
                        SelectedSession.Name, SelectedSession.WorkingDirectory);

                    _selectionCompletionSource.TrySetResult(SelectedSession);

                    // NON chiudere qui - MainPage si occuperà di:
                    // 1. Chiudere SessionSelectorPage
                    // 2. Aprire la nuova sessione con OpenSessionInNewTabAsync
                    Log.Information("SessionSelectorPage completed - waiting for MainPage to close and open session");
                    return;
                }

                Log.Information("New session dialog closed without creating a session");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to show new session dialog");
                await this.DisplaySelectableAlert("Errore", $"Impossibile aprire il dialog per nuova sessione:\n{ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Handler per il pulsante "Assegna Nome".
        /// Mostra un dialog per assegnare un nome a una sessione senza nome.
        /// </summary>
        private async void OnAssignNameClicked(object? sender, EventArgs e)
        {
            try
            {
                if (sender is Button button && button.CommandParameter is SessionDisplayItem sessionItem)
                {
                    Log.Information("Assign name clicked for session: {SessionId}", sessionItem.SessionId);

                    // Mostra il dialog per assegnare un nome
                    var assignNameDialog = new AssignNameDialog(sessionItem.SessionId, _dbService);
                    await Navigation.PushModalAsync(new NavigationPage(assignNameDialog));

                    // Aspetta che il dialog completi
                    await assignNameDialog.CompletionTask;

                    // Se il nome è stato assegnato, ricarica la lista
                    if (assignNameDialog.WasNameAssigned)
                    {
                        await LoadSessionsAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to show assign name dialog");
                await this.DisplaySelectableAlert("Errore", $"Impossibile assegnare il nome:\n{ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Handler per il pulsante "Annulla".
        /// Chiude la pagina senza selezionare alcuna sessione.
        /// </summary>
        private async void OnCancelClicked(object? sender, EventArgs e)
        {
            Log.Information("Session selector cancelled by user");

            // Controlla se il task è già completato (race condition)
            if (_selectionCompletionSource.Task.IsCompleted)
            {
                Log.Warning("SelectionTask already completed, ignoring cancel click");
                return;
            }

            SelectedSession = null;

            // Completa il Task con null (nessuna selezione)
            Log.Information("Setting SelectionTask result to null");
            _selectionCompletionSource.TrySetResult(null);

            // Chiudi questa pagina
            Log.Information("SessionSelectorPage closing itself after cancel");
            await Navigation.PopModalAsync();
            Log.Information("SessionSelectorPage closed itself after cancel");
        }

        /// <summary>
        /// Handler per il pulsante "Ricarica Lista".
        /// Ricarica tutte le sessioni dal filesystem e dal database.
        /// </summary>
        private async void OnRefreshClicked(object? sender, EventArgs e)
        {
            Log.Information("Refreshing session list...");
            await LoadSessionsAsync();
        }

        /// <summary>
        /// Handler per il pulsante "Riscansiona".
        /// Resetta il flag processed di tutte le sessioni e riavvia la scansione completa del filesystem.
        /// I nomi assegnati alle sessioni vengono preservati.
        /// </summary>
        private async void OnRescanClicked(object? sender, EventArgs e)
        {
            try
            {
                // Chiedi conferma all'utente
                bool confirmed = await this.DisplaySelectableAlert(
                    "Conferma Riscansione",
                    "Questa operazione rieseguirà la scansione completa del filesystem.\n" +
                    "I nomi assegnati alle sessioni verranno preservati.\n\n" +
                    "Sei sicuro di voler continuare?",
                    "Sì, Riscansiona",
                    "Annulla"
                );

                if (!confirmed)
                {
                    Log.Information("Rescan cancelled by user");
                    return;
                }

                Log.Information("Starting full rescan - resetting processed flags");

                // Resetta i flag processed (preserva i nomi)
                await _dbService.ResetProcessedFlagAsync();

                // Esegui la scansione completa del filesystem
                await _sessionScanner.SyncFilesystemWithDatabaseAsync();

                // Ricarica la lista delle sessioni
                await LoadSessionsAsync();

                Log.Information("Full rescan completed successfully");

                await this.DisplaySelectableAlert("Riscansione Completata",
                    "La scansione del filesystem è stata completata con successo.",
                    "OK");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to rescan filesystem");
                await this.DisplaySelectableAlert("Errore", $"Impossibile completare la riscansione:\n{ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Handler per la modifica inline del campo Name.
        /// Quando l'utente esce dal campo di testo, aggiorna il nome nel database.
        /// </summary>
        private async void OnNameEntryUnfocused(object sender, FocusEventArgs e)
        {
            try
            {
                if (sender is Entry entry && entry.BindingContext is SessionDisplayItem item)
                {
                    var newName = entry.Text?.Trim();

                    // Valida il nome
                    if (string.IsNullOrWhiteSpace(newName))
                    {
                        await this.DisplaySelectableAlert("Nome Invalido", "Il nome non può essere vuoto.", "OK");
                        entry.Text = item.DisplayName; // Ripristina il valore originale
                        return;
                    }

                    // Se non è cambiato, non fare nulla
                    if (newName == item.DisplayName)
                        return;

                    Log.Information("Updating session name inline: {SessionId} -> {NewName}", item.SessionId, newName);

                    // Aggiorna nel database
                    await _dbService.UpdateSessionNameAsync(item.SessionId, newName);

                    // Aggiorna l'oggetto in memoria
                    item.DisplayName = newName;

                    // Ricarica la lista per garantire coerenza
                    UpdateCurrentSessions();

                    Log.Information("Session name updated successfully");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update session name inline");
                await this.DisplaySelectableAlert("Errore", $"Impossibile aggiornare il nome:\n{ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Handler per il menu contestuale "Aggiorna Messaggi".
        /// Importa tutti i messaggi dal file .jsonl nel database con progress dialog.
        /// </summary>
        private async void OnUpdateMessagesClicked(object? sender, EventArgs e)
        {
            ProgressDialog? progressDialog = null;

            try
            {
                if (sender is MenuFlyoutItem menuItem && menuItem.BindingContext is SessionDisplayItem selectedItem)
                {
                    Log.Information("Update messages clicked for session: {SessionId}", selectedItem.SessionId);

                    var filePath = GetSessionFilePath(selectedItem.SessionId, selectedItem.WorkingDirectory);

                    if (!File.Exists(filePath))
                    {
                        await this.DisplaySelectableAlert("File non trovato", $"Il file non esiste:\n{filePath}", "OK");
                        return;
                    }

                    Log.Information("Starting message import from file: {FilePath}", filePath);

                    // Crea e mostra il progress dialog
                    progressDialog = new ProgressDialog();
                    await Navigation.PushModalAsync(progressDialog);

                    // Crea un CancellationTokenSource separato per l'import
                    // (non usiamo progressDialog.CancellationToken perché viene cancellato quando apriamo dialog modali sopra)
                    using var importCts = new CancellationTokenSource();

                    // Crea il progress callback
                    var progress = new Progress<(int current, int total)>(update =>
                    {
                        progressDialog.UpdateProgress(
                            update.current,
                            update.total,
                            $"Processando messaggi dal file..."
                        );
                    });

                    // Crea il callback per gestire unknown fields
                    Func<string, string, List<string>, Task<bool>> unknownFieldsCallback = async (jsonLine, uuid, unknownFields) =>
                    {
                        try
                        {
                            // Mostra UnknownFieldsDialog completo con syntax highlighting
                            var dialog = new UnknownFieldsDialog(jsonLine, unknownFields, uuid);
                            await Navigation.PushModalAsync(new NavigationPage(dialog));

                            // Aspetta che l'utente prenda una decisione (TaskCompletionSource)
                            var result = await dialog.Result;

                            // Chiudi il dialog DOPO aver ricevuto il risultato
                            await Navigation.PopModalAsync();

                            return result;
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Error handling unknown fields dialog");
                            // In caso di errore, interrompi l'import per sicurezza
                            return false;
                        }
                    };

                    // Esegui l'import con progress e cancellation support
                    Models.MessageImportResult? result = null;
                    bool cancelled = false;

                    try
                    {
                        result = await _dbService.ImportMessagesFromJsonlAsync(
                            selectedItem.SessionId,
                            filePath,
                            progress,
                            unknownFieldsCallback,
                            importCts.Token);

                        // Segna come completato
                        progressDialog.Complete();
                    }
                    catch (OperationCanceledException)
                    {
                        Log.Information("Import cancelled by user");
                        progressDialog.SetCancelled();
                        cancelled = true;
                    }

                    // Aspetta che l'utente chiuda il progress dialog
                    await progressDialog.CompletionTask;
                    await Navigation.PopModalAsync();

                    // Se cancellato, esci senza mostrare altri dialog
                    if (cancelled || result == null)
                        return;

                    // Mostra risultati con summary completo
                    if (result.HasUnknownFields)
                    {
                        var uniqueFields = result.AllUnknownFieldsUnique;
                        var fieldsPreview = string.Join(", ", uniqueFields.Take(5));
                        if (uniqueFields.Count > 5)
                            fieldsPreview += $" ... (+{uniqueFields.Count - 5} altri)";

                        await this.DisplaySelectableAlert("Import Completato con Warning",
                            $"✅ Importati: {result.ImportedCount} messaggi\n" +
                            $"⚠️ Saltati: {result.SkippedCount} messaggi con campi sconosciuti\n" +
                            $"📊 Totale: {result.TotalProcessed} messaggi processati\n\n" +
                            $"Campi sconosciuti trovati ({uniqueFields.Count}):\n{fieldsPreview}",
                            "OK");

                        Log.Warning("Import completed with unknown fields: {UniqueCount} unique fields in {MessageCount} messages",
                            uniqueFields.Count, result.MessagesWithUnknownFieldsCount);
                    }
                    else
                    {
                        await this.DisplaySelectableAlert("Import Completato",
                            $"✅ Importati con successo {result.ImportedCount} messaggi nel database.\n" +
                            $"📊 Totale processato: {result.TotalProcessed} messaggi",
                            "OK");

                        Log.Information("Import completed successfully: {Imported} imported", result.ImportedCount);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update messages");

                // Segna il progress dialog come errore se ancora aperto
                if (progressDialog != null)
                {
                    progressDialog.SetError(ex.Message);
                    await progressDialog.CompletionTask;
                    await Navigation.PopModalAsync();
                }

                await this.DisplaySelectableAlert("Errore", $"Impossibile aggiornare i messaggi:\n{ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Cerca una riga JSON nel file .jsonl per UUID.
        /// Usato per trovare la riga con campi sconosciuti quando l'import viene interrotto.
        /// </summary>
        private async Task<string?> FindJsonLineByUuidAsync(string filePath, string? uuid)
        {
            if (string.IsNullOrEmpty(uuid))
                return null;

            try
            {
                using var reader = new StreamReader(filePath);
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (line?.Contains($"\"uuid\":\"{uuid}\"") == true)
                        return line;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to find JSON line by UUID: {Uuid}", uuid);
            }

            return null;
        }
    }
}
