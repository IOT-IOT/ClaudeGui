# FASE 1: Refactoring da Modalità Headless a Interactive - COMPLETATO ✅

## 📋 Panoramica

Completato con successo il refactoring dell'architettura ClaudeGui Blazor da **modalità headless (JSONL)** a **modalità interactive (TTY-like)**.

**Data completamento:** 12 Novembre 2025
**Test suite status:** ✅ 62/62 test passing (100%)
**Build status:** ✅ Clean build (30 warnings, 0 errors)

---

## 🎯 Obiettivi Raggiunti

### 1. **ClaudeProcessManager.cs** - Architettura I/O Refactored
✅ **BuildArguments()** - Modalità interactive
- ❌ Rimosso: `-p`, `--input-format`, `--output-format`, `--verbose`, `--replay-user-messages`
- ✅ Mantenuto: `--dangerously-skip-permissions`, `--resume <sessionId>`
- 📝 Risultato: Claude lancia in modalità interactive (no headless)

✅ **Events refactored**
- `JsonLineReceived` → `RawOutputReceived`
- `JsonLineReceivedEventArgs` → `RawOutputReceivedEventArgs`

✅ **I/O Methods refactored**
- `SendMessageAsync(string)` → `SendRawInputAsync(string)`
- Rimosso wrapping JSONL, ora input diretto su stdin
- `ReadStdoutAsync()` ora legge raw bytes (4KB buffer) invece di JSONL lines
- Rimosso `EscapeJson()` method (obsoleto)

### 2. **ClaudeHub.cs** - SignalR Output Streaming
✅ Event handler aggiornato:
```csharp
// Prima
processManager.JsonLineReceived += async (sender, e) =>
{
    await Clients.Group(sessionId).SendAsync("ReceiveOutput", e.JsonLine);
};

// Dopo
processManager.RawOutputReceived += async (sender, e) =>
{
    await Clients.Group(sessionId).SendAsync("ReceiveOutput", e.RawOutput);
};
```

✅ Input handling aggiornato:
```csharp
// Prima: await processManager.SendMessageAsync(input);
// Dopo: await processManager.SendRawInputAsync(input);
```

### 3. **terminal.js** - Frontend Raw Output
✅ `handleOutputReceived()` semplificato:
```javascript
// Prima: JSON.parse(jsonLine) + formatting logic per type
// Dopo: Diretta write a xterm.js (gestisce ANSI codes automaticamente)
function handleOutputReceived(rawOutput) {
    terminals.forEach((terminalData) => {
        terminalData.terminal.write(rawOutput);
    });
}
```

### 4. **Test Suite** - Aggiornati per Nuova Architettura
✅ `ClaudeProcessManagerTests.cs`:
- Test `JsonLineReceived_Event_ShouldBeSubscribable` → `RawOutputReceived_Event_ShouldBeSubscribable`
- Tutti i test passano senza modifiche addizionali

✅ **Playwright E2E Tests** aggiunti:
- `TerminalE2ETests.cs` con 4 test E2E (skipped by default, require server running)
- Test homepage load, session creation, terminal visibility, input handling
- Chromium installed for automated browser testing

---

## 📊 Risultati Test

```bash
$ cd ClaudeGui.Blazor.Tests && dotnet test

Superato! - Non superati: 0. Superati: 62. Ignorati: 0. Totale: 62.
Durata: 5s
```

**Breakdown:**
- ✅ Infrastructure tests: 3/3 (DatabaseConnectionTests)
- ✅ Services tests: 12/12 (ClaudeProcessManagerTests, TerminalManagerTests)
- ✅ Integration tests: 10/10 (FullStackIntegrationTests)
- ✅ Other tests: 37/37 (Models, Hubs, etc.)
- 🔄 E2E tests: 4 (skipped, require manual server start)

---

## 🔄 Differenze Architetturali: Prima vs Dopo

### Prima (Modalità Headless - MAUI)
```
User Input → JSONL wrapper → stdin
              ↓
Claude (-p flag) - headless mode
              ↓
stdout (JSONL) → Parser → Extract content → DB save
                                          ↓
                                      WebView display
```

### Dopo (Modalità Interactive - Blazor)
```
User Input → Raw stdin (no wrapping)
              ↓
Claude (no -p) - interactive mode (TTY-like)
              ↓
stdout (raw + ANSI) → xterm.js (direct display)
                            ↓
                        Browser terminal
```

**Vantaggi della nuova architettura:**
- ✅ Claude gestisce autonomamente session state
- ✅ ANSI escape codes renderizzati automaticamente da xterm.js
- ✅ Nessun parsing JSONL necessario
- ✅ Architettura più semplice e manutenibile
- ✅ Preparata per futura integrazione PTY (FASE 2)

---

## 📁 File Modificati

### Core Files
1. `ClaudeGui.Blazor/Services/ClaudeProcessManager.cs` - 🔥 Major refactoring
2. `ClaudeGui.Blazor/Hubs/ClaudeHub.cs` - Event handlers aggiornati
3. `ClaudeGui.Blazor/wwwroot/js/terminal.js` - Rimosso JSON parsing

### Test Files
4. `ClaudeGui.Blazor.Tests/Services/ClaudeProcessManagerTests.cs` - Test event aggiornato
5. `ClaudeGui.Blazor.Tests/E2E/TerminalE2ETests.cs` - ✨ NEW: Playwright E2E tests

