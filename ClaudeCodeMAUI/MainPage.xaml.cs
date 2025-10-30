using ClaudeCodeMAUI.Services;
using ClaudeCodeMAUI.Models;
using ClaudeCodeMAUI.Utilities;
using Serilog;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Diagnostics;

namespace ClaudeCodeMAUI;

public partial class MainPage : ContentPage
{
    private readonly DbService? _dbService;
    private ClaudeProcessManager? _processManager;
    private StreamJsonParser? _parser;
    private ConversationSession? _currentSession;
    private bool _isPlanMode;
    private MarkdownHtmlRenderer? _htmlRenderer;
    private bool _isDarkTheme = true;  // Default tema scuro
    private bool _isWebViewReady = false;
    private bool _sessionInitialized = false;  // Flag per mostrare messaggio init solo prima volta

    // ===== NUOVO APPROCCIO: "Reload Full Page" =====
    // Buffer per memorizzare tutto l'HTML della conversazione
    // Ogni nuovo messaggio viene aggiunto al buffer e l'intera pagina HTML viene rigenerata
    private System.Text.StringBuilder _conversationHtml = new System.Text.StringBuilder();

    public MainPage()
    {
        InitializeComponent();
        Log.Information("MainPage initialized");
    }

    public MainPage(DbService dbService) : this()
    {
        _dbService = dbService;
        Log.Information("MainPage initialized with DbService");
    }

    private void OnNewConversationClicked(object? sender, EventArgs e)
    {
        StartNewConversation();
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

    private async void OnThemeToggled(object? sender, ToggledEventArgs e)
    {
        _isDarkTheme = e.Value;
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

    protected override void OnAppearing()
    {
        base.OnAppearing();

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
                ConversationWebView.Navigated += (s, e) =>
                {
                    _isWebViewReady = true;
                    Log.Information("WebView ready and navigated to: {Url}", e.Url);
                };

                Log.Information("WebView initialized with HTML file from disk");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize WebView with HTML file");
            }
        }
    }

    private void OnSendMessage(object? sender, EventArgs e)
    {
        var message = InputEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(message))
            return;

        SendMessageAsync(message);
        InputEntry.Text = string.Empty;
    }

    private async void StartNewConversation()
    {
        try
        {
            Log.Information("StartNewConversation called");

            // Reset flag sessione inizializzata
            _sessionInitialized = false;

            // Crea nuova sessione
            _currentSession = new ConversationSession
            {
                IsPlanMode = _isPlanMode,
                Status = "active",
                TabTitle = "New Conversation"
            };
            Log.Information("Session created");

            // Crea parser
            _parser = new StreamJsonParser();
            _parser.SessionInitialized += OnSessionInitialized;
            _parser.TextReceived += OnTextReceived;
            _parser.ToolCallReceived += OnToolCallReceived;
            _parser.ToolResultReceived += OnToolResultReceived;
            _parser.MetadataReceived += OnMetadataReceived;
            Log.Information("Parser created");

            // Crea process manager
            _processManager = new ClaudeProcessManager(_isPlanMode, null, null);
            _processManager.JsonLineReceived += OnJsonLineReceived;
            _processManager.ErrorReceived += OnErrorReceived;
            _processManager.ProcessCompleted += OnProcessCompleted;
            Log.Information("Process manager created");

            // Avvia processo
            Log.Information("Starting Claude process...");
            _processManager.Start();
            Log.Information("Claude process started");

            // Aggiorna UI
            BtnStop.IsEnabled = true;
            LblStatus.Text = "Running...";
            LblStatus.TextColor = Colors.Green;
            MetadataLabel.Text = string.Empty;

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

                // Invia messaggio a Claude
                await _processManager.SendMessageAsync(message);
                Log.Debug("Message sent: {Length} chars", message.Length);
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

                // Aggiorna la metadata bar (tutto su una riga, allineato a destra, colore giallo)
                MetadataLabel.Text = $"Duration: {e.DurationMs}ms  |  Cost: ${e.TotalCostUsd:F4}  |  Tokens: {e.InputTokens} in / {e.OutputTokens} out  |  Turns: {e.NumTurns}";
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

}
