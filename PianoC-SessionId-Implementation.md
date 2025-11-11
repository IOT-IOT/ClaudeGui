# Piano C: Implementazione Completa Piano B per SessionId

## 📋 Stato Attuale vs Piano B

### ✅ Già Implementato (Commit precedente):
- Estrazione Model e ClaudeVersion da messaggio "system"
- Visualizzazione model/version nel tab header
- Processing diretto di jsonLine (invece di rileggere dal file)

### ❌ Da Implementare (7 Commit del Piano B):

---

## Commit 1: Aggiungi InsertOrUpdateSessionAsync a DbService

**File**: `DbService.cs`

**Azione**: Creare nuovo metodo `InsertOrUpdateSessionAsync`
- Se la sessione esiste: aggiorna `lastActivity` (e `name` se vuoto)
- Se non esiste: inserisce nuova sessione
- Gestione errori con logging

**Codice da aggiungere**:
```csharp
/// <summary>
/// Inserisce una nuova sessione o aggiorna lastActivity se esiste già.
/// Usato quando si estrae SessionId da messaggi stdout.
/// </summary>
/// <param name="sessionId">UUID della sessione</param>
/// <param name="name">Nome della sessione (opzionale)</param>
/// <param name="workingDirectory">Working directory</param>
/// <param name="lastActivity">Timestamp ultima attività</param>
public async Task<bool> InsertOrUpdateSessionAsync(
    string sessionId,
    string? name,
    string workingDirectory,
    DateTime lastActivity)
{
    try
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var existingSession = await dbContext.Sessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);

        if (existingSession != null)
        {
            // Aggiorna lastActivity se esiste
            existingSession.LastActivity = lastActivity;

            // Aggiorna nome se vuoto e viene fornito
            if (string.IsNullOrWhiteSpace(existingSession.Name) && !string.IsNullOrWhiteSpace(name))
            {
                existingSession.Name = name;
            }

            await dbContext.SaveChangesAsync();
            Log.Debug("Updated lastActivity for session: {SessionId}", sessionId);
            return false; // Esisteva già
        }
        else
        {
            // Inserisci nuova sessione
            var session = new Session
            {
                SessionId = sessionId,
                Name = string.IsNullOrWhiteSpace(name) ? null : name,
                WorkingDirectory = workingDirectory,
                Status = "open",
                LastActivity = lastActivity,
                CreatedAt = DateTime.Now,
                Processed = false,
                Excluded = false
            };
            dbContext.Sessions.Add(session);
            await dbContext.SaveChangesAsync();
            Log.Information("Inserted new session: {SessionId}", sessionId);
            return true; // Nuova sessione
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to insert or update session: {SessionId}", sessionId);
        throw;
    }
}
```

**Motivazione**: `InsertSessionAsync` ritorna false se esiste già, ma non aggiorna `lastActivity`. Quando estraiamo SessionId da stdout, vogliamo sempre aggiornare `lastActivity`.

**Note**: MessageExistsAsync NON serve (Piano B originale lo richiedeva ma non è necessario nel nostro caso)

---

## Commit 2: ~~LoadHistoricalMessagesFromFileAsync~~ **SALTATO**

**Motivazione**: Non necessario - i messaggi storici vengono già caricati dal database con `GetLastMessagesAsync(sessionId, 50)` in `OpenSessionInNewTabAsync` (linee 303-335).

**Verifica**: I messaggi vengono visualizzati correttamente all'apertura di una sessione esistente.

---

## Commit 3: Modifica OnJsonLineReceived per estrarre SessionId

**File**: `MainPage.xaml.cs` → metodo `OnJsonLineReceived()`

**PRIMA** (linee 962-989):
```csharp
private async void OnJsonLineReceived(SessionTabItem tabItem, string jsonLine)
{
    try
    {
        await ProcessMessageLineFromFileAsync(tabItem, jsonLine);
        return;

        // ⚠️ DEAD CODE - unreachable dopo return ⚠️
        var filePath = GetSessionFilePath(tabItem.SessionId, tabItem.WorkingDirectory);
        // ... (linee 967-983)
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to process JSON line for tab: {SessionId}", tabItem.SessionId);
    }
}
```

