# Piano A: Fix SessionId - Approccio Conservativo (File-based)

## Sommario
Questo piano risolve il problema del SessionId vuoto per nuove sessioni mantenendo l'architettura attuale basata sulla lettura dei file .jsonl. Estrae il SessionId dal parametro `jsonLine` (stdout) solo quando necessario, poi continua a leggere i messaggi dal file come prima.

---

## Problemi da Risolvere

### 1. SessionId generato manualmente invece che da Claude
**File**: `MainPage.xaml.cs`, linea 638-647

**Problema attuale**:
```csharp
if (isNewSession)
{
    // Genera un nuovo SessionId (verrà popolato dal processo Claude quando si avvia)
    selected.SessionId = Guid.NewGuid().ToString();  // ❌ UUID LOCALE SBAGLIATO
}
```

**Conseguenza**:
- Il database contiene un UUID generato manualmente
- Claude genera il suo SessionId reale che viene ignorato
- Mismatch tra database e file .jsonl sul filesystem

### 2. GetSessionFilePath fallisce con SessionId vuoto
**File**: `MainPage.xaml.cs`, linea 966

**Problema attuale**:
```csharp
private async void OnJsonLineReceived(SessionTabItem tabItem, string jsonLine)
{
    // Se tabItem.SessionId = "", questo costruisce un path sbagliato
    var filePath = GetSessionFilePath(tabItem.SessionId, tabItem.WorkingDirectory);
    // Risultato: C:\Users\user\.claude\projects\C--Sources-claudegui\.jsonl ❌

    if (!File.Exists(filePath))
    {
        Log.Warning("Session file not found: {FilePath}", filePath);
        return;  // ❌ Nessun messaggio viene processato
    }
}
```

**Conseguenza**:
- Per nuove sessioni, nessun messaggio viene mai processato
- Il file esiste ma non viene trovato perché il path è sbagliato
- L'utente vede una sessione vuota anche se Claude sta rispondendo

### 3. SessionSelectorPage chiude se stesso invece di lasciare fare a MainPage
**File**: `SessionSelectorPage.xaml.cs`, linea 551-553

**Problema attuale**:
```csharp
// Chiudi QUESTO SessionSelectorPage immediatamente
Log.Information("SessionSelectorPage closing itself after new session creation");
await Navigation.PopModalAsync();
```

**Conseguenza**:
- Responsabilità di chiusura e apertura sessione sono frammentate
- MainPage non chiama `OpenSessionInNewTabAsync` per nuove sessioni da SessionSelectorPage
- Nuova sessione viene creata ma non aperta automaticamente

---

## Modifiche da Implementare

### Modifica 1: Rimuovere generazione manuale di SessionId

**File**: `MainPage.xaml.cs`
**Metodo**: `OnSelectSessionClicked`
**Linea**: ~638-647

**PRIMA**:
```csharp
// Previeni aperture multiple
if (_isSessionSelectorOpen)
{
    Log.Warning("Session selector already open, ignoring request");
    return;
}

_isSessionSelectorOpen = true;

Log.Information("Opening session selector dialog");

var selectorPage = new SessionSelectorPage(_sessionScanner, _dbService);

await Navigation.PushModalAsync(selectorPage);

var selected = await selectorPage.SelectionTask;

Log.Information("Session selector returned with selection: {HasSelection}", selected != null);

if (selected != null)
{
    bool isNewSession = string.IsNullOrWhiteSpace(selected.SessionId) || selected.SessionId == "NEW_SESSION";

    if (!isNewSession && string.IsNullOrWhiteSpace(selected.Name))
    {
        await this.DisplaySelectableAlert("Nome Mancante", ...);
        return;
    }

    if (isNewSession)
    {
        selected.SessionId = Guid.NewGuid().ToString();  // ❌ RIMUOVERE
    }

    await OpenSessionInNewTabAsync(selected, resumeExisting: !isNewSession);
}
```