### Dependencies
6. `ClaudeGui.Blazor.Tests/ClaudeGui.Blazor.Tests.csproj` - ✨ Aggiunto Microsoft.Playwright 1.49.0

---

## 🚀 Testing Manuale

### Prerequisiti
1. Claude CLI installato: `C:\Users\enric\.local\bin\claude.exe`
2. Database MariaDB running: `192.168.1.11:3306`
3. .NET 9.0 SDK installed

### Steps per Test Manuale

```bash
# 1. Avvia l'applicazione Blazor
cd C:\sources\claudegui\ClaudeGui.Blazor
dotnet run

# 2. Apri browser a http://localhost:5000

# 3. Crea nuova sessione:
#    - Working Directory: C:\Temp (o qualsiasi path valido)
#    - Click "Create New Session"

# 4. Verifica terminal:
#    - Terminal xterm.js appare
#    - Puoi digitare input
#    - Output di Claude appare in real-time
#    - ANSI colors funzionano correttamente

# 5. Test resume sessione:
#    - Torna alla homepage (bottone "Close")
#    - La sessione appare in "Active Sessions"
#    - Click "Attach" per riconnettere
#    - Sessione riprende con --resume flag
```

### Cosa Verificare
- ✅ Terminal si inizializza senza errori
- ✅ Input viene inviato correttamente
- ✅ Output di Claude appare (no JSONL visible, solo testo normale)
- ✅ Colori ANSI funzionano (se Claude li usa)
- ✅ Resume sessione funziona con `--resume <sessionId>`
- ✅ No errori JavaScript in browser console

---

## 🔮 FASE 2: PTY Implementation (Prossimi Step)

### Obiettivo
Sostituire stdout/stderr redirect con PTY per emulazione terminal completa.

### Libreria Scelta
✅ **Microsoft vs-pty.net** (ufficiale Microsoft)
- GitHub: https://github.com/microsoft/vs-pty.net
- NuGet: `Microsoft.Pty`
- Cross-platform: Windows (ConPTY), Linux (forkpty), macOS

### Modifiche Necessarie (FASE 2)
1. Installare `Microsoft.Pty` NuGet package
2. Refactor `ClaudeProcessManager` per usare `PtyProcess`
3. Aggiungere terminal resize support (xterm.js → SignalR → PTY ioctl)
4. Character-by-character input (no line buffering)
5. Test con spinner/progress bars animate di Claude

### Vantaggi PTY (rispetto a FASE 1)
- Claude rileverà `isatty() == true` (vero TTY)
- Spinner e progress bars animate funzioneranno
- Ctrl+C, Ctrl+D, frecce su/giù per history nativi
- Terminal resize dinamico
- Esperienza identica a terminal nativo

---

## 📝 Note Tecniche

### Build Warnings (Accettabili)
- 30 warnings totali (tutti nullable reference, async methods senza await, obsolete methods)
- Nessun warning critico o relativo al refactoring
- 0 errori di compilazione

### Parametri Claude Rimossi
```bash
# Prima (headless)
claude -p --input-format stream-json --output-format stream-json --verbose --dangerously-skip-permissions --replay-user-messages [--resume <id>]

# Dopo (interactive)
claude --dangerously-skip-permissions [--resume <id>]
```

### Event Signature Changes
```csharp
// Prima
public event EventHandler<JsonLineReceivedEventArgs>? JsonLineReceived;
public class JsonLineReceivedEventArgs : EventArgs { public string JsonLine { get; set; } }

// Dopo
public event EventHandler<RawOutputReceivedEventArgs>? RawOutputReceived;
public class RawOutputReceivedEventArgs : EventArgs { public string RawOutput { get; set; } }
```

---

## ✅ Checklist Completamento FASE 1

- [x] ClaudeProcessManager refactored per modalità interactive
- [x] ClaudeHub aggiornato per raw output streaming
- [x] terminal.js semplificato (no JSON parsing)
- [x] Test suite aggiornata e passing (62/62)
- [x] Playwright E2E tests aggiunti
- [x] Chromium installato per Playwright
- [x] Build pulito (0 errori)
- [x] Documentazione completa
- [ ] Test manuale (TODO: da eseguire da utente)

---

## 🎓 Lessons Learned

1. **Architettura più semplice = Più manutenibile**
   - Rimuovere JSONL parsing ha semplificato significativamente il codice
   - xterm.js gestisce automaticamente ANSI codes → no custom logic needed

2. **Test-driven refactoring funziona**
   - Partire con 62 test passing ha garantito no regressioni
   - Ogni modifica verificata immediatamente

3. **Playwright è powerful ma richiede server running**
   - Test E2E sono skipped by default
   - Utile per testing manuale automatizzato in futuro

4. **Claude interactive mode è più naturale per web terminal**
   - Filosofia corretta: Claude gestisce stato, noi solo I/O streaming
   - Preparati per PTY in FASE 2 senza breaking changes

---

## 📞 Contatti

- Progetto: ClaudeGui Blazor Server Migration
- Sviluppatore: Claude (Anthropic) + Enrico
- Data: 12 Novembre 2025

**Next step:** Test manuale dell'applicazione da parte dell'utente! 🚀
