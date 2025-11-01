using ClaudeCodeMAUI.Models;
using ClaudeCodeMAUI.Services;
using ClaudeCodeMAUI.Utilities;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ClaudeCodeMAUI.Views
{
    /// <summary>
    /// Modalità di filtro per Session ID.
    /// </summary>
    public enum SessionIdFilterMode
    {
        /// <summary>Mostra tutti i messaggi (con e senza session ID)</summary>
        All,
        /// <summary>Mostra solo messaggi con session ID valido</summary>
        OnlyWithSessionId,
        /// <summary>Mostra solo messaggi senza session ID (vuoto o null)</summary>
        OnlyWithoutSessionId
    }

    /// <summary>
    /// Viewer per esplorare il file JSONL di una sessione Claude Code.
    /// Mostra il JSON raw a sinistra e il contenuto renderizzato a destra.
    /// Supporta timeline unificata con main session + agent messages.
    /// </summary>
    public partial class SessionViewer : ContentPage
    {
        private int _currentIndex = 0;
        private List<UnifiedMessage> _timeline = new List<UnifiedMessage>();
        private List<UnifiedMessage> _fullTimeline = new List<UnifiedMessage>(); // Timeline completa per filtri
        private MarkdownHtmlRenderer _htmlRenderer;
        private readonly string? _sessionId;
        private readonly string _workingDirectory;
        private bool _filterOnlyMain = false; // Filtro Main/All attivo
        private bool _showTools = true; // Mostra/nascondi tool calls e results
        private SessionIdFilterMode _sessionIdFilter = SessionIdFilterMode.All; // Filtro per session ID

        /// <summary>
        /// Costruttore del viewer.
        /// </summary>
        /// <param name="sessionId">ID della sessione da visualizzare</param>
        /// <param name="workingDirectory">Working directory del progetto</param>
        public SessionViewer(string sessionId, string workingDirectory)
        {
            InitializeComponent();

            _sessionId = sessionId;
            _workingDirectory = workingDirectory;
            _htmlRenderer = new MarkdownHtmlRenderer();

            try
            {
                // Aggiorna label con session ID
                SessionIdLabel.Text = $"Session: {sessionId.Substring(0, 8)}...";

                // Carica la timeline unificata (main session + agents)
                Log.Information("Loading unified timeline for {SessionId}", sessionId);
                _timeline = TimelineMerger.MergeTimeline(sessionId, workingDirectory);

                Log.Information("Loaded {Count} messages in timeline", _timeline.Count);

                if (_timeline.Count == 0)
                {
                    DisplayAlert("No Messages", "The session file is empty or could not be parsed.", "OK");
                    return;
                }

                // Mostra statistiche nella label
                var mainCount = _timeline.Count(m => m.Source == MessageSource.MainSession);
                var agentCount = _timeline.Count(m => m.Source == MessageSource.Agent);
                SessionIdLabel.Text = $"Session: {sessionId.Substring(0, 8)}... ({mainCount} main + {agentCount} agent)";

                // Mostra il primo messaggio
                DisplayCurrentMessage();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load session timeline");
                DisplayAlert("Error", $"Failed to load session timeline:\n\n{ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Costruttore semplificato per visualizzare TUTTE le sessioni della working directory.
        /// </summary>
        /// <param name="workingDirectory">Working directory del progetto</param>
        public SessionViewer(string workingDirectory)
        {
            InitializeComponent();

            _sessionId = null; // Non c'è una singola sessione
            _workingDirectory = workingDirectory;
            _htmlRenderer = new MarkdownHtmlRenderer();

            try
            {
                // Carica TUTTE le sessioni
                Log.Information("Loading all sessions for {WorkingDir}", workingDirectory);
                _timeline = TimelineMerger.MergeAllSessions(workingDirectory);
                _fullTimeline = _timeline; // Salva per filtri

                Log.Information("Loaded {Count} total messages", _timeline.Count);

                if (_timeline.Count == 0)
                {
                    DisplayAlert("No Messages", "No sessions found.", "OK");
                    return;
                }

                // Mostra statistiche
                var sessions = _timeline.Select(m => m.SessionId).Distinct().Count();
                var mainCount = _timeline.Count(m => m.Source == MessageSource.MainSession);
                var agentCount = _timeline.Count(m => m.Source == MessageSource.Agent);

                SessionIdLabel.Text = $"All Sessions ({sessions} sessions: {mainCount} main + {agentCount} agent)";

                DisplayCurrentMessage();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load all sessions");
                DisplayAlert("Error", $"Failed to load sessions:\n\n{ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Visualizza il messaggio corrente in entrambi i pannelli.
        /// Distingue visivamente tra messaggi main e agent.
        /// </summary>
        private void DisplayCurrentMessage()
        {
            try
            {
                var unifiedMessage = _timeline[_currentIndex];
                var message = unifiedMessage.RawMessage;

                // Determina colore di sfondo per pannello sinistro in base alla source
                var jsonBgColor = unifiedMessage.Source == MessageSource.MainSession
                    ? "#1E1E1E"  // Nero per main session
                    : "#2D2D30"; // Grigio più chiaro per agent

                // PANNELLO SINISTRO: JSON raw formattato
                var jsonFormatted = JsonSerializer.Serialize(message, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                });
                JsonEditor.Text = jsonFormatted;
                JsonEditor.BackgroundColor = Color.FromArgb(jsonBgColor);

                // PANNELLO DESTRO: Contenuto messaggio estratto e renderizzato
                var messageContent = MessageContentExtractor.ExtractContent(message);

                // Aggiungi header con informazioni sulla source del messaggio
                var sourceHeader = BuildSourceHeader(unifiedMessage);
                var htmlContent = _htmlRenderer.RenderMarkdown(sourceHeader + "\n\n---\n\n" + messageContent);

                // Colore bordo in base alla source
                var borderColor = unifiedMessage.Source == MessageSource.MainSession
                    ? "#0078D4"  // Blu per main
                    : "#28A745"; // Verde per agent

                // Avvolgi l'HTML in un template completo con charset UTF-8 e highlight.js
                var htmlPage = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">

    <!-- Highlight.js CSS per syntax highlighting -->
    <link rel=""stylesheet"" href=""https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/styles/github.min.css"">

    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif;
            padding: 15px;
            line-height: 1.6;
            color: #24292f;
            background-color: #ffffff;
            border-left: 4px solid {borderColor};
        }}
        code {{
            background-color: #f6f8fa;
            padding: 2px 6px;
            border-radius: 3px;
            font-family: 'Consolas', 'Monaco', monospace;
            font-size: 0.9em;
        }}
        pre {{
            background-color: #f6f8fa;
            padding: 16px;
            border-radius: 6px;
            overflow-x: auto;
            margin: 10px 0;
        }}
        pre code {{
            background-color: transparent;
            padding: 0;
        }}
        .source-header {{
            background-color: #f0f6fc;
            padding: 10px;
            border-radius: 6px;
            border-left: 3px solid {borderColor};
            margin-bottom: 15px;
        }}
    </style>
</head>
<body>
{htmlContent}

<!-- Highlight.js JavaScript per syntax highlighting -->
<script src=""https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/highlight.min.js""></script>
<script src=""https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/languages/markdown.min.js""></script>
<script src=""https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/languages/json.min.js""></script>
<script src=""https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/languages/bash.min.js""></script>
<script src=""https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/languages/csharp.min.js""></script>
<script>hljs.highlightAll();</script>
</body>
</html>";

                MessageWebView.Source = new HtmlWebViewSource { Html = htmlPage };

                // Aggiorna contatore e pulsanti con icona source
                var sourceIcon = unifiedMessage.Source == MessageSource.MainSession ? "📘" : "🤖";
                var sourceText = unifiedMessage.Source == MessageSource.MainSession
                    ? "Main"
                    : $"Agent ({unifiedMessage.AgentName})";

                CounterLabel.Text = $"Message {_currentIndex + 1} / {_timeline.Count} [{sourceIcon} {sourceText}]";
                BtnFirst.IsEnabled = _currentIndex > 0;
                BtnPrevious.IsEnabled = _currentIndex > 0;
                BtnNext.IsEnabled = _currentIndex < _timeline.Count - 1;
                BtnLast.IsEnabled = _currentIndex < _timeline.Count - 1;

                Log.Debug("Displayed message {Index} / {Total} (Source: {Source})",
                    _currentIndex + 1, _timeline.Count, unifiedMessage.Source);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to display message at index {Index}", _currentIndex);
                DisplayAlert("Error", $"Failed to display message:\n\n{ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Costruisce l'header informativo con i dettagli sulla source del messaggio.
        /// </summary>
        /// <param name="unifiedMessage">Messaggio unificato da cui estrarre le info</param>
        /// <returns>Markdown formattato per l'header</returns>
        private string BuildSourceHeader(UnifiedMessage unifiedMessage)
        {
            if (unifiedMessage.Source == MessageSource.MainSession)
            {
                return $@"<div class=""source-header"">
📘 <strong>Main Session</strong><br/>
<small>Original Index: {unifiedMessage.OriginalIndex + 1}</small><br/>
<small>Timestamp: {unifiedMessage.Timestamp:yyyy-MM-dd HH:mm:ss}</small>
</div>";
            }
            else
            {
                return $@"<div class=""source-header"">
🤖 <strong>Agent: {unifiedMessage.AgentName}</strong><br/>
<small>Agent ID: <code>{unifiedMessage.AgentId}</code></small><br/>
<small>Original Index: {unifiedMessage.OriginalIndex + 1}</small><br/>
<small>Timestamp: {unifiedMessage.Timestamp:yyyy-MM-dd HH:mm:ss}</small>
</div>";
            }
        }

        /// <summary>
        /// Handler per il pulsante "Previous".
        /// </summary>
        private void OnPreviousClicked(object sender, EventArgs e)
        {
            if (_currentIndex > 0)
            {
                _currentIndex--;
                DisplayCurrentMessage();
            }
        }

        /// <summary>
        /// Handler per il pulsante "Next".
        /// </summary>
        private void OnNextClicked(object sender, EventArgs e)
        {
            if (_currentIndex < _timeline.Count - 1)
            {
                _currentIndex++;
                DisplayCurrentMessage();
            }
        }

        /// <summary>
        /// Handler per il pulsante "First".
        /// Salta al primo messaggio della sessione.
        /// </summary>
        private void OnFirstClicked(object sender, EventArgs e)
        {
            if (_currentIndex > 0)
            {
                _currentIndex = 0;
                DisplayCurrentMessage();
            }
        }

        /// <summary>
        /// Handler per il pulsante "Last".
        /// Salta all'ultimo messaggio della sessione.
        /// </summary>
        private void OnLastClicked(object sender, EventArgs e)
        {
            if (_currentIndex < _timeline.Count - 1)
            {
                _currentIndex = _timeline.Count - 1;
                DisplayCurrentMessage();
            }
        }

        /// <summary>
        /// Handler per il pulsante "Close".
        /// Chiude la finestra modale.
        /// </summary>
        private async void OnCloseClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }

        /// <summary>
        /// Alterna il filtro Main/All.
        /// Disponibile solo quando sono caricate tutte le sessioni (non singola sessione).
        /// </summary>
        public void OnFilterMainClicked(object sender, EventArgs e)
        {
            if (_fullTimeline.Count == 0)
            {
                // Nessun filtro disponibile se non ci sono dati
                return;
            }

            _filterOnlyMain = !_filterOnlyMain;

            // Aggiorna UI del pulsante
            if (_filterOnlyMain)
            {
                BtnFilterMain.Text = "🌐 Show All";
                BtnFilterMain.BackgroundColor = Color.FromArgb("#28A745"); // Verde
            }
            else
            {
                BtnFilterMain.Text = "📘 Main Only";
                BtnFilterMain.BackgroundColor = Color.FromArgb("#0078D4"); // Blu
            }

            // Applica filtri
            ApplyFilters();

            // Reset a primo messaggio
            _currentIndex = 0;

            // Aggiorna statistiche nel label
            UpdateSessionLabel();

            // Mostra messaggio solo se ci sono risultati
            if (_timeline.Count > 0)
            {
                DisplayCurrentMessage();
            }
            else
            {
                // Nessun risultato con i filtri correnti
                JsonEditor.Text = "No messages match current filters";
                DisplayAlert("No Results", "No messages match the current filter combination.", "OK");
            }
        }

        /// <summary>
        /// Alterna il filtro Show/Hide Tools.
        /// Nasconde i messaggi che contengono tool_use o tool_result.
        /// </summary>
        public void OnFilterToolsClicked(object sender, EventArgs e)
        {
            if (_fullTimeline.Count == 0)
            {
                return;
            }

            _showTools = !_showTools;

            // Aggiorna UI del pulsante
            if (_showTools)
            {
                BtnFilterTools.Text = "🔧 Hide Tools";
                BtnFilterTools.BackgroundColor = Color.FromArgb("#0078D4"); // Blu
            }
            else
            {
                BtnFilterTools.Text = "🔧 Show Tools";
                BtnFilterTools.BackgroundColor = Color.FromArgb("#FF8C00"); // Arancione
            }

            // Applica filtri
            ApplyFilters();

            // Reset a primo messaggio
            _currentIndex = 0;

            // Aggiorna statistiche nel label
            UpdateSessionLabel();

            // Mostra messaggio solo se ci sono risultati
            if (_timeline.Count > 0)
            {
                DisplayCurrentMessage();
            }
            else
            {
                // Nessun risultato con i filtri correnti
                JsonEditor.Text = "No messages match current filters";
                DisplayAlert("No Results", "No messages match the current filter combination.", "OK");
            }
        }

        /// <summary>
        /// Cicla tra le tre modalità di filtro Session ID: All → Only With → Only Without → All.
        /// </summary>
        public void OnFilterSessionIdClicked(object sender, EventArgs e)
        {
            if (_fullTimeline.Count == 0)
            {
                return;
            }

            // Cicla tra le tre modalità
            _sessionIdFilter = _sessionIdFilter switch
            {
                SessionIdFilterMode.All => SessionIdFilterMode.OnlyWithSessionId,
                SessionIdFilterMode.OnlyWithSessionId => SessionIdFilterMode.OnlyWithoutSessionId,
                SessionIdFilterMode.OnlyWithoutSessionId => SessionIdFilterMode.All,
                _ => SessionIdFilterMode.All
            };

            // Aggiorna UI del pulsante
            switch (_sessionIdFilter)
            {
                case SessionIdFilterMode.All:
                    BtnFilterSessionId.Text = "🆔 All IDs";
                    BtnFilterSessionId.BackgroundColor = Color.FromArgb("#0078D4"); // Blu
                    break;
                case SessionIdFilterMode.OnlyWithSessionId:
                    BtnFilterSessionId.Text = "✅ With ID";
                    BtnFilterSessionId.BackgroundColor = Color.FromArgb("#28A745"); // Verde
                    break;
                case SessionIdFilterMode.OnlyWithoutSessionId:
                    BtnFilterSessionId.Text = "⚠️ No ID";
                    BtnFilterSessionId.BackgroundColor = Color.FromArgb("#DC3545"); // Rosso
                    break;
            }

            // Applica filtri
            ApplyFilters();

            // Reset a primo messaggio
            _currentIndex = 0;

            // Aggiorna statistiche nel label
            UpdateSessionLabel();

            // Mostra messaggio solo se ci sono risultati
            if (_timeline.Count > 0)
            {
                DisplayCurrentMessage();
            }
            else
            {
                // Nessun risultato con i filtri correnti
                JsonEditor.Text = "No messages match current filters";
                DisplayAlert("No Results", "No messages match the current filter combination.", "OK");
            }
        }

        /// <summary>
        /// Aggiorna il label con le statistiche della timeline corrente.
        /// </summary>
        private void UpdateSessionLabel()
        {
            if (_sessionId != null)
            {
                // Modalità sessione singola
                var mainCount = _timeline.Count(m => m.Source == MessageSource.MainSession);
                var agentCount = _timeline.Count(m => m.Source == MessageSource.Agent);
                SessionIdLabel.Text = $"Session: {_sessionId.Substring(0, 8)}... ({mainCount} main + {agentCount} agent)";
            }
            else
            {
                // Modalità tutte le sessioni
                var sessions = _timeline.Select(m => m.SessionId).Distinct().Count();
                var mainCount = _timeline.Count(m => m.Source == MessageSource.MainSession);
                var agentCount = _timeline.Count(m => m.Source == MessageSource.Agent);

                SessionIdLabel.Text = _filterOnlyMain
                    ? $"Main Only ({sessions} sessions: {mainCount} messages)"
                    : $"All Sessions ({sessions} sessions: {mainCount} main + {agentCount} agent)";
            }
        }

        /// <summary>
        /// Verifica se un messaggio contiene tool calls o tool results.
        /// </summary>
        /// <param name="message">Messaggio da verificare</param>
        /// <returns>True se il messaggio contiene tool_use o tool_result</returns>
        private bool ContainsTools(UnifiedMessage message)
        {
            try
            {
                var rawMessage = message.RawMessage;

                // Verifica che esista il campo "message"
                if (!rawMessage.TryGetProperty("message", out var messageElement))
                {
                    return false;
                }

                // Verifica che esista il campo "content"
                if (!messageElement.TryGetProperty("content", out var content))
                {
                    return false;
                }

                // Se content è un array, cerca tool_use o tool_result
                if (content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in content.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var typeElement))
                        {
                            var type = typeElement.GetString();
                            if (type == "tool_use" || type == "tool_result")
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to check if message contains tools");
                return false;
            }
        }

        /// <summary>
        /// Verifica se il messaggio JSON raw contiene un campo session_id valido.
        /// </summary>
        /// <param name="message">Messaggio da verificare</param>
        /// <returns>True se il JSON ha un campo session_id non vuoto</returns>
        private bool HasSessionIdInJson(UnifiedMessage message)
        {
            try
            {
                var rawMessage = message.RawMessage;

                // Cerca il campo "session_id" nel JSON root
                if (rawMessage.TryGetProperty("session_id", out var sessionIdElement))
                {
                    var sessionId = sessionIdElement.GetString();
                    return !string.IsNullOrWhiteSpace(sessionId);
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to check session_id in JSON");
                return false;
            }
        }

        /// <summary>
        /// Applica i filtri correnti alla timeline completa.
        /// Filtra per Main/All, Show/Hide Tools e Session ID.
        /// </summary>
        private void ApplyFilters()
        {
            var filtered = _fullTimeline.AsEnumerable();

            // Filtro Main/All
            if (_filterOnlyMain)
            {
                filtered = filtered.Where(m => m.Source == MessageSource.MainSession);
            }

            // Filtro Show/Hide Tools
            if (!_showTools)
            {
                filtered = filtered.Where(m => !ContainsTools(m));
            }

            // Filtro Session ID (controlla il JSON raw, non il campo UnifiedMessage.SessionId)
            if (_sessionIdFilter == SessionIdFilterMode.OnlyWithSessionId)
            {
                filtered = filtered.Where(m => HasSessionIdInJson(m));
            }
            else if (_sessionIdFilter == SessionIdFilterMode.OnlyWithoutSessionId)
            {
                filtered = filtered.Where(m => !HasSessionIdInJson(m));
            }
            // Se All, non filtrare nulla

            _timeline = filtered.ToList();

            Log.Information("Filters applied: Main={FilterMain}, Tools={ShowTools}, SessionId={SessionIdFilter}, Result={Count} messages",
                _filterOnlyMain, _showTools, _sessionIdFilter, _timeline.Count);
        }
    }
}