**DOPO**:
```csharp
// Previeni aperture multiple
if (_isSessionSelectorOpen)
{
    Log.Warning("Session selector already open, ignoring request");
    return;
}

_isSessionSelectorOpen = true;

try
{
    Log.Information("Opening session selector dialog");

    var selectorPage = new SessionSelectorPage(_sessionScanner, _dbService);

    await Navigation.PushModalAsync(selectorPage);

    var selected = await selectorPage.SelectionTask;

    Log.Information("Session selector returned with selection: {HasSelection}", selected != null);

    if (selected != null)
    {
        // Determina se è nuova sessione (SessionId vuoto o placeholder)
        bool isNewSession = string.IsNullOrWhiteSpace(selected.SessionId);

        if (!isNewSession && string.IsNullOrWhiteSpace(selected.Name))
        {
            await this.DisplaySelectableAlert("Nome Mancante",
                "Questa sessione non ha un nome assegnato.\n\n" +
                "Assegna un nome prima di aprirla utilizzando il pulsante 'Assegna Nome' " +
                "o modificando direttamente il campo nella tabella.",
                "OK");
            return;
        }

        // ✅ NON generare UUID locale per nuove sessioni
        // Il SessionId verrà estratto dal primo messaggio di Claude in OnJsonLineReceived

        Log.Information("Opening selected session: SessionId={SessionId}, Resume={Resume}",
            selected.SessionId ?? "(empty - will be populated)", !isNewSession);

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
finally
{
    _isSessionSelectorOpen = false;
    Log.Information("Session selector closed, flag reset");
}
```

**Modifiche chiave**:
- ✅ Rimosso `Guid.NewGuid().ToString()` per nuove sessioni
- ✅ Semplificato check `isNewSession` (solo verifica se SessionId è vuoto)
- ✅ SessionId rimarrà vuoto fino al primo messaggio da Claude
- ✅ Aggiunto try-catch-finally per gestione errori robusta

---

### Modifica 2: Estrarre SessionId da stdout in OnJsonLineReceived

**File**: `MainPage.xaml.cs`
**Metodo**: `OnJsonLineReceived`
**Linea**: ~961-990

**PRIMA**:
```csharp
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

        var lastLines = await _dbService?.ReadLastLinesFromFileAsync(filePath, maxLines: 20, bufferSizeKb: 32)
            ?? new List<string>();

        foreach (var line in lastLines)
        {
            await ProcessMessageLineFromFileAsync(tabItem, line);
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to process JSON line from file");
    }
}
```

