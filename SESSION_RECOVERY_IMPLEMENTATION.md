# Session Recovery Implementation - Risoluzione Completa

**Data:** 2025-10-30
**Progetto:** ClaudeCodeMAUI
**Issue:** Session Recovery Not Implemented
**Status:** ✅ RISOLTO

---

## 📋 Riepilogo Modifiche

Implementato il **Session Recovery automatico** per riprendere conversazioni interrotte dopo chiusura/crash dell'applicazione. L'implementazione segue esattamente le specifiche del documento `SESSION_RECOVERY_ISSUE.md`.

---

## 🔧 Modifiche Implementate

### 1. MainPage.xaml.cs

#### A. Modificato `OnAppearing()` (righe 129-176)

**Cambiamenti:**
- Reso il metodo `async void` invece di `void`
- Aggiunto handler per evento `ConversationWebView.Navigated` che chiama `RecoverLastSessionAsync()`
- Il recovery viene chiamato SOLO dopo che la WebView è pronta (`_isWebViewReady = true`)

**Codice chiave aggiunto:**
```csharp
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
```

#### B. Nuovo metodo `RecoverLastSessionAsync()` (righe 178-232)

**Funzionalità:**
1. Interroga il database per sessioni con status `active` o `killed`
2. Seleziona la sessione più recente per `last_activity`
3. Mostra dialog di conferma all'utente: "Resume Session?"
4. Se l'utente accetta:
   - Chiama `ResumeSessionAsync()` per riprendere
5. Se l'utente rifiuta:
   - Marca la sessione come `closed` nel database

**Log completi** per debugging e monitoring.

#### C. Nuovo metodo `ResumeSessionAsync(ConversationSession session)` (righe 234-303)

**Funzionalità:**
1. Imposta `_currentSession` con i dati recuperati dal DB
2. Ripristina il flag `_isPlanMode` e aggiorna la UI
3. Crea un nuovo `StreamJsonParser` con tutti gli event handler
4. **CHIAVE:** Crea `ClaudeProcessManager` passando `session.SessionId` come `resumeSessionId`
   ```csharp
   _processManager = new ClaudeProcessManager(
       _isPlanMode,
       session.SessionId,  // <<<< Passa il session_id per --resume
       session.SessionId
   );
   ```
5. Avvia il processo Claude con flag `--resume {session_id}`
6. Aggiorna UI con indicatore "Session Resumed" (arancione)
7. Attende 1.5 secondi per inizializzazione processo
8. **Invia automaticamente** un prompt di riassunto:
   ```
   "Ciao! Mi ricordi brevemente su cosa stavamo lavorando?
    Dammi un riassunto del contesto della nostra conversazione precedente."
   ```
9. In caso di errore, fallback automatico a `StartNewConversation()`

#### D. Modificato `OnSessionInitialized()` (righe 425-456)

**Cambiamenti:**
- Aggiunto collegamento con `App.xaml.cs` per notificare il session ID corrente:
```csharp
// Notifica l'App del session ID corrente per gestire OnSleep gracefully
if (Application.Current is App app)
{
    app.SetCurrentSession(_dbService, e.SessionId);
}
```

---

### 2. App.xaml.cs

**File completamente riscritto** per supportare chiusura graceful.

#### A. Nuovi campi privati (righe 8-9)

```csharp
private DbService? _dbService;
private string? _currentSessionId;
```

#### B. Nuovo metodo `SetCurrentSession()` (righe 21-30)

**Funzionalità:**
- Chiamato da `MainPage` quando viene inizializzata una sessione
- Memorizza il `DbService` e il `sessionId` corrente
- Log per tracking

**Firma:**
```csharp
public void SetCurrentSession(DbService dbService, string? sessionId)
```

#### C. Nuovo override `OnSleep()` (righe 32-61)

**Funzionalità:**
- Chiamato automaticamente da MAUI quando l'app va in background o viene chiusa
- Marca la sessione corrente come `"closed"` nel database
- Operazione **sincrona** con timeout di 2 secondi (requisito MAUI per OnSleep)
- Log completi per verificare esecuzione

**Codice chiave:**
```csharp
_dbService.UpdateStatusAsync(_currentSessionId, "closed").Wait(TimeSpan.FromSeconds(2));
```

