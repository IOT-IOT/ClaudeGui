# Piano B: Fix SessionId - Approccio Ottimizzato (Stdout-based)

## Sommario
Questo piano risolve il problema del SessionId vuoto processando **direttamente** il messaggio JSON da stdout (`jsonLine`) invece di rileggerlo dal file .jsonl. Elimina ridondanza I/O e race conditions, ma richiede modifiche più invasive al codice.

---

## Problemi da Risolvere
(Identici al Piano A - vedi sezione corrispondente in PianoA-SessionId-Fix.md)

---

## Filosofia del Piano B

### Differenza chiave vs Piano A:

**Piano A (File-based)**:
```
Claude stdout → OnJsonLineReceived → Estrai SessionId → Leggi file .jsonl → Processa messaggi
                    ↓ ignora jsonLine                        ↑
                    └────────────────────────────────────────┘
```

**Piano B (Stdout-based)**:
```
Claude stdout → OnJsonLineReceived → Estrai SessionId → Processa jsonLine direttamente
                    ↑ usa jsonLine
```

### Vantaggi:
- ✅ Zero ridondanza I/O (non legge file appena scritto)
- ✅ Nessuna race condition (stdout sempre sincronizzato)
- ✅ Latenza inferiore (no disk read)
- ✅ Più semplice logicamente (1 fonte dati invece di 2)

### Svantaggi:
- ❌ File .jsonl usato solo per recovery/backup
- ❌ Se stdout perde messaggi, non vengono recuperati
- ❌ Modifiche più invasive al codice esistente

---

## Modifiche da Implementare

### Modifica 1: Rimuovere generazione manuale di SessionId
**(Identica al Piano A - vedi PianoA-SessionId-Fix.md, Modifica 1)**

---