**DOPO**:
```csharp
private async void OnJsonLineReceived(SessionTabItem tabItem, string jsonLine)
{
    try
    {
        // ========== STEP 1: Estrai SessionId da stdout se necessario ==========

        // Se SessionId è vuoto (nuova sessione), estrailo dal messaggio JSON ricevuto da Claude
        if (string.IsNullOrEmpty(tabItem.SessionId))
        {
            try
            {
                Log.Information("SessionId is empty, attempting to extract from stdout message");

                var json = JsonDocument.Parse(jsonLine);

                // Claude include il campo "sessionId" nei messaggi JSON
                if (json.RootElement.TryGetProperty("sessionId", out var sessionIdProp))
                {
                    var sessionId = sessionIdProp.GetString();

                    if (!string.IsNullOrWhiteSpace(sessionId))
                    {
                        Log.Information("✅ Extracted SessionId from Claude stdout: {SessionId}", sessionId);

                        // Aggiorna SessionId in memoria
                        tabItem.SessionId = sessionId;

                        // Aggiorna anche il Session object se presente
                        if (tabItem.SessionInfo != null)
                        {
                            tabItem.SessionInfo.SessionId = sessionId;
                        }

                        // Inserisci/aggiorna nel database
                        await _dbService?.InsertOrUpdateSessionAsync(
                            sessionId: sessionId,
                            name: tabItem.SessionInfo?.Name ?? "",
                            workingDirectory: tabItem.WorkingDirectory,
                            lastActivity: DateTime.Now
                        );

                        Log.Information("SessionId updated in memory and database");
                    }
                    else
                    {
                        Log.Warning("SessionId property found but value is empty");
                    }
                }
                else
                {
                    Log.Debug("No sessionId field in JSON message (might be metadata or other message type)");
                }
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, "Failed to parse jsonLine as JSON for SessionId extraction");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to extract SessionId from stdout");
            }
        }

        // ========== STEP 2: Controlla che ora abbiamo un SessionId valido ==========

        if (string.IsNullOrEmpty(tabItem.SessionId))
        {
            Log.Warning("SessionId still empty after extraction attempt, cannot construct file path. Waiting for next message...");
            return;
        }

        // ========== STEP 3: Costruisci path del file .jsonl (ora con SessionId valido) ==========

        var filePath = GetSessionFilePath(tabItem.SessionId, tabItem.WorkingDirectory);

        if (!File.Exists(filePath))
        {
            Log.Warning("Session file not found: {FilePath}", filePath);
            return;
        }

        // ========== STEP 4: Leggi e processa messaggi dal file (COME PRIMA) ==========

        var lastLines = await _dbService?.ReadLastLinesFromFileAsync(filePath, maxLines: 20, bufferSizeKb: 32)
            ?? new List<string>();

        foreach (var line in lastLines)
        {
            await ProcessMessageLineFromFileAsync(tabItem, line);
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to process JSON line from file");
    }
}
```

**Modifiche chiave**:
- ✅ Estrae SessionId dal parametro `jsonLine` (stdout) quando `tabItem.SessionId` è vuoto
- ✅ Aggiorna `tabItem.SessionId` in memoria
- ✅ Chiama `InsertOrUpdateSessionAsync` per salvare nel database
- ✅ Dopo estrazione, continua il flusso normale leggendo dal file .jsonl
- ✅ Se SessionId ancora vuoto dopo tentativo, aspetta il prossimo messaggio
- ✅ Logging dettagliato per debug

---

### Modifica 3: SessionSelectorPage non chiude se stesso

**File**: `SessionSelectorPage.xaml.cs`
**Metodo**: `ShowNewSessionDialogAsync`
**Linea**: ~543-556

**PRIMA**:
```csharp
// Se una sessione è stata creata (callback invocato), chiudi SessionSelectorPage
if (SelectedSession != null)
{
    Log.Information("Setting SelectionTask result with new session...");
    _selectionCompletionSource.TrySetResult(SelectedSession);

    // Chiudi QUESTO SessionSelectorPage immediatamente
    Log.Information("SessionSelectorPage closing itself after new session creation");
    await Navigation.PopModalAsync();
    Log.Information("SessionSelectorPage closed itself");
    return;
}
```

**DOPO**:
```csharp
// Se una sessione è stata creata (callback invocato), notifica MainPage
if (SelectedSession != null)
{
    Log.Information("Setting SelectionTask result with new session...");
    Log.Information("New session: Name={Name}, WorkingDirectory={WorkingDirectory}, SessionId={SessionId}",
        SelectedSession.Name, SelectedSession.WorkingDirectory, SelectedSession.SessionId ?? "(empty)");

    _selectionCompletionSource.TrySetResult(SelectedSession);

    // NON chiudere qui - MainPage si occuperà di:
    // 1. Chiudere SessionSelectorPage
    // 2. Aprire la nuova sessione con OpenSessionInNewTabAsync
    Log.Information("SessionSelectorPage completed - waiting for MainPage to close and open session");
    return;
}
```

**Modifiche chiave**:
- ✅ Rimosso `await Navigation.PopModalAsync()` da SessionSelectorPage
- ✅ Responsabilità di chiusura delegata a MainPage
- ✅ MainPage ora gestisce sia chiusura che apertura sessione
- ✅ Flusso uniforme per nuove sessioni e sessioni esistenti