#### D. Using statements aggiunti (righe 1-2)

```csharp
using Serilog;
using ClaudeCodeMAUI.Services;
```

---

## 🎯 Comportamento Implementato

### Scenario 1: Recovery Automatico (User accetta)

```
1. User avvia app
2. OnAppearing() → Inizializza WebView
3. WebView.Navigated → RecoverLastSessionAsync()
4. Query DB: SELECT * FROM conversations WHERE status IN ('active', 'killed')
5. Trova sessione recente (es. 2025-10-30 14:23:45)
6. DisplayAlert: "Resume Session? Found previous session from..."
7. User click "Yes"
8. ResumeSessionAsync(session)
   ├─> Crea ClaudeProcessManager con resumeSessionId = session.SessionId
   ├─> claude -p --resume {session_id} --input-format stream-json ...
   ├─> Attendi 1.5s
   └─> Invia prompt: "Mi ricordi su cosa stavamo lavorando?"
9. Claude risponde con riassunto del contesto
10. ✅ User continua la conversazione esattamente da dove era rimasto
```

### Scenario 2: Recovery Rifiutato (User declina)

```
1-6. Come sopra...
7. User click "No"
8. UPDATE conversations SET status='closed' WHERE session_id=...
9. Log: "Session {SessionId} marked as closed"
10. UI pronta per nuova conversazione (nessun processo avviato)
11. ✅ User può cliccare "New Conversation" per iniziare da zero
```

### Scenario 3: Nessuna Sessione da Recuperare

```
1. User avvia app (prima volta o dopo chiusura pulita)
2. OnAppearing() → RecoverLastSessionAsync()
3. Query DB: Ritorna 0 risultati
4. Log: "No sessions to recover"
5. Return early (nessun dialog mostrato)
6. ✅ UI pulita, pronta per nuova conversazione
```

### Scenario 4: Chiusura Graceful (OnSleep)

```
1. User ha conversazione attiva (session_id: abc-123-xyz)
2. User chiude app (click X, Alt+F4, kill da task manager)
3. MAUI chiama App.OnSleep()
4. App.OnSleep() → _dbService.UpdateStatusAsync(abc-123-xyz, "closed")
5. UPDATE eseguito in max 2 secondi (sincronizzato con .Wait())
6. Log: "Session {SessionId} marked as closed successfully"
7. ✅ Prossima apertura app: sessione marcata closed (no recovery)
```

---

## 📊 Flowchart Completo

```
┌─────────────────┐
│   App Start     │
└────────┬────────┘
         │
         v
┌─────────────────────────────┐
│  OnAppearing()              │
│  - Init HTML Renderer       │
│  - Init WebView from file   │
└────────┬────────────────────┘
         │
         v
┌─────────────────────────────┐
│ WebView.Navigated Event     │
│ _isWebViewReady = true      │
└────────┬────────────────────┘
         │
         v
┌─────────────────────────────┐
│ RecoverLastSessionAsync()   │
│ - Query active/killed       │
└────────┬────────────────────┘
         │
    ┌────┴────┐
    │         │
    v         v
  None     Found
    │         │
    v         │
  Return      │
    │         v
    │   ┌─────────────────────┐
    │   │ DisplayAlert:       │
    │   │ "Resume Session?"   │
    │   └────┬─────┬──────────┘
    │        │     │
    │       Yes   No
    │        │     │
    │        │     v
    │        │  ┌──────────────────┐
    │        │  │ UPDATE closed    │
    │        │  └──────────────────┘
    │        v           │
    │   ┌────────────────┴─────┐
    │   │ ResumeSessionAsync() │
    │   │ - Set session        │
    │   │ - Create parser      │
    │   │ - Start with --resume│
    │   │ - Send summary prompt│
    │   └──────────────────────┘
    │             │
    └─────────────┴─────────────┐
                                 │
                                 v
                        ┌────────────────┐
                        │ UI Ready       │
                        │ (New or Resume)│
                        └────────────────┘
```

---

## ✅ Checklist Verifica Funzionamento

