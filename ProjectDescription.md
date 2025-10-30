# ClaudeCodeGUI - Project Description

## Panoramica del Progetto

**ClaudeCodeGUI** è un'applicazione Windows Forms (.NET 9) che fornisce un'interfaccia grafica per interagire con Claude Code in modalità headless. L'applicazione permette di gestire conversazioni multiple con Claude mantenendo il contesto, supportando funzionalità avanzate come il Plan Mode, persistenza delle sessioni, e recovery automatico dopo crash.

### Obiettivi Principali

1. **Interfaccia user-friendly** per Claude Code headless mode
2. **Gestione conversazioni multiple** tramite tab
3. **Persistenza automatica** delle sessioni su database MariaDB
4. **Recovery automatico** dopo crash o chiusura applicazione
5. **Visualizzazione real-time** delle operazioni di Claude (tool calls, thinking, output)
6. **Metadati dettagliati** (costi, tokens, durata, tools utilizzati)

---

## Architettura Generale

### Stack Tecnologico

- **Framework**: .NET 9 Windows Forms
- **Database**: MariaDB (remoto)
- **Logging**: Serilog (file-based)
- **Process Management**: System.Diagnostics.Process
- **JSON**: Newtonsoft.Json
- **Database Driver**: MySql.Data

### Struttura del Progetto

```
C:\sources\ClaudeGui\
├── ClaudeCodeGUI.sln
├── ProjectDescription.md
├── README.md (optional)
└── ClaudeCodeGUI\
    ├── Models\
    │   └── ConversationSession.cs
    ├── Services\
    │   ├── ClaudeProcessManager.cs
    │   ├── DbService.cs
    │   └── SessionRecoveryService.cs
    ├── Utilities\
    │   ├── StreamJsonParser.cs
    │   ├── RichTextBoxFormatter.cs
    │   └── CommandHistoryManager.cs
    ├── Controls\
    │   ├── ConversationTab.cs (UserControl)
    │   └── MetadataPanel.cs (UserControl)
    ├── MainForm.cs
    ├── Program.cs
    ├── appsettings.json
    └── logs\ (generated at runtime)
```

---

## Componenti Dettagliati

### 1. MainForm (UI Layer)

**Responsabilità:**
- Gestione TabControl per conversazioni multiple
- ToolStrip con bottoni: New, Stop, Toggle Plan Mode
- StatusBar con stato applicazione (Idle/Working/Stopped)
- Gestione eventi globali (Alt+M per toggle Plan Mode)
- Chiusura graceful con UPDATE status delle conversazioni

**Componenti UI:**
```
┌────────────────────────────────────────────────────────────┐
│ [New] [Stop] [Plan Mode: OFF]                      Status │
├────────────────────────────────────────────────────────────┤
│ ┌─Tab 1──┬─Tab 2──┬─Tab 3──┐                              │
│ │                                                          │
│ │  ┌───────────────────────────────┐  ┌──────────────┐   │
│ │  │                               │  │  Metadata    │   │
│ │  │   RichTextBox (Output)        │  │  Panel       │   │
│ │  │                               │  │              │   │
│ │  │                               │  │  Session ID  │   │
│ │  │                               │  │  Costi: $X   │   │
│ │  │                               │  │  Tokens: X   │   │
│ │  └───────────────────────────────┘  │  Durata: Xs  │   │
│ │  ┌───────────────────────────────┐  │  Tools: N    │   │
│ │  │ Input: >                      │  │  Model: X    │   │
│ │  └───────────────────────────────┘  └──────────────┘   │
│ └──────────────────────────────────────────────────────────┘
├────────────────────────────────────────────────────────────┤
│ Idle | Model: claude-sonnet-4-5                           │
└────────────────────────────────────────────────────────────┘
```

### 2. ClaudeProcessManager (Service)

**Responsabilità:**
- Gestione del processo `claude.exe` persistente per ogni tab
- Lancio con parametri corretti per stream-json input/output
- Scrittura messaggi JSONL su stdin
- Lettura asincrona continua da stdout
- Kill istantaneo del processo su richiesta

