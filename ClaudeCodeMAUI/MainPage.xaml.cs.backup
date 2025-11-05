using ClaudeCodeMAUI.Services;
using ClaudeCodeMAUI.Models;
using ClaudeCodeMAUI.Utilities;
using Microsoft.Extensions.Configuration;
using Serilog;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Diagnostics;

#if WINDOWS
using Microsoft.Maui.Platform;
#endif

namespace ClaudeCodeMAUI;

public partial class MainPage : ContentPage
{
    private readonly DbService? _dbService;
    private readonly SettingsService? _settingsService;
    private readonly IConfiguration _configuration;
    private ClaudeProcessManager? _processManager;
    private StreamJsonParser? _parser;
    private ConversationSession? _currentSession;
    private SessionTokenTracker? _tokenTracker;  // Tracker per monitorare l'utilizzo del contesto
    private bool _isPlanMode;
    private MarkdownHtmlRenderer? _htmlRenderer;
    private bool _isDarkTheme = true;  // Default tema scuro
    private bool _isWebViewReady = false;
    private bool _sessionInitialized = false;  // Flag per mostrare messaggio init solo prima volta
    private string? _currentWorkingDirectory;  // Working directory selezionata per la conversazione corrente

    // ===== NUOVO APPROCCIO: "Reload Full Page" =====
    // Buffer per memorizzare tutto l'HTML della conversazione
    // Ogni nuovo messaggio viene aggiunto al buffer e l'intera pagina HTML viene rigenerata
    private System.Text.StringBuilder _conversationHtml = new System.Text.StringBuilder();

    public MainPage(IConfiguration configuration)
    {
        InitializeComponent();
        _configuration = configuration;
        _settingsService = new SettingsService();
        Log.Information("MainPage initialized");
    }

    /// <summary>
    /// Mostra un dialog di errore con testo copiabile.
    /// Da usare al posto di DisplayAlert per errori che l'utente potrebbe voler copiare.
    /// </summary>
    /// <param name="title">Titolo dell'errore</param>
    /// <param name="message">Messaggio di errore dettagliato</param>
    private async Task ShowCopyableErrorAsync(string title, string message)
    {
        try
        {
            var errorDialog = new ErrorDialog();
            errorDialog.SetError(title, message);
            await Navigation.PushModalAsync(new NavigationPage(errorDialog));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to show copyable error dialog, falling back to DisplayAlert");
            // Fallback al DisplayAlert standard se il dialog custom fallisce
            await DisplayAlert(title, message, "OK");
        }
    }

    public MainPage(IConfiguration configuration, DbService dbService) : this(configuration)
    {
        _dbService = dbService;
        Log.Information("MainPage initialized with DbService");

        // Aggiungi handler per gestione tastiera su Editor
        InitializeInputEditor();

        // Carica le impostazioni salvate per il tema (solo se _settingsService è inizializzato)
        if (_settingsService != null)
        {
            _isDarkTheme = _settingsService.IsDarkTheme;
            SwitchTheme.IsToggled = _isDarkTheme;
        }
    }

    /// <summary>
    /// Inizializza l'InputEditor con la gestione delle keyboard shortcuts.
    /// Enter = invia messaggio, Ctrl+Enter = nuova linea
    /// </summary>
    private void InitializeInputEditor()
    {
#if WINDOWS
        // Su Windows, possiamo intercettare i key press usando l'handler nativo
        InputEditor.HandlerChanged += (s, e) =>
        {
            if (InputEditor.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox textBox)
            {
                // Usa PreviewKeyDown invece di KeyDown per intercettare PRIMA dell'inserimento
                textBox.PreviewKeyDown += OnInputEditorKeyDown;
            }
        };
#endif
    }

#if WINDOWS
    /// <summary>
    /// Handler per KeyDown su Windows - gestisce Enter vs Ctrl+Enter.
    /// IMPORTANTE: Usa PreviewKeyDown per intercettare PRIMA che il carattere venga inserito.
    /// </summary>
    private void OnInputEditorKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        // Enter senza modificatori = invia messaggio
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (!ctrlPressed)
            {
                // Enter normale = invia messaggio
                e.Handled = true; // Previeni il default (nuova linea)

                // Rimuovi eventuali newline già inseriti (fallback se e.Handled non funziona)
                var currentText = InputEditor.Text;
                if (!string.IsNullOrEmpty(currentText) && currentText.EndsWith("\r"))
                {
                    InputEditor.Text = currentText.TrimEnd('\r', '\n');
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    OnSendMessage(sender, EventArgs.Empty);
                });
            }
            // Se Ctrl è premuto, lascia il comportamento default (nuova linea)
        }
    }