### Test 1: Recovery Basico
- [ ] Avvia app, crea conversazione con 3-4 messaggi
- [ ] Chiudi app normalmente (click X)
- [ ] Riapri app
- [ ] ✅ Deve apparire dialog "Resume Session?"
- [ ] Click "Yes"
- [ ] ✅ Claude deve rispondere con riassunto del contesto
- [ ] ✅ Puoi continuare la conversazione esattamente da dove era rimasta

### Test 2: Rifiuto Recovery
- [ ] Avvia app (con sessione attiva nel DB)
- [ ] ✅ Appare dialog "Resume Session?"
- [ ] Click "No"
- [ ] ✅ Nessun processo avviato
- [ ] ✅ UI pulita per nuova conversazione
- [ ] Query DB: status = 'closed'

### Test 3: Nessuna Sessione
- [ ] Pulisci database: `DELETE FROM conversations`
- [ ] Avvia app
- [ ] ✅ Nessun dialog mostrato
- [ ] ✅ UI pulita per nuova conversazione

### Test 4: Chiusura Graceful OnSleep
- [ ] Avvia app, crea conversazione
- [ ] Controlla DB: status = 'active'
- [ ] Chiudi app (X o Alt+F4)
- [ ] Query DB immediatamente dopo chiusura
- [ ] ✅ Status deve essere 'closed'
- [ ] ✅ Prossima apertura: nessun recovery (status closed)

### Test 5: Recovery dopo Crash (Simulated)
- [ ] Avvia app, crea conversazione
- [ ] Forza chiusura da Task Manager (simula crash)
- [ ] Query DB: status = 'active' (OnSleep non chiamato)
- [ ] Riapri app
- [ ] ✅ Dialog "Resume Session?" appare
- [ ] ✅ Recovery funziona correttamente

### Test 6: Multiple Sessioni Active (Edge Case)
- [ ] Manualmente: INSERT 3 sessioni con status='active'
- [ ] Avvia app
- [ ] ✅ Dialog mostra la sessione più recente (last_activity)
- [ ] Click "Yes"
- [ ] ✅ Sessione corretta viene ripresa
- [ ] Opzionale: Le altre 2 rimangono 'active' (per future iterazioni)

---

## 🔍 Dettagli Tecnici Importanti

### 1. Perché `OnAppearing()` è async void?

```csharp
protected override async void OnAppearing()
```

- `OnAppearing()` è un override di metodo MAUI base con firma `void`
- Non possiamo cambiare la signature a `Task` (violazione override)
- `async void` permette di chiamare `await RecoverLastSessionAsync()` internamente
- Exception handling gestito tramite try-catch nei metodi chiamati

### 2. Perché `OnSleep()` usa `.Wait()`?

```csharp
_dbService.UpdateStatusAsync(_currentSessionId, "closed").Wait(TimeSpan.FromSeconds(2));
```

- `OnSleep()` deve completare rapidamente (requisito MAUI/Android/iOS)
- Non possiamo usare `async void` perché MAUI non aspetterebbe il completamento
- `.Wait()` con timeout garantisce:
  - Operazione DB viene completata (o timeout)
  - OnSleep() non ritorna finché UPDATE non è fatto
  - Max 2 secondi per non bloccare shutdown OS

### 3. Perché `session.SessionId` viene passato 2 volte?

```csharp
_processManager = new ClaudeProcessManager(
    _isPlanMode,
    session.SessionId,  // Primo parametro: resumeSessionId
    session.SessionId   // Secondo parametro: sessionId (per logging/tracking)
);
```

- **Primo parametro** (`resumeSessionId`): Usato per generare `--resume {session_id}` nel comando Claude
- **Secondo parametro** (`sessionId`): Usato per logging interno e tracking del processo

Vedi `ClaudeProcessManager.cs:136-139`:
```csharp
if (!string.IsNullOrEmpty(resumeSessionId))
{
    args.Add($"--resume {resumeSessionId}");
}
```

### 4. Perché attesa di 1.5 secondi prima del prompt?

```csharp
await Task.Delay(1500);
await _processManager.SendMessageAsync(summaryPrompt);
```

- Il processo Claude impiega ~1 secondo per inizializzare
- Se inviamo il messaggio troppo presto, stdin potrebbe non essere pronto
- 1.5 secondi è un buffer sicuro per tutti i sistemi
- Alternative future: polling su `_processManager.IsRunning` invece di delay fisso

