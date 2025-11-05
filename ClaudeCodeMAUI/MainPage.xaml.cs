using ClaudeCodeMAUI.Extensions;
using ClaudeCodeMAUI.Services;
using ClaudeCodeMAUI.Models;
using ClaudeCodeMAUI.Utilities;
using ClaudeCodeMAUI.Views;
using Microsoft.Extensions.Configuration;
using Serilog;
using System.Collections.ObjectModel;
using System.Text.Json;

#if WINDOWS
using Microsoft.Maui.Platform;
#endif

namespace ClaudeCodeMAUI;

/// <summary>
/// MainPage con supporto multi-sessione tramite TabView.
/// Ogni tab rappresenta una sessione Claude Code indipendente con il proprio ProcessManager.
/// </summary>
public partial class MainPage : ContentPage
{
    // ===== Servizi =====
    private readonly DbService? _dbService;
    private readonly SessionScannerService? _sessionScanner;
    private readonly SettingsService? _settingsService;
    private readonly IConfiguration _configuration;

    // ===== Multi-Session State =====
    private ObservableCollection<SessionTabItem> _sessionTabs = new ObservableCollection<SessionTabItem>();
    private SessionTabItem? _currentTab;

    // ===== Message Processing State =====
    private DateTime? _lastProcessedTimestamp = null;

    // ===== UI State =====
    private bool _isPlanMode;
    private bool _isDarkTheme = true;

    /// <summary>
    /// Costruttore principale con dependency injection
    /// </summary>
    public MainPage(IConfiguration configuration, DbService? dbService = null, SessionScannerService? sessionScanner = null)
    {
        InitializeComponent();

        _configuration = configuration;
        _dbService = dbService;
        _sessionScanner = sessionScanner;
        _settingsService = new SettingsService();

        Log.Information("MainPage initialized (Multi-Session Mode)");

        // Inizializza UI
        InitializeInputEditor();

        // Carica tema salvato
        if (_settingsService != null)
        {
            _isDarkTheme = _settingsService.IsDarkTheme;
            SwitchTheme.IsToggled = _isDarkTheme;
        }

        // Carica sessioni open all'avvio
        _ = LoadOpenSessionsOnStartupAsync();
    }

    #region Initialization & Startup

    /// <summary>
    /// Inizializza l'InputEditor con keyboard shortcuts.
    /// Enter = invia, Ctrl+Enter = nuova linea
    /// </summary>
    private void InitializeInputEditor()
    {
#if WINDOWS
        InputEditor.HandlerChanged += (s, e) =>
        {
            if (InputEditor.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox textBox)
            {
                textBox.PreviewKeyDown += OnInputEditorKeyDown;
            }
        };
#endif
    }

#if WINDOWS
    private void OnInputEditorKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (!ctrlPressed)
            {
                e.Handled = true;
                MainThread.BeginInvokeOnMainThread(() => OnSendMessage(sender, EventArgs.Empty));
            }
        }
    }