#endif

    /// <summary>
    /// Handler per il click sul pulsante "New Conversation".
    /// Richiede la selezione obbligatoria della working directory prima di iniziare.
    /// </summary>
    private async void OnNewConversationClicked(object? sender, EventArgs e)
    {
        try
        {
            Log.Information("New conversation button clicked");

            // Mostra il picker per la selezione della working directory
            var selectedDirectory = await PickWorkingDirectoryAsync();

            // Se l'utente cancella la selezione, non avviare la conversazione
            if (string.IsNullOrWhiteSpace(selectedDirectory))
            {
                Log.Information("Conversation start cancelled: no working directory selected");
                await DisplayAlert("Working Directory Required",
                    "You must select a working directory to start a new conversation.",
                    "OK");
                return;
            }

            // Salva la directory selezionata
            _currentWorkingDirectory = selectedDirectory;
            Log.Information("Working directory set to: {Directory}", _currentWorkingDirectory);

            // Mostra una conferma all'utente
            var confirm = await DisplayAlert("Confirm Working Directory",
                $"Start new conversation in:\n\n{_currentWorkingDirectory}",
                "Start", "Cancel");

            if (!confirm)
            {
                Log.Information("Conversation start cancelled by user after directory confirmation");
                _currentWorkingDirectory = null;  // Reset
                return;
            }

            // Avvia la nuova conversazione con la directory selezionata
            StartNewConversation();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to handle new conversation click");
            await DisplayAlert("Error", $"Failed to start new conversation: {ex.Message}", "OK");
        }
    }

    private void OnStopClicked(object? sender, EventArgs e)
    {
        StopCurrentConversation();
    }

    private void OnPlanModeToggled(object? sender, ToggledEventArgs e)
    {
        _isPlanMode = e.Value;
        Log.Information("Plan mode toggled: {IsPlanMode}", _isPlanMode);
    }

    /// <summary>
    /// Handler per il click sul pulsante Context Info.
    /// Esegue il comando /context e mostra le informazioni dettagliate in un popup.
    /// </summary>
    private async void OnContextInfoClicked(object? sender, EventArgs e)
    {
        try
        {
            Log.Information("Context Info button clicked");

            // Verifica che ci sia una sessione corrente
            if (_currentSession == null || string.IsNullOrWhiteSpace(_currentSession.SessionId))
            {
                Log.Warning("No active session for context info");
                await ShowCopyableErrorAsync("No Active Session", "There is no active session. Start a new conversation first.");
                return;
            }

            var sessionId = _currentSession.SessionId;
            Log.Information("Getting context info for session: {SessionId}", sessionId);

            // Mostra indicatore di caricamento
            LblStatus.Text = "Loading context info...";
            LblStatus.TextColor = Colors.Blue;
            BtnContextInfo.IsEnabled = false;

            try
            {
                // Esegui comando /context
                // Nota: timeout aumentato a 15 secondi perché modalità interattiva richiede più tempo
                var runner = new ClaudeCodeMAUI.Services.ClaudeCommandRunner();
                var output = await runner.ExecuteCommandAsync(
                    sessionId,
                    "/context",
                    ClaudeCodeMAUI.Services.AppConfig.ClaudeWorkingDirectory,
                    timeoutMs: 15000  // 15 secondi timeout (interattivo richiede più tempo)
                );

                Log.Information("Command completed, output length: {Length} chars", output.Length);

                // Debug: Log output per troubleshooting
                if (string.IsNullOrWhiteSpace(output))
                {
                    Log.Error("Output is empty! This should not happen.");
                }

                // Parse output
                var parser = new ClaudeCodeMAUI.Services.ContextOutputParser();
                var info = parser.Parse(output);

                Log.Information("Parsing completed successfully");

                // Mostra popup con le informazioni
                var contextPage = new ContextInfoPage();
                contextPage.SetContextInfo(info);
                await Navigation.PushModalAsync(new NavigationPage(contextPage));

                Log.Information("Context info page displayed");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get or parse context info");
                await ShowCopyableErrorAsync("Context Info Error", $"Failed to get context information:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}");
            }
            finally
            {
                // Ripristina UI
                LblStatus.Text = "Ready";
                LblStatus.TextColor = Colors.Gray;
                BtnContextInfo.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error in OnContextInfoClicked");
            await ShowCopyableErrorAsync("Unexpected Error", $"Unexpected error: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Handler per il click sul pulsante View Session.
    /// Apre il viewer unificato con TUTTE le sessioni della working directory.
    /// Le sessioni sono ordinate per timestamp con timeline gerarchica (agent dopo tool_use).
    /// </summary>
    private async void OnViewSessionClicked(object? sender, EventArgs e)
    {
        try
        {
            Log.Information("View Session button clicked");

            // Determina la working directory da usare
            var workingDir = _currentSession?.WorkingDirectory ?? _currentWorkingDirectory;

            if (string.IsNullOrWhiteSpace(workingDir))
            {
                Log.Warning("No working directory set");
                await DisplayAlert("No Working Directory",
                    "No working directory is set. Start a conversation first or select a working directory.",
                    "OK");
                return;
            }

            Log.Information("Opening unified session viewer for all sessions in working directory: {WorkingDir}", workingDir);

            // Apri il viewer semplificato in modale (mostra TUTTE le sessioni)
            var viewer = new ClaudeCodeMAUI.Views.SessionViewer(workingDir);
            await Navigation.PushModalAsync(new NavigationPage(viewer));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open session viewer");
            await DisplayAlert("Error", $"Failed to open session viewer:\n\n{ex.Message}", "OK");
        }
    }


    /// <summary>
    /// Handler per il click sul pulsante Terminal.
    /// Lancia un terminale Windows con il comando claude --resume [Context ID].
    /// </summary>
    private async void OnTerminalClicked(object? sender, EventArgs e)
    {
        try
        {
            Log.Information("Opening terminal with resume command");

            // Verifica che ci sia una sessione corrente
            if (_currentSession == null || string.IsNullOrWhiteSpace(_currentSession.SessionId))
            {
                Log.Warning("No active session to resume");
                await DisplayAlert("No Active Session", "There is no active session to resume. Start a new conversation first.", "OK");
                return;
            }

            var sessionId = _currentSession.SessionId;
            var workingDir = ClaudeCodeMAUI.Services.AppConfig.ClaudeWorkingDirectory;

            // Comando da eseguire nel terminale
            var command = $"claude --resume {sessionId}";

            Log.Information("Launching terminal with command: {Command} in directory: {WorkingDir}", command, workingDir);

#if WINDOWS
            // Su Windows, usa Windows Terminal se disponibile, altrimenti cmd.exe
            // Windows Terminal: wt.exe -d "path" cmd /k "command"
            // Cmd.exe: cmd.exe /k "cd /d path && command"

            try
            {
                // Prova prima con Windows Terminal
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "wt.exe",
                    Arguments = $"-d \"{workingDir}\" cmd /k \"{command}\"",
                    UseShellExecute = true
                };

                System.Diagnostics.Process.Start(processInfo);
                Log.Information("Terminal launched successfully using Windows Terminal");
            }
            catch (Exception wtEx)
            {
                // Fallback a cmd.exe se Windows Terminal non è disponibile
                Log.Warning(wtEx, "Windows Terminal not available, falling back to cmd.exe");

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k \"cd /d \"{workingDir}\" && {command}\"",
                    UseShellExecute = true
                };

                System.Diagnostics.Process.Start(processInfo);
                Log.Information("Terminal launched successfully using cmd.exe");
            }
#else
            // Su altre piattaforme (Linux/Mac), usa il terminale di default
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "bash",
                Arguments = $"-c \"cd '{workingDir}' && {command}\"",
                UseShellExecute = true
            };

            System.Diagnostics.Process.Start(processInfo);
            Log.Information("Terminal launched successfully");
#endif
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error launching terminal");
            await DisplayAlert("Error", $"Failed to launch terminal: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Handler per il click sul pulsante Settings.
    /// Apre la pagina delle impostazioni in modalità modale.
    /// </summary>
    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        try
        {
            Log.Information("Opening Settings page");

            // Verifica che _settingsService sia inizializzato
            if (_settingsService == null)
            {
                Log.Error("SettingsService is not initialized");
                await DisplayAlert("Error", "Settings service is not available.", "OK");
                return;
            }

            // Crea la pagina Settings passando il SettingsService e un callback per notificare i cambiamenti
            var settingsPage = new SettingsPage(_settingsService, OnSettingsChanged);

            // Apri la pagina in modalità modale
            await Navigation.PushModalAsync(new NavigationPage(settingsPage));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error opening Settings page");
            await DisplayAlert("Error", "Failed to open settings page.", "OK");
        }
    }

    /// <summary>
    /// Callback chiamato quando le impostazioni vengono modificate.
    /// Aggiorna l'UI della MainPage per riflettere le nuove impostazioni.
    /// </summary>
    private void OnSettingsChanged()
    {
        Log.Information("Settings changed, updating UI");

        // Aggiorna il tema se è stato modificato (solo se _settingsService è inizializzato)
        if (_settingsService != null)
        {
            var newTheme = _settingsService.IsDarkTheme;
            if (newTheme != _isDarkTheme)
            {
                _isDarkTheme = newTheme;
                SwitchTheme.IsToggled = _isDarkTheme;
                // Il toggle dello switch chiamerà OnThemeToggled che aggiornerà la WebView
            }
        }
    }

    private async void OnThemeToggled(object? sender, ToggledEventArgs e)
    {
        _isDarkTheme = e.Value;

        // Salva l'impostazione del tema (solo se _settingsService è inizializzato)
        if (_settingsService != null)
        {
            _settingsService.IsDarkTheme = _isDarkTheme;
        }

        if (LblTheme != null)
        {
            LblTheme.Text = _isDarkTheme ? "Dark" : "Light";
        }
        Log.Information("Theme toggled: {Theme}", _isDarkTheme ? "Dark" : "Light");

        // Cambia tema nella WebView se è già inizializzata
        if (_isWebViewReady)
        {
            await SetThemeAsync(_isDarkTheme);
        }
    }

    /// <summary>
    /// Aggiorna la barra superiore con la directory di lavoro corrente.
    /// Mostra la working directory effettivamente usata dal processo Claude.
    /// </summary>
    /// <summary>
    /// Aggiorna la label della working directory nell'UI con il valore della sessione corrente.
    /// </summary>
    private void UpdateWorkingDirectory()
    {
        try
        {
            // Usa la working directory della sessione corrente (se presente)
            var workingDir = _currentSession?.WorkingDirectory ?? _currentWorkingDirectory;

            if (!string.IsNullOrWhiteSpace(workingDir))
            {
                WorkingDirectoryLabel.Text = $"Working Directory: {workingDir}";
                Log.Information("Working directory displayed: {Directory}", workingDir);
            }
            else
            {
                WorkingDirectoryLabel.Text = "Working Directory: <not set>";
                Log.Warning("Working directory not set for current session");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get working directory");
            WorkingDirectoryLabel.Text = "Working Directory: <error>";
        }
    }

    /// <summary>
    /// Aggiorna la barra superiore con il Context ID corrente.
    /// </summary>
    private void UpdateContextId(string contextId)
    {
        try
        {
            ContextIdLabel.Text = $"Context ID: {contextId}";
            Log.Information("Context ID updated: {ContextId}", contextId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update Context ID");
            ContextIdLabel.Text = "Context ID: <error>";
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Aggiorna la barra con la working directory
        UpdateWorkingDirectory();

        // Inizializza HTML renderer
        if (_htmlRenderer == null)
        {
            _htmlRenderer = new MarkdownHtmlRenderer();
            Log.Information("MarkdownHtmlRenderer initialized");
        }

        // Inizializza WebView con template HTML salvato su file
        if (!_isWebViewReady)
        {
            try
            {
                // Genera HTML completo
                var initialHtml = _htmlRenderer.GenerateFullPage(_isDarkTheme);

                // Salva in un file temporaneo
                var tempPath = Path.Combine(Path.GetTempPath(), "ClaudeCodeMAUI_conversation.html");
                File.WriteAllText(tempPath, initialHtml);
                Log.Information("HTML saved to temp file: {Path}", tempPath);

                // Carica da file:/// URL invece di HtmlWebViewSource
                ConversationWebView.Source = new Uri($"file:///{tempPath.Replace("\\", "/")}");

                // Handler per quando la WebView è pronta
                ConversationWebView.Navigated += async (s, e) =>
                {
                    _isWebViewReady = true;
                    Log.Information("WebView ready and navigated to: {Url}", e.Url);

                    // Dopo che la WebView è pronta, controlla se ci sono sessioni da recuperare
                    if (_dbService != null && _currentSession == null)
                    {
                        await RecoverLastSessionAsync();
                    }
                };

                Log.Information("WebView initialized with HTML file from disk");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize WebView with HTML file");
            }
        }
    }

    /// <summary>
    /// Recupera l'ultima sessione attiva dal database e chiede all'utente se vuole riprenderla.
    /// Chiamato automaticamente all'avvio dell'applicazione dopo che la WebView è pronta.
    /// </summary>
    private async Task RecoverLastSessionAsync()
    {
        try
        {
            Log.Information("RecoverLastSessionAsync: Starting session recovery check");

            // 1. Interroga DB per sessioni attive/killed
            var sessions = await _dbService!.GetActiveConversationsAsync();

            if (sessions.Count == 0)
            {
                Log.Information("No sessions to recover");
                return;
            }

            Log.Information("Found {Count} active sessions", sessions.Count);

            // 2. Prendi la più recente (per last_activity)
            var lastSession = sessions.OrderByDescending(s => s.LastActivity).First();
            Log.Information("Most recent session: {SessionId}, Last activity: {LastActivity}",
                lastSession.SessionId, lastSession.LastActivity);

            // 3. Controlla se mostrare il dialog o riprendere automaticamente
            bool shouldResume = false;

            if (_settingsService != null && _settingsService.ShowResumeDialog)
            {
                // Mostra il dialog in italiano
                shouldResume = await DisplayAlert(
                    "Riprendere la sessione?",
                    $"Trovata sessione precedente del {lastSession.LastActivity:g}\n" +
                    $"Stato: {lastSession.Status}\n\n" +
                    "Vuoi riprenderla?",
                    "Sì", "No"
                );
            }
            else
            {
                // Riprendi automaticamente senza chiedere
                shouldResume = true;
                Log.Information("ShowResumeDialog is OFF, resuming automatically");
            }

            if (shouldResume)
            {
                Log.Information("User chose to resume session {SessionId}", lastSession.SessionId);
                // 4. Riprendi la sessione
                await ResumeSessionAsync(lastSession);
            }
            else
            {
                Log.Information("User chose NOT to resume session {SessionId}", lastSession.SessionId);
                // 5. Marca come closed e inizia nuova
                await _dbService.UpdateStatusAsync(lastSession.SessionId, "closed");
                Log.Information("Session {SessionId} marked as closed", lastSession.SessionId);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to recover session");
            await DisplayAlert("Error", $"Failed to recover session: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Riprende una sessione esistente usando il flag --resume di Claude.
    /// Riavvia il processo Claude con il session_id esistente per recuperare tutto il contesto.
    /// </summary>
    private async Task ResumeSessionAsync(ConversationSession session)
    {
        try
        {
            Log.Information("Resuming session: {SessionId}", session.SessionId);

            // Reset flag sessione inizializzata
            _sessionInitialized = false;

            // 1. Imposta sessione corrente e carica la working directory
            _currentSession = session;
            _currentWorkingDirectory = session.WorkingDirectory;
            Log.Information("Session set as current. Working directory: {WorkingDir}, Plan mode: {IsPlanMode}",
                            _currentWorkingDirectory, _isPlanMode);

            // Verifica che la working directory sia valida
            if (string.IsNullOrWhiteSpace(_currentWorkingDirectory))
            {
                Log.Warning("Session has no working directory set, using default");
                _currentWorkingDirectory = @"C:\Sources\ClaudeGui";  // Fallback
            }

            // Aggiorna il Context ID e la working directory nella barra superiore
            UpdateContextId(session.SessionId);
            UpdateWorkingDirectory();

            // 2. Crea parser
            _parser = new StreamJsonParser();
            _parser.SessionInitialized += OnSessionInitialized;
            _parser.TextReceived += OnTextReceived;
            _parser.ToolCallReceived += OnToolCallReceived;
            _parser.ToolResultReceived += OnToolResultReceived;
            _parser.MetadataReceived += OnMetadataReceived;
            Log.Information("Parser created and events wired");

            // 3. Crea process manager CON resumeSessionId e working directory
            _processManager = new ClaudeProcessManager(
                session.SessionId,      // Passa il session_id per --resume
                session.SessionId,
                _currentWorkingDirectory  // Passa la working directory della sessione
            );
            _processManager.JsonLineReceived += OnJsonLineReceived;
            _processManager.ErrorReceived += OnErrorReceived;
            _processManager.ProcessCompleted += OnProcessCompleted;
            Log.Information("Process manager created with session_id for --resume in {WorkingDir}",
                            _currentWorkingDirectory);

            // 4. Avvia processo con --resume
            _processManager.Start();
            Log.Information("Claude process started with --resume {SessionId}", session.SessionId);

            // 5. Aggiorna UI
            BtnStop.IsEnabled = true;
            LblStatus.Text = "Session Resumed";
            LblStatus.TextColor = Colors.Orange;
            Log.Information("UI updated - session resumed indicator shown");

            // 6. Attendi che il processo sia pronto
            await Task.Delay(1500);

            // 6.5. Carica e visualizza la storia dei messaggi precedenti
            await LoadAndDisplayHistoryAsync(session.SessionId);

            // 7. Invia prompt di riassunto automatico SOLO se abilitato nelle impostazioni
            if (_settingsService != null && _settingsService.AutoSendSummaryPrompt)
            {
                var summaryPrompt = "Ciao! Mi ricordi brevemente su cosa stavamo lavorando? " +
                                    "Dammi un riassunto del contesto della nostra conversazione precedente.";

                Log.Information("Auto-send summary prompt is enabled, sending summary request to Claude");
                await _processManager.SendMessageAsync(summaryPrompt);
            }
            else
            {
                Log.Information("Auto-send summary prompt is disabled or SettingsService not initialized, skipping summary request");
            }

            Log.Information("Session resumed successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to resume session");
            await DisplayAlert("Error", $"Failed to resume session: {ex.Message}", "OK");

            // Fallback: avvia nuova conversazione se recovery fallisce
            StartNewConversation();
        }
    }

    /// <summary>
    /// Carica e visualizza la storia degli ultimi N messaggi di una sessione.
    /// Chiamato durante il resume di una sessione per mostrare il contesto precedente.
    /// Il numero di messaggi da caricare è configurabile nelle impostazioni.
    /// </summary>
    /// <param name="sessionId">ID della sessione di cui caricare la storia</param>
    private async Task LoadAndDisplayHistoryAsync(string sessionId)
    {
        try
        {
            // Verifica che i servizi siano inizializzati
            if (_dbService == null || _settingsService == null || _htmlRenderer == null)
            {
                Log.Warning("LoadAndDisplayHistoryAsync: Servizi non inizializzati, skip caricamento storia");
                return;
            }

            // Ottieni il numero di messaggi da caricare dalle impostazioni
            int messageCount = _settingsService.HistoryMessageCount;

            // Se il conteggio è 0, non caricare nessuna storia
            if (messageCount <= 0)
            {
                Log.Information("LoadAndDisplayHistoryAsync: HistoryMessageCount è 0, skip caricamento storia");
                return;
            }

            Log.Information("LoadAndDisplayHistoryAsync: Caricamento ultimi {Count} messaggi per sessione {SessionId}",
                           messageCount, sessionId);

            // Carica i messaggi dal database
            var messages = await _dbService.GetLastMessagesAsync(sessionId, messageCount);

            if (messages.Count == 0)
            {
                Log.Information("LoadAndDisplayHistoryAsync: Nessun messaggio storico trovato per sessione {SessionId}", sessionId);
                return;
            }

            Log.Information("LoadAndDisplayHistoryAsync: Trovati {Count} messaggi storici, renderizzazione in corso...", messages.Count);

            // Renderizza ogni messaggio in ordine cronologico
            foreach (var message in messages)
            {
                string html;
                if (message.Role == "user")
                {
                    html = _htmlRenderer.RenderUserMessage(message.Content);
                }
                else // assistant
                {
                    html = _htmlRenderer.RenderAssistantMessage(message.Content);
                }

                await AppendHtmlAsync(html);
                Log.Debug("LoadAndDisplayHistoryAsync: Messaggio {Sequence} ({Role}) renderizzato", message.Sequence, message.Role);
            }

            // Aggiungi un separatore visivo per indicare la fine della storia
            var separatorHtml = _htmlRenderer.RenderSeparator();
            await AppendHtmlAsync(separatorHtml);

            Log.Information("LoadAndDisplayHistoryAsync: Storia completa visualizzata con successo ({Count} messaggi)", messages.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "LoadAndDisplayHistoryAsync: Errore durante il caricamento della storia");
            // Non fare throw - il caricamento della storia è opzionale e non deve bloccare il resume
        }
    }

    private void OnSendMessage(object? sender, EventArgs e)
    {
        var message = InputEditor.Text?.Trim();
        

        if (string.IsNullOrWhiteSpace(message))
            return;

        SendMessageAsync(message);
        InputEditor.Text = string.Empty;
    }

    private async void StartNewConversation()
    {
        try
        {
            Log.Information("StartNewConversation called");

            // Verifica che la working directory sia stata selezionata
            if (string.IsNullOrWhiteSpace(_currentWorkingDirectory))
            {
                Log.Error("Cannot start conversation: no working directory selected");
                await DisplayAlert("Error", "Working directory not set. This should not happen.", "OK");
                return;
            }

            // Reset flag sessione inizializzata
            _sessionInitialized = false;

            // Crea nuova sessione con la working directory selezionata
            _currentSession = new ConversationSession
            {
                Status = "active",
                TabTitle = "New Conversation",
                WorkingDirectory = _currentWorkingDirectory
            };
            Log.Information("Session created with working directory: {WorkingDir}", _currentWorkingDirectory);

            // Crea parser
            _parser = new StreamJsonParser();
            _parser.SessionInitialized += OnSessionInitialized;
            _parser.TextReceived += OnTextReceived;
            _parser.ToolCallReceived += OnToolCallReceived;
            _parser.ToolResultReceived += OnToolResultReceived;
            _parser.MetadataReceived += OnMetadataReceived;
            Log.Information("Parser created");

            // Crea process manager con la working directory specificata
            _processManager = new ClaudeProcessManager(null, null, _currentWorkingDirectory);
            _processManager.JsonLineReceived += OnJsonLineReceived;
            _processManager.ErrorReceived += OnErrorReceived;
            _processManager.ProcessCompleted += OnProcessCompleted;
            Log.Information("Process manager created with working directory: {WorkingDir}", _currentWorkingDirectory);

            // Avvia processo
            Log.Information("Starting Claude process...");
            _processManager.Start();
            Log.Information("Claude process started");

            // Aggiorna UI
            BtnStop.IsEnabled = true;
            LblStatus.Text = "Running...";
            LblStatus.TextColor = Colors.Green;

            // Aggiorna la working directory label con la directory corrente
            UpdateWorkingDirectory();

            // Pulisci WebView
            if (_isWebViewReady)
            {
                await ClearConversationAsync();
            }

            Log.Information("New conversation started successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start new conversation");
            await DisplayAlert("Error", $"Failed to start conversation: {ex.Message}\n\nStack: {ex.StackTrace}", "OK");
        }
    }

    private void StopCurrentConversation()
    {
        try
        {
            _processManager?.Kill();
            BtnStop.IsEnabled = false;
            LblStatus.Text = "Stopped";
            LblStatus.TextColor = Colors.Red;
            Log.Information("Conversation stopped");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to stop conversation");
            DisplayAlert("Error", $"Failed to stop conversation: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Mostra un dialog per la selezione della working directory.
    /// Questo metodo usa l'API nativa di Windows (FolderPicker).
    /// Restituisce null se l'utente cancella la selezione.
    /// </summary>
    /// <returns>Percorso della directory selezionata, o null se cancellato</returns>
    private async Task<string?> PickWorkingDirectoryAsync()
    {
        try
        {
#if WINDOWS
            // Usa l'API nativa di Windows per il FolderPicker
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();

            // Ottieni l'handle della finestra principale
            var windowHandle = ((MauiWinUIWindow)Application.Current!.Windows[0].Handler.PlatformView!).WindowHandle;

            // Inizializza il picker con l'handle della finestra
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, windowHandle);

            // Configura il picker
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
            folderPicker.FileTypeFilter.Add("*");  // Obbligatorio per FolderPicker

            // Mostra il dialog e attendi la selezione
            var folder = await folderPicker.PickSingleFolderAsync();

            if (folder != null)
            {
                Log.Information("Working directory selected: {Path}", folder.Path);
                return folder.Path;
            }
            else
            {
                Log.Information("Working directory selection cancelled by user");
                return null;
            }
#else
            // Su altre piattaforme, usa un dialog MAUI standard
            // TODO: implementare per Android/iOS/Mac se necessario
            await DisplayAlert("Not Supported", "Working directory selection is only supported on Windows", "OK");
            Log.Warning("Working directory selection not supported on this platform");
            return null;
#endif
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to pick working directory");
            await DisplayAlert("Error", $"Failed to select directory: {ex.Message}", "OK");
            return null;
        }
    }

    private async void SendMessageAsync(string message)
    {
        try
        {
            if (_processManager == null || !_processManager.IsRunning)
            {
                StartNewConversation();
                // Attendi un momento per l'inizializzazione
                await Task.Delay(500);
            }

            if (_processManager != null)
            {
                // Renderizza messaggio utente
                if (_htmlRenderer != null)
                {
                    var userMessageHtml = _htmlRenderer.RenderUserMessage(message);
                    await AppendHtmlAsync(userMessageHtml);
                }

                // Salva il messaggio utente nel database se abbiamo sessione attiva e dbService
                if (_dbService != null && _currentSession != null && !string.IsNullOrEmpty(_currentSession.SessionId))
                {
                    await _dbService.SaveMessageAsync(_currentSession.SessionId, "user", message);
                    Log.Debug("SendMessageAsync: Messaggio user salvato nel DB");
                }

                var mmm = $"{message}\n {(_isPlanMode ? "PLANMODE=ON" : "PLANMODE=OFF")}";
                // Invia messaggio a Claude
                await _processManager.SendMessageAsync(mmm);
                Log.Debug($"Message sent {mmm}: {mmm.Length} chars");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send message");
            await DisplayAlert("Error", $"Failed to send message: {ex.Message}", "OK");
        }
    }

    // Eventi del parser
    private void OnSessionInitialized(object? sender, SessionInitializedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_currentSession != null && _dbService != null)
            {
                _currentSession.SessionId = e.SessionId;
                _currentSession.CurrentModel = e.Model;
                _dbService.InsertSessionAsync(_currentSession);

                // Notifica l'App del session ID corrente per gestire OnSleep gracefully
                if (Application.Current is App app)
                {
                    app.SetCurrentSession(_dbService, e.SessionId);
                }
            }

            // Aggiorna il Context ID nella barra superiore
            UpdateContextId(e.SessionId);

            // Inizializza il SessionTokenTracker per monitorare il contesto
            try
            {
                // Usa la working directory della sessione corrente per trovare i file di sessione
                var workingDir = _currentSession?.WorkingDirectory ?? _currentWorkingDirectory;

                if (string.IsNullOrWhiteSpace(workingDir))
                {
                    Log.Warning("Cannot initialize SessionTokenTracker: no working directory set");
                }
                else
                {
                    _tokenTracker = new SessionTokenTracker(e.SessionId, workingDir);
                    Log.Information("SessionTokenTracker initialized for session: {SessionId} in {WorkingDir}",
                                    e.SessionId, workingDir);

                    // Aggiorna subito il display (anche se probabilmente a 0 token inizialmente)
                    UpdateTokenBudgetDisplay();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize SessionTokenTracker");
            }

            // Mostra messaggio solo la PRIMA volta
            if (!_sessionInitialized)
            {
                _sessionInitialized = true;
                Log.Information("Session initialized (first time): {SessionId}, Model: {Model}", e.SessionId, e.Model);

                // Opzionalmente, mostra messaggio nell'UI (puoi commentare se non vuoi mostrarlo)
                // await AppendHtmlAsync($"<div style='color: #888; font-size: 0.85em; margin: 8px 0;'>Session: {e.SessionId} | Model: {e.Model}</div>");
            }
            else
            {
                Log.Debug("Session init message received again (ignored for UI)");
            }
        });
    }

    private void OnTextReceived(object? sender, TextReceivedEventArgs e)
    {
        Log.Information("OnTextReceived called with {Length} chars", e.Text.Length);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                Log.Information("OnTextReceived: Inside MainThread callback");
                Log.Information("OnTextReceived: _htmlRenderer is {IsNull}", _htmlRenderer == null ? "NULL" : "NOT NULL");
                Log.Information("OnTextReceived: _isWebViewReady = {IsReady}", _isWebViewReady);

                if (_htmlRenderer != null)
                {
                    Log.Information("OnTextReceived: Calling RenderAssistantMessage...");
                    var html = _htmlRenderer.RenderAssistantMessage(e.Text);
                    Log.Information("OnTextReceived: HTML generated ({Length} chars), calling AppendHtmlAsync...", html.Length);
                    await AppendHtmlAsync(html);
                    Log.Information("OnTextReceived: AppendHtmlAsync completed");

                    // Salva il messaggio assistant nel database se abbiamo sessione attiva e dbService
                    if (_dbService != null && _currentSession != null && !string.IsNullOrEmpty(_currentSession.SessionId))
                    {
                        await _dbService.SaveMessageAsync(_currentSession.SessionId, "assistant", e.Text);
                        Log.Debug("OnTextReceived: Messaggio assistant salvato nel DB");
                    }
                }
                else
                {
                    Log.Warning("OnTextReceived: _htmlRenderer is NULL, cannot render!");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in OnTextReceived");
            }
        });
    }

    private void OnToolCallReceived(object? sender, ToolCallEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (_htmlRenderer != null)
                {
                    var html = _htmlRenderer.RenderToolCall(e.ToolName, e.Description);
                    await AppendHtmlAsync(html);
                }
                _currentSession?.RecordToolUsage(e.ToolName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in OnToolCallReceived");
            }
        });
    }

    private void OnToolResultReceived(object? sender, ToolResultEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (_htmlRenderer != null)
                {
                    var html = _htmlRenderer.RenderToolResult(e.Content, e.IsError);
                    await AppendHtmlAsync(html);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in OnToolResultReceived");
            }
        });
    }

    private void OnMetadataReceived(object? sender, MetadataEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_currentSession != null)
            {
                _currentSession.UpdateFromResult(
                    e.TotalCostUsd,
                    e.InputTokens,
                    e.OutputTokens,
                    e.CacheReadTokens,
                    e.CacheCreationTokens,
                    e.NumTurns,
                    e.DurationMs,
                    e.Model
                );

                // Converti duration da millisecondi a secondi con 1 cifra decimale
                var durationSeconds = e.DurationMs / 1000.0;

                // Genera HTML per i metadata da aggiungere sotto l'ultimo messaggio di Claude
                var metadataHtml = $@"
<div class=""metadata-container"" onclick=""this.classList.toggle('collapsed')"">
    <span class=""metadata-content"">Duration: {durationSeconds:F1}s  |  Cost: ${e.TotalCostUsd:F4}  |  Tokens: {e.InputTokens} in / {e.OutputTokens} out  |  Turns: {e.NumTurns}</span>
</div>";

                // Aggiungi i metadata al buffer della conversazione
                _conversationHtml.Append(metadataHtml);
                Log.Information("Added metadata to conversation buffer. Total buffer size: {Size} chars", _conversationHtml.Length);

                // Riproduci beep se abilitato nelle impostazioni
                if (_settingsService != null && _settingsService.PlayBeepOnMetadata)
                {
                    try
                    {
                        // Usa System.Console.Beep per Windows
                        #if WINDOWS
                        System.Console.Beep(800, 200); // Frequenza 800Hz, durata 200ms
                        Log.Debug("Beep played for metadata received");
                        #else
                        Log.Warning("Beep not supported on this platform");
                        #endif
                    }
                    catch (Exception beepEx)
                    {
                        Log.Warning(beepEx, "Failed to play beep sound");
                    }
                }

                // Rigenera e ricarica la pagina HTML completa nella WebView
                if (_htmlRenderer != null && _isWebViewReady)
                {
                    var fullHtml = _htmlRenderer.GenerateFullPage(_isDarkTheme, _conversationHtml.ToString());
                    ConversationWebView.Source = new HtmlWebViewSource { Html = fullHtml };
                    Log.Information("WebView reloaded with metadata appended");
                }

                // Aggiorna il display del budget token
                UpdateTokenBudgetDisplay();
            }
        });
    }

    // Eventi del processo
    private void OnJsonLineReceived(object? sender, JsonLineReceivedEventArgs e)
    {
        _parser?.ParseLine(e.JsonLine);
    }

    private void OnErrorReceived(object? sender, string e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (_htmlRenderer != null)
                {
                    // Render error come tool result error
                    var html = _htmlRenderer.RenderToolResult(e, isError: true);
                    await AppendHtmlAsync(html);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in OnErrorReceived");
            }
        });
    }

    private void OnProcessCompleted(object? sender, ProcessCompletedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            BtnStop.IsEnabled = false;
            LblStatus.Text = "Completed";
            LblStatus.TextColor = Colors.Gray;

            try
            {
                if (_htmlRenderer != null)
                {
                    var separator = _htmlRenderer.RenderSeparator();
                    await AppendHtmlAsync(separator);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in OnProcessCompleted");
            }
        });
    }

    /*
    ╔══════════════════════════════════════════════════════════════════════════════╗
    ║                     APPROCCIO V2: "Reload Full Page"                        ║
    ║                                                                              ║
    ║  - Mantiene un buffer (StringBuilder) di tutto l'HTML della conversazione   ║
    ║  - Ogni nuovo messaggio: appende al buffer, rigenera HTML completo,         ║
    ║    ricarica pagina                                                           ║
    ║  - Usa HtmlWebViewSource invece di EvaluateJavaScriptAsync                  ║
    ║  - Elimina TUTTI i problemi di escaping JavaScript                          ║
    ║  - Trade-off: Causa sfarfallio (flickering) per via del reload della pagina ║
    ║                                                                              ║
    ║  Per riabilitare: nel metodo AppendHtmlAsync, chiama AppendHtmlAsync_V2    ║
    ║  invece di AppendHtmlAsync_V3.                                              ║
    ╚══════════════════════════════════════════════════════════════════════════════╝
    */
    /// <summary>
    /// Appende HTML alla WebView usando l'approccio "Reload Full Page" (V2)
    /// Aggiunge l'HTML al buffer interno e ricarica l'intera pagina HTML
    /// </summary>
    private async Task AppendHtmlAsync_V2(string htmlFragment)
    {
        Log.Information("AppendHtmlAsync (Reload Full Page) called with {Length} chars", htmlFragment.Length);

        if (!_isWebViewReady)
        {
            Log.Warning("WebView not ready, skipping HTML append");
            return;
        }

        try
        {
            // Aggiungi l'HTML al buffer della conversazione
            _conversationHtml.Append(htmlFragment);
            Log.Information("Added to conversation buffer. Total buffer size: {Size} chars", _conversationHtml.Length);

            // Rigenera la pagina HTML completa con tutto il contenuto
            if (_htmlRenderer != null)
            {
                var fullHtml = _htmlRenderer.GenerateFullPage(_isDarkTheme, _conversationHtml.ToString());
                Log.Information("Generated full HTML page: {Size} chars", fullHtml.Length);

                // Ricarica l'intera pagina HTML
                ConversationWebView.Source = new HtmlWebViewSource { Html = fullHtml };
                Log.Information("WebView reloaded with updated HTML");
            }
            else
            {
                Log.Error("_htmlRenderer is null, cannot generate HTML page");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AppendHtmlAsync: Failed to append HTML to WebView");
            Log.Error("Exception type: {Type}, Message: {Message}", ex.GetType().Name, ex.Message);
            if (ex.InnerException != null)
            {
                Log.Error("Inner exception: {Type}, Message: {Message}",
                    ex.InnerException.GetType().Name, ex.InnerException.Message);
            }
        }
    }

    /*
    ╔══════════════════════════════════════════════════════════════════════════════╗
    ║                     APPROCCIO V3: JavaScript + Base64 (ATTIVO)              ║
    ║                                                                              ║
    ║  - Mantiene il buffer sincronizzato (come V2)                               ║
    ║  - Encode HTML in Base64 (elimina TUTTI i problemi di escaping)            ║
    ║  - JavaScript decode + insertAdjacentHTML (no reload = no flickering!)     ║
    ║  - Auto-scroll integrato nella funzione JavaScript                          ║
    ║  - Approccio IBRIDO: best of both worlds                                    ║
    ║                                                                              ║
    ║  VANTAGGI:                                                                   ║
    ║  ✅ Nessun flickering (no reload pagina)                                    ║
    ║  ✅ Nessun problema di escaping (Base64)                                    ║
    ║  ✅ Buffer sincronizzato (utile per export/salvataggio)                    ║
    ║  ✅ Auto-scroll fluido                                                      ║
    ╚══════════════════════════════════════════════════════════════════════════════╝
    */
    /// <summary>
    /// Appende HTML alla WebView usando l'approccio "JavaScript + Base64" (V3)
    /// Encode l'HTML in Base64, lo passa a JavaScript che fa decode e append senza reload
    /// </summary>
    private async Task AppendHtmlAsync_V3(string htmlFragment)
    {
        Log.Information("AppendHtmlAsync_V3 (JavaScript + Base64) called with {Length} chars", htmlFragment.Length);

        if (!_isWebViewReady)
        {
            Log.Warning("WebView not ready, skipping HTML append");
            return;
        }

        try
        {
            // 1. Mantieni buffer sincronizzato (utile per export/salvataggio/clear)
            _conversationHtml.Append(htmlFragment);
            Log.Information("Added to conversation buffer. Total buffer size: {Size} chars", _conversationHtml.Length);

            // 2. Encode HTML in Base64 (RFC 4648) - elimina TUTTI i problemi di escaping
            var htmlBytes = System.Text.Encoding.UTF8.GetBytes(htmlFragment);
            var base64Html = Convert.ToBase64String(htmlBytes);
            Log.Information("HTML encoded to Base64: {Length} chars -> {Base64Length} chars",
                htmlFragment.Length, base64Html.Length);

            // 3. Chiama funzione JavaScript appendHtmlBase64() che:
            //    - Decodifica Base64 -> HTML string (atob)
            //    - Appende al container (insertAdjacentHTML)
            //    - Auto-scroll in fondo (window.scrollTo)
            var script = $"appendHtmlBase64('{base64Html}')";
            Log.Information("Executing JavaScript: appendHtmlBase64({Length} chars)", base64Html.Length);

            var result = await ConversationWebView.EvaluateJavaScriptAsync(script);
            Log.Information("JavaScript result: {Result}", result ?? "null");

            // 4. Verifica risultato
            if (result != null && result.StartsWith("OK"))
            {
                Log.Information("HTML appended successfully via V3 (no flickering!)");
            }
            else if (result != null && result.StartsWith("ERROR"))
            {
                Log.Error("JavaScript returned error: {Error}", result);
            }
            else
            {
                Log.Warning("JavaScript returned unexpected result: {Result}", result ?? "null");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AppendHtmlAsync_V3: Failed to append HTML to WebView");
            Log.Error("Exception type: {Type}, Message: {Message}", ex.GetType().Name, ex.Message);
            if (ex.InnerException != null)
            {
                Log.Error("Inner exception: {Type}, Message: {Message}",
                    ex.InnerException.GetType().Name, ex.InnerException.Message);
            }
        }
    }

    /// <summary>
    /// Metodo wrapper attivo - chiama la versione V3 (JavaScript + Base64)
    /// Per tornare a V2: cambia la chiamata da AppendHtmlAsync_V3 a AppendHtmlAsync_V2
    /// </summary>
    private async Task AppendHtmlAsync(string htmlFragment)
    {
        // ATTIVO: V3 (JavaScript + Base64 - no flickering)
        await AppendHtmlAsync_V3(htmlFragment);

        // Per tornare a V2 (Reload Full Page - con flickering), commenta la riga sopra e decommenta:
        // await AppendHtmlAsync_V2(htmlFragment);
    }

    /*
    ╔══════════════════════════════════════════════════════════════════════════════╗
    ║                     VECCHIO APPROCCIO (COMMENTATO)                           ║
    ║                                                                              ║
    ║  APPROCCIO VECCHIO: EvaluateJavaScriptAsync + Global Variable (TEST 5)     ║
    ║  - Usa EvaluateJavaScriptAsync per chiamare funzione JavaScript             ║
    ║  - Passa HTML tramite variabile globale JavaScript con JSON.parse           ║
    ║  - Problemi: EvaluateJavaScriptAsync ritorna sempre null                    ║
    ║  - JavaScript string escaping è intrinsecamente fragile                     ║
    ║                                                                              ║
    ║  Per riabilitare: rinomina questo metodo in AppendHtmlAsync e commenta     ║
    ║  il nuovo metodo sopra.                                                     ║
    ╚══════════════════════════════════════════════════════════════════════════════╝
    */
    /*
    private async Task AppendHtmlAsync_OLD(string htmlFragment)
    {
        Log.Information("AppendHtmlAsync_OLD called with {Length} chars", htmlFragment.Length);
        Log.Information("AppendHtmlAsync_OLD: _isWebViewReady = {IsReady}", _isWebViewReady);

        if (!_isWebViewReady)
        {
            Log.Warning("WebView not ready, skipping HTML append");
            return;
        }

        try
        {
            // ===== APPROCCIO VARIABILE GLOBALE (TEST 5) =====
            // Problema TEST 1-4: Passare HTML come parametro JavaScript tramite string interpolation $"..."
            // è intrinsecamente fragile - ogni approccio di escaping ha breaking points diversi.
            //
            // Soluzione: NON passare HTML come parametro, ma usare variabile globale JavaScript.
            // Il template HTML ha già implementato:
            // - var htmlToAppend = ''; (variabile globale)
            // - function appendFromGlobal() { return appendMessage(htmlToAppend); }
            //
            // Processo:
            // 1. Impostiamo htmlToAppend = HTML (tramite JSON.parse per gestire tutti i caratteri speciali)
            // 2. Chiamiamo appendFromGlobal() che legge htmlToAppend e la passa ad appendMessage()

            // Log primi 200 caratteri dell'HTML originale
            var htmlPreview = htmlFragment.Length > 200
                ? htmlFragment.Substring(0, 200) + "..."
                : htmlFragment;
            Log.Information("HTML Fragment preview: {Preview}", htmlPreview);

            // Encoding: HTML -> JSON string (gestisce automaticamente tutti i caratteri speciali)
            var jsonEncoded = System.Text.Json.JsonSerializer.Serialize(htmlFragment);
            Log.Information("JSON encoded length: {Length} chars", jsonEncoded.Length);
            Log.Information("JSON encoded preview (first 150 chars): {Preview}",
                jsonEncoded.Length > 150 ? jsonEncoded.Substring(0, 150) + "..." : jsonEncoded);

            // SCRIPT 1: Imposta variabile globale htmlToAppend usando JSON.parse
            var script1 = $"htmlToAppend = JSON.parse({jsonEncoded});";
            Log.Information("JavaScript script 1 (set variable): {Script}",
                script1.Length > 300 ? script1.Substring(0, 300) + "..." : script1);

            var result1 = await ConversationWebView.EvaluateJavaScriptAsync(script1);
            Log.Information("Script 1 result: {Result}", result1 ?? "null");

            // SCRIPT 2: Chiama appendFromGlobal() che legge htmlToAppend e la appende
            var script2 = "appendFromGlobal();";
            Log.Information("JavaScript script 2 (call function): {Script}", script2);

            var result2 = await ConversationWebView.EvaluateJavaScriptAsync(script2);
            Log.Information("Script 2 result: {Result}", result2 ?? "null");

            Log.Information("AppendHtmlAsync_OLD: HTML appended successfully ({Length} chars)", htmlFragment.Length);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AppendHtmlAsync_OLD: Failed to append HTML to WebView");
            Log.Error("Exception type: {Type}, Message: {Message}", ex.GetType().Name, ex.Message);
            if (ex.InnerException != null)
            {
                Log.Error("Inner exception: {Type}, Message: {Message}",
                    ex.InnerException.GetType().Name, ex.InnerException.Message);
            }
        }
    }
    */

    /// <summary>
    /// Cambia il tema della WebView (dark/light)
    /// </summary>
    private async Task SetThemeAsync(bool isDark)
    {
        if (!_isWebViewReady)
            return;

        try
        {
            var theme = isDark ? "dark" : "light";
            var script = $"setTheme('{theme}')";
            await ConversationWebView.EvaluateJavaScriptAsync(script);
            Log.Information("Theme changed to: {Theme}", theme);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to change theme");
        }
    }

    /// <summary>
    /// Pulisce la conversazione nella WebView
    /// </summary>
    private async Task ClearConversationAsync()
    {
        if (!_isWebViewReady)
            return;

        try
        {
            // Pulisce il buffer HTML
            _conversationHtml.Clear();
            Log.Information("Conversation buffer cleared");

            // Rigenera pagina HTML vuota
            if (_htmlRenderer != null)
            {
                var emptyHtml = _htmlRenderer.GenerateFullPage(_isDarkTheme, "");
                ConversationWebView.Source = new HtmlWebViewSource { Html = emptyHtml };
                Log.Information("WebView reloaded with empty page");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to clear conversation");
        }
    }



    /// <summary>
    /// Codifica l'HTML per l'inserimento sicuro in JavaScript via WebView usando Template Literals.
    /// APPROCCIO: Escape manuale dei 3 caratteri speciali per template literals.
    /// Mantiene TUTTA la formattazione originale (newlines, entity HTML, etc).
    /// </summary>
    private string EscapeHtmlForJavaScript(string html)
    {
        // Rimuovi Unicode line/paragraph separators che rompono JavaScript
        html = html.Replace("\u2028", " ").Replace("\u2029", " ");

        // Escape manuale per template literals (ORDINE IMPORTANTE!)
        // 1. Backslash (DEVE essere fatto per primo, altrimenti escapa gli altri escape)
        html = html.Replace("\\", "\\\\");
        // 2. Backtick (carattere delimitatore del template literal)
        html = html.Replace("`", "\\`");
        // 3. Interpolazione template literals (previene injection di codice JS)
        html = html.Replace("${", "\\${");

        return html;
    }

    /// <summary>
    /// Aggiorna il display del budget token leggendo il file JSONL della sessione corrente
    /// </summary>
    private void UpdateTokenBudgetDisplay()
    {
        if (_tokenTracker == null)
        {
            TokenBudgetLabel.Text = "Context: N/A";
            TokenBudgetLabel.TextColor = Colors.Gray;
            return;
        }

        try
        {
            // Calcola l'utilizzo dei token dal file JSONL
            var usage = _tokenTracker.CalculateUsage();

            if (!usage.IsValid)
            {
                TokenBudgetLabel.Text = "Context: calculating...";
                TokenBudgetLabel.TextColor = Colors.Gray;
                return;
            }

            // Formatta il testo con migliaia separate e percentuale
            var text = $"Context: {usage.TotalTokens:N0} / {usage.TotalBudget:N0} ({usage.PercentageUsed:F1}%)";
            TokenBudgetLabel.Text = text;

            // Cambia colore in base al warning level
            TokenBudgetLabel.TextColor = usage.GetWarningLevel() switch
            {
                WarningLevel.None => Colors.LightGreen,      // 0-70%
                WarningLevel.Low => Colors.Yellow,           // 70-85%
                WarningLevel.Medium => Colors.Orange,        // 85-95%
                WarningLevel.High => Colors.Red,             // 95-100%
                _ => Colors.Gray
            };

            Log.Debug("Token budget updated: {Text}, Level: {Level}", text, usage.GetWarningLevel());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update token budget display");
            TokenBudgetLabel.Text = "Context: error";
            TokenBudgetLabel.TextColor = Colors.Gray;
        }
    }

}