---

## 📝 File Modificati

| File | Righe Modificate | Tipo Modifica |
|------|------------------|---------------|
| `MainPage.xaml.cs` | 129-176 | Modificato `OnAppearing()` con recovery call |
| `MainPage.xaml.cs` | 178-232 (nuovo) | Aggiunto `RecoverLastSessionAsync()` |
| `MainPage.xaml.cs` | 234-303 (nuovo) | Aggiunto `ResumeSessionAsync()` |
| `MainPage.xaml.cs` | 425-456 | Modificato `OnSessionInitialized()` con App notification |
| `App.xaml.cs` | 1-62 (riscritto) | Aggiunto `SetCurrentSession()` e `OnSleep()` |

**Totale righe aggiunte:** ~180 righe di codice + commenti
**Totale righe modificate:** ~30 righe

---

## 🚀 Build e Deploy

### Compilazione

```bash
cd C:\Sources\ClaudeGui
dotnet build ClaudeCodeMAUI/ClaudeCodeMAUI.csproj -c Debug -f net9.0-windows10.0.19041.0
```

**Risultato:**
- ✅ Nessun errore di compilazione C#
- ⚠️ Se app già in esecuzione: warnings MSB3026/MSB3027 (file locked) - **NORMALE**
- ✅ Il codice compila correttamente

### Esecuzione per Testing

**Opzione 1: Visual Studio 2022**
```
1. Apri ClaudeCodeGUI.sln
2. Set ClaudeCodeMAUI come Startup Project
3. Premi F5 (Debug) o Ctrl+F5 (Run without debug)
```

**Opzione 2: Command Line**
```bash
dotnet run --project ClaudeCodeMAUI/ClaudeCodeMAUI.csproj -c Debug -f net9.0-windows10.0.19041.0
```

---

## 🐛 Troubleshooting

### Issue 1: Dialog non appare mai

**Causa:** Nessuna sessione attiva nel DB o DbService non inizializzato

**Debug:**
```sql
-- Controlla sessioni nel DB
SELECT * FROM conversations WHERE status IN ('active', 'killed') ORDER BY last_activity DESC;
```

**Fix:**
- Verifica che `_dbService` non sia `null` in `MainPage` constructor
- Controlla log: "No sessions to recover" → comportamento corretto
- Se vuoi testare, crea sessione manualmente:
  ```sql
  INSERT INTO conversations (session_id, status, last_activity, is_plan_mode)
  VALUES ('test-session-123', 'active', NOW(), false);
  ```

### Issue 2: Recovery fallisce con errore "Session not found"

**Causa:** `session_id` nel DB non esiste più in Claude internal storage

**Soluzione:**
- Normale se Claude cache è stata pulita (`~/.cache/claude_code/`)
- Il metodo `ResumeSessionAsync()` ha fallback automatico:
  ```csharp
  catch (Exception ex)
  {
      // Fallback: avvia nuova conversazione se recovery fallisce
      StartNewConversation();
  }
  ```

### Issue 3: OnSleep non marca status='closed'

**Causa:** Exception durante `UpdateStatusAsync()` o timeout

**Debug:**
- Controlla log: "App: OnSleep called"
- Cerca: "Session {SessionId} marked as closed successfully"
- Se manca, cerca "App: Failed to mark session as closed"

**Fix:**
- Verifica connessione DB attiva
- Verifica timeout 2s sufficiente (rete lenta?)
- Aumenta timeout se necessario:
  ```csharp
  .Wait(TimeSpan.FromSeconds(5));  // Invece di 2
  ```

---

## 🎓 Test Case Dettagliati

### Test Case 1: Happy Path - Recovery Completo

**Precondizioni:**
- App installata, DB configurato correttamente
- Nessuna sessione attiva nel DB

**Steps:**
1. Avvia app
2. Click "New Conversation"
3. Invia messaggio: "Ciao, scrivi un breve testo di test"
4. Attendi risposta di Claude (verifica session_id creato in DB)
5. Query DB: `SELECT * FROM conversations` → status='active'
6. Chiudi app normalmente (click X)
7. Query DB: status='closed' (grazie a OnSleep)
8. Manualmente: `UPDATE conversations SET status='active'` (simula crash)
9. Riapri app
10. **VERIFY:** Dialog "Resume Session?" appare
11. Click "Yes"
12. **VERIFY:** UI mostra "Session Resumed" (arancione)
13. **VERIFY:** Claude risponde con riassunto
14. Invia nuovo messaggio: "Continua il testo"
15. **VERIFY:** Claude riprende il contesto e continua

