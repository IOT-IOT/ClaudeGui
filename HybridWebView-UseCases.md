# HybridWebView: Casi d'Uso e Linee Guida per ClaudeCodeMAUI

**Versione**: 1.0
**Data**: 2025-01-05
**Autore**: Documentazione tecnica per migrazione futura

---

## 📚 Indice

1. [Introduzione](#introduzione)
2. [Architettura e Comunicazione](#architettura-e-comunicazione)
3. [Casi d'Uso Dettagliati](#casi-duso-dettagliati)
4. [Confronto WebView vs HybridWebView](#confronto-webview-vs-hybridwebview)
5. [Roadmap Suggerita](#roadmap-suggerita)
6. [Implementazione Pratica](#implementazione-pratica)
7. [Risorse e Riferimenti](#risorse-e-riferimenti)

---

## 🎯 Introduzione

### Cos'è HybridWebView?

`HybridWebView` è un controllo .NET MAUI che consente la **comunicazione bidirezionale** tra codice C# e JavaScript in tempo reale. A differenza del `WebView` standard (che mostra solo contenuto HTML statico), HybridWebView permette:

- ✅ **JavaScript → C#**: Chiamare metodi C# dal codice JavaScript
- ✅ **C# → JavaScript**: Invocare funzioni JavaScript dal codice C#
- ✅ **Event-driven**: Comunicazione asincrona basata su eventi
- ✅ **Type-safe**: Serializzazione/deserializzazione automatica con System.Text.Json

### Situazione Attuale di ClaudeCodeMAUI

**Componente attuale**: `WebView` standard
**File**: `Views/SessionTabContent.xaml`
**Scopo**: Visualizzare messaggi markdown convertiti in HTML

```xml
<WebView x:Name="ConversationWebView"
         VerticalOptions="FillAndExpand"
         HorizontalOptions="FillAndExpand" />
```

**Flusso attuale**:
```
Messaggio Claude (Markdown)
    ↓
MarkdownHtmlRenderer.GenerateFullPage()
    ↓
HTML completo con CSS inline
    ↓
WebView.Source = new HtmlWebViewSource { Html = htmlContent }
    ↓
Visualizzazione (READ-ONLY)
```

### Quando Considerare la Migrazione

**Rimani con WebView SE**:
- ✅ I messaggi sono principalmente **read-only**
- ✅ Le interazioni sono **semplici** (scroll, copia testo, click link)
- ✅ Non serve **editing avanzato** o **interazioni complesse**
- ✅ **Performance attuali** sono soddisfacenti

**Passa a HybridWebView QUANDO**:
- 🔄 Serve **comunicazione bidirezionale** C# ↔ JavaScript
- ⚡ Serve **performance UI superiore** (virtualizzazione, lazy loading)
- 🎨 Vuoi integrare **librerie JavaScript complesse** (Monaco Editor, Chart.js, D3.js)
- 👥 Aggiungi **funzionalità collaborative**
- 🤖 Implementi **AI-powered features** interattive

---

## 🏗️ Architettura e Comunicazione

### Diagramma del Flusso di Comunicazione

```
┌─────────────────────────────────────────────────────────────┐
│                    ClaudeCodeMAUI (C#)                       │
│  ┌────────────────────────────────────────────────────┐     │
│  │  SessionTabContent.xaml.cs                          │     │
│  │  ┌──────────────────────────────────────────────┐  │     │
│  │  │  HybridWebView hybridWebView                 │  │     │
│  │  │                                               │  │     │
│  │  │  • RawMessageReceived event handler          │  │     │
│  │  │    └─> Riceve messaggi da JavaScript         │  │     │
│  │  │                                               │  │     │
│  │  │  • InvokeJavaScriptAsync()                   │  │     │
│  │  │    └─> Chiama funzioni JavaScript            │  │     │
│  │  │                                               │  │     │
│  │  │  • EvaluateJavaScriptAsync()                 │  │     │
│  │  │    └─> Esegue JS e restituisce risultato     │  │     │
│  │  └──────────────────────────────────────────────┘  │     │
│  └────────────────────────────────────────────────────┘     │
└─────────────────────────┬───────────────────────────────────┘
                          │ ↕ Bidirezionale
┌─────────────────────────┴───────────────────────────────────┐
│                JavaScript (WebView Content)                  │
│  ┌────────────────────────────────────────────────────┐     │
│  │  conversation-app.js                                │     │
│  │  ┌──────────────────────────────────────────────┐  │     │
│  │  │  window.chrome.webview.postMessage()         │  │     │
│  │  │    └─> Invia messaggi a C#                   │  │     │
│  │  │                                               │  │     │
│  │  │  window.receiveMessageFromCSharp()           │  │     │
│  │  │    └─> Riceve chiamate da C#                 │  │     │
│  │  └──────────────────────────────────────────────┘  │     │
│  └────────────────────────────────────────────────────┘     │
└─────────────────────────────────────────────────────────────┘
```

### Pattern di Comunicazione

#### 1️⃣ JavaScript → C# (Invia Evento)

**JavaScript**:
```javascript
function onMessageReaction(messageId, emoji) {
    window.chrome.webview.postMessage({
        action: 'addReaction',
        messageId: messageId,
        emoji: emoji,
        timestamp: Date.now()
    });
}
```

**C#**:
```csharp
hybridWebView.RawMessageReceived += async (sender, e) =>
{
    var message = JsonSerializer.Deserialize<WebViewMessage>(e.Message);

    switch (message.Action)
    {
        case "addReaction":
            await HandleReactionAsync(message.MessageId, message.Emoji);
            break;
        // ... altri casi
    }
};
```

#### 2️⃣ C# → JavaScript (Chiama Funzione)

**C#**:
```csharp
// Opzione 1: InvokeJavaScriptAsync (fire-and-forget)
await hybridWebView.InvokeJavaScriptAsync(
    $"updateTokenBudget({remaining}, {total})"
);

// Opzione 2: EvaluateJavaScriptAsync (con risultato)
var scrollPosition = await hybridWebView.EvaluateJavaScriptAsync(
    "window.scrollY"
);
```

**JavaScript**:
```javascript
function updateTokenBudget(remaining, total) {
    document.getElementById('token-budget').textContent =
        `${remaining.toLocaleString()} / ${total.toLocaleString()}`;
}
```

---

## 💡 Casi d'Uso Dettagliati

### 1. Sistema di Reazioni ai Messaggi 👍❤️🔥

**Descrizione**: Permetti agli utenti di reagire ai messaggi di Claude con emoji, salvando le reazioni nel database.

**Valore**: Alta interattività, feedback emotivo, bookmarking intelligente
**Complessità**: 🟢 Bassa (1-2 giorni)
**Priorità**: ⭐⭐⭐ Alta (Quick Win)

#### Implementazione

**JavaScript** (`conversation-reactions.js`):
```javascript
class MessageReactions {
    constructor() {
        this.reactions = ['👍', '👎', '❤️', '🔥', '🤔', '💡', '⭐'];
        this.initReactionButtons();
    }

    initReactionButtons() {
        document.querySelectorAll('.message-container').forEach(container => {
            const messageId = container.dataset.messageId;
            const reactionBar = this.createReactionBar(messageId);
            container.appendChild(reactionBar);
        });
    }

    createReactionBar(messageId) {
        const bar = document.createElement('div');
        bar.className = 'reaction-bar';

        this.reactions.forEach(emoji => {
            const btn = document.createElement('button');
            btn.textContent = emoji;
            btn.className = 'reaction-btn';
            btn.onclick = () => this.addReaction(messageId, emoji);
            bar.appendChild(btn);
        });

        return bar;
    }

    addReaction(messageId, emoji) {
        // Invia a C#
        window.chrome.webview.postMessage({
            action: 'addReaction',
            messageId: messageId,
            emoji: emoji,
            timestamp: Date.now()
        });

        // Update UI ottimistico
        this.updateReactionUI(messageId, emoji);
    }

    updateReactionUI(messageId, emoji) {
        const container = document.querySelector(`[data-message-id="${messageId}"]`);
        const existingReaction = container.querySelector(`.reaction-${emoji}`);

        if (existingReaction) {
            const count = parseInt(existingReaction.dataset.count) + 1;
            existingReaction.dataset.count = count;
            existingReaction.textContent = `${emoji} ${count}`;
        } else {
            const reaction = document.createElement('span');
            reaction.className = `reaction reaction-${emoji}`;
            reaction.dataset.count = 1;
            reaction.textContent = `${emoji} 1`;
            container.querySelector('.reactions-display').appendChild(reaction);
        }
    }
}

// Inizializza
const reactionSystem = new MessageReactions();
```

**C#** (`SessionTabContent.xaml.cs`):
```csharp
public partial class SessionTabContent : ContentView
{
    private readonly DbService _dbService;

    private void InitializeHybridWebView()
    {
        hybridWebView.RawMessageReceived += OnWebViewMessageReceived;
    }

    private async void OnWebViewMessageReceived(object sender, HybridWebViewRawMessageReceivedEventArgs e)
    {
        try
        {
            var message = JsonSerializer.Deserialize<WebViewMessage>(e.Message);

            switch (message.Action)
            {
                case "addReaction":
                    await HandleReactionAsync(message);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process WebView message");
        }
    }

    private async Task HandleReactionAsync(WebViewMessage message)
    {
        Log.Information("Adding reaction: {Emoji} to message {MessageId}",
            message.Emoji, message.MessageId);

        // Salva nel database
        await _dbService.SaveReactionAsync(
            sessionId: CurrentSessionId,
            messageId: message.MessageId,
            emoji: message.Emoji,
            timestamp: DateTime.Now
        );

        // Opzionale: Sincronizza con altri dispositivi
        await SyncReactionToCloudAsync(message);
    }
}

// Model per deserializzazione
public class WebViewMessage
{
    public string Action { get; set; }
    public string MessageId { get; set; }
    public string Emoji { get; set; }
    public long Timestamp { get; set; }
}
```

**Database** (nuovo schema):
```sql
CREATE TABLE MessageReactions (
    id INT AUTO_INCREMENT PRIMARY KEY,
    session_id VARCHAR(36) NOT NULL,
    message_id VARCHAR(36) NOT NULL,
    emoji VARCHAR(10) NOT NULL,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_session_message (session_id, message_id)
);
```

**Benefici**:
- ✅ Feedback emotivo immediato
- ✅ Bookmarking intelligente (filtra messaggi con ❤️)
- ✅ Analytics: quali tipi di risposte piacciono di più
- ✅ UX moderna e coinvolgente

---

### 2. Monaco Editor Integration 💻

**Descrizione**: Integra Monaco Editor (editor di VS Code) per editing avanzato di codice inline.

**Valore**: Editing professionale, autocomplete, syntax highlighting live
**Complessità**: 🟡 Media (3-5 giorni)
**Priorità**: ⭐⭐⭐⭐ Molto Alta (Game Changer)

#### Implementazione

**HTML Template**:
```html
<!DOCTYPE html>
<html>
<head>
    <link rel="stylesheet" data-name="vs/editor/editor.main"
          href="https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.45.0/min/vs/editor/editor.main.min.css">
</head>
<body>
    <div id="conversation-container"></div>

    <!-- Monaco Editor viene iniettato dinamicamente -->
    <script src="https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.45.0/min/vs/loader.min.js"></script>
    <script src="monaco-integration.js"></script>
</body>
</html>
```

**JavaScript** (`monaco-integration.js`):
```javascript
class MonacoIntegration {
    constructor() {
        this.editors = new Map();
        this.initMonaco();
    }

    initMonaco() {
        require.config({
            paths: {
                'vs': 'https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.45.0/min/vs'
            }
        });

        require(['vs/editor/editor.main'], () => {
            console.log('Monaco Editor loaded');
            this.setupEditors();
        });
    }

    setupEditors() {
        // Trova tutti i code block nel messaggio
        document.querySelectorAll('pre code').forEach(codeBlock => {
            const language = this.detectLanguage(codeBlock);
            const code = codeBlock.textContent;
            const messageId = codeBlock.closest('.message-container').dataset.messageId;

            // Aggiungi pulsante "Edit with Monaco"
            this.addEditButton(codeBlock, messageId, language, code);
        });
    }

    addEditButton(codeBlock, messageId, language, code) {
        const btn = document.createElement('button');
        btn.textContent = '✏️ Edit';
        btn.className = 'monaco-edit-btn';
        btn.onclick = () => this.openEditor(messageId, language, code, codeBlock);

        codeBlock.parentElement.insertBefore(btn, codeBlock);
    }

    openEditor(messageId, language, initialCode, originalBlock) {
        const editorId = `editor-${messageId}-${Date.now()}`;

        // Crea container per l'editor
        const container = document.createElement('div');
        container.id = editorId;
        container.style.width = '100%';
        container.style.height = '400px';
        container.style.border = '1px solid #444';

        // Sostituisci il code block con l'editor
        originalBlock.parentElement.replaceChild(container, originalBlock);

        // Crea l'editor Monaco
        const editor = monaco.editor.create(document.getElementById(editorId), {
            value: initialCode,
            language: language,
            theme: 'vs-dark',
            minimap: { enabled: false },
            automaticLayout: true,
            fontSize: 14,
            lineNumbers: 'on',
            roundedSelection: false,
            scrollBeyondLastLine: false,
            readOnly: false
        });

        this.editors.set(editorId, editor);

        // Invia cambiamenti a C# quando l'utente modifica
        editor.onDidChangeModelContent((e) => {
            this.notifyCodeChange(messageId, editor.getValue(), language);
        });

        // Aggiungi toolbar
        this.addEditorToolbar(container, editor, messageId, language);
    }

    addEditorToolbar(container, editor, messageId, language) {
        const toolbar = document.createElement('div');
        toolbar.className = 'editor-toolbar';

        // Pulsante "Run"
        const runBtn = document.createElement('button');
        runBtn.textContent = '▶️ Run';
        runBtn.onclick = () => this.runCode(messageId, editor.getValue(), language);
        toolbar.appendChild(runBtn);

        // Pulsante "Save"
        const saveBtn = document.createElement('button');
        saveBtn.textContent = '💾 Save';
        saveBtn.onclick = () => this.saveCode(messageId, editor.getValue(), language);
        toolbar.appendChild(saveBtn);

        // Pulsante "Format"
        const formatBtn = document.createElement('button');
        formatBtn.textContent = '✨ Format';
        formatBtn.onclick = () => {
            editor.getAction('editor.action.formatDocument').run();
        };
        toolbar.appendChild(formatBtn);

        container.insertBefore(toolbar, container.firstChild);
    }

    notifyCodeChange(messageId, code, language) {
        window.chrome.webview.postMessage({
            action: 'codeChanged',
            messageId: messageId,
            code: code,
            language: language,
            timestamp: Date.now()
        });
    }

    runCode(messageId, code, language) {
        window.chrome.webview.postMessage({
            action: 'runCode',
            messageId: messageId,
            code: code,
            language: language
        });
    }

    saveCode(messageId, code, language) {
        window.chrome.webview.postMessage({
            action: 'saveCode',
            messageId: messageId,
            code: code,
            language: language
        });
    }

    // Chiamato da C# con risultati di compilazione/esecuzione
    showCompilationErrors(errors) {
        const editor = this.editors.get(this.currentEditorId);
        if (!editor) return;

        const markers = errors.map(err => ({
            startLineNumber: err.line,
            startColumn: err.column,
            endLineNumber: err.line,
            endColumn: err.column + err.length,
            message: err.message,
            severity: monaco.MarkerSeverity.Error
        }));

        monaco.editor.setModelMarkers(editor.getModel(), 'compilation', markers);
    }

    detectLanguage(codeBlock) {
        const className = codeBlock.className;
        if (className.includes('language-')) {
            return className.split('language-')[1].split(' ')[0];
        }
        return 'plaintext';
    }
}

// Inizializza
const monacoIntegration = new MonacoIntegration();

// Funzione globale per C# callback
window.showCompilationErrors = (errors) => {
    monacoIntegration.showCompilationErrors(errors);
};
```

**C#** (`SessionTabContent.xaml.cs`):
```csharp
private async void OnWebViewMessageReceived(object sender, HybridWebViewRawMessageReceivedEventArgs e)
{
    var message = JsonSerializer.Deserialize<WebViewMessage>(e.Message);

    switch (message.Action)
    {
        case "runCode":
            await HandleRunCodeAsync(message);
            break;

        case "saveCode":
            await HandleSaveCodeAsync(message);
            break;

        case "codeChanged":
            // Opzionale: validazione in tempo reale
            await ValidateCodeAsync(message);
            break;
    }
}

private async Task HandleRunCodeAsync(WebViewMessage message)
{
    Log.Information("Running code: {Language}", message.Language);

    try
    {
        string output;

        switch (message.Language.ToLower())
        {
            case "csharp":
            case "cs":
                output = await RunCSharpCodeAsync(message.Code);
                break;

            case "python":
                output = await RunPythonCodeAsync(message.Code);
                break;

            case "javascript":
            case "js":
                output = await RunJavaScriptCodeAsync(message.Code);
                break;

            default:
                output = $"Language '{message.Language}' not supported for execution";
                break;
        }

        // Invia output alla WebView
        await hybridWebView.InvokeJavaScriptAsync(
            $"showCodeOutput('{EscapeJs(output)}')"
        );
    }
    catch (Exception ex)
    {
        await hybridWebView.InvokeJavaScriptAsync(
            $"showCodeError('{EscapeJs(ex.Message)}')"
        );
    }
}

private async Task<string> RunCSharpCodeAsync(string code)
{
    // Opzione 1: Usa Roslyn per compilazione ed esecuzione
    var scriptOptions = ScriptOptions.Default
        .AddReferences(typeof(Console).Assembly)
        .AddImports("System", "System.Linq", "System.Collections.Generic");

    var result = await CSharpScript.EvaluateAsync(code, scriptOptions);
    return result?.ToString() ?? "(no output)";
}

private async Task ValidateCodeAsync(WebViewMessage message)
{
    // Validazione in tempo reale
    if (message.Language.ToLower() == "csharp")
    {
        var errors = await CompileCSharpAsync(message.Code);

        if (errors.Any())
        {
            var errorsJson = JsonSerializer.Serialize(errors);
            await hybridWebView.InvokeJavaScriptAsync(
                $"showCompilationErrors({errorsJson})"
            );
        }
    }
}

private string EscapeJs(string str)
{
    return str.Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "");
}
```

**Benefici**:
- ✅ Editor professionale come VS Code
- ✅ Autocomplete intelligente
- ✅ Syntax highlighting live
- ✅ Formattazione automatica
- ✅ Errori di compilazione in tempo reale
- ✅ Esecuzione codice inline
- ✅ UX di livello enterprise

---

### 3. Grafici Interattivi Token Usage 📊

**Descrizione**: Visualizza consumo token nel tempo con grafici interattivi usando Chart.js.

**Valore**: Analytics visuale, ottimizzazione costi, trend analysis
**Complessità**: 🟡 Media (2-3 giorni)
**Priorità**: ⭐⭐⭐ Alta (Value Feature)

#### Implementazione

**JavaScript** (`token-analytics.js`):
```javascript
class TokenAnalytics {
    constructor() {
        this.chart = null;
        this.data = [];
        this.initChart();
    }

    initChart() {
        const ctx = document.getElementById('tokenChart').getContext('2d');

        this.chart = new Chart(ctx, {
            type: 'line',
            data: {
                labels: [],
                datasets: [{
                    label: 'Token Usage',
                    data: [],
                    borderColor: 'rgb(75, 192, 192)',
                    backgroundColor: 'rgba(75, 192, 192, 0.2)',
                    tension: 0.1
                }, {
                    label: 'Remaining',
                    data: [],
                    borderColor: 'rgb(255, 99, 132)',
                    backgroundColor: 'rgba(255, 99, 132, 0.2)',
                    tension: 0.1
                }]
            },
            options: {
                responsive: true,
                interaction: {
                    mode: 'index',
                    intersect: false,
                },
                plugins: {
                    tooltip: {
                        callbacks: {
                            afterLabel: (context) => {
                                // Click su punto → mostra dettagli sessione
                                return 'Click to see details';
                            }
                        }
                    }
                },
                onClick: (event, elements) => {
                    if (elements.length > 0) {
                        const index = elements[0].index;
                        const sessionId = this.data[index].sessionId;

                        // Richiedi dettagli a C#
                        window.chrome.webview.postMessage({
                            action: 'showSessionDetails',
                            sessionId: sessionId
                        });
                    }
                }
            }
        });

        // Richiedi dati a C#
        this.loadData();
    }

    loadData() {
        window.chrome.webview.postMessage({
            action: 'getTokenAnalytics',
            timeRange: '7days' // ultima settimana
        });
    }

    // Chiamato da C# con i dati
    updateChart(analyticsData) {
        this.data = analyticsData;

        this.chart.data.labels = analyticsData.map(d =>
            new Date(d.timestamp).toLocaleDateString()
        );

        this.chart.data.datasets[0].data = analyticsData.map(d => d.used);
        this.chart.data.datasets[1].data = analyticsData.map(d => d.remaining);

        this.chart.update();
    }
}

const tokenAnalytics = new TokenAnalytics();

// Funzione globale per callback C#
window.updateTokenAnalytics = (data) => {
    tokenAnalytics.updateChart(data);
};
```

**C#**:
```csharp
private async void OnWebViewMessageReceived(object sender, HybridWebViewRawMessageReceivedEventArgs e)
{
    var message = JsonSerializer.Deserialize<WebViewMessage>(e.Message);

    switch (message.Action)
    {
        case "getTokenAnalytics":
            await LoadTokenAnalyticsAsync(message.TimeRange);
            break;

        case "showSessionDetails":
            await ShowSessionDetailsDialogAsync(message.SessionId);
            break;
    }
}

private async Task LoadTokenAnalyticsAsync(string timeRange)
{
    // Carica dati dal database
    var analytics = await _dbService.GetTokenAnalyticsAsync(
        startDate: DateTime.Now.AddDays(-7),
        endDate: DateTime.Now
    );

    var analyticsJson = JsonSerializer.Serialize(analytics);

    await hybridWebView.InvokeJavaScriptAsync(
        $"updateTokenAnalytics({analyticsJson})"
    );
}
```

**Benefici**:
- ✅ Visualizzazione trend consumo token
- ✅ Identificazione sessioni costose
- ✅ Ottimizzazione budget
- ✅ Interattività: click su punto → dettagli sessione

---

### 4. Search Avanzata con Preview 🔍

**Descrizione**: Full-text search con highlighting, filtri dinamici e preview dei risultati.

**Valore**: Trovare informazioni velocemente, UX fluida
**Complessità**: 🟡 Media (3-4 giorni)
**Priorità**: ⭐⭐⭐⭐ Molto Alta (Core Feature)

#### Implementazione

**JavaScript** (`search-system.js`):
```javascript
class SearchSystem {
    constructor() {
        this.searchIndex = null;
        this.initLunr();
    }

    initLunr() {
        // Richiedi tutti i messaggi per indicizzazione
        window.chrome.webview.postMessage({
            action: 'getAllMessagesForIndexing'
        });
    }

    // Chiamato da C# con tutti i messaggi
    buildSearchIndex(messages) {
        this.searchIndex = lunr(function() {
            this.field('content', { boost: 10 });
            this.field('role');
            this.field('timestamp');
            this.ref('id');

            messages.forEach(msg => {
                this.add({
                    id: msg.id,
                    content: msg.content,
                    role: msg.role,
                    timestamp: msg.timestamp
                });
            });
        });

        console.log('Search index built with', messages.length, 'messages');
    }

    search(query) {
        if (!this.searchIndex) {
            console.warn('Search index not ready');
            return [];
        }

        const results = this.searchIndex.search(query);

        // Mostra risultati con highlighting
        this.displayResults(results, query);

        return results;
    }

    displayResults(results, query) {
        const container = document.getElementById('search-results');
        container.innerHTML = '';

        results.forEach(result => {
            const resultItem = this.createResultItem(result, query);
            container.appendChild(resultItem);

            // Click su risultato → scroll to message e apri dettagli
            resultItem.onclick = () => this.openSearchResult(result.ref);
        });
    }

    createResultItem(result, query) {
        const item = document.createElement('div');
        item.className = 'search-result-item';

        // Richiedi preview a C#
        window.chrome.webview.postMessage({
            action: 'getMessagePreview',
            messageId: result.ref,
            highlightQuery: query
        });

        // Placeholder temporaneo
        item.innerHTML = `
            <div class="result-score">${(result.score * 100).toFixed(0)}% match</div>
            <div class="result-content" id="preview-${result.ref}">Loading...</div>
        `;

        return item;
    }

    openSearchResult(messageId) {
        // Notifica C# per scroll al messaggio
        window.chrome.webview.postMessage({
            action: 'scrollToMessage',
            messageId: messageId
        });

        // Highlight temporaneo del messaggio
        const messageElement = document.querySelector(`[data-message-id="${messageId}"]`);
        if (messageElement) {
            messageElement.classList.add('search-highlight');
            setTimeout(() => {
                messageElement.classList.remove('search-highlight');
            }, 2000);
        }
    }
}

const searchSystem = new SearchSystem();

// Listener per input search
document.getElementById('search-input').addEventListener('input', (e) => {
    const query = e.target.value;
    if (query.length >= 3) {
        searchSystem.search(query);
    }
});

// Funzioni globali per callback C#
window.buildSearchIndex = (messages) => {
    searchSystem.buildSearchIndex(messages);
};

window.updateMessagePreview = (messageId, preview) => {
    const previewElement = document.getElementById(`preview-${messageId}`);
    if (previewElement) {
        previewElement.innerHTML = preview;
    }
};
```

**C#**:
```csharp
private async void OnWebViewMessageReceived(object sender, HybridWebViewRawMessageReceivedEventArgs e)
{
    var message = JsonSerializer.Deserialize<WebViewMessage>(e.Message);

    switch (message.Action)
    {
        case "getAllMessagesForIndexing":
            await LoadAllMessagesForIndexingAsync();
            break;

        case "getMessagePreview":
            await GetMessagePreviewAsync(message.MessageId, message.HighlightQuery);
            break;

        case "scrollToMessage":
            await ScrollToMessageAsync(message.MessageId);
            break;
    }
}

private async Task LoadAllMessagesForIndexingAsync()
{
    // Carica tutti i messaggi della sessione
    var messages = await _dbService.GetAllMessagesAsync(CurrentSessionId);

    var messagesJson = JsonSerializer.Serialize(messages.Select(m => new {
        id = m.Id,
        content = m.Content,
        role = m.Role,
        timestamp = m.Timestamp
    }));

    await hybridWebView.InvokeJavaScriptAsync(
        $"buildSearchIndex({messagesJson})"
    );
}

private async Task GetMessagePreviewAsync(string messageId, string highlightQuery)
{
    var message = await _dbService.GetMessageByIdAsync(messageId);

    // Crea preview con highlighting
    var preview = CreateHighlightedPreview(message.Content, highlightQuery, maxLength: 200);

    await hybridWebView.InvokeJavaScriptAsync(
        $"updateMessagePreview('{messageId}', '{EscapeJs(preview)}')"
    );
}

private string CreateHighlightedPreview(string content, string query, int maxLength)
{
    // Trova la prima occorrenza del query
    var index = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);

    if (index == -1)
    {
        return content.Substring(0, Math.Min(content.Length, maxLength)) + "...";
    }

    // Prendi contesto intorno al match
    var start = Math.Max(0, index - 50);
    var end = Math.Min(content.Length, index + query.Length + 100);

    var preview = content.Substring(start, end - start);

    // Aggiungi highlighting
    preview = preview.Replace(query, $"<mark>{query}</mark>");

    return "..." + preview + "...";
}
```

**Benefici**:
- ✅ Search istantanea (tutto in JavaScript)
- ✅ Highlighting dei risultati
- ✅ Preview contestuale
- ✅ Click → scroll al messaggio
- ✅ Fuzzy matching con Lunr.js

---

### 5. Virtual Scrolling per Conversazioni Lunghe ⚡

**Descrizione**: Rendering efficiente di migliaia di messaggi con virtualizzazione.

**Valore**: Performance, scalabilità
**Complessità**: 🔴 Alta (5-7 giorni)
**Priorità**: ⭐⭐ Media (solo se hai >1000 messaggi)

**Concetto**:
- Renderizza solo messaggi visibili + buffer
- Carica messaggi on-demand durante lo scroll
- Rimuove messaggi fuori viewport dalla DOM

**Libreria consigliata**: `react-window` o `virtual-scroller`

---

### 6. Collaborative Features 👥

**Descrizione**: Sessioni condivise, cursori live, commenti collaborativi.

**Valore**: Team collaboration
**Complessità**: 🔴 Molto Alta (2-3 settimane)
**Priorità**: ⭐ Bassa (feature futura)

**Stack necessario**:
- SignalR per real-time communication
- WebSocket per sync
- Conflict resolution

---

### 7. AI-Powered Smart Suggestions 🤖

**Descrizione**: Suggerimenti intelligenti mentre scrivi il prompt.

**Valore**: Produttività, migliori prompt
**Complessità**: 🟡 Media (4-5 giorni)
**Priorità**: ⭐⭐⭐ Alta (AI Feature)

**Esempio**:
- Suggerisce completamenti del prompt basati su storico
- Suggerisce file da includere nel context
- Suggerisce tool calls rilevanti

---

### 8. Code Diff Viewer 🔀

**Descrizione**: Visualizza differenze tra versioni di codice con syntax highlighting.

**Valore**: Review code changes
**Complessità**: 🟡 Media (2-3 giorni)
**Priorità**: ⭐⭐⭐ Alta (Developer Tool)

**Libreria consigliata**: `monaco-editor` diff editor

---

### 9. Export Conversations 📤

**Descrizione**: Esporta conversazioni in vari formati (PDF, Markdown, HTML).

**Valore**: Documentazione, sharing
**Complessità**: 🟢 Bassa (1-2 giorni)
**Priorità**: ⭐⭐ Media

**Formati**:
- PDF con styling
- Markdown per GitHub
- HTML standalone
- JSON raw data

---

### 10. Keyboard Shortcuts ⌨️

**Descrizione**: Scorciatoie da tastiera per azioni comuni.

**Valore**: Power user productivity
**Complessità**: 🟢 Bassa (1 giorno)
**Priorità**: ⭐⭐⭐ Alta (Quick Win)

**Esempi**:
- `Ctrl+K` → Quick search
- `Ctrl+Enter` → Send message
- `Ctrl+/` → Toggle sidebar
- `Ctrl+R` → Add reaction
- `Ctrl+E` → Edit last message

---

## 📊 Confronto WebView vs HybridWebView

### Feature Matrix

| Feature | WebView | HybridWebView | Complessità Migrazione |
|---------|---------|---------------|------------------------|
| **Visualizza HTML/CSS** | ✅ Eccellente | ✅ Eccellente | N/A |
| **Mostra Markdown** | ✅ Con renderer | ✅ Con renderer | N/A |
| **Link esterni** | ✅ Funziona | ✅ Funziona | N/A |
| **Copia testo** | ✅ Nativo | ✅ Nativo | N/A |
| **Reazioni messaggi** | ❌ Impossibile | ✅ Facile | 🟢 Bassa |
| **Monaco Editor** | ❌ Impossibile | ✅ Possibile | 🟡 Media |
| **Grafici interattivi** | ⚠️ Limitato | ✅ Completo | 🟢 Bassa |
| **Search real-time** | ❌ Lento in C# | ✅ Veloce in JS | 🟡 Media |
| **Virtual scrolling** | ❌ Impossibile | ✅ Possibile | 🔴 Alta |
| **Collaborative** | ❌ Impossibile | ✅ Con SignalR | 🔴 Molto Alta |
| **Keyboard shortcuts** | ⚠️ Limitato | ✅ Completo | 🟢 Bassa |
| **Export funzionalità** | ✅ Possibile | ✅ Più flessibile | 🟢 Bassa |

### Performance Comparison

| Metrica | WebView | HybridWebView |
|---------|---------|---------------|
| **Rendering statico** | ⚡⚡⚡⚡⚡ | ⚡⚡⚡⚡⚡ |
| **Update dinamici** | ⚡⚡ (ricarica completa) | ⚡⚡⚡⚡⚡ (update parziali) |
| **Interattività** | ⚡ (nessuna) | ⚡⚡⚡⚡⚡ |
| **Memory footprint** | 🟢 Basso | 🟡 Medio (+JavaScript runtime) |
| **Latenza comunicazione** | N/A | ~10-50ms (trascurabile) |

### Complessità Sviluppo

| Aspetto | WebView | HybridWebView |
|---------|---------|---------------|
| **Setup iniziale** | 🟢 Semplice | 🟡 Moderato |
| **Manutenzione** | 🟢 Facile | 🟡 Richiede sync C#/JS |
| **Debugging** | 🟢 Solo C# | 🟡 C# + JS (2 runtime) |
| **Testing** | 🟢 Unit test C# | 🟡 Unit test C# + JS + Integration |
| **Learning curve** | 🟢 Bassa | 🟡 Media (serve conoscere JS) |

---

## 🗺️ Roadmap Suggerita

### Fase 1: Quick Wins (1-2 settimane) 🎯

**Obiettivo**: Aggiungere interattività di base senza stravolgere architettura

1. **Reazioni ai messaggi** (2 giorni)
   - Setup HybridWebView base
   - Implement reaction system
   - Database schema update

2. **Keyboard shortcuts** (1 giorno)
   - Setup event listeners JavaScript
   - Map shortcuts to actions

3. **Basic export** (2 giorni)
   - Export to Markdown
   - Export to PDF

**Valore**: Alta interattività con effort minimo
**Rischio**: Basso (features isolate)

### Fase 2: Power Features (3-4 settimane) 💪

**Obiettivo**: Trasformare l'app in un IDE per conversazioni AI

1. **Monaco Editor integration** (1 settimana)
   - Setup Monaco
   - Code execution
   - Syntax highlighting

2. **Search avanzata** (1 settimana)
   - Full-text search con Lunr.js
   - Highlighting
   - Preview

3. **Token analytics** (3 giorni)
   - Chart.js integration
   - Database analytics queries
   - Interactive charts

4. **Code diff viewer** (3 giorni)
   - Monaco diff editor
   - Compare code versions

**Valore**: Professional-grade features
**Rischio**: Medio (richiede testing approfondito)

### Fase 3: Scalabilità (2-3 settimane) 📈

**Obiettivo**: Gestire migliaia di messaggi efficientemente

1. **Virtual scrolling** (1 settimana)
   - Implement virtual scroller
   - Lazy loading messages
   - Performance optimization

2. **Advanced caching** (1 settimana)
   - IndexedDB per cache locale
   - Offline support

3. **Background sync** (5 giorni)
   - Service Worker
   - Sync quando app chiusa

**Valore**: Scalabilità enterprise
**Rischio**: Alto (architettura complessa)

### Fase 4: Collaborative (1-2 mesi) 👥

**Obiettivo**: Funzionalità collaborative real-time

1. **SignalR integration** (2 settimane)
   - Real-time sync
   - Conflict resolution

2. **Shared sessions** (2 settimane)
   - Multi-user support
   - Permissions

3. **Live cursors & presence** (1 settimana)
   - Show other users
   - Activity indicators

**Valore**: Team collaboration
**Rischio**: Molto Alto (sistema distribuito)

---

## 🛠️ Implementazione Pratica

### Setup Base HybridWebView

**1. Aggiungi NuGet Package** (se necessario):
```bash
dotnet add package Microsoft.Maui.Controls
```

**2. Modifica `SessionTabContent.xaml`**:
```xml
<!-- PRIMA (WebView) -->
<WebView x:Name="ConversationWebView"
         VerticalOptions="FillAndExpand"
         HorizontalOptions="FillAndExpand" />

<!-- DOPO (HybridWebView) -->
<HybridWebView x:Name="ConversationHybridWebView"
               VerticalOptions="FillAndExpand"
               HorizontalOptions="FillAndExpand"
               RawMessageReceived="OnWebViewMessageReceived" />
```

**3. Setup Code-Behind `SessionTabContent.xaml.cs`**:
```csharp
public partial class SessionTabContent : ContentView
{
    private void InitializeHybridWebView()
    {
        // Imposta il contenuto HTML iniziale
        ConversationHybridWebView.HybridWebViewUrl = "app://localhost/index.html";

        // Event handler per messaggi da JavaScript
        ConversationHybridWebView.RawMessageReceived += OnWebViewMessageReceived;
    }

    private async void OnWebViewMessageReceived(object sender, HybridWebViewRawMessageReceivedEventArgs e)
    {
        try
        {
            var message = JsonSerializer.Deserialize<WebViewMessage>(e.Message);
            await HandleWebViewMessageAsync(message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process WebView message: {Message}", e.Message);
        }
    }

    private async Task HandleWebViewMessageAsync(WebViewMessage message)
    {
        Log.Debug("WebView message received: {Action}", message.Action);

        switch (message.Action)
        {
            case "addReaction":
                await HandleReactionAsync(message);
                break;

            case "runCode":
                await HandleRunCodeAsync(message);
                break;

            case "search":
                await HandleSearchAsync(message);
                break;

            default:
                Log.Warning("Unknown WebView action: {Action}", message.Action);
                break;
        }
    }

    // Helper per chiamare JavaScript da C#
    private async Task InvokeJavaScriptAsync(string script)
    {
        await ConversationHybridWebView.InvokeJavaScriptAsync(script);
    }
}
```

**4. Struttura File HTML**:
```
ClaudeCodeMAUI/
├── Resources/
│   └── Raw/
│       └── hybridwebview/
│           ├── index.html
│           ├── styles.css
│           ├── app.js
│           ├── reactions.js
│           ├── search.js
│           └── monaco-integration.js
```

**5. Template HTML Base (`index.html`)**:
```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Claude Conversation</title>
    <link rel="stylesheet" href="styles.css">
</head>
<body>
    <div id="app">
        <div id="conversation-container"></div>
        <div id="search-panel" class="hidden"></div>
    </div>

    <!-- Scripts -->
    <script src="app.js"></script>
    <script src="reactions.js"></script>
    <script src="search.js"></script>
</body>
</html>
```

---

## 📚 Risorse e Riferimenti

### Documentazione Microsoft

- [HybridWebView Documentation](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/hybridwebview)
- [WebView vs HybridWebView](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/webview)
- [JavaScript Interop](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/invoke-javascript)

### Librerie JavaScript Consigliate

#### 1. **Editor di Codice**
- [Monaco Editor](https://microsoft.github.io/monaco-editor/) - Editor di VS Code
- [CodeMirror 6](https://codemirror.net/) - Alternativa leggera

#### 2. **Grafici e Visualizzazioni**
- [Chart.js](https://www.chartjs.org/) - Grafici semplici e belli
- [D3.js](https://d3js.org/) - Visualizzazioni custom complesse
- [Apache ECharts](https://echarts.apache.org/) - Grafici enterprise

#### 3. **Full-Text Search**
- [Lunr.js](https://lunrjs.com/) - Search client-side
- [Fuse.js](https://fusejs.io/) - Fuzzy search
- [FlexSearch](https://github.com/nextapps-de/flexsearch) - Fastest

#### 4. **Virtual Scrolling**
- [react-window](https://github.com/bvaughn/react-window) - Se usi React
- [virtual-scroller](https://github.com/valdrinkoshi/virtual-scroller) - Vanilla JS

#### 5. **Markdown Rendering**
- [Marked](https://marked.js.org/) - Veloce
- [Markdown-it](https://github.com/markdown-it/markdown-it) - Estensibile
- ~~Markdig~~ (già usi in C#) - Continua ad usare

#### 6. **Syntax Highlighting**
- [Prism.js](https://prismjs.com/) - Leggero
- [Highlight.js](https://highlightjs.org/) - Più lingue
- ~~Monaco~~ (se usi Monaco Editor, già incluso)

### Best Practices

#### 1. **Comunicazione C# ↔ JavaScript**

✅ **DO**:
- Usa `JsonSerializer` per serializzazione type-safe
- Valida sempre i messaggi ricevuti
- Log dettagliato per debugging
- Gestisci errori con try-catch
- Usa action-based routing pattern

❌ **DON'T**:
- Non passare oggetti complessi (mantieni messaggi semplici)
- Non assumere che JavaScript sia sempre pronto
- Non fare chiamate sincrone (tutto async)

#### 2. **Performance**

✅ **DO**:
- Throttle/debounce eventi frequenti (scroll, input)
- Lazy load contenuto fuori viewport
- Cache risultati costosi
- Usa Web Workers per operazioni pesanti
- Minimize payload messaggi

❌ **DON'T**:
- Non inviare migliaia di messaggi al secondo
- Non caricare tutto in memoria
- Non bloccare UI thread

#### 3. **Sicurezza**

✅ **DO**:
- Sanitize input da JavaScript
- Escape output HTML
- Valida azioni permesse
- Usa Content Security Policy

❌ **DON'T**:
- Non eseguire codice arbitrario
- Non fidarti ciecamente di messaggi JavaScript
- Non esporre API sensibili

### Esempi di Codice Completi

Repository GitHub con esempi:
- [MAUI HybridWebView Samples](https://github.com/dotnet/maui-samples)
- [Monaco Editor MAUI Integration](https://github.com/search?q=monaco+editor+maui)

---

## 🎓 Conclusioni

### Quando Migrare a HybridWebView

**Migra ORA se**:
- ✅ Vuoi aggiungere reazioni ai messaggi
- ✅ Vuoi editor di codice avanzato
- ✅ Vuoi search interattiva veloce
- ✅ Vuoi grafici interattivi

**Rimani con WebView se**:
- ✅ Messaggi sono solo read-only
- ✅ Performance attuali sono OK
- ✅ Non hai bisogno di interattività avanzata
- ✅ Vuoi mantenere semplicità

### Prossimi Passi

1. **Prova un Quick Win**: Implementa reaction system (2 giorni)
2. **Valuta risultati**: Performance, UX, complessità
3. **Decide roadmap**: Se positivo, continua con Monaco Editor
4. **Iterazione**: Aggiungi features incrementalmente

### Supporto

Per domande o assistenza sull'implementazione:
- 📧 Email: [tuo contatto]
- 💬 GitHub Issues: [repository]
- 📖 Wiki: [link wiki progetto]

---

**Fine Documentazione**
Ultimo aggiornamento: 2025-01-05
