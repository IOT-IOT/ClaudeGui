# Session Recovery Issue - Analisi del Problema

## 📋 Riepilogo

L'applicazione **ClaudeCodeMAUI** attualmente **NON riprende automaticamente le sessioni** dopo la chiusura e riapertura dell'app, nonostante tutta l'infrastruttura necessaria sia già implementata e funzionante.

---

## 🔍 Situazione Attuale

### ✅ Componenti IMPLEMENTATI e FUNZIONANTI

1. **ClaudeProcessManager** - Supporto completo per `--resume`
   - Costruttore accetta parametro `resumeSessionId` (riga 52)
   - Metodo `BuildArguments()` aggiunge `--resume {session_id}` se presente (righe 136-139)
   - **Codice**: `ClaudeCodeMAUI/Services/ClaudeProcessManager.cs:136-139`

2. **DbService** - Persistenza e recupero sessioni
   - `InsertSessionAsync()` - salva nuove sessioni (riga 78)
   - `UpdateStatusAsync()` - marca sessioni come killed/closed (riga 111)
   - `GetActiveConversationsAsync()` - recupera sessioni attive/killed (riga 208)
   - **Codice**: `ClaudeCodeMAUI/Services/DbService.cs`

3. **Database Schema** - Tabella conversations
   - Campo `session_id` (UUID univoco di Claude)
   - Campo `status` (active/closed/killed)
   - Campo `last_activity` per ordinamento
   - Query con indice ottimizzato per recovery

4. **ConversationSession Model** - Dati runtime
   - Proprietà `SessionId` per memorizzare UUID Claude
   - Metadati (costi, tokens, model, etc.)
   - **Codice**: `ClaudeCodeMAUI/Models/ConversationSession.cs`

### ❌ Componenti MANCANTI

1. **Session Recovery Service** - NON ESISTE
   - Nessun servizio dedicato al recovery
   - Nessuna logica di startup che interroga il database

2. **UI Recovery Flow** - NON IMPLEMENTATO
   - Nessun dialog "Vuoi riprendere la conversazione precedente?"
   - Nessun caricamento automatico all'avvio
   - `OnAppearing()` non chiama mai `GetActiveConversationsAsync()`

3. **Conversation History Persistence** - NON IMPLEMENTATO
   - L'HTML della conversazione (_conversationHtml buffer) NON viene salvato nel DB
   - Al recovery, la WebView sarebbe vuota anche se la sessione Claude ha il contesto

---

## 🐛 Il Bug nel Dettaglio

### Comportamento Corrente

1. **Avvio applicazione**
   ```
   OnAppearing() → Inizializza WebView → NESSUN recovery
   ```

2. **Utente clicca "New Conversation"**
   ```csharp
   // MainPage.xaml.cs:153
   _processManager = new ClaudeProcessManager(_isPlanMode, null, null);
   //                                                       ^^^^
   //                                         resumeSessionId è SEMPRE null!
   ```

3. **Utente invia un messaggio senza processo attivo**
   ```csharp
   // MainPage.xaml.cs:206-211
   if (_processManager == null || !_processManager.IsRunning)
   {
       StartNewConversation(); // <<<< Inizia SEMPRE una nuova conversazione
       await Task.Delay(500);
   }
   ```

4. **Claude inizializza una NUOVA sessione**
   - Genera un nuovo `session_id`
   - Eventi `SessionInitialized` salvano nel DB (riga 239-244)
   - **Risultato**: Sessione precedente rimane "active" nel DB ma non viene mai riutilizzata

5. **Chiusura applicazione**
   - Processo Claude viene killato
   - Sessione rimane con status "active" (nessun update a "closed")
   - Al prossimo avvio, ciclo ricomincia da capo

### Conseguenze