### Modifica 2: Processare jsonLine direttamente invece di leggere dal file

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

        if (string.IsNullOrEmpty(tabItem.SessionId))
        {
            try
            {
                Log.Information("SessionId is empty, attempting to extract from stdout message");

                var json = JsonDocument.Parse(jsonLine);

                if (json.RootElement.TryGetProperty("sessionId", out var sessionIdProp))
                {
                    var sessionId = sessionIdProp.GetString();

                    if (!string.IsNullOrWhiteSpace(sessionId))
                    {
                        Log.Information("✅ Extracted SessionId from Claude stdout: {SessionId}", sessionId);

                        // Aggiorna SessionId in memoria
                        tabItem.SessionId = sessionId;

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

        // ========== STEP 2: Verifica che abbiamo un SessionId valido ==========

        if (string.IsNullOrEmpty(tabItem.SessionId))
        {
            Log.Warning("SessionId still empty, cannot process message. Waiting for next message with sessionId...");
            return;
        }

        // ========== STEP 3: Processa direttamente il messaggio da stdout ==========

        Log.Debug("Processing message directly from stdout for session {SessionId}", tabItem.SessionId);

        // Processa il messaggio ricevuto direttamente da stdout
        await ProcessMessageLineFromFileAsync(tabItem, jsonLine);

        Log.Debug("Message processed successfully from stdout");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to process JSON line from stdout");
    }
}
```

**Modifiche chiave**:
- ✅ Estrae SessionId come Piano A
- ✅ Aggiorna memoria e database
- ✅ **DIFFERENZA**: Chiama direttamente `ProcessMessageLineFromFileAsync(tabItem, jsonLine)` invece di leggere dal file
- ✅ Nessuna chiamata a `GetSessionFilePath` o `ReadLastLinesFromFileAsync`
- ✅ Logging specifico per stdout processing

---

### Modifica 3: SessionSelectorPage non chiude se stesso
**(Identica al Piano A - vedi PianoA-SessionId-Fix.md, Modifica 3)**

---

### Modifica 4: Aggiungere metodo InsertOrUpdateSessionAsync al DbService
**(Identico al Piano A - vedi PianoA-SessionId-Fix.md, Modifica 4)**

---

### Modifica 5: MainPage gestisce chiusura SessionSelectorPage
**(Identica al Piano A - vedi PianoA-SessionId-Fix.md, Modifica 5)**

---

### Modifica 6 (NUOVA - solo Piano B): Recovery messaggi dal file al boot

**File**: `MainPage.xaml.cs`
**Nuovo metodo da aggiungere**

```csharp
/// <summary>
/// Carica messaggi storici dal file .jsonl quando si riapre una sessione esistente.
/// Usato solo per recovery/resume, i nuovi messaggi vengono processati direttamente da stdout.
/// </summary>
/// <param name="tabItem">Tab della sessione</param>
private async Task LoadHistoricalMessagesFromFileAsync(SessionTabItem tabItem)
{
    try
    {
        if (string.IsNullOrEmpty(tabItem.SessionId))
        {
            Log.Warning("Cannot load historical messages: SessionId is empty");
            return;
        }

        var filePath = GetSessionFilePath(tabItem.SessionId, tabItem.WorkingDirectory);

        if (!File.Exists(filePath))
        {
            Log.Information("No historical messages file found: {FilePath}", filePath);
            return;
        }

        Log.Information("Loading historical messages from file: {FilePath}", filePath);

        // Leggi TUTTI i messaggi dal file (non solo ultimi 20)
        var allLines = await File.ReadAllLinesAsync(filePath);

        Log.Information("Found {Count} historical messages in file", allLines.Length);

        int processed = 0;
        int skipped = 0;

        foreach (var line in allLines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                skipped++;
                continue;
            }

            try
            {
                // Controlla se il messaggio è già nel database (by UUID)
                var json = JsonDocument.Parse(line);
                if (json.RootElement.TryGetProperty("uuid", out var uuidProp))
                {
                    var messageUuid = uuidProp.GetString();
                    if (!string.IsNullOrWhiteSpace(messageUuid))
                    {
                        // Verifica se esiste già nel database
                        var exists = await _dbService?.MessageExistsAsync(messageUuid) ?? false;

                        if (exists)
                        {
                            Log.Debug("Message {UUID} already in database, skipping", messageUuid);
                            skipped++;
                            continue;
                        }
                    }
                }

                // Processa il messaggio
                await ProcessMessageLineFromFileAsync(tabItem, line);
                processed++;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to process historical message line: {Line}", line.Substring(0, Math.Min(100, line.Length)));
                skipped++;
            }
        }

        Log.Information("Historical messages loaded: {Processed} processed, {Skipped} skipped", processed, skipped);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to load historical messages from file");
    }
}
```

**Utilizzo**: Chiamare questo metodo in `OpenSessionInNewTabAsync` DOPO aver avviato il processo ma PRIMA di mostrare il tab:

```csharp
private async Task OpenSessionInNewTabAsync(Session session, bool resumeExisting)
{
    // ... codice esistente per creare tab e process manager ...

    // Avvia il processo
    await processManager.StartAsync();

    // ✅ NUOVO: Se è resume, carica messaggi storici dal file
    if (resumeExisting && !string.IsNullOrEmpty(session.SessionId))
    {
        await LoadHistoricalMessagesFromFileAsync(tabItem);
    }

    // ... resto del codice ...
}
```

---

### Modifica 7 (NUOVA - solo Piano B): Aggiungi metodo MessageExistsAsync al DbService

**File**: `DbService.cs`

```csharp
/// <summary>
/// Verifica se un messaggio con il dato UUID esiste già nel database.
/// Usato per evitare duplicati durante il caricamento di messaggi storici.
/// </summary>
/// <param name="messageUuid">UUID del messaggio da verificare</param>
/// <returns>True se il messaggio esiste, altrimenti False</returns>
public async Task<bool> MessageExistsAsync(string messageUuid)
{
    try
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var exists = await context.Messages
            .AnyAsync(m => m.Uuid == messageUuid);

        return exists;
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to check if message exists: {UUID}", messageUuid);
        return false; // In caso di errore, assume che non esista (safe default)
    }
}
```

---

## Flusso Completo - Nuova Sessione

### 1-4. User crea sessione e MainPage avvia processo
(Identico al Piano A - vedi PianoA-SessionId-Fix.md)

### 5. OpenSessionInNewTabAsync avvia ClaudeProcessManager
```csharp
var processManager = new ClaudeProcessManager(
    resumeSessionId: null,  // Nessun --resume
    dbSessionId: "",
    workingDirectory: "C:\\Sources\\MyProject"
);

await processManager.StartAsync();

// ✅ NON carica messaggi storici (è nuova sessione)
```

### 6. Claude avvia e genera SessionId
(Identico al Piano A)

### 7. OnJsonLineReceived riceve primo messaggio
```csharp
// tabItem.SessionId = ""
// jsonLine = '{"sessionId": "abc-123-...", "type": "user", "content": "...", "uuid": "msg-1", ...}'

// STEP 1: Estrai SessionId
var json = JsonDocument.Parse(jsonLine);
var sessionId = json.RootElement.GetProperty("sessionId").GetString();
tabItem.SessionId = sessionId;

await _dbService.InsertOrUpdateSessionAsync(sessionId, "My Session", workingDirectory, DateTime.Now);