**DOPO**:
```csharp
private async void OnJsonLineReceived(SessionTabItem tabItem, string jsonLine)
{
    try
    {
        // ========== STEP 1: Estrai SessionId se necessario ==========
        if (string.IsNullOrEmpty(tabItem.SessionId))
        {
            try
            {
                Log.Information("SessionId is empty, attempting to extract from stdout message");

                var json = JsonDocument.Parse(jsonLine);

                // Claude include il campo "session_id" nei messaggi JSON
                if (json.RootElement.TryGetProperty("session_id", out var sessionIdProp))
                {
                    var sessionId = sessionIdProp.GetString();

                    if (!string.IsNullOrWhiteSpace(sessionId))
                    {
                        Log.Information("✅ Extracted SessionId from Claude stdout: {SessionId}", sessionId);

                        // Aggiorna SessionId in memoria
                        tabItem.SessionId = sessionId;

                        // Inserisci/aggiorna nel database
                        if (_dbService != null)
                        {
                            await _dbService.InsertOrUpdateSessionAsync(
                                sessionId: sessionId,
                                name: tabItem.Name,
                                workingDirectory: tabItem.WorkingDirectory,
                                lastActivity: DateTime.Now
                            );
                        }

                        Log.Information("SessionId updated in memory and database");
                    }
                    else
                    {
                        Log.Warning("session_id property found but value is empty");
                    }
                }
                else
                {
                    Log.Debug("No session_id field in JSON message (might be metadata or other message type)");
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

        // ========== STEP 2: Verifica che abbiamo un SessionId valido ==========
        if (string.IsNullOrEmpty(tabItem.SessionId))
        {
            Log.Warning("SessionId still empty after extraction attempt - message will not be processed");
            return;
        }

        // ========== STEP 3: Processa il messaggio ==========
        await ProcessMessageLineFromFileAsync(tabItem, jsonLine);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to process JSON line for tab: {SessionId}", tabItem.SessionId);
    }
}
```

**Modifiche chiave**:
- ✅ Estrae `session_id` da QUALSIASI messaggio (non solo "system")
- ✅ Estrazione avviene PRIMA del processing
- ✅ Chiama `InsertOrUpdateSessionAsync` invece di `InsertSessionAsync`
- ✅ Verifica che SessionId sia valido prima di processare
- ✅ Rimuove dead code (linee 967-983)

**Motivazione**: Piano B richiede estrazione SessionId prima di processare messaggi, per gestire il caso in cui il primo messaggio non sia "system".

**Nota Importante**: Il campo JSON è `session_id` (snake_case), NON `sessionId` (camelCase). Il Piano B originale aveva questo errore.

---

## Commit 4: Rimuovi generazione manuale UUID

**File**: `MainPage.xaml.cs`

**Azione 1** - Linea 602 nel metodo `OnSelectSessionClicked()`:
```csharp
// PRIMA:
if (createdSession != null)
{
    createdSession.SessionId = Guid.NewGuid().ToString();  // ❌ Rimuovere
    await OpenSessionInNewTabAsync(createdSession, resumeExisting: false);
}

// DOPO:
if (createdSession != null)
{
    // SessionId verrà popolato da Claude quando invia il primo messaggio con session_id
    await OpenSessionInNewTabAsync(createdSession, resumeExisting: false);
}
```

**Azione 2** - Linea 643 nel metodo `OnSelectSessionClicked()`:
```csharp
// PRIMA:
if (isNewSession)
{
    // Genera un nuovo SessionId (verrà popolato dal processo Claude quando si avvia)
    selected.SessionId = Guid.NewGuid().ToString();  // ❌ Rimuovere
}

// DOPO:
if (isNewSession)
{
    // SessionId rimarrà vuoto finché Claude non invia il primo messaggio con session_id
    // L'estrazione avviene in OnJsonLineReceived quando arriva il primo messaggio
}
```

**Motivazione**: SessionId deve essere assegnato esclusivamente da Claude Code tramite i messaggi stdout, non generato manualmente dalla nostra applicazione. Questo garantisce che il SessionId sia sempre sincronizzato con quello usato da Claude.

---

## Commit 5: SessionSelectorPage non chiude se stesso