---

### Modifica 4: Aggiungere metodo InsertOrUpdateSessionAsync al DbService

**File**: `DbService.cs`
**Nuovo metodo da aggiungere**

```csharp
/// <summary>
/// Inserisce o aggiorna una sessione nel database.
/// Se la sessione con il SessionId esiste già, aggiorna solo lastActivity.
/// Altrimenti inserisce un nuovo record.
/// </summary>
/// <param name="sessionId">UUID della sessione</param>
/// <param name="name">Nome della sessione (opzionale)</param>
/// <param name="workingDirectory">Working directory</param>
/// <param name="lastActivity">Timestamp ultima attività</param>
public async Task InsertOrUpdateSessionAsync(
    string sessionId,
    string name,
    string workingDirectory,
    DateTime lastActivity)
{
    try
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        // Cerca se la sessione esiste già
        var existingSession = await context.Sessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);

        if (existingSession != null)
        {
            // Aggiorna solo lastActivity e eventualmente il nome se vuoto
            existingSession.LastActivity = lastActivity;

            if (string.IsNullOrWhiteSpace(existingSession.Name) && !string.IsNullOrWhiteSpace(name))
            {
                existingSession.Name = name;
            }

            await context.SaveChangesAsync();

            Log.Information("Updated existing session: {SessionId}, LastActivity: {LastActivity}",
                sessionId, lastActivity);
        }
        else
        {
            // Inserisci nuova sessione
            var newSession = new Session
            {
                SessionId = sessionId,
                Name = name,
                WorkingDirectory = workingDirectory,
                Status = "open",
                CreatedAt = lastActivity,
                LastActivity = lastActivity,
                Processed = true,  // Già processata perché proviene da runtime
                Excluded = false
            };

            context.Sessions.Add(newSession);
            await context.SaveChangesAsync();

            Log.Information("Inserted new session: {SessionId}, Name: {Name}, WorkingDirectory: {WorkingDirectory}",
                sessionId, name, workingDirectory);
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to insert or update session: {SessionId}", sessionId);
        throw;
    }
}
```

**Caratteristiche**:
- ✅ Controlla se sessione esiste (by SessionId)
- ✅ Se esiste: aggiorna solo `LastActivity` e `Name` (se vuoto)
- ✅ Se non esiste: inserisce nuovo record con `Processed = true`
- ✅ Gestione errori con logging

---

### Modifica 5: MainPage gestisce chiusura SessionSelectorPage

**File**: `MainPage.xaml.cs`
**Metodo**: `OnSelectSessionClicked`
**Linea**: dopo `var selected = await selectorPage.SelectionTask;`

**PRIMA**:
```csharp
var selected = await selectorPage.SelectionTask;

Log.Information("Session selector returned with selection: {HasSelection}", selected != null);

if (selected != null)
{
    // ... validazioni e apertura sessione ...
    await OpenSessionInNewTabAsync(selected, resumeExisting: !isNewSession);
}
```

**DOPO**:
```csharp
var selected = await selectorPage.SelectionTask;

Log.Information("Session selector returned with selection: {HasSelection}", selected != null);

// Chiudi SessionSelectorPage (sia per sessioni nuove che esistenti)
try
{
    await Navigation.PopModalAsync();
    Log.Information("SessionSelectorPage closed by MainPage");
}
catch (Exception ex)
{
    Log.Warning(ex, "Failed to close SessionSelectorPage modal");
}

if (selected != null)
{
    // Determina se è nuova sessione
    bool isNewSession = string.IsNullOrWhiteSpace(selected.SessionId);

    if (!isNewSession && string.IsNullOrWhiteSpace(selected.Name))
    {
        await this.DisplaySelectableAlert("Nome Mancante", ...);
        return;
    }

    Log.Information("Opening selected session: SessionId={SessionId}, Resume={Resume}",
        selected.SessionId ?? "(empty - will be populated)", !isNewSession);

    // Apri la sessione (sia nuova che esistente)
    await OpenSessionInNewTabAsync(selected, resumeExisting: !isNewSession);

    Log.Information("Session opened successfully");
}
else
{
    Log.Information("No session selected - user cancelled");
}
```