// STEP 2: Processa direttamente jsonLine (NO lettura file!)
await ProcessMessageLineFromFileAsync(tabItem, jsonLine);
// ↑ Salva in database, mostra in WebView
```

### 8. Messaggi successivi
```csharp
// Ogni messaggio da stdout viene processato immediatamente
OnJsonLineReceived(tabItem, '{"sessionId": "abc-123-...", "type": "assistant", ...}')
→ ProcessMessageLineFromFileAsync(tabItem, jsonLine)
→ Salva in database + mostra in WebView

// File .jsonl viene scritto da Claude in parallelo (ignorato dall'app durante runtime)
```

---

## Flusso Completo - Resume Sessione Esistente

### 1. User riapre sessione esistente
```csharp
var session = await _dbService.GetSessionByIdAsync("abc-123-...");
await OpenSessionInNewTabAsync(session, resumeExisting: true);
```

### 2. OpenSessionInNewTabAsync avvia con --resume
```csharp
var processManager = new ClaudeProcessManager(
    resumeSessionId: "abc-123-...",  // ✅ Passa SessionId per --resume
    dbSessionId: "abc-123-...",
    workingDirectory: "C:\\Sources\\MyProject"
);

await processManager.StartAsync();

// ✅ NUOVO: Carica messaggi storici dal file
await LoadHistoricalMessagesFromFileAsync(tabItem);
```

### 3. LoadHistoricalMessagesFromFileAsync carica storia
```csharp
var filePath = "C:\\Users\\user\\.claude\\projects\\C--Sources-MyProject\\abc-123-....jsonl";
var allLines = await File.ReadAllLinesAsync(filePath);

foreach (var line in allLines)
{
    var json = JsonDocument.Parse(line);
    var messageUuid = json.RootElement.GetProperty("uuid").GetString();

    // Controlla se già nel database
    if (await _dbService.MessageExistsAsync(messageUuid))
    {
        continue; // Skip duplicati
    }

    // Processa messaggio storico
    await ProcessMessageLineFromFileAsync(tabItem, line);
}
```

### 4. User invia nuovo messaggio
```csharp
// Claude risponde su stdout
OnJsonLineReceived(tabItem, '{"sessionId": "abc-123-...", "type": "assistant", ...}')
→ SessionId già presente, processa direttamente
→ ProcessMessageLineFromFileAsync(tabItem, jsonLine)
```

---

## Gestione Edge Cases

### Edge Case 1: Stdout perde un messaggio
**Scenario**: Network glitch, buffer overflow, bug in ClaudeProcessManager

**Mitigazione**:
1. Al prossimo avvio app, `LoadHistoricalMessagesFromFileAsync` recupera dal file
2. Controlla UUID per evitare duplicati
3. Messaggi mancanti vengono inseriti nel database

**Alternativa**: Aggiungere background sync periodica (ogni 5 minuti) che confronta file vs database

### Edge Case 2: Claude crasha prima di scrivere SessionId
**Scenario**: Claude si blocca durante startup, nessun messaggio ricevuto

**Comportamento attuale**:
- `tabItem.SessionId` rimane vuoto
- User vede tab ma non può inviare messaggi
- Log: "SessionId still empty, cannot process message"

**Mitigazione**:
1. Timeout: dopo 30 secondi senza SessionId, mostra errore e chiudi tab
2. Retry: riavvia processo Claude automaticamente

### Edge Case 3: File .jsonl eliminato manualmente
**Scenario**: User elimina file mentre app è aperta

**Comportamento**:
- Runtime: nessun impatto (usa stdout)
- Resume: `LoadHistoricalMessagesFromFileAsync` logga warning e continua
- Database mantiene messaggi già salvati

### Edge Case 4: Messaggi arrivano fuori ordine
**Scenario**: Threading issues, async race conditions

**Mitigazione**:
- UUID dei messaggi garantisce unicità
- Timestamp permette riordino
- Database constraint previene duplicati

---

## Vantaggi del Piano B

### ✅ Pro:
1. **Performance**: Zero I/O ridondante, latenza inferiore
2. **Semplicità logica**: Una sola fonte dati (stdout) durante runtime
3. **Sincronizzazione**: Nessuna race condition con file
4. **Scalabilità**: Migliore per sessioni con alto throughput di messaggi
5. **Efficienza**: CPU e disco meno stressati

### ⚠️ Contro:
1. **Dipendenza stdout**: Se stdout perde messaggi, servono recovery
2. **Testing**: Più difficile simulare scenari di errore
3. **Debugging**: Non si può "rileggere" stdout, solo file
4. **Modifiche invasive**: Cambia filosofia del processing

---

## Confronto Piano A vs Piano B

| Aspetto | Piano A (File-based) | Piano B (Stdout-based) |
|---------|---------------------|----------------------|
| **Complessità modifiche** | Bassa | Media |
| **Performance runtime** | Media (I/O ridondante) | Alta |
| **Robustezza recovery** | Alta (file sempre disponibile) | Media (dipende da sync) |
| **Race conditions** | Possibili (file non ancora scritto) | Nessuna |
| **Debugging** | Facile (file consultabili) | Media (solo logs) |
| **Backward compatibility** | Alta | Alta |
| **Recommended per** | Produzione stabile | Performance-critical |

---

## Testing

### Test Case 1-4: Identici al Piano A
(Vedi PianoA-SessionId-Fix.md)

### Test Case 5 (NUOVO): Stdout perde messaggio
1. Avvia nuova sessione
2. Simula perdita messaggio (commenta `JsonLineReceived.Invoke`)
3. Invia messaggio a Claude
4. **Verifica**: Messaggio NON appare in WebView
5. Riavvia app e riapri sessione (resume)
6. **Verifica**: `LoadHistoricalMessagesFromFileAsync` recupera messaggio dal file
7. **Verifica**: Messaggio appare in WebView

### Test Case 6 (NUOVO): Performance con molti messaggi
1. Avvia nuova sessione
2. Invia 100 messaggi rapidamente
3. **Misura**: Tempo tra stdout e apparizione in WebView
4. **Confronta**: Piano A vs Piano B (Piano B dovrebbe essere ~50% più veloce)

### Test Case 7 (NUOVO): Recovery senza duplicati
1. Crea sessione con 10 messaggi
2. Chiudi app
3. Riavvia e riapri sessione
4. **Verifica**: `LoadHistoricalMessagesFromFileAsync` carica messaggi
5. **Verifica**: `MessageExistsAsync` previene duplicati
6. **Verifica**: Database contiene esattamente 10 messaggi (no duplicati)

---

## Rollback
(Identico al Piano A - vedi PianoA-SessionId-Fix.md)

---

## Note Implementative

### Ordine dei commit:
1. **Commit 1**: Aggiungi `InsertOrUpdateSessionAsync` e `MessageExistsAsync` a DbService
2. **Commit 2**: Aggiungi `LoadHistoricalMessagesFromFileAsync` a MainPage
3. **Commit 3**: Modifica `OnJsonLineReceived` per processare stdout direttamente
4. **Commit 4**: Rimuovi generazione manuale UUID in MainPage
5. **Commit 5**: SessionSelectorPage non chiude se stesso
6. **Commit 6**: MainPage gestisce chiusura SessionSelectorPage
7. **Commit 7**: Integra `LoadHistoricalMessagesFromFileAsync` in `OpenSessionInNewTabAsync`

### Feature Flags (opzionale):
Per testare Piano B senza rischi, aggiungi feature flag:

```csharp
// In appsettings.json o user-secrets
{
  "Features": {
    "UseStdoutDirectProcessing": true  // true = Piano B, false = Piano A
  }
}