**File**: `SessionSelectorPage.xaml.cs` → metodo `ShowNewSessionDialogAsync()`

**PRIMA** (linee 543-554):
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
    Log.Information("New session: Name={Name}, WorkingDirectory={WorkingDirectory}",
        SelectedSession.Name, SelectedSession.WorkingDirectory);

    _selectionCompletionSource.TrySetResult(SelectedSession);

    // NON chiudere qui - MainPage si occuperà di:
    // 1. Chiudere SessionSelectorPage
    // 2. Aprire la nuova sessione con OpenSessionInNewTabAsync
    Log.Information("SessionSelectorPage completed - waiting for MainPage to close and open session");
    return;
}
```

**Modifiche**:
- ❌ Rimosso `await Navigation.PopModalAsync();`
- ✅ Aggiunto commento che MainPage gestirà la chiusura
- ✅ Logging migliorato con dettagli sessione

**Motivazione**: Delegare responsabilità di chiusura a MainPage per avere un flusso uniforme e risolvere il bug MAUI #26418 dove PopModalAsync non funziona correttamente su Windows.

---

## Commit 6: MainPage gestisce chiusura SessionSelectorPage

**File**: `MainPage.xaml.cs` → metodo `OnSelectSessionClicked()`

**PRIMA** (linee 620-649):
```csharp
// Aspetta che l'utente selezioni una sessione o annulli
var selected = await selectorPage.SelectionTask;

Log.Information("Session selector returned with selection: {HasSelection}", selected != null);

// Se è stata selezionata una sessione, aprila
if (selected != null)
{
    // ... validazioni ...
    await OpenSessionInNewTabAsync(selected, resumeExisting: !isNewSession);

    // NON chiudere qui - SessionSelectorPage si chiude da solo dopo TrySetResult
}
```

**DOPO**:
```csharp
// Aspetta che l'utente selezioni una sessione o annulli
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

// Se è stata selezionata una sessione, aprila
if (selected != null)
{
    // ... validazioni ...

    Log.Information("Opening selected session: SessionId={SessionId}, Resume={Resume}",
        selected.SessionId ?? "(empty - will be populated by Claude)", !isNewSession);

    await OpenSessionInNewTabAsync(selected, resumeExisting: !isNewSession);

    Log.Information("Session opened successfully");
}
else
{
    Log.Information("No session selected - user cancelled");
}
```

**Modifiche chiave**:
- ✅ MainPage chiude SessionSelectorPage con `PopModalAsync()` in try-catch
- ✅ Chiusura avviene PRIMA di aprire la sessione
- ✅ Gestione uniforme per nuove sessioni e sessioni esistenti
- ✅ Logging migliorato per debugging
- ❌ Rimosso commento "NON chiudere qui"

**Motivazione**: MainPage deve avere il controllo completo del flusso di navigazione. Questo risolve il bug dove SessionSelectorPage rimaneva visibile dopo la creazione di una nuova sessione.

---

## Commit 7: ~~Integra LoadHistoricalMessagesFromFileAsync~~ **SALTATO**

**Motivazione**: Non necessario - i messaggi storici sono già caricati dal database in `OpenSessionInNewTabAsync` (linee 303-335):

```csharp
// Carica ultimi 50 messaggi storici se riprendendo sessione esistente
if (resumeExisting && _dbService != null)
{
    var messages = await _dbService.GetLastMessagesAsync(session.SessionId, count: 50);
    Log.Information("Loaded {Count} historical messages", messages.Count);

    if (messages.Count > 0)
    {
        var renderer = new Utilities.MarkdownHtmlRenderer();
        var conversationHtml = new System.Text.StringBuilder();

        foreach (var message in messages)
        {
            if (message.MessageType == "user")
                conversationHtml.AppendLine(renderer.RenderUserMessage(message.Content));
            else if (message.MessageType == "assistant")
                conversationHtml.AppendLine(renderer.RenderAssistantMessage(message.Content));
        }

        var fullHtmlPage = renderer.GenerateFullPage(_isDarkTheme, conversationHtml.ToString());
        tabContent.InitializeWebView(fullHtmlPage);
    }
}
```

---

## Commit Extra: Rimuovi estrazione SessionId dal case "system"

**File**: `MainPage.xaml.cs` → metodo `ProcessMessageLineFromFileAsync()`

**PRIMA** (linee 1059-1122):
```csharp
case "system":
    // Messaggio di init da Claude - contiene session_id, model, version

    // ========== ESTRAI SESSION_ID ==========
    if (string.IsNullOrEmpty(tabItem.SessionId))
    {
        if (root.TryGetProperty("session_id", out var sessionIdProp))
        {
            var sessionId = sessionIdProp.GetString();

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                Log.Information("✅ Extracted SessionId from system message: {SessionId}", sessionId);

                // Aggiorna tabItem in memoria
                tabItem.SessionId = sessionId;

                // Inserisci la nuova sessione nel database con il SessionId estratto
                if (_dbService != null)
                {
                    await _dbService.InsertSessionAsync(
                        sessionId: sessionId,
                        name: tabItem.Name,
                        workingDirectory: tabItem.WorkingDirectory,
                        lastActivity: DateTime.Now
                    );
                }

                Log.Information("SessionId updated in memory and database");
            }
        }
    }

    // ========== ESTRAI MODEL E VERSION ==========
    if (root.TryGetProperty("model", out var modelProp))
    {
        // ... mantieni questo ...
    }

    if (root.TryGetProperty("claude_code_version", out var versionProp))
    {
        // ... mantieni questo ...
    }

    // Aggiorna il tab header se model/version sono stati estratti
    if (!string.IsNullOrWhiteSpace(tabItem.Model) || !string.IsNullOrWhiteSpace(tabItem.ClaudeVersion))
    {
        tabItem.RefreshTabTitle();
        Log.Information("Tab header updated with model/version info");
    }

    break;