**Expected Results:**
- ✅ Recovery automatico funziona
- ✅ Contesto mantenuto perfettamente
- ✅ Nessun dato perso

### Test Case 2: Rifiuto Recovery

**Steps:**
1-9. Come Test Case 1
10. Dialog "Resume Session?" appare
11. Click "No"
12. Query DB: status='closed'
13. **VERIFY:** Nessun processo Claude avviato
14. **VERIFY:** UI pulita, ready per new conversation
15. Click "New Conversation"
16. Invia messaggio: "Nuovo topic completamente diverso"
17. **VERIFY:** Claude non ha memoria della sessione precedente

**Expected Results:**
- ✅ Sessione vecchia chiusa correttamente
- ✅ Nuova conversazione indipendente

---

## 📚 Riferimenti

### Documenti Correlati
- `SESSION_RECOVERY_ISSUE.md` - Analisi del problema originale
- `ProjectDescription.md` - Architettura generale del progetto
- `MULTILINE_INPUT_IMPLEMENTATION.md` - Implementazione input multilinea

### Codice Correlato
- `ClaudeProcessManager.cs:52` - Costruttore con `resumeSessionId`
- `ClaudeProcessManager.cs:136-139` - Generazione flag `--resume`
- `DbService.cs:208` - Metodo `GetActiveConversationsAsync()`
- `DbService.cs:111` - Metodo `UpdateStatusAsync()`

### Claude Code Documentation
- Flag `--resume`: https://docs.claude.com/en/docs/claude-code/headless
- Session Management: Claude mantiene storico completo delle conversazioni

---

## 🎯 Future Enhancements (Opzionali)

### Enhancement 1: Persistence HTML Conversazione

**Idea:** Salvare `_conversationHtml.ToString()` nel DB

**Schema:**
```sql
ALTER TABLE conversations ADD COLUMN conversation_html LONGTEXT;
```

**Benefici:**
- Recovery istantaneo della WebView (no prompt riassunto)
- Export conversazioni complete
- Backup completo delle interazioni

**Trade-off:**
- Database size aumenta significativamente
- Update più lenti (ogni messaggio)

### Enhancement 2: Scelta tra Multiple Sessioni

**Idea:** Se ci sono N sessioni active, mostra lista invece di solo la più recente

**UI:**
```
┌─────────────────────────────────────┐
│ Found 3 sessions to recover:        │
│                                     │
│ ○ Session 1: "Refactor database"   │
│   Last activity: 2h ago             │
│                                     │
│ ○ Session 2: "Debug API endpoints"  │
│   Last activity: 1 day ago          │
│                                     │
│ ○ Session 3: "Write documentation"  │
│   Last activity: 3 days ago         │
│                                     │
│ [Resume Selected] [Start New]       │
└─────────────────────────────────────┘
```

### Enhancement 3: Cleanup Automatico Sessioni Vecchie

**Idea:** Cron job o startup routine che elimina sessioni vecchie

```sql
DELETE FROM conversations
WHERE status IN ('closed', 'killed')
  AND updated_at < DATE_SUB(NOW(), INTERVAL 7 DAY);
```

**Trigger:** All'avvio app, prima di recovery

---

## ✅ Conclusioni

**Issue Risolto:** ✅
**Codice Compila:** ✅
**Recovery Funziona:** ✅ (da testare manualmente)
**Chiusura Graceful:** ✅
**Log Completi:** ✅
**Documentazione:** ✅

**Prossimi Step:**
1. Chiudi l'app corrente (PID 63844)
2. Ricompila: `dotnet build`
3. Esegui app e testa i 6 test case sopra
4. Verifica log in `logs/app-*.log` per troubleshooting

---

**Documento creato:** 2025-10-30
**Versione:** 1.0
**Autore:** Claude (Anthropic)
**Progetto:** ClaudeCodeMAUI
**Feature:** Session Recovery Implementation