#endif

    /// <summary>
    /// Carica tutte le sessioni con status='open' all'avvio dell'applicazione.
    /// Prima sincronizza il filesystem con il database, poi crea un tab per ciascuna sessione open
    /// e avvia il processo Claude con --resume.
    /// </summary>
    private async Task LoadOpenSessionsOnStartupAsync()
    {
        try
        {
            if (_sessionScanner == null || _dbService == null)
            {
                Log.Warning("SessionScanner or DbService not available, skipping session restoration");
                return;
            }

            // STEP 1: Sincronizza filesystem con database
            Log.Information("Syncing filesystem with database...");
            await _sessionScanner.SyncFilesystemWithDatabaseAsync();
            Log.Information("Filesystem sync completed");

            // STEP 2: Carica sessioni open
            Log.Information("Loading open sessions from database...");

            var openSessions = await _sessionScanner.GetOpenSessionsAsync();

            if (openSessions.Count == 0)
            {
                Log.Information("No open sessions found");
                return;
            }

            Log.Information("Found {Count} open sessions to restore", openSessions.Count);

            foreach (var session in openSessions)
            {
                await OpenSessionInNewTabAsync(session, resumeExisting: true);
            }

            Log.Information("Session restoration completed");

            // TEST: Mostra dialog di test per DisplaySelectableAlert
            await this.DisplaySelectableAlert(
                "Test DisplaySelectableAlert",
                "Questo è un messaggio di test.\n\nProva a selezionare questo testo con il mouse e copiarlo.\n\nSe riesci a selezionare e copiare il testo, DisplaySelectableAlert funziona correttamente! ✓\n\nRiga 1\nRiga 2\nRiga 3",
                "OK");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load open sessions on startup");
            await this.DisplaySelectableAlert("Error", $"Failed to restore sessions:\n{ex.Message}", "OK");
        }
    }

    #endregion

    #region Tab Management

    /// <summary>
    /// Apre una sessione in un nuovo tab (da SessionDbRow).
    /// </summary>
    /// <param name="sessionDbRow">Record del database della sessione da aprire</param>
    /// <param name="resumeExisting">True se è una sessione esistente da riprendere con --resume</param>
    private async Task OpenSessionInNewTabAsync(DbService.SessionDbRow sessionDbRow, bool resumeExisting)
    {
        try
        {
            Log.Information("Opening session in new tab: {SessionId}, Resume: {Resume}",
                sessionDbRow.SessionId, resumeExisting);

            var displayName = string.IsNullOrWhiteSpace(sessionDbRow.Name)
                ? $"Session {sessionDbRow.SessionId.Substring(0, 8)}..."
                : sessionDbRow.Name;

            // Crea un nuovo SessionTabItem
            var tabItem = new SessionTabItem
            {
                SessionId = sessionDbRow.SessionId,
                Name = sessionDbRow.Name,
                WorkingDirectory = sessionDbRow.WorkingDirectory,
                Session = new ConversationSession
                {
                    SessionId = sessionDbRow.SessionId,
                    TabTitle = displayName,
                    WorkingDirectory = sessionDbRow.WorkingDirectory,
                    Status = "active"
                }
            };

            // Crea ProcessManager per questa sessione
            var processManager = new ClaudeProcessManager(
                resumeSessionId: resumeExisting ? sessionDbRow.SessionId : null,
                dbSessionId: sessionDbRow.SessionId,
                workingDirectory: sessionDbRow.WorkingDirectory
            );

            // Sottoscrivi eventi ProcessManager
            processManager.JsonLineReceived += (s, e) => OnJsonLineReceived(tabItem, e.JsonLine);
            processManager.ProcessCompleted += (s, e) => OnProcessCompleted(tabItem, e);
            processManager.ErrorReceived += (s, e) => OnErrorReceived(tabItem, e);
            processManager.IsRunningChanged += (s, isRunning) => OnIsRunningChanged(tabItem, isRunning);

            tabItem.ProcessManager = processManager;

            // Crea il contenuto del tab
            var tabContent = new SessionTabContent();
            tabContent.SetSessionTabItem(tabItem);

            // Aggiungi alla collezione
            _sessionTabs.Add(tabItem);

            // Crea l'header del tab con Grid (Label + Label X cliccabile)
            var tabHeader = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = new GridLength(30, GridUnitType.Absolute) }
                },
                BackgroundColor = Colors.DarkGray,
                Padding = new Thickness(10, 8),
                Margin = new Thickness(2, 0)
            };

            var titleLabel = new Label
            {
                Text = $"{tabItem.StatusIcon} {tabItem.TabTitle}",
                TextColor = Colors.White,
                VerticalOptions = LayoutOptions.Center,
                VerticalTextAlignment = TextAlignment.Center,
                Padding = new Thickness(5, 0)
            };

            var closeLabel = new Label
            {
                Text = "✖",
                FontSize = 16,
                TextColor = Colors.White,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                Padding = 0
            };

            int tabIndex = _sessionTabs.Count - 1;
            titleLabel.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => SwitchToTab(tabIndex))
            });
            closeLabel.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await OnCloseTabButtonClicked(tabItem))
            });

            tabHeader.Children.Add(titleLabel);
            Grid.SetColumn(titleLabel, 0);
            tabHeader.Children.Add(closeLabel);
            Grid.SetColumn(closeLabel, 1);

            TabHeadersContainer.Children.Add(tabHeader);

            // Mostra il container e nascondi il placeholder
            SessionTabContainer.IsVisible = true;
            NoSessionsPlaceholder.IsVisible = false;

            // Seleziona il nuovo tab
            _currentTab = tabItem;
            CurrentTabContent.Content = tabContent;

            // Carica gli ultimi 50 messaggi se stiamo riprendendo una sessione esistente
            if (resumeExisting && _dbService != null)
            {
                var messages = await _dbService.GetLastMessagesAsync(sessionDbRow.SessionId, count: 50);
                Log.Information("Loaded {Count} historical messages for session {SessionId}",
                    messages.Count, sessionDbRow.SessionId);

                // Renderizza i messaggi storici in HTML
                if (messages.Count > 0)
                {
                    var renderer = new Utilities.MarkdownHtmlRenderer();
                    var conversationHtml = new System.Text.StringBuilder();

                    foreach (var message in messages)
                    {
                        if (message.Role == "user")
                        {
                            conversationHtml.AppendLine(renderer.RenderUserMessage(message.Content));
                        }
                        else if (message.Role == "assistant")
                        {
                            conversationHtml.AppendLine(renderer.RenderAssistantMessage(message.Content));
                        }
                    }

                    // Genera la pagina HTML completa con i messaggi
                    var fullHtmlPage = renderer.GenerateFullPage(_isDarkTheme, conversationHtml.ToString());

                    // Inizializza la WebView con i messaggi storici
                    tabContent.InitializeWebView(fullHtmlPage);

                    Log.Information("Initialized WebView with {Count} historical messages", messages.Count);
                }
            }

            // Avvia il processo Claude
            processManager.Start();

            // Aggiorna status nel database (se necessario)
            if (_dbService != null && sessionDbRow.Status != "open")
            {
                await _dbService.UpdateSessionStatusAsync(sessionDbRow.SessionId, "open");
            }

            Log.Information("Session opened successfully in new tab");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open session in new tab: {SessionId}", sessionDbRow.SessionId);
            await this.DisplaySelectableAlert("Error", $"Failed to open session:\n{ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Apre una sessione in un nuovo tab (da SessionInfo - legacy).
    /// </summary>
    /// <param name="sessionInfo">Informazioni della sessione da aprire</param>
    /// <param name="resumeExisting">True se è una sessione esistente da riprendere con --resume</param>
    private async Task OpenSessionInNewTabAsync(SessionInfo sessionInfo, bool resumeExisting)
    {
        try
        {
            Log.Information("Opening session in new tab: {SessionId}, Resume: {Resume}",
                sessionInfo.SessionId, resumeExisting);

            // Crea un nuovo SessionTabItem
            var tabItem = new SessionTabItem
            {
                SessionId = sessionInfo.SessionId,
                Name = sessionInfo.Name,
                WorkingDirectory = sessionInfo.WorkingDirectory,
                Session = new ConversationSession
                {
                    SessionId = sessionInfo.SessionId,
                    TabTitle = sessionInfo.DisplayName,
                    WorkingDirectory = sessionInfo.WorkingDirectory,
                    Status = "active"
                }
            };

            // Crea ProcessManager per questa sessione
            var processManager = new ClaudeProcessManager(
                resumeSessionId: resumeExisting ? sessionInfo.SessionId : null,
                dbSessionId: sessionInfo.SessionId,
                workingDirectory: sessionInfo.WorkingDirectory
            );

            // Sottoscrivi eventi ProcessManager
            processManager.JsonLineReceived += (s, e) => OnJsonLineReceived(tabItem, e.JsonLine);
            processManager.ProcessCompleted += (s, e) => OnProcessCompleted(tabItem, e);
            processManager.ErrorReceived += (s, e) => OnErrorReceived(tabItem, e);
            processManager.IsRunningChanged += (s, isRunning) => OnIsRunningChanged(tabItem, isRunning);

            tabItem.ProcessManager = processManager;

            // Crea il contenuto del tab
            var tabContent = new SessionTabContent();
            tabContent.SetSessionTabItem(tabItem);

            // Aggiungi alla collezione
            _sessionTabs.Add(tabItem);

            // Crea l'header del tab con Grid (Label + Label X cliccabile)
            var tabHeader = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = new GridLength(30, GridUnitType.Absolute) }
                },
                BackgroundColor = Colors.DarkGray,
                Padding = new Thickness(10, 8),
                Margin = new Thickness(2, 0)
            };

            var titleLabel = new Label
            {
                Text = $"{tabItem.StatusIcon} {tabItem.TabTitle}",
                TextColor = Colors.White,
                VerticalOptions = LayoutOptions.Center,
                VerticalTextAlignment = TextAlignment.Center,
                Padding = new Thickness(5, 0)
            };

            var closeLabel = new Label
            {
                Text = "✖",
                FontSize = 16,
                TextColor = Colors.White,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                Padding = 0
            };

            int tabIndex = _sessionTabs.Count - 1;
            titleLabel.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => SwitchToTab(tabIndex))
            });
            closeLabel.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await OnCloseTabButtonClicked(tabItem))
            });

            tabHeader.Children.Add(titleLabel);
            Grid.SetColumn(titleLabel, 0);
            tabHeader.Children.Add(closeLabel);
            Grid.SetColumn(closeLabel, 1);

            TabHeadersContainer.Children.Add(tabHeader);

            // Mostra il container e nascondi il placeholder
            SessionTabContainer.IsVisible = true;
            NoSessionsPlaceholder.IsVisible = false;

            // Seleziona il nuovo tab
            _currentTab = tabItem;
            CurrentTabContent.Content = tabContent;

            // Avvia il processo Claude
            processManager.Start();

            // Aggiorna o inserisci nel database
            if (!resumeExisting && _dbService != null)
            {
                await _dbService.InsertSessionAsync(
                    sessionInfo.SessionId,
                    sessionInfo.Name,
                    sessionInfo.WorkingDirectory
                );
            }

            Log.Information("Session tab created and process started successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open session in new tab");
            await this.DisplaySelectableAlert("Error", $"Failed to open session:\n{ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Handler per il pulsante "X" negli header dei tab.
    /// Chiude il tab selezionato immediatamente senza conferma.
    /// </summary>
    private async Task OnCloseTabButtonClicked(SessionTabItem tabItem)
    {
        try
        {
            Log.Information("Close button clicked for tab: {SessionId}", tabItem.SessionId);

            // Invia comando exit al processo se ancora attivo
            if (tabItem.ProcessManager != null && tabItem.IsRunning)
            {
                Log.Information("Sending exit command to process: {SessionId}", tabItem.SessionId);
                await tabItem.ProcessManager.SendExitCommandAsync();
                await Task.Delay(500); // Attendi 500ms per terminazione graceful
            }

            // Chiudi il tab
            await CloseTabAsync(tabItem, setStatusClosed: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to close tab via button");
            await this.DisplaySelectableAlert("Errore", $"Impossibile chiudere il tab:\n{ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Chiude un tab e termina il processo associato.
    /// </summary>
    private async Task CloseTabAsync(SessionTabItem tabItem, bool setStatusClosed = true)
    {
        try
        {
            Log.Information("Closing tab: {SessionId}", tabItem.SessionId);

            // Termina il processo
            tabItem.ProcessManager?.Dispose();

            // Aggiorna status nel database
            if (setStatusClosed && _dbService != null)
            {
                await _dbService.UpdateSessionStatusAsync(tabItem.SessionId, "closed");
            }

            // Rimuovi dalla collezione
            var index = _sessionTabs.IndexOf(tabItem);
            if (index >= 0)
            {
                _sessionTabs.RemoveAt(index);
                TabHeadersContainer.Children.RemoveAt(index);
            }

            // Se non ci sono più tab, mostra il placeholder
            if (_sessionTabs.Count == 0)
            {
                SessionTabContainer.IsVisible = false;
                NoSessionsPlaceholder.IsVisible = true;
                _currentTab = null;
                CurrentTabContent.Content = null;
            }
            else if (_currentTab == tabItem)
            {
                // Se era il tab corrente, seleziona il primo disponibile
                SwitchToTab(0);
            }

            Log.Information("Tab closed successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to close tab");
        }
    }

    #endregion

    #region Event Handlers - UI

    /// <summary>
    /// Handler per il pulsante "Seleziona Sessione".
    /// Apre il dialog per selezionare una sessione esistente o crearne una nuova.
    /// </summary>
    private async void OnSelectSessionClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_sessionScanner == null || _dbService == null)
            {
                var result = await this.DisplaySelectableAlert(
                    "Database Not Connected",
                    "Cannot load existing sessions because database credentials are not configured.\n\n" +
                    "To configure:\n" +
                    "1. Open terminal in ClaudeCodeMAUI folder\n" +
                    "2. Run: dotnet user-secrets set \"DatabaseCredentials:Username\" \"claudegui\"\n" +
                    "3. Run: dotnet user-secrets set \"DatabaseCredentials:Password\" \"your-password\"\n\n" +
                    "Do you want to create a new session anyway?",
                    "Yes, New Session",
                    "Cancel"
                );

                if (!result)
                    return;

                // Mostra dialog per nuova sessione
                var newSessionDialog = new NewSessionDialog();
                await Navigation.PushModalAsync(new NavigationPage(newSessionDialog));

                if (newSessionDialog.WasSessionCreated && newSessionDialog.CreatedSession != null)
                {
                    var newSession = newSessionDialog.CreatedSession;
                    newSession.SessionId = Guid.NewGuid().ToString();
                    await OpenSessionInNewTabAsync(newSession, resumeExisting: false);
                }

                return;
            }

            Log.Information("Opening session selector dialog");

            var selectorPage = new SessionSelectorPage(_sessionScanner, _dbService);
            await Navigation.PushModalAsync(new NavigationPage(selectorPage));

            // Aspetta che l'utente selezioni una sessione o annulli
            var selected = await selectorPage.SelectionTask;

            Log.Information("Session selector returned with selection: {HasSelection}", selected != null);

            // Se è stata selezionata una sessione, aprila
            if (selected != null)
            {
                // Validazione nome obbligatorio per sessioni esistenti
                bool isNewSession = string.IsNullOrWhiteSpace(selected.SessionId) || selected.SessionId == "NEW_SESSION";

                if (!isNewSession && string.IsNullOrWhiteSpace(selected.Name))
                {
                    await this.DisplaySelectableAlert("Nome Mancante",
                        "Questa sessione non ha un nome assegnato.\n\n" +
                        "Assegna un nome prima di aprirla utilizzando il pulsante 'Assegna Nome' " +
                        "o modificando direttamente il campo nella tabella.",
                        "OK");
                    return;
                }

                if (isNewSession)
                {
                    // Genera un nuovo SessionId (verrà popolato dal processo Claude quando si avvia)
                    selected.SessionId = Guid.NewGuid().ToString();
                }

                Log.Information("Opening selected session: {SessionId}, Resume: {Resume}", selected.SessionId, !isNewSession);
                await OpenSessionInNewTabAsync(selected, resumeExisting: !isNewSession);
            }
            else
            {
                Log.Information("No session selected - user cancelled");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to handle session selection");
            await this.DisplaySelectableAlert("Error", $"Failed to open session:\n{ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Handler per il pulsante Stop.
    /// Killa il processo e lo riavvia immediatamente con --resume.
    /// </summary>
    private async void OnStopClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_currentTab == null || _currentTab.ProcessManager == null)
            {
                await this.DisplaySelectableAlert("Error", "No active session", "OK");
                return;
            }

            Log.Information("Stop button clicked - killing and restarting process");

            var sessionId = _currentTab.SessionId;
            var workingDir = _currentTab.WorkingDirectory;

            // Kill il processo corrente
            _currentTab.ProcessManager.Kill();

            // Attendi un momento per assicurarsi che il processo sia terminato
            await Task.Delay(500);

            // Crea un nuovo ProcessManager con --resume
            var newProcessManager = new ClaudeProcessManager(
                resumeSessionId: sessionId,
                dbSessionId: sessionId,
                workingDirectory: workingDir
            );

            // Sottoscrivi eventi
            newProcessManager.JsonLineReceived += (s, e) => OnJsonLineReceived(_currentTab, e.JsonLine);
            newProcessManager.ProcessCompleted += (s, e) => OnProcessCompleted(_currentTab, e);
            newProcessManager.ErrorReceived += (s, e) => OnErrorReceived(_currentTab, e);
            newProcessManager.IsRunningChanged += (s, isRunning) => OnIsRunningChanged(_currentTab, isRunning);

            _currentTab.ProcessManager = newProcessManager;

            // Avvia il nuovo processo
            newProcessManager.Start();

            Log.Information("Process restarted successfully with --resume");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restart process");
            await this.DisplaySelectableAlert("Error", $"Failed to restart:\n{ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Cambia al tab specificato dall'indice.
    /// </summary>
    private void SwitchToTab(int index)
    {
        try
        {
            if (index >= 0 && index < _sessionTabs.Count)
            {
                _currentTab = _sessionTabs[index];

                // Trova il contenuto del tab
                var tabContent = new SessionTabContent();
                tabContent.SetSessionTabItem(_currentTab);
                CurrentTabContent.Content = tabContent;

                // Aggiorna stato Stop button
                BtnStop.IsEnabled = _currentTab.IsRunning;

                // Aggiorna colore degli header (ora sono Grid invece di Button)
                for (int i = 0; i < TabHeadersContainer.Children.Count; i++)
                {
                    if (TabHeadersContainer.Children[i] is Grid grid)
                    {
                        grid.BackgroundColor = i == index ? Colors.Gray : Colors.DarkGray;
                    }
                }

                Log.Debug("Tab switched to: {SessionId}", _currentTab.SessionId);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error switching to tab {Index}", index);
        }
    }

    /// <summary>
    /// Handler per l'invio di un messaggio.
    /// </summary>
    private async void OnSendMessage(object? sender, EventArgs e)
    {
        try
        {
            var message = InputEditor.Text?.Trim();

            if (string.IsNullOrWhiteSpace(message))
                return;

            if (_currentTab == null || _currentTab.ProcessManager == null)
            {
                await this.DisplaySelectableAlert("Error", "No active session. Select or create a session first.", "OK");
                return;
            }

            // Verifica se è il comando "exit"
            if (message.Trim().ToLower() == "exit")
            {
                await HandleExitCommandAsync();
                return;
            }

            // Invia il messaggio al processo Claude
            await _currentTab.ProcessManager.SendMessageAsync(message);

            // Salva il messaggio utente nel database
            if (_dbService != null)
            {
                await _dbService.SaveMessageAsync(_currentTab.SessionId, "user", message);
            }

            // Pulisci l'input
            InputEditor.Text = string.Empty;

            Log.Debug("Message sent: {Length} chars", message.Length);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send message");
            await this.DisplaySelectableAlert("Error", $"Failed to send message:\n{ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Gestisce il comando "exit" inviato dall'utente.
    /// Chiude la sessione in modo pulito e rimuove il tab.
    /// </summary>
    private async Task HandleExitCommandAsync()
    {
        try
        {
            if (_currentTab == null)
                return;

            Log.Information("Exit command received for session: {SessionId}", _currentTab.SessionId);

            var confirm = await this.DisplaySelectableAlert(
                "Confirm Exit",
                $"Close session '{_currentTab.TabTitle}'?\n\nThis will set the session status to 'closed' and remove the tab.",
                "Yes, Exit",
                "Cancel"
            );

            if (!confirm)
                return;

            // Invia il comando exit al processo
            if (_currentTab.ProcessManager != null)
            {
                await _currentTab.ProcessManager.SendExitCommandAsync();
                await Task.Delay(1000); // Attendi che il processo termini
            }

            // Chiudi il tab
            await CloseTabAsync(_currentTab, setStatusClosed: true);

            Log.Information("Session exited successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to handle exit command");
            await this.DisplaySelectableAlert("Error", $"Failed to exit session:\n{ex.Message}", "OK");
        }
    }

    private void OnPlanModeToggled(object? sender, ToggledEventArgs e)
    {
        _isPlanMode = e.Value;
        Log.Information("Plan mode toggled: {IsPlanMode}", _isPlanMode);
    }

    private async void OnThemeToggled(object? sender, ToggledEventArgs e)
    {
        _isDarkTheme = e.Value;

        if (_settingsService != null)
        {
            _settingsService.IsDarkTheme = _isDarkTheme;
        }

        if (LblTheme != null)
        {
            LblTheme.Text = _isDarkTheme ? "Dark" : "Light";
        }

        // Aggiorna tutte le WebView dei tab
        // TODO: Implementare refresh HTML per tutti i tab

        Log.Information("Theme changed to: {Theme}", _isDarkTheme ? "Dark" : "Light");
    }

    private async void OnContextInfoClicked(object? sender, EventArgs e)
    {
        // TODO: Implementare context info per tab corrente
        await this.DisplaySelectableAlert("Context Info", "Feature coming soon", "OK");
    }

    private async void OnViewSessionClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_currentTab == null)
            {
                await this.DisplaySelectableAlert("No Session", "No active session to view", "OK");
                return;
            }

            var viewer = new SessionViewer(_currentTab.WorkingDirectory);
            await Navigation.PushModalAsync(new NavigationPage(viewer));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open session viewer");
            await this.DisplaySelectableAlert("Error", $"Failed to open viewer:\n{ex.Message}", "OK");
        }
    }

    private async void OnTerminalClicked(object? sender, EventArgs e)
    {
        // TODO: Implementare apertura terminale per tab corrente
        await this.DisplaySelectableAlert("Terminal", "Feature coming soon", "OK");
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_settingsService == null)
            {
                await this.DisplaySelectableAlert("Error", "Settings service not available", "OK");
                return;
            }

            var settingsPage = new SettingsPage(_settingsService, OnSettingsChanged);
            await Navigation.PushModalAsync(new NavigationPage(settingsPage));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open settings");
            await this.DisplaySelectableAlert("Error", "Failed to open settings", "OK");
        }
    }

    private void OnSettingsChanged()
    {
        if (_settingsService != null)
        {
            var newTheme = _settingsService.IsDarkTheme;
            if (newTheme != _isDarkTheme)
            {
                _isDarkTheme = newTheme;
                SwitchTheme.IsToggled = _isDarkTheme;
            }
        }
    }

    #endregion

    #region Event Handlers - Process Events

    /// <summary>
    /// Handler per linee JSON ricevute dallo stdout del processo Claude.
    /// IMPORTANTE: Usa stdout SOLO come trigger per leggere dal file .jsonl (source of truth).
    /// Non usa il contenuto di jsonLine direttamente per evitare race conditions.
    /// </summary>
    private async void OnJsonLineReceived(SessionTabItem tabItem, string jsonLine)
    {
        try
        {
            // IGNORA il contenuto di stdout - usa solo come trigger
            var filePath = GetSessionFilePath(tabItem.SessionId, tabItem.WorkingDirectory);

            if (!File.Exists(filePath))
            {
                Log.Warning("Session file not found: {FilePath}", filePath);
                return;
            }

            // Leggi ultime righe dal file con retry (32KB buffer)
            var lastLines = await _dbService?.ReadLastLinesFromFileAsync(filePath, maxLines: 20, bufferSizeKb: 32)
                ?? new List<string>();

            foreach (var line in lastLines)
            {
                await ProcessMessageLineFromFileAsync(tabItem, line);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process JSON line for tab: {SessionId}", tabItem.SessionId);
        }
    }

    /// <summary>
    /// Processa una singola riga JSON dal file .jsonl.
    /// Rileva campi sconosciuti e salva nel database.
    /// </summary>
    private async Task ProcessMessageLineFromFileAsync(SessionTabItem tabItem, string jsonLine)
    {
        try
        {
            using var json = JsonDocument.Parse(jsonLine);
            var root = json.RootElement;

            var timestamp = ExtractTimestamp(root);

            // Skip duplicati basati su timestamp
            if (_lastProcessedTimestamp.HasValue && timestamp <= _lastProcessedTimestamp)
                return;

            _lastProcessedTimestamp = timestamp;

            // Rileva campi sconosciuti - MOSTRA DIALOG per decidere
            if (_dbService != null)
            {
                var unknownFields = _dbService.DetectUnknownFields(root, _dbService.GetKnownJsonFields());
                if (unknownFields.Count > 0)
                {
                    var uuid = ExtractUuid(root);
                    Log.Warning("Unknown fields detected in live message {Uuid}: {Fields}",
                        uuid, string.Join(", ", unknownFields));

                    // Mostra UnknownFieldsDialog completo con syntax highlighting
                    bool shouldContinue = await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        var dialog = new Views.UnknownFieldsDialog(jsonLine, unknownFields, uuid);
                        await Navigation.PushModalAsync(new NavigationPage(dialog));

                        // Aspetta che il dialog venga chiuso
                        while (Navigation.ModalStack.Count > 0)
                        {
                            await Task.Delay(100);
                        }

                        return dialog.ShouldContinue;
                    });

                    if (!shouldContinue)
                    {
                        Log.Information("Live message processing interrupted by user at message {Uuid}", uuid);
                        // Ferma il FileWatcher o gestisci l'interruzione
                        return;
                    }

                    // Se l'utente ha scelto "Continua", skip questo messaggio ma continua
                    return;
                }
            }

            // Salva nel DB - TUTTI i tipi (no filtro)
            await SaveMessageFromJson(tabItem.SessionId, root);

            // Aggiorna UI
            var type = root.GetProperty("type").GetString();
            Log.Debug("[{SessionId}] Processed: {Type}", tabItem.SessionId.Substring(0, 8), type);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                // TODO: Aggiornare WebView del tab con nuovo contenuto
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process message line from file");
        }
    }

    /// <summary>
    /// Costruisce il percorso del file .jsonl per una sessione.
    /// </summary>
    private string GetSessionFilePath(string sessionId, string workingDirectory)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var encodedDir = workingDirectory.Replace(":\\", "--").Replace("\\", "-");
        return Path.Combine(userProfile, ".claude", "projects", encodedDir, $"{sessionId}.jsonl");
    }

    /// <summary>
    /// Mostra il dialog per campi JSON sconosciuti.
    /// </summary>
    private async Task ShowUnknownFieldsDialogAsync(string jsonLine, List<string> unknownFields, string uuid)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var dialog = new UnknownFieldsDialog(jsonLine, unknownFields, uuid);
            await Navigation.PushModalAsync(new NavigationPage(dialog));
        });
    }

    /// <summary>
    /// Estrae il timestamp da un JsonElement.
    /// </summary>
    private DateTime ExtractTimestamp(JsonElement root)
    {
        if (root.TryGetProperty("timestamp", out var timestampProp))
        {
            if (timestampProp.ValueKind == JsonValueKind.Number)
            {
                var unixTime = timestampProp.GetInt64();
                return DateTimeOffset.FromUnixTimeMilliseconds(unixTime).DateTime;
            }
            else if (timestampProp.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(timestampProp.GetString(), out var parsed))
                    return parsed;
            }
        }

        return DateTime.UtcNow;
    }

    /// <summary>
    /// Estrae l'UUID da un JsonElement.
    /// </summary>
    private string ExtractUuid(JsonElement root)
    {
        if (root.TryGetProperty("uuid", out var uuidProp))
            return uuidProp.GetString() ?? "unknown";

        return "unknown";
    }

    /// <summary>
    /// Salva un messaggio dal JSON nel database con tutti i metadata.
    /// Supporta TUTTI i tipi di messaggio (user, assistant, tool_use, tool_result, summary, etc.)
    /// </summary>
    private async Task SaveMessageFromJson(string sessionId, JsonElement root)
    {
        try
        {
            if (_dbService == null)
                return;

            var type = root.GetProperty("type").GetString() ?? "unknown";
            var uuid = root.TryGetProperty("uuid", out var u) ? u.GetString() : null;
            var timestamp = ExtractTimestamp(root);

            // Estrai metadata completi
            string? parentUuid = root.TryGetProperty("parentUuid", out var pu) ? pu.GetString() : null;
            string? version = root.TryGetProperty("version", out var v) ? v.GetString() : null;
            string? gitBranch = root.TryGetProperty("gitBranch", out var gb) ? gb.GetString() : null;
            bool? isSidechain = root.TryGetProperty("isSidechain", out var isc) ? isc.GetBoolean() : null;
            string? userType = root.TryGetProperty("userType", out var ut) ? ut.GetString() : null;
            string? cwd = root.TryGetProperty("cwd", out var c) ? c.GetString() : null;

            string? requestId = null;
            string? model = null;
            string? usageJson = null;
            int? cache5mTokens = null;
            int? cache1hTokens = null;
            string? serviceTier = null;

            // Per messaggi assistant, estrai requestId, model e usage
            if (root.TryGetProperty("message", out var messageProp))
            {
                if (messageProp.TryGetProperty("id", out var idProp))
                    requestId = idProp.GetString();

                if (messageProp.TryGetProperty("model", out var modelProp))
                    model = modelProp.GetString();

                if (messageProp.TryGetProperty("usage", out var usageProp))
                {
                    usageJson = usageProp.GetRawText();

                    // Estrai campi cache Anthropic
                    if (usageProp.TryGetProperty("cache_creation", out var cacheCreation))
                    {
                        if (cacheCreation.TryGetProperty("ephemeral_5m_input_tokens", out var cache5m))
                            cache5mTokens = cache5m.GetInt32();

                        if (cacheCreation.TryGetProperty("ephemeral_1h_input_tokens", out var cache1h))
                            cache1hTokens = cache1h.GetInt32();
                    }

                    if (usageProp.TryGetProperty("service_tier", out var tierProp))
                        serviceTier = tierProp.GetString();
                }
            }

            // Estrai contenuto
            string content = ExtractBasicContent(root);

            // Salva con tutti i metadata
            await _dbService.SaveMessageAsync(
                sessionId,
                type,
                content,
                timestamp,
                uuid,
                parentUuid,
                version,
                gitBranch,
                isSidechain,
                userType,
                cwd,
                requestId,
                model,
                usageJson,
                type, // messageType = type
                cache5mTokens,
                cache1hTokens,
                serviceTier
            );
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save message from JSON");
        }
    }

    /// <summary>
    /// Estrae il contenuto testuale base da un JsonElement.
    /// Supporta vari formati (message.content array, text diretto, etc.)
    /// </summary>
    private string ExtractBasicContent(JsonElement root)
    {
        // Caso 1: message.content array (per assistant/user)
        if (root.TryGetProperty("message", out var messageProp) &&
            messageProp.TryGetProperty("content", out var contentProp))
        {
            if (contentProp.ValueKind == JsonValueKind.Array)
            {
                var texts = new List<string>();
                foreach (var item in contentProp.EnumerateArray())
                {
                    if (item.TryGetProperty("text", out var textProp))
                        texts.Add(textProp.GetString() ?? "");
                }
                return string.Join("\n", texts);
            }
            else if (contentProp.ValueKind == JsonValueKind.String)
            {
                return contentProp.GetString() ?? "";
            }
        }

        // Caso 2: campo "text" diretto
        if (root.TryGetProperty("text", out var textField))
            return textField.GetString() ?? "";

        // Caso 3: summary.text
        if (root.TryGetProperty("summary", out var summaryProp) &&
            summaryProp.TryGetProperty("text", out var summaryText))
            return summaryText.GetString() ?? "";

        // Fallback: ritorna JSON completo
        return root.GetRawText();
    }

    /// <summary>
    /// Handler per completamento processo.
    /// </summary>
    private void OnProcessCompleted(SessionTabItem tabItem, ProcessCompletedEventArgs e)
    {
        Log.Information("[{SessionId}] Process completed. ExitCode: {ExitCode}, WasKilled: {WasKilled}",
            tabItem.SessionId, e.ExitCode, e.WasKilled);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            BtnStop.IsEnabled = false;
        });
    }

    /// <summary>
    /// Handler per errori ricevuti dallo stderr.
    /// </summary>
    private void OnErrorReceived(SessionTabItem tabItem, string error)
    {
        Log.Warning("[{SessionId}] Error: {Error}", tabItem.SessionId, error);
    }

    /// <summary>
    /// Handler per cambio stato IsRunning del processo.
    /// </summary>
    private void OnIsRunningChanged(SessionTabItem tabItem, bool isRunning)
    {
        tabItem.IsRunning = isRunning;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (tabItem == _currentTab)
            {
                BtnStop.IsEnabled = isRunning;
            }
        });

        Log.Debug("[{SessionId}] IsRunning changed to: {IsRunning}", tabItem.SessionId, isRunning);
    }

    #endregion
}