**Metodi principali:**
```csharp
public class ClaudeProcessManager
{
    public event EventHandler<JsonLineReceivedEventArgs> JsonLineReceived;
    public event EventHandler<ProcessCompletedEventArgs> ProcessCompleted;

    public void Start(bool isPlanMode, string resumeSessionId = null);
    public void SendMessage(string prompt);
    public void Kill(); // Istantaneo + fire-and-forget DB update
    public void Close(); // Chiusura graceful via stdin.Close()
}
```

**Comando di lancio:**
```bash
claude -p --input-format stream-json --output-format stream-json --verbose [--permission-mode plan] [--resume {session_id}]
```

**Caratteristiche chiave:**
- **Processo persistente**: Rimane vivo per tutta la durata della conversazione
- **Stream continuo**: stdin/stdout aperti per comunicazione bidirezionale
- **Kill non bloccante**: Fire-and-forget per DB update dopo kill
- **Gestione errori**: Log e notifica UI in caso di crash del processo

### 3. StreamJsonParser (Utility)

**Responsabilità:**
- Parsing line-by-line del JSONL da stdout
- Distinzione tra tipi di messaggio (system/assistant/user/result)
- Estrazione dati strutturati (text, tool_use, tool_result, metadata)
- Eventi tipizzati per notificare la UI

**Tipi di messaggio gestiti:**

1. **System Init** (`type="system", subtype="init"`)
   - Contiene: `session_id`, `tools[]`, `model`, `permissionMode`
   - Trigger: INSERT nel DB quando ricevuto per prima volta

2. **Assistant Message** (`type="assistant"`)
   - Content array: può contenere `text` o `tool_use`
   - Parsing ricorsivo del content array
   - Formattazione per RichTextBox

3. **User Message** (`type="user"`)
   - Solitamente tool_result con output dei tool
   - Mostra risultato operazioni (successo/errore)

4. **Result** (`type="result"`)
   - Metadati finali: costi, durata, tokens, num_turns
   - Update metadata panel

**Eventi:**
```csharp
public event EventHandler<SessionInitializedEventArgs> SessionInitialized; // session_id ricevuto
public event EventHandler<ToolCallEventArgs> ToolCallReceived;
public event EventHandler<TextReceivedEventArgs> TextReceived;
public event EventHandler<ToolResultEventArgs> ToolResultReceived;
public event EventHandler<MetadataEventArgs> MetadataReceived;
```

### 4. RichTextBoxFormatter (Utility)

**Responsabilità:**
- Applicazione sintassi colorata al RichTextBox
- Rendering markdown base (code blocks, bold, italic)
- Auto-scroll durante aggiornamenti
- Formattazione messaggi speciali (errori, warning, tool calls)

**Color scheme:**
- **Tool names** (Bash, Read, Edit, Grep): `Color.Blue`, `FontStyle.Bold`
- **Code blocks**: `Color.Green`, `Font: Consolas`
- **User prompts**: Default (Black/White)
- **Errori**: `Color.Red`, `FontStyle.Bold`
- **Metadati**: `Color.Gray`, `FontStyle.Italic`
- **Tool results success**: `Color.DarkGreen`
- **Tool results error**: `Color.DarkRed`

**Formattazione tool calls:**
```
🔧 [Bash] Run build command
✓ Build completed successfully (output...)
```

### 5. CommandHistoryManager (Utility)

**Responsabilità:**
- Gestione storico comandi per ogni tab (max 50 comandi)
- Navigazione con frecce ↑/↓ nel TextBox input
- Persistenza dello storico durante la sessione (in-memory)

**Comportamento:**
- ↑: Comando precedente
- ↓: Comando successivo (o torna a input vuoto se alla fine)
- Enter: Aggiunge comando allo storico

### 6. DbService (Service)

**Responsabilità:**
- Connessione al database MariaDB
- CRUD operations su tabella `conversations`
- Validazione connessione all'avvio (blocca app se fallisce)
- Query per recovery delle sessioni