- ❌ **Perdita di contesto**: Ogni riavvio = conversazione completamente nuova
- ❌ **Sessioni orfane**: Database si riempie di sessioni "active" mai chiuse
- ❌ **Spreco risorse**: Ogni conversazione ricrea contesto da zero (no cache riutilizzo)
- ❌ **UX scadente**: Utente perde tutto il lavoro se chiude l'app

---

## 🔧 Dove Serve Intervenire

### 1. MainPage.xaml.cs

**File**: `ClaudeCodeMAUI/MainPage.xaml.cs`

#### A. Metodo `OnAppearing()` (righe 72-113)

**Problema**: Non controlla mai il database per sessioni esistenti

**Dove aggiungere**:
```csharp
protected override async void OnAppearing()
{
    base.OnAppearing();

    // ... codice esistente inizializzazione WebView ...

    // ===== NUOVO CODICE DA AGGIUNGERE =====
    if (_dbService != null && _currentSession == null)
    {
        await RecoverLastSessionAsync(); // <<<< CHIAMARE QUI
    }
}
```

#### B. Nuovo metodo `RecoverLastSessionAsync()` (DA CREARE)

**Dove**: Dopo `OnAppearing()` (circa riga 114)

**Funzionalità**:
```csharp
private async Task RecoverLastSessionAsync()
{
    try
    {
        // 1. Interroga DB per sessioni attive/killed
        var sessions = await _dbService.GetActiveConversationsAsync();

        if (sessions.Count == 0)
        {
            Log.Information("No sessions to recover");
            return;
        }

        // 2. Prendi la più recente
        var lastSession = sessions.OrderByDescending(s => s.LastActivity).First();

        // 3. Chiedi all'utente se vuole riprendere
        var resume = await DisplayAlert(
            "Resume Session?",
            $"Found previous session: {lastSession.TabTitle}\n" +
            $"Last activity: {lastSession.LastActivity:g}\n\n" +
            "Do you want to resume it?",
            "Yes", "No"
        );

        if (resume)
        {
            // 4. Riprendi la sessione
            await ResumeSessionAsync(lastSession);
        }
        else
        {
            // 5. Marca come closed e inizia nuova
            await _dbService.UpdateStatusAsync(lastSession.SessionId, "closed");
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to recover session");
    }
}
```

#### C. Nuovo metodo `ResumeSessionAsync()` (DA CREARE)

**Dove**: Dopo `RecoverLastSessionAsync()` (circa riga 160)

**Funzionalità**:
```csharp
private async Task ResumeSessionAsync(ConversationSession session)
{
    try
    {
        Log.Information("Resuming session: {SessionId}", session.SessionId);

        // 1. Imposta sessione corrente
        _currentSession = session;
        _isPlanMode = session.IsPlanMode;
        SwitchPlanMode.IsToggled = _isPlanMode;

        // 2. Crea parser
        _parser = new StreamJsonParser();
        _parser.SessionInitialized += OnSessionInitialized;
        _parser.TextReceived += OnTextReceived;
        _parser.ToolCallReceived += OnToolCallReceived;
        _parser.ToolResultReceived += OnToolResultReceived;
        _parser.MetadataReceived += OnMetadataReceived;

        // 3. Crea process manager CON resumeSessionId
        _processManager = new ClaudeProcessManager(
            _isPlanMode,
            session.SessionId,  // <<<< QUESTO È IL FIX PRINCIPALE!
            session.SessionId
        );
        _processManager.JsonLineReceived += OnJsonLineReceived;
        _processManager.ErrorReceived += OnErrorReceived;
        _processManager.ProcessCompleted += OnProcessCompleted;

        // 4. Avvia processo con --resume
        _processManager.Start();

        // 5. Invia prompt di riassunto
        await Task.Delay(1000); // Attendi processo attivo
        await _processManager.SendMessageAsync(
            "Ciao! Mi ricordi su cosa stavamo lavorando? " +
            "Fammi un breve riassunto del contesto."
        );

        // 6. Aggiorna UI
        BtnStop.IsEnabled = true;
        LblStatus.Text = "Session Resumed";
        LblStatus.TextColor = Colors.Orange;

        Log.Information("Session resumed successfully");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to resume session");
        await DisplayAlert("Error", $"Failed to resume session: {ex.Message}", "OK");
    }
}
```