```

**DOPO**:
```csharp
case "system":
    // Messaggio di init da Claude - contiene model e version
    // NOTA: SessionId viene estratto in OnJsonLineReceived, non qui

    // ========== ESTRAI MODEL E VERSION ==========
    if (root.TryGetProperty("model", out var modelProp))
    {
        var model = modelProp.GetString();
        if (!string.IsNullOrWhiteSpace(model))
        {
            tabItem.Model = model;
            Log.Information("Model extracted: {Model}", model);
        }
    }

    if (root.TryGetProperty("claude_code_version", out var versionProp))
    {
        var version = versionProp.GetString();
        if (!string.IsNullOrWhiteSpace(version))
        {
            tabItem.ClaudeVersion = version;
            Log.Information("Claude version extracted: {Version}", version);
        }
    }

    // Aggiorna il tab header se model/version sono stati estratti
    if (!string.IsNullOrWhiteSpace(tabItem.Model) || !string.IsNullOrWhiteSpace(tabItem.ClaudeVersion))
    {
        tabItem.RefreshTabTitle();
        Log.Information("Tab header updated with model/version info");
    }

    break;
```

**Modifiche**:
- ❌ Rimosso blocco di estrazione SessionId (linee 1062-1090)
- ✅ Mantenuto l'estrazione di Model e ClaudeVersion
- ✅ Aggiunto commento che SessionId viene estratto in OnJsonLineReceived

**Motivazione**: Evitare duplicazione - l'estrazione di SessionId viene gestita centralmente in `OnJsonLineReceived`, che viene chiamato per OGNI messaggio.

---

## 📊 Riepilogo Modifiche

| Commit | File | Azione | Status |
|--------|------|--------|--------|
| 1 | DbService.cs | Crea InsertOrUpdateSessionAsync | ✅ Da fare |
| 2 | MainPage.xaml.cs | ~~LoadHistoricalMessagesFromFileAsync~~ | ❌ Saltato |
| 3 | MainPage.xaml.cs | Sposta estrazione SessionId + cleanup | ✅ Da fare |
| 4 | MainPage.xaml.cs | Rimuovi Guid.NewGuid() | ✅ Da fare |
| 5 | SessionSelectorPage.xaml.cs | Rimuovi PopModalAsync | ✅ Da fare |
| 6 | MainPage.xaml.cs | Aggiungi PopModalAsync in MainPage | ✅ Da fare |
| 7 | MainPage.xaml.cs | ~~Integra LoadHistorical...~~ | ❌ Saltato |
| Extra | MainPage.xaml.cs | Rimuovi SessionId da case "system" | ✅ Da fare |

**Totale**: 6 commit da implementare

---

## ⚠️ Note Importanti

### Campo JSON Corretto
- ✅ **Usare**: `session_id` (snake_case)
- ❌ **NON usare**: `sessionId` (camelCase)
- Il Piano B originale aveva questo errore

### Messaggi Storici
- ✅ Già caricati dal database con `GetLastMessagesAsync()`
- ❌ Non serve `LoadHistoricalMessagesFromFileAsync`
- ❌ Non serve `MessageExistsAsync`

### Claude Code Behavior
- **`--resume`**: Carica contesto dal file, NON scrive su stdout
- **`--replay-user-messages`**: Fa sì che i messaggi user vengano anche scritti su stdout (per visualizzarli nella nostra WebView)
- **SessionId**: Viene inviato da Claude nel primo messaggio (solitamente "system")

### Bug MAUI
- `PopModalAsync` non funziona correttamente su Windows (issue #26418)
- Soluzione: MainPage gestisce la chiusura invece di SessionSelectorPage

---

## 🎯 Ordine di Esecuzione

1. **Commit 1**: InsertOrUpdateSessionAsync (DbService)
2. **Commit 3**: Estrazione SessionId in OnJsonLineReceived
3. **Commit Extra**: Rimuovi SessionId dal case "system"
4. **Commit 4**: Rimuovi Guid.NewGuid()
5. **Commit 5**: SessionSelectorPage non chiude
6. **Commit 6**: MainPage gestisce chiusura

Ogni commit sarà separato per facilitare debug e rollback se necessario.

---

## 🔍 Testing

Dopo ogni commit, testare:

### Test 1: Nuova Sessione
1. Avvia app
2. Click "Seleziona Sessione"
3. Click "Nuova Sessione"
4. Compila nome e working directory
5. Click "Crea Sessione"
6. **Verifica**: SessionSelectorPage si chiude
7. **Verifica**: Nuovo tab appare con nome sessione
8. Invia un messaggio a Claude
9. **Verifica nei log**: "✅ Extracted SessionId from Claude stdout"
10. **Verifica nel tab**: Header mostra model e version (es: "Test Session (sonnet v2.0.37)")

### Test 2: Resume Sessione Esistente
1. Riapri sessione esistente con messaggi
2. **Verifica**: Messaggi storici appaiono immediatamente
3. **Verifica**: Tab header mostra model e version
4. Invia nuovo messaggio
5. **Verifica**: Messaggio viene processato normalmente

### Test 3: SessionId Extraction
1. Avvia nuova sessione
2. Monitora log
3. **Verifica**: "SessionId is empty, attempting to extract"
4. **Verifica**: "✅ Extracted SessionId from Claude stdout: {SessionId}"
5. **Verifica**: "SessionId updated in memory and database"
6. Controlla database: session deve avere SessionId popolato

---

## 📝 Checklist Implementazione

- [ ] Commit 1: InsertOrUpdateSessionAsync creato e testato
- [ ] Commit 3: OnJsonLineReceived modificato per estrarre SessionId
- [ ] Commit Extra: case "system" pulito
- [ ] Commit 4: Guid.NewGuid() rimosso
- [ ] Commit 5: SessionSelectorPage non chiude se stesso
- [ ] Commit 6: MainPage gestisce chiusura
- [ ] Test completo nuova sessione
- [ ] Test completo resume sessione
- [ ] Verifica logs per conferma estrazione SessionId
- [ ] Verifica database per SessionId corretto
- [ ] Push finale su repository

---

## 🎉 Risultato Finale

Dopo l'implementazione completa del Piano C:

1. ✅ SessionId viene estratto automaticamente da Claude
2. ✅ Nessuna generazione manuale di UUID
3. ✅ Model e version mostrati nel tab header
4. ✅ SessionSelectorPage si chiude correttamente
5. ✅ Flusso uniforme per nuove sessioni e resume
6. ✅ Messaggi storici caricati dal database
7. ✅ Bug MAUI #26418 risolto
8. ✅ Conformità completa con Piano B (versione corretta)