**Modifiche chiave**:
- ✅ MainPage chiude SessionSelectorPage con `PopModalAsync()`
- ✅ Gestione uniforme per nuove sessioni e sessioni esistenti
- ✅ Chiusura avviene PRIMA di aprire la sessione
- ✅ Try-catch per gestire eventuali errori di chiusura

---

## Flusso Completo - Nuova Sessione

### 1. User clicca "Nuova Sessione" in SessionSelectorPage
- NewSessionDialog viene aperto con callback
- User compila nome e working directory
- User clicca "Crea Sessione"

### 2. NewSessionDialog invoca callback
```csharp
_onSessionCreated?.Invoke(new Session {
    SessionId = string.Empty,  // ✅ VUOTO
    Name = "My Session",
    WorkingDirectory = "C:\\Sources\\MyProject",
    Status = "open",
    CreatedAt = DateTime.Now,
    ...
});
```

### 3. SessionSelectorPage riceve session via callback
```csharp
SelectedSession = session;  // SessionId è vuoto
_selectionCompletionSource.TrySetResult(SelectedSession);
// NON chiude se stesso
```

### 4. MainPage riceve session da SelectionTask
```csharp
var selected = await selectorPage.SelectionTask;
// selected.SessionId = ""

await Navigation.PopModalAsync();  // Chiude SessionSelectorPage

bool isNewSession = string.IsNullOrWhiteSpace(selected.SessionId);  // true

await OpenSessionInNewTabAsync(selected, resumeExisting: false);
```

### 5. OpenSessionInNewTabAsync avvia ClaudeProcessManager
```csharp
var processManager = new ClaudeProcessManager(
    resumeSessionId: null,  // ✅ Nessun --resume
    dbSessionId: "",        // ✅ SessionId vuoto
    workingDirectory: "C:\\Sources\\MyProject"
);
processManager.Start();
```

### 6. Claude avvia e genera SessionId
- Claude crea file: `C:\Users\user\.claude\projects\C--Sources-MyProject\{uuid}.jsonl`
- Claude scrive primo messaggio con campo `sessionId: "abc-123-..."`
- Messaggio viene emesso su stdout

### 7. OnJsonLineReceived riceve primo messaggio
```csharp
// tabItem.SessionId = ""
// jsonLine = '{"sessionId": "abc-123-...", "type": "...", ...}'

// STEP 1: Estrai SessionId da jsonLine
var json = JsonDocument.Parse(jsonLine);
var sessionId = json.RootElement.GetProperty("sessionId").GetString();
// sessionId = "abc-123-..."

// Aggiorna in memoria
tabItem.SessionId = sessionId;

// Aggiorna database
await _dbService.InsertOrUpdateSessionAsync(sessionId, "My Session", workingDirectory, DateTime.Now);

// STEP 2: Costruisci filePath (ora con SessionId valido)
var filePath = GetSessionFilePath(sessionId, workingDirectory);
// filePath = "C:\Users\user\.claude\projects\C--Sources-MyProject\abc-123-....jsonl"

// STEP 3: Leggi messaggi dal file
var lastLines = await _dbService.ReadLastLinesFromFileAsync(filePath, 20);

// STEP 4: Processa messaggi
foreach (var line in lastLines)
{
    await ProcessMessageLineFromFileAsync(tabItem, line);
}
```

### 8. Database finale
```sql
-- Tabella Sessions
SessionId: "abc-123-..."  ✅ UUID REALE DA CLAUDE
Name: "My Session"
WorkingDirectory: "C:\Sources\MyProject"
Status: "open"
Processed: true
Excluded: false

-- Tabella Messages
conversation_id: "abc-123-..."  ✅ FK corretta
uuid: "msg-uuid-1"
type: "user"
content: "..."
...
```