#### D. Metodo `StartNewConversation()` (riga 125)

**Problema**: Non passa mai `resumeSessionId`

**Fix**: Assicurarsi che passi `null` esplicitamente (già corretto):
```csharp
_processManager = new ClaudeProcessManager(_isPlanMode, null, null);
// OK - per nuova conversazione, resumeSessionId deve essere null
```

### 2. App.xaml.cs o MauiProgram.cs

**File**: `ClaudeCodeMAUI/App.xaml.cs` o `ClaudeCodeMAUI/MauiProgram.cs`

**Problema**: Nessuna chiusura graceful dell'app

**Aggiungere**: Handler per chiusura app che marca sessioni come "closed"

```csharp
// In App.xaml.cs
protected override async void OnSleep()
{
    base.OnSleep();

    // Marca tutte le sessioni attive come closed
    if (_dbService != null && _currentSession != null)
    {
        await _dbService.UpdateStatusAsync(_currentSession.SessionId, "closed");
    }
}
```

---

## 📊 Flowchart del Recovery Mancante

### Flusso ATTUALE (Bug)
```
┌─────────────────┐
│   App Start     │
└────────┬────────┘
         │
         v
┌─────────────────┐
│  OnAppearing()  │
│  - Init WebView │
│  - NO recovery! │
└────────┬────────┘
         │
         v
┌─────────────────┐
│ User sends msg  │
└────────┬────────┘
         │
         v
┌─────────────────────────────┐
│ StartNewConversation()      │
│ - resumeSessionId = null    │
│ - Claude creates NEW session│
└─────────────────────────────┘
         │
         v
    ❌ Contesto perso!
```

### Flusso DESIDERATO (Fix)
```
┌─────────────────┐
│   App Start     │
└────────┬────────┘
         │
         v
┌─────────────────────────────┐
│  OnAppearing()              │
│  - Init WebView             │
│  - RecoverLastSessionAsync()│
└────────┬────────────────────┘
         │
         v
┌─────────────────────────────┐
│ GetActiveConversationsAsync()│
│ - Query DB for active/killed│
└────────┬────────────────────┘
         │
    ┌────┴────┐
    │         │
    v         v
  None     Found
    │         │
    │         v
    │   ┌─────────────────┐
    │   │ DisplayAlert:   │
    │   │ "Resume?"       │
    │   └────┬─────┬──────┘
    │        │     │
    │       Yes   No
    │        │     │
    │        │     v
    │        │  UPDATE status='closed'
    │        │     │
    │        v     │
    │   ┌──────────┴──────────┐
    │   │ ResumeSessionAsync()│
    │   │ - Pass session_id   │
    │   │ - Start with --resume
    │   │ - Send summary prompt
    │   └─────────────────────┘
    │             │
    └─────────────┘
                  │
                  v
            ✅ Contesto ripristinato!
```

---

## 🎯 Checklist Implementazione

### Fase 1: Recovery Basico
- [ ] Creare metodo `RecoverLastSessionAsync()` in MainPage
- [ ] Creare metodo `ResumeSessionAsync()` in MainPage
- [ ] Chiamare `RecoverLastSessionAsync()` in `OnAppearing()`
- [ ] Testare recovery con `--resume` funzionante

### Fase 2: UI e UX
- [ ] Dialog conferma recovery ("Resume Session?")
- [ ] Messaggio riassunto automatico ("Su cosa stavamo lavorando?")
- [ ] Indicatore visivo "Session Resumed" nella UI
- [ ] Gestione errori se session_id non valido

### Fase 3: Cleanup
- [ ] Handler `OnSleep()` per marcare sessioni come "closed"
- [ ] Pulizia automatica sessioni vecchie (>7 giorni?)
- [ ] Gestione multiple sessioni (scegliere quale riprendere)

