# Metadata Display Implementation - Spostamento sotto i Messaggi

**Data:** 2025-10-30
**Progetto:** ClaudeCodeMAUI
**Feature:** Metadata sotto messaggi + Working Directory nella barra superiore
**Status:** ✅ IMPLEMENTATO

---

## 📋 Riepilogo

Spostati i metadata statistici (durata, costo, token, turni) dalla barra superiore a **sotto ogni risposta di Claude**, con:
- ✅ Font piccolo (10px)
- ✅ Colore giallo (#FFD700)
- ✅ Allineamento a destra
- ✅ Tutto su una unica riga
- ✅ Click per mostrare/nascondere (opzionale)

La **barra superiore** ora mostra la **Working Directory corrente** (allineata a sinistra).

---

## 🎯 Obiettivi Raggiunti

### 1. Metadata sotto Messaggi
- I metadata appaiono subito dopo ogni risposta di Claude
- Visibili di default, con icona 📊
- Click sulla riga per collassare/espandere
- Stile: giallo, font piccolo, allineato a destra

### 2. Working Directory nella Barra Superiore
- Mostra la directory corrente di lavoro
- Allineato a sinistra
- Aggiornato all'avvio e ad ogni nuova conversazione

---

## 🛠️ Modifiche Implementate

### PARTE 1: Modifica UI (MainPage.xaml)

**File:** `ClaudeCodeMAUI/MainPage.xaml`

**Cambiamenti:**

```xml
<!-- PRIMA: Metadata Bar -->
<Entry x:Name="MetadataLabel"
       ...
       HorizontalTextAlignment="End" />

<!-- DOPO: Working Directory Bar -->
<Entry x:Name="WorkingDirectoryLabel"
       ...
       HorizontalTextAlignment="Start"
       Placeholder="Working Directory: ..." />
```

**Righe modificate:** 57-66

**Dettagli:**
- Rinominato controllo da `MetadataLabel` a `WorkingDirectoryLabel`
- Cambiato allineamento da `End` (destra) a `Start` (sinistra)
- Aggiunto placeholder

---

### PARTE 2: CSS per Metadata (MarkdownHtmlRenderer.cs)

**File:** `ClaudeCodeMAUI/Utilities/MarkdownHtmlRenderer.cs`

**Aggiunto CSS:**

```css
.metadata-container {
    text-align: right;
    font-size: 10px;
    color: #FFD700;  /* giallo */
    margin: 5px 0 15px 0;
    padding: 4px 8px;
    cursor: pointer;
    font-family: 'Consolas', 'Courier New', monospace;
    user-select: none;
}

.metadata-container:hover {
    opacity: 0.8;
}

.metadata-content {
    display: inline-block;
    white-space: nowrap;  /* Tutto su una riga */
}

.metadata-container.collapsed .metadata-content {
    display: none;  /* Nascosto quando collassato */
}

.metadata-container::before {
    content: '📊 ';
}

.metadata-container.collapsed::before {
    content: '📊 Stats (click to show)';
    opacity: 0.6;
}
```

**Righe aggiunte:** 237-275

**Caratteristiche:**
- Font piccolissimo (10px) per non essere invasivo
- `white-space: nowrap` garantisce che tutto stia su una riga
- `cursor: pointer` indica che è cliccabile
- `user-select: none` previene selezione accidentale
- Icona 📊 sempre visibile
- Quando collassato, mostra "Stats (click to show)"

---

### PARTE 3: Generazione HTML Metadata (MainPage.xaml.cs)

**File:** `ClaudeCodeMAUI/MainPage.xaml.cs`

**Metodo modificato:** `OnMetadataReceived` (riga 603)

**Prima:**
```csharp
MetadataLabel.Text = $"Duration: {e.DurationMs}ms | Cost: ...";
```

**Dopo:**
```csharp
var metadataHtml = $@"
<div class=""metadata-container"" onclick=""this.classList.toggle('collapsed')"">
    <span class=""metadata-content"">Duration: {e.DurationMs}ms  |  Cost: ${e.TotalCostUsd:F4}  |  Tokens: {e.InputTokens} in / {e.OutputTokens} out  |  Turns: {e.NumTurns}</span>
</div>";

_conversationHtml.Append(metadataHtml);

// Rigenera pagina HTML e ricarica WebView
var fullHtml = _htmlRenderer.GenerateFullPage(_isDarkTheme, _conversationHtml.ToString());
ConversationWebView.Source = new HtmlWebViewSource { Html = fullHtml };
```

**Righe modificate:** 620-636

**Comportamento:**
1. Quando arrivano i metadata, genera un blocco HTML `<div>` con classe `metadata-container`
2. Include tutti i dati su **una singola riga** dentro `<span class="metadata-content">`
3. JavaScript inline `onclick="this.classList.toggle('collapsed')"` gestisce show/hide
4. Aggiunge l'HTML al buffer `_conversationHtml`
5. Rigenera l'intera pagina HTML e ricarica la WebView

**Nota importante:** Ogni volta che arrivano metadata, la WebView viene ricaricata con tutto il contenuto. Questo è il comportamento attuale dell'app ("Reload Full Page" approach).

---

### PARTE 4: Working Directory Display (MainPage.xaml.cs)

**File:** `ClaudeCodeMAUI/MainPage.xaml.cs`

#### A. Nuovo metodo `UpdateWorkingDirectory()` (righe 197-213)

```csharp
private void UpdateWorkingDirectory()
{
    try
    {
        var currentDir = Directory.GetCurrentDirectory();
        WorkingDirectoryLabel.Text = $"Working Directory: {currentDir}";
        Log.Information("Working directory updated: {Directory}", currentDir);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to get current directory");
        WorkingDirectoryLabel.Text = "Working Directory: <error>";
    }
}
```

**Funzionalità:**
- Legge la directory corrente con `Directory.GetCurrentDirectory()`
- Aggiorna il testo della barra superiore
- Gestisce errori con fallback graceful

#### B. Chiamate a `UpdateWorkingDirectory()`

**1. In `OnAppearing()` (riga 220):**
```csharp
protected override async void OnAppearing()
{
    base.OnAppearing();

    // Aggiorna la barra con la working directory
    UpdateWorkingDirectory();

    // ... resto del codice
}
```

**2. In `StartNewConversation()` (riga 456):**
```csharp
// Aggiorna UI
BtnStop.IsEnabled = true;
LblStatus.Text = "Running...";
LblStatus.TextColor = Colors.Green;

// Aggiorna la working directory quando si inizia una nuova conversazione
UpdateWorkingDirectory();
```

**Quando viene aggiornata:**
- All'avvio dell'app (`OnAppearing`)
- Quando si inizia una nuova conversazione
- (Futuro) Potrebbe essere aggiornata quando Claude cambia directory

---

## 📊 Esempio Visivo Risultato

```
┌─────────────────────────────────────────────────────────────┐
│ Working Directory: C:\Projects\MyApp                        │  ← Barra gialla, sinistra
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│ User:                                                       │
│ Ciao, aiutami con un progetto                              │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│ Claude:                                                     │
│ Certo! Dimmi di cosa hai bisogno e sarò felice di aiutarti │
│                                                             │
│          📊 Duration: 1234ms | Cost: $0.0123 | ...          │  ← Giallo, piccolo, destra
└─────────────────────────────────────────────────────────────┘
                                                            ↑
                                    Click qui per nascondere
```

**Dopo il click (collassato):**
```
│          📊 Stats (click to show)                           │  ← Più piccolo, opaco
```

---

## 🔍 Dettagli Tecnici

### Come funziona il Toggle Show/Hide?

**Meccanismo:**
1. HTML include `onclick="this.classList.toggle('collapsed')"`
2. Quando clicchi, JavaScript aggiunge/rimuove la classe CSS `collapsed`
3. Il CSS ha regola `.metadata-container.collapsed .metadata-content { display: none; }`
4. Quando collassato, solo l'icona + testo "Stats (click to show)" sono visibili

**Vantaggi:**
- ✅ Tutto gestito in HTML/CSS/JS (no comunicazione C# ↔ WebView)
- ✅ Performante (no reload della pagina)
- ✅ Semplice da implementare

**Limitazione:**
- ⚠️ Lo stato (mostrato/nascosto) si perde quando la WebView viene ricaricata
- Questo succede ad ogni nuovo messaggio di Claude (perché rigeneriano l'intera pagina)
- **Workaround futuro:** Salvare stato in Preferences e iniettare JavaScript per riapplicarlo dopo ogni reload

### Perché la WebView viene ricaricata ad ogni metadata?

L'app usa l'approccio **"Reload Full Page"**:
- `_conversationHtml` è un `StringBuilder` che accumula tutto l'HTML della conversazione
- Ad ogni nuovo messaggio/metadata, si aggiunge HTML al buffer
- Si rigenera l'intera pagina HTML con `GenerateFullPage()`
- Si ricarica la WebView con `ConversationWebView.Source = new HtmlWebViewSource { Html = fullHtml }`

**Pro:** Semplice, prevedibile, nessun rischio di desync
**Contro:** Performance (reload completo), perde stato JS (scroll position, collapsed state)

---

## 📝 File Modificati

| File | Righe Modificate/Aggiunte | Descrizione |
|------|---------------------------|-------------|
| `MainPage.xaml` | 57-66 | Rinominato controllo + cambio allineamento |
| `Utilities/MarkdownHtmlRenderer.cs` | 237-275 (nuove) | Aggiunto CSS per metadata container |
| `MainPage.xaml.cs:197-213` | Nuove | Metodo `UpdateWorkingDirectory()` |
| `MainPage.xaml.cs:220` | Modificata | Chiamata `UpdateWorkingDirectory()` in `OnAppearing` |
| `MainPage.xaml.cs:456` | Modificata | Chiamata `UpdateWorkingDirectory()` in `StartNewConversation` |
| `MainPage.xaml.cs:620-636` | Modificate | `OnMetadataReceived()` genera HTML invece di aggiornare label |

**Totale righe aggiunte:** ~60
**Totale righe modificate:** ~20

---

## ✅ Test Checklist

### Test 1: Visualizzazione Working Directory
- [ ] Avvia app
- [ ] ✅ Barra superiore mostra "Working Directory: C:\..."
- [ ] ✅ Testo allineato a sinistra
- [ ] ✅ Colore giallo su sfondo scuro

### Test 2: Metadata sotto Messaggio
- [ ] Inizia una conversazione
- [ ] Invia un messaggio a Claude
- [ ] Attendi risposta di Claude
- [ ] ✅ Dopo la risposta, appaiono i metadata sotto il messaggio
- [ ] ✅ Metadata sono gialli, piccoli, allineati a destra
- [ ] ✅ Tutto su una riga: "Duration: XXms | Cost: $X.XX | Tokens: X in / X out | Turns: X"

### Test 3: Toggle Metadata (Show/Hide)
- [ ] Click sulla riga dei metadata
- [ ] ✅ Metadata scompaiono
- [ ] ✅ Appare solo "📊 Stats (click to show)" in grigio/opaco
- [ ] Click di nuovo
- [ ] ✅ Metadata riappaiono

### Test 4: Metadata per Messaggi Multipli
- [ ] Invia 3 messaggi di seguito a Claude
- [ ] ✅ Ogni risposta ha i suoi metadata
- [ ] ✅ I metadata sono indipendenti (click su uno non affetta gli altri)

### Test 5: Aggiornamento Working Directory
- [ ] Avvia app (nota la working directory)
- [ ] Click "New Conversation"
- [ ] ✅ Working directory viene riaggiornata (stessa o diversa se Claude ha cambiato dir)

### Test 6: Metadata non bloccano scroll
- [ ] Crea conversazione lunga (10+ messaggi)
- [ ] ✅ I metadata non interferiscono con lo scroll
- [ ] ✅ Lo scroll arriva fino in fondo

---

## 🐛 Troubleshooting

### Issue 1: Metadata non appaiono

**Causa:** `OnMetadataReceived` non viene chiamato

**Debug:**
- Controlla log: cerca "Added metadata to conversation buffer"
- Verifica che `_parser.MetadataReceived += OnMetadataReceived` sia eseguito
- Verifica che Claude process stia effettivamente inviando metadata nel JSON

**Fix:**
Nessun cambiamento nel flusso di ricezione metadata, dovrebbe funzionare come prima.

### Issue 2: Metadata non sono cliccabili

**Causa:** JavaScript `onclick` non funziona nella WebView

**Debug:**
- Verifica che la WebView supporti JavaScript (dovrebbe di default)
- Controlla che l'HTML generato contenga `onclick="this.classList.toggle('collapsed')"`

**Fix:**
Se WebView blocca JavaScript inline, sostituire con event listener:

```javascript
document.querySelectorAll('.metadata-container').forEach(el => {
    el.addEventListener('click', () => el.classList.toggle('collapsed'));
});
```

### Issue 3: Working Directory mostra "<error>"

**Causa:** `Directory.GetCurrentDirectory()` solleva exception

**Debug:**
- Controlla log: "Failed to get current directory"
- Verifica permessi dell'app

**Fix:**
Usa fallback con `AppContext.BaseDirectory` o `Environment.CurrentDirectory`.

### Issue 4: Metadata su più righe (non su una riga)

**Causa:** CSS `white-space: nowrap` non applicato correttamente

**Debug:**
- Ispeziona HTML generato (salva file temporaneo e aprilo in browser)
- Verifica che `<span class="metadata-content">` abbia il CSS corretto

**Fix:**
Aggiungi `white-space: nowrap !important;` per forzare.

---

## 🚀 Miglioramenti Futuri

### Opzione 1: Persistere Stato Collapsed

**Idea:** Salvare in Preferences quali metadata sono collassati

**Implementazione:**
```csharp
// In SettingsService
public bool MetadataDefaultCollapsed
{
    get => Preferences.Get("MetadataDefaultCollapsed", false);
    set => Preferences.Set("MetadataDefaultCollapsed", value);
}

// In OnMetadataReceived
var collapsedClass = _settingsService?.MetadataDefaultCollapsed == true ? " collapsed" : "";
var metadataHtml = $@"<div class=""metadata-container{collapsedClass}"" ...>";
```

### Opzione 2: Aggiornamento Real-time Working Directory

**Idea:** Monitorare quando Claude cambia directory e aggiornare la barra

**Implementazione:**
- Parsare output di tool calls come `bash` per rilevare `cd` commands
- Chiamare `UpdateWorkingDirectory()` dopo ogni `cd`
- Oppure: aggiungere watcher su `Directory.GetCurrentDirectory()`

### Opzione 3: Tooltip Esteso su Hover

**Idea:** Mostrare più dettagli al passaggio del mouse

**Implementazione:**
```html
<div class="metadata-container"
     title="Cache: {e.CacheReadTokens} read, {e.CacheCreationTokens} created | Model: {e.Model}">
```

### Opzione 4: Evitare Reload Completo WebView

**Idea:** Usare JavaScript per appendere metadata invece di rigenerare pagina

**Benefici:**
- Mantiene scroll position
- Mantiene stato collapsed/expanded
- Più performante

**Implementazione:**
```csharp
await ConversationWebView.EvaluateJavaScriptAsync(
    $"appendMetadata('{metadataHtml.Replace("'", "\\'")}')"
);
```

---

## 📚 Riferimenti

### Documenti Correlati
- `SESSION_RECOVERY_IMPLEMENTATION.md` - Sistema di recovery sessioni
- `SETTINGS_IMPLEMENTATION.md` - Menu configurazione
- `NULLREF_FIX.md` - Fix NullReferenceException

### Codice Correlato
- `MarkdownHtmlRenderer.cs:39` - Metodo `GenerateFullPage()`
- `StreamJsonParser.cs` - Parsing eventi metadata

---

## ✅ Conclusioni

**Feature Implementata:** ✅
**Codice Compila:** ✅
**Metadata sotto Messaggi:** ✅
**Working Directory Visibile:** ✅
**Click to Toggle:** ✅
**Una Riga:** ✅
**Allineamento Corretto:** ✅

**Prossimi Step:**
1. Chiudi l'app corrente (se in esecuzione)
2. Ricompila: `dotnet build`
3. Esegui app e testa tutti i 6 test case sopra
4. Verifica che i metadata appaiano subito dopo le risposte di Claude
5. Verifica che il click per nascondere/mostrare funzioni
6. Verifica che la working directory sia corretta

---

**Documento creato:** 2025-10-30
**Versione:** 1.0
**Autore:** Claude (Anthropic)
**Feature:** Metadata Display under Messages + Working Directory Bar