---

## Vantaggi del Piano A

### ✅ Pro:
1. **Minimo impatto sul codice esistente**: Cambia solo `OnJsonLineReceived` e rimuove generazione UUID
2. **Robustezza**: Continua a leggere dal file, fonte di verità
3. **Recovery**: Se app crasha, messaggi vengono recuperati al riavvio
4. **Gestione duplicati**: Già gestita tramite UUID dei messaggi
5. **Backward compatible**: Sessioni esistenti continuano a funzionare
6. **Debugging facile**: File .jsonl sempre consultabili manualmente

### ⚠️ Contro:
1. **Ridondanza I/O**: Legge file che Claude ha appena scritto
2. **Possibile race condition**: File potrebbe non essere scritto quando leggiamo (mitigato da retry in `ReadLastLinesFromFileAsync`)
3. **Latency**: Leggere dal file aggiunge latenza rispetto a processare direttamente stdout

---

## Testing

### Test Case 1: Nuova Sessione
1. Avvia app
2. Clicca "Seleziona Sessione"
3. Clicca "Nuova Sessione"
4. Compila nome e working directory
5. Clicca "Crea Sessione"
6. **Verifica**: SessionSelectorPage si chiude
7. **Verifica**: MainPage apre nuovo tab con sessione
8. Invia primo messaggio a Claude
9. **Verifica**: Nel log appare "✅ Extracted SessionId from Claude stdout: {uuid}"
10. **Verifica**: Messaggi appaiono nella WebView
11. **Verifica**: Database contiene SessionId reale di Claude (query: `SELECT * FROM Sessions WHERE Name = 'test'`)

### Test Case 2: Resume Sessione Esistente
1. Avvia app
2. Clicca "Seleziona Sessione"
3. Seleziona sessione esistente dalla lista
4. **Verifica**: SessionSelectorPage si chiude
5. **Verifica**: Sessione viene aperta con --resume
6. **Verifica**: Messaggi storici vengono caricati

### Test Case 3: Annulla Selezione
1. Avvia app
2. Clicca "Seleziona Sessione"
3. Clicca "Annulla"
4. **Verifica**: SessionSelectorPage si chiude
5. **Verifica**: Nessuna sessione aperta

### Test Case 4: Crash Recovery
1. Crea nuova sessione e invia messaggi
2. Chiudi app forzatamente (kill process)
3. Riavvia app
4. **Verifica**: SessionScannerService trova la sessione dal file .jsonl
5. **Verifica**: SessionId nel database corrisponde al nome file

---

## Rollback

Se il piano A causa problemi:

### Rollback Parziale - Solo SessionId Fix:
```bash
git revert {commit-hash-modifica-1}
git revert {commit-hash-modifica-2}
```
Mantiene: SessionSelectorPage che non chiude se stesso

### Rollback Completo:
```bash
git revert {commit-hash-inizio}..{commit-hash-fine}
```

---

## Note Implementative

### Ordine dei commit:
1. **Commit 1**: Aggiungi `InsertOrUpdateSessionAsync` a DbService
2. **Commit 2**: Modifica `OnJsonLineReceived` per estrarre SessionId
3. **Commit 3**: Rimuovi generazione manuale UUID in MainPage
4. **Commit 4**: SessionSelectorPage non chiude se stesso
5. **Commit 5**: MainPage gestisce chiusura SessionSelectorPage

### Logging da aggiungere:
- SessionId estratto da stdout
- SessionId aggiornato in memoria e database
- File path costruito con SessionId valido
- Eventuali errori di parsing JSON

### Metriche da monitorare:
- Numero di sessioni con SessionId vuoto nel database (dovrebbe essere 0 dopo fix)
- Tempo tra avvio Claude e ricezione primo SessionId
- Frequenza di race conditions (file non trovato)