### Fase 4: Persistence Completa (Opzionale)
- [ ] Salvare `_conversationHtml` nel database (campo TEXT/LONGTEXT)
- [ ] Ripristinare HTML completo al recovery
- [ ] Evitare prompt riassunto se HTML disponibile

---

## 📝 Note Tecniche

### Perché il codice `--resume` esiste ma non viene usato?

Il progetto originale era progettato per **Windows Forms con multi-tab**, dove ogni tab poteva riprendere una sessione diversa. Il codice è stato portato a **MAUI con singola conversazione**, ma la logica di recovery multi-sessione non è stata adattata.

### Cosa fa `--resume {session_id}`?

Claude Code mantiene uno storico completo delle conversazioni nel suo internal storage. Quando passi `--resume`, Claude:
1. Carica tutto il contesto precedente (messaggi, tool calls, risultati)
2. Riprende la conversazione esattamente da dove era rimasta
3. Riutilizza la cache dei prompt (risparmio costi e latenza)

### Limitazioni attuali

- **HTML non persistito**: Al recovery, la WebView sarà vuota anche se Claude ha il contesto
  - **Soluzione**: Salvare `_conversationHtml.ToString()` nel DB e ripristinarlo
  - **Alternativa**: Usare prompt di riassunto (implementato in `ResumeSessionAsync()`)

- **Singola sessione**: Attualmente non supporta multi-tab come previsto nel design originale
  - **Soluzione futura**: Implementare TabControl MAUI o lista conversazioni

---

## 🚀 Priorità di Implementazione

### 🔴 Priorità ALTA (Blocker UX)
1. Recovery automatico all'avvio
2. Dialog conferma recovery
3. Passaggio `session_id` al ClaudeProcessManager

### 🟡 Priorità MEDIA (Nice to have)
4. Chiusura graceful con update DB
5. Prompt riassunto automatico
6. Gestione errori recovery fallito

### 🟢 Priorità BASSA (Enhancement)
7. Persistence HTML conversazione
8. Scelta tra multiple sessioni
9. Cleanup automatico sessioni vecchie

---

## 📚 File da Modificare

| File | Linee | Modifiche |
|------|-------|-----------|
| `MainPage.xaml.cs` | 72-113 | Aggiungere chiamata recovery in `OnAppearing()` |
| `MainPage.xaml.cs` | ~114 (nuovo) | Creare `RecoverLastSessionAsync()` |
| `MainPage.xaml.cs` | ~160 (nuovo) | Creare `ResumeSessionAsync()` |
| `App.xaml.cs` | Nuovo handler | Aggiungere `OnSleep()` per cleanup |
| `ConversationSession.cs` | Opzionale | Aggiungere campo `ConversationHtml` |
| `DbService.cs` | Opzionale | Aggiungere save/load HTML |

---

## ✅ Verifica Funzionamento

### Test Case 1: Recovery Basico
1. Avvia app
2. Crea conversazione con 3-4 messaggi
3. Chiudi app
4. Riapri app
5. ✅ Deve apparire dialog "Resume Session?"
6. Clicca "Yes"
7. ✅ Claude deve rispondere con riassunto del contesto

### Test Case 2: Rifiuto Recovery
1. Avvia app (con sessione attiva nel DB)
2. ✅ Appare dialog "Resume Session?"
3. Clicca "No"
4. ✅ Sessione marcata "closed" nel DB
5. ✅ Nuova conversazione inizia normalmente

### Test Case 3: Nessuna Sessione
1. Pulisci database (DELETE FROM conversations)
2. Avvia app
3. ✅ Nessun dialog
4. ✅ UI pronta per nuova conversazione

---

**Documento creato**: 2025-10-30
**Versione**: 1.0
**Autore**: Claude (Anthropic)
**Progetto**: ClaudeCodeMAUI
**Issue**: Session Recovery Not Implemented