// In OnJsonLineReceived
if (_configuration.GetValue<bool>("Features:UseStdoutDirectProcessing"))
{
    // Piano B: processa direttamente jsonLine
    await ProcessMessageLineFromFileAsync(tabItem, jsonLine);
}
else
{
    // Piano A: leggi dal file
    var lastLines = await _dbService?.ReadLastLinesFromFileAsync(filePath, 20);
    foreach (var line in lastLines)
    {
        await ProcessMessageLineFromFileAsync(tabItem, line);
    }
}
```

### Metriche da monitorare:
- **Latency**: Tempo medio tra ricezione stdout e salvataggio database
- **Throughput**: Messaggi processati al secondo
- **Recovery rate**: % messaggi recuperati da file vs stdout
- **Duplicati**: Numero di duplicati prevenuti da `MessageExistsAsync`

### Background Sync (opzionale - migliora robustezza Piano B):

Aggiungi timer che periodicamente sincronizza file vs database:

```csharp
// In MainPage constructor
_syncTimer = new System.Timers.Timer(5 * 60 * 1000); // 5 minuti
_syncTimer.Elapsed += async (s, e) => await SyncFileWithDatabaseAsync();
_syncTimer.Start();

private async Task SyncFileWithDatabaseAsync()
{
    foreach (var tab in _sessionTabs)
    {
        if (!string.IsNullOrEmpty(tab.SessionId))
        {
            await LoadHistoricalMessagesFromFileAsync(tab);
        }
    }
}
```

---

## Raccomandazione Finale

**Per ambiente di produzione stabile**: Usa **Piano A**
- Meno rischi
- Più facile da debuggare
- File come fonte di verità garantisce recovery

**Per performance-critical o sperimentazione**: Usa **Piano B**
- Più efficiente
- Architettura più pulita
- Richiede testing approfondito

**Migliore compromesso**: Implementa **Piano A prima**, poi migra a **Piano B** se le metriche mostrano colli di bottiglia I/O.