**Configurazione:**
- **Hardcoded**: Host, Port, Database (valori forniti dall'utente)
- **User Secrets**: Username, Password

**Schema database:**
```sql
CREATE TABLE conversations (
    id INT AUTO_INCREMENT PRIMARY KEY,
    session_id VARCHAR(36) NOT NULL UNIQUE,
    tab_title VARCHAR(255),
    is_plan_mode BOOLEAN DEFAULT FALSE,
    last_activity DATETIME NOT NULL,
    status ENUM('active', 'closed', 'killed') DEFAULT 'active',
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_recovery (status, last_activity)
);
```

**Metodi principali:**
```csharp
public class DbService
{
    public Task<bool> TestConnectionAsync(); // All'avvio
    public Task InsertSessionAsync(ConversationSession session);
    public Task UpdateStatusAsync(string sessionId, string status);
    public Task UpdateLastActivityAsync(string sessionId);
    public Task<List<ConversationSession>> GetActiveConversationsAsync();
}
```

**Quando salvare/aggiornare:**

| Evento | Operazione | Tipo |
|--------|-----------|------|
| Primo session_id ricevuto | INSERT | Sync (veloce) |
| Ogni 5 minuti | UPDATE last_activity | Async fire-and-forget |
| Kill processo | UPDATE status='killed' | Async fire-and-forget |
| Chiusura tab normale | UPDATE status='closed' | Sync (veloce) |
| Chiusura app (FormClosing) | UPDATE tutti tab a 'closed' | Sync (batch) |

### 7. ConversationSession (Model)

**Proprietà:**
```csharp
public class ConversationSession
{
    public int? Id { get; set; } // DB primary key
    public string SessionId { get; set; } // Claude session UUID
    public string TabTitle { get; set; }
    public bool IsPlanMode { get; set; }
    public DateTime LastActivity { get; set; }
    public string Status { get; set; } // active/closed/killed

    // Runtime data (non in DB)
    public decimal TotalCostUsd { get; set; }
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public int NumTurns { get; set; }
    public List<string> ToolsUsed { get; set; }
    public string CurrentModel { get; set; }
}
```

### 8. MetadataPanel (UserControl)

**Responsabilità:**
- Visualizzazione metadati real-time della conversazione corrente
- Update automatico quando cambia tab o arrivano nuovi dati

**Dati visualizzati:**
```
┌─────────────────────────┐
│ Metadata                │
├─────────────────────────┤
│ Session ID:             │
│ abc123...               │
│                         │
│ Total Cost:             │
│ $0.0234                 │
│                         │
│ Tokens:                 │
│ Input: 5,234            │
│ Output: 892             │
│ Cache Read: 12,456      │
│ Cache Create: 328       │
│                         │
│ Duration:               │
│ Last: 3.2s              │
│ Total: 45.6s            │
│                         │
│ Tools Used:             │
│ • Bash (3x)             │
│ • Read (5x)             │
│ • Edit (2x)             │
│                         │
│ Model:                  │
│ claude-sonnet-4-5       │
│                         │
│ Turns: 7                │
└─────────────────────────┘
```

---

## Flussi di Esecuzione

### Flusso 1: Apertura Nuova Conversazione

```
1. User click "New" → Crea nuovo tab
2. Tab creato con RichTextBox vuoto e input box
3. User digita primo prompt → preme Enter
4. ClaudeProcessManager.Start(isPlanMode=false, resumeSessionId=null)
   ├─> Lancia: claude -p --input-format stream-json --output-format stream-json --verbose
   ├─> Scrive su stdin: {"type":"user","message":{"role":"user","content":"..."}}
   └─> Avvia lettura asincrona stdout
5. StreamJsonParser riceve primo messaggio: type="system", subtype="init"
   ├─> Estrae session_id
   ├─> Evento SessionInitialized
   └─> DbService.InsertSessionAsync() ← INSERT immediato
6. Parser continua: type="assistant" → formatta e mostra in RichTextBox
7. Parser: type="result" → aggiorna metadata panel
8. Processo rimane vivo in attesa di nuovo input
```

### Flusso 2: Conversazione in Corso

```
1. User digita nuovo prompt → Enter
2. ClaudeProcessManager.SendMessage(prompt)
   └─> Scrive su stdin: {"type":"user","message":{"role":"user","content":"..."}}
3. Parser riceve cycle: init → assistant (con tool_use?) → user (tool_result?) → assistant (text) → result
4. RichTextBoxFormatter applica colori:
   ├─> Tool call: "🔧 [Bash] description" in blu
   ├─> Tool result: "✓ output" in verde (o "✗ error" in rosso)
   └─> Text: formattazione normale con markdown
5. Metadata panel aggiornato con nuovi dati da result
6. DbService.UpdateLastActivityAsync() ← Fire-and-forget ogni 5 min
7. Processo rimane vivo per prossimo messaggio
```

### Flusso 3: Stop Execution (Kill)

```
1. User click bottone "Stop" (o Ctrl+C, shortcut)
2. ClaudeProcessManager.Kill()
   ├─> process.Kill(entireProcessTree: true) ← ISTANTANEO
   ├─> Append a RichTextBox: "⚠️ Execution stopped by user"
   └─> Task.Run(() => DbService.UpdateStatusAsync(sessionId, "killed")) ← Fire-and-forget
3. UI torna a stato Idle
4. session_id mantenuto per future riprese
5. User può continuare conversazione → nuovo prompt rilancia processo con --resume
```

### Flusso 4: Chiusura Tab

```
1. User chiude tab (click X sul tab)
2. If conversazione has unsaved work:
   └─> MessageBox conferma: "Close conversation?"
3. ClaudeProcessManager.Close()
   ├─> stdin.Close() ← Segnala EOF a Claude
   ├─> Aspetta processo termini gracefully (timeout 5s)
   └─> Se non termina: Kill()
4. DbService.UpdateStatusAsync(sessionId, "closed") ← Sync (veloce)
5. Tab rimosso dal TabControl
```

### Flusso 5: Chiusura Applicazione

```
1. User chiude finestra (o Alt+F4)
2. MainForm.FormClosing event
3. Foreach tab aperto:
   ├─> ClaudeProcessManager.Close() per ogni processo
   └─> Aggiungi session_id a lista
4. DbService.UpdateStatusBatchAsync(sessionIds, "closed") ← Batch update
5. Aspetta max 10s per processi terminino
6. Force kill processi rimanenti
7. Application.Exit()
```

### Flusso 6: Recovery Dopo Crash

```
1. User rilancia applicazione
2. Program.Main() → DbService.TestConnectionAsync()
   └─> Se fallisce: MessageBox errore + Application.Exit() ← Blocca avvio
3. SessionRecoveryService.RecoverSessionsAsync()
   └─> Query: SELECT * FROM conversations WHERE status IN ('active', 'killed')
4. Foreach conversazione da recuperare:
   ├─> Crea nuovo tab con tab_title salvato
   ├─> ClaudeProcessManager.Start(isPlanMode, resumeSessionId)
   │   └─> Lancia: claude -p --resume {session_id} --input-format stream-json ...
   ├─> Invia automaticamente: {"type":"user","message":{"role":"user","content":"Su cosa stavamo lavorando?"}}
   └─> Claude risponde con riassunto del contesto
5. UI mostra tutti i tab recuperati
6. User può continuare conversazioni normalmente
```

### Flusso 7: Toggle Plan Mode

```
1. User preme Alt+M (o click bottone "Plan Mode")
2. MainForm.TogglePlanMode()
   ├─> Inverte flag currentSession.IsPlanMode
   ├─> Update UI indicator: "Plan Mode: ON" (cambia colore)
   └─> DbService.UpdatePlanModeAsync(sessionId, isPlanMode)
3. Flag applicato al PROSSIMO lancio di Claude
   └─> Se processo già attivo: applica al prossimo messaggio che riavvia processo
4. Quando si invia nuovo messaggio:
   ├─> Se processo non attivo: Start(isPlanMode=true/false)
   └─> Se processo attivo: continua normalmente (plan mode del processo corrente)
```

**Nota**: Non è possibile cambiare plan mode di un processo già attivo. Il flag viene applicato solo quando si (ri)avvia il processo.

---

## Gestione Plan Mode

Claude Code supporta plan mode tramite il parametro `--permission-mode`:

```bash
# Plan mode attivo
claude -p --permission-mode plan ...

# Modalità normale
claude -p --permission-mode default ...
```

**Comportamento in Plan Mode:**
- Claude non esegue tool che modificano il sistema senza approvazione
- Usa il tool `ExitPlanMode` quando completa la pianificazione
- Nel JSON stream, appare come normale tool_use
- Il sistema risponde con errore: "Exit plan mode?"
- Claude presenta il piano finale in formato testuale

**Limitazione importante:**
- **Non è possibile cambiare plan mode dinamicamente** durante l'esecuzione
- Il parametro `--permission-mode` deve essere passato all'avvio del processo
- Per cambiare plan mode di una conversazione in corso, bisogna:
  1. Killare il processo corrente
  2. Riavviarlo con `--resume` e il nuovo `--permission-mode`

---

## Configurazione

### appsettings.json

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "logs/app-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30,
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ]
  },
  "Database": {
    "Host": "TBD_BY_USER",
    "Port": 3306,
    "Database": "TBD_BY_USER"
  },
  "DatabaseCredentials": {
    "Username": "",
    "Password": ""
  }
}
```

### User Secrets (secrets.json)

```json
{
  "DatabaseCredentials": {
    "Username": "actual_username",
    "Password": "actual_password"
  }
}
```

**Setup User Secrets:**
```bash
cd C:\sources\ClaudeGui\ClaudeCodeGUI
dotnet user-secrets init
dotnet user-secrets set "DatabaseCredentials:Username" "your_username"
dotnet user-secrets set "DatabaseCredentials:Password" "your_password"
```

### Hardcoded in DbService.cs

```csharp
// Valori forniti dall'utente
private const string DB_HOST = "host.example.com";
private const int DB_PORT = 3306;
private const string DB_NAME = "claudecodegui";
```

---

## Dettagli Tecnici: Claude Code Headless Mode

### Modalità Persistente con Stream JSON

Claude Code supporta una modalità persistente dove un singolo processo può gestire multiple conversazioni senza riavvio:

**Comando:**
```bash
claude -p --input-format stream-json --output-format stream-json --verbose
```

**Caratteristiche:**
1. Il processo rimane in ascolto su stdin
2. Ogni messaggio JSONL inviato su stdin genera una risposta
3. Il contesto viene mantenuto automaticamente tra messaggi
4. Il processo termina quando stdin riceve EOF

**Formato input (JSONL su stdin):**
```json
{"type":"user","message":{"role":"user","content":"Il tuo prompt qui"}}
```

**Formato output (JSONL su stdout):**

Ogni messaggio genera un ciclo completo di eventi:

1. **Init Event:**
```json
{
  "type": "system",
  "subtype": "init",
  "session_id": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "tools": ["Task", "Bash", "Glob", ...],
  "model": "claude-sonnet-4-5-20250929",
  "permissionMode": "default",
  ...
}
```

2. **Assistant Response (text):**
```json
{
  "type": "assistant",
  "message": {
    "model": "claude-sonnet-4-5-20250929",
    "id": "msg_xxx",
    "role": "assistant",
    "content": [
      {
        "type": "text",
        "text": "Risposta di Claude..."
      }
    ],
    "usage": {
      "input_tokens": 123,
      "output_tokens": 45,
      "cache_read_input_tokens": 1234,
      ...
    }
  },
  "session_id": "...",
  ...
}
```

3. **Assistant Response (tool_use):**
```json
{
  "type": "assistant",
  "message": {
    "content": [
      {
        "type": "tool_use",
        "id": "toolu_xxx",
        "name": "Bash",
        "input": {
          "command": "ls -la",
          "description": "List files in directory"
        }
      }
    ],
    ...
  },
  ...
}
```

4. **Tool Result:**
```json
{
  "type": "user",
  "message": {
    "role": "user",
    "content": [
      {
        "type": "tool_result",
        "tool_use_id": "toolu_xxx",
        "content": "file1.txt\nfile2.txt",
        "is_error": false
      }
    ]
  },
  ...
}
```

5. **Result (metadata finale):**
```json
{
  "type": "result",
  "subtype": "success",
  "duration_ms": 3425,
  "duration_api_ms": 4126,
  "num_turns": 3,
  "result": "Testo finale della risposta",
  "session_id": "...",
  "total_cost_usd": 0.0123,
  "usage": {
    "input_tokens": 234,
    "cache_creation_input_tokens": 123,
    "cache_read_input_tokens": 4567,
    "output_tokens": 89,
    ...
  },
  "modelUsage": {
    "claude-sonnet-4-5-20250929": { ... }
  },
  ...
}
```

### Mantenimento del Contesto

**All'interno dello stesso stream:**
- Il `session_id` viene riutilizzato automaticamente
- Il campo `num_turns` incrementa progressivamente (1, 3, 5, 7...)
- La cache dei token viene riutilizzata per efficienza

**Tra invocazioni separate:**
- Usare `--resume {session_id}` per recuperare una conversazione precedente
- Claude mantiene lo storico completo della conversazione
- Esempio: `claude -p --resume abc123-... --input-format stream-json ...`

### Flags Importanti

| Flag | Descrizione |
|------|-------------|
| `--input-format stream-json` | Abilita input JSONL su stdin |
| `--output-format stream-json` | Abilita output JSONL su stdout |
| `--verbose` | Include più dettagli nei messaggi JSON |
| `--permission-mode plan` | Attiva Plan Mode |
| `--resume {session_id}` | Riprende conversazione esistente |
| `--include-partial-messages` | Stream parziali durante generazione (non usato) |

---

## Logging con Serilog

### Configurazione

```csharp
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .CreateLogger();
```

### Eventi da loggare

**Livello Information:**
- Avvio applicazione
- Connessione database successful
- Lancio nuovo processo Claude
- Recovery di sessioni
- Chiusura graceful

**Livello Warning:**
- Connessione database fallita (con retry)
- Processo Claude terminato inaspettatamente
- Timeout operazioni

**Livello Error:**
- Errori parsing JSON
- Eccezioni durante operazioni DB
- Crash processo Claude
- Errori critici che richiedono attenzione

**Esempio log:**
```
2025-10-29 14:23:45.123 +01:00 [INF] Application started
2025-10-29 14:23:45.456 +01:00 [INF] Database connection successful
2025-10-29 14:23:50.789 +01:00 [INF] Recovering 2 active sessions
2025-10-29 14:23:51.123 +01:00 [INF] Process launched for session abc123... (PID: 12345)
2025-10-29 14:25:30.456 +01:00 [WRN] Process terminated unexpectedly (exit code: -1)
2025-10-29 14:30:00.000 +01:00 [ERR] Failed to parse JSON: {"type":"invalid"}
```

---

## Features UI Avanzate

### 1. Sintassi Colorata

Il `RichTextBoxFormatter` applica colori e stili in base al tipo di contenuto:

- **Tool calls**: Icona 🔧, nome tool in blu bold
- **Tool results success**: Icona ✓, testo in verde scuro
- **Tool results error**: Icona ✗, testo in rosso
- **Code blocks**: Sfondo grigio chiaro, font Consolas, testo verde
- **User prompts**: Font normale, grassetto
- **Errori**: Rosso bold
- **Metadati**: Grigio italic

### 2. Storico Comandi

Ogni tab mantiene uno storico dei comandi inviati:
- **↑**: Naviga indietro nello storico
- **↓**: Naviga avanti nello storico
- **Enter**: Invia comando e lo aggiunge allo storico
- Max 50 comandi per sessione (rolling window)

### 3. Auto-scroll

Il RichTextBox segue automaticamente l'output durante la generazione:
- Scroll automatico verso il basso quando arrivano nuovi messaggi
- Disabilita auto-scroll se l'utente scrolla manualmente verso l'alto
- Riabilita quando l'utente torna alla fine

### 4. Indicatori di Stato

**StatusBar mostra:**
- Stato corrente: Idle / Working / Stopped
- Model attivo: claude-sonnet-4-5-20250929
- Numero conversazioni attive

**Tab title mostra:**
- Primi 30 caratteri del primo prompt
- Icona diversa se in Plan Mode (📋) vs normale (💬)
- Badge rosso se processo stopped

### 5. Keyboard Shortcuts

| Shortcut | Azione |
|----------|--------|
| `Ctrl+N` | Nuova conversazione |
| `Ctrl+W` | Chiudi tab corrente |
| `Alt+M` | Toggle Plan Mode |
| `Ctrl+K` | Stop execution (Kill) |
| `↑` | Comando precedente (in input box) |
| `↓` | Comando successivo (in input box) |
| `Enter` | Invia messaggio |
| `Shift+Enter` | Newline in input (multiline) |

---

## Limitazioni Note

### 1. Plan Mode Non Dinamico

- Non è possibile cambiare plan mode di un processo già attivo
- Il flag `--permission-mode` deve essere passato all'avvio
- Workaround: Killare e riavviare con `--resume` e nuovo flag

### 2. No Thinking Blocks Visibili

- I thinking blocks di Claude non sono esposti in stream-json (almeno non di default)
- Possibile che esistano flag non documentati per abilitarli

### 3. Kill Non Graceful

- `process.Kill()` termina il processo immediatamente
- Claude non ha possibilità di cleanup
- Alternative come stdin.Close() potrebbero non funzionare se Claude è impegnato in operazioni lunghe

### 4. No Interrupt Durante Tool Execution

- Non è possibile interrompere Claude tramite stream-json input durante l'esecuzione
- Unica opzione: Killare il processo

### 5. Database Obbligatorio

- Se MariaDB non è raggiungibile, l'app non parte
- No fallback locale (design choice per semplicità)

### 6. No Export/Import

- Per ora non è implementata l'esportazione conversazioni (feature future)
- Le conversazioni sono solo nel DB

---

## Roadmap Futura

### Fase 1 (MVP - Current)
- [x] Interfaccia base con TabControl
- [x] Gestione processi Claude persistenti
- [x] Parsing stream-json
- [x] Persistenza MariaDB
- [x] Recovery automatico
- [x] Sintassi colorata
- [x] Metadata panel

### Fase 2 (Enhancement)
- [ ] Export conversazioni (Markdown, JSON)
- [ ] Import conversazioni da file
- [ ] Ricerca full-text nelle conversazioni
- [ ] Filtri per data/costo/model
- [ ] Statistiche aggregate (costi totali, tools più usati)

### Fase 3 (Advanced)
- [ ] Templates di prompt predefiniti
- [ ] Snippet di codice salvati
- [ ] Integrazione con VS Code (aprire file da tool calls)
- [ ] Diff viewer per Edit tool calls
- [ ] Multi-account support (diversi API key)

### Fase 4 (Enterprise)
- [ ] Team collaboration (condivisione conversazioni)
- [ ] Role-based access control
- [ ] Audit log completo
- [ ] Metriche avanzate e dashboard
- [ ] API REST per automazione

---

## Note di Sviluppo

### Testing Strategy

1. **Unit Tests:**
   - StreamJsonParser: parsing di vari formati JSON
   - RichTextBoxFormatter: formattazione corretta
   - CommandHistoryManager: navigazione storico

2. **Integration Tests:**
   - DbService: CRUD operations su MariaDB
   - ClaudeProcessManager: lancio e comunicazione con processo

3. **Manual Tests:**
   - Recovery dopo crash simulato
   - Kill durante operazioni lunghe
   - Toggle Plan Mode
   - Multi-tab concurrency

### Performance Considerations

- **Throttling DB updates**: Update last_activity max ogni 5 minuti
- **Async IO**: Lettura stdout non bloccante
- **Fire-and-forget**: DB updates dopo kill non bloccano UI
- **Connection pooling**: Riuso connessioni MariaDB

### Security Considerations

- **User Secrets**: Credenziali mai committate
- **SQL Injection**: Uso di parametrized queries
- **Process isolation**: Ogni processo Claude isolato
- **Logging**: No logging di credenziali o dati sensibili

---

## Conclusioni

ClaudeCodeGUI è un'applicazione robusta e user-friendly per interagire con Claude Code in modalità headless. L'architettura persistente con stream-json garantisce performance ottimali e una UX fluida, mentre la persistenza su MariaDB e il recovery automatico assicurano che nessun lavoro vada perso in caso di crash.

Le funzionalità avanzate come sintassi colorata, metadata panel dettagliato, e multi-tab support rendono l'applicazione competitiva rispetto all'esperienza da terminale, aggiungendo il vantaggio di una GUI moderna e intuitiva.

---

**Documento creato**: 2025-10-29
**Versione**: 1.0
**Autore**: Claude (Anthropic) & User
**Target Framework**: .NET 9 Windows Forms
**Database**: MariaDB
**License**: TBD
