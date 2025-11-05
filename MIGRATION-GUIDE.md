# Guida alla Migrazione Multi-Sessione

## Panoramica

Questa guida illustra i passi necessari per migrare dall'architettura single-session all'architettura multi-sessione con gestione tab.

## Modifiche Implementate

### 1. Database
- **Tabella**: `conversations` → `Sessions`
- **Campi aggiunti**: `name` (VARCHAR(255), nullable)
- **Status semplificato**: `active`, `running`, `closed`, `killed` → `open`, `closed`
- **Logica**:
  - `open`: Sessione da riaprire al boot dell'applicazione
  - `closed`: Sessione chiusa definitivamente dall'utente

### 2. Nuovi Componenti

#### Models
- **SessionInfo**: Dati sessione da filesystem + database
- **SessionTabItem**: Modello runtime per singolo tab con ProcessManager

#### Services
- **SessionScannerService**: Scansiona `~/.claude/projects/` per file .jsonl
- **DbService**: Nuovi metodi per tabella Sessions

#### Views
- **SessionSelectorPage**: Dialog per selezionare/creare sessioni
- **NewSessionDialog**: Dialog per nuove sessioni (nome + working directory)
- **AssignNameDialog**: Dialog per assegnare nome a sessioni esistenti
- **SessionTabContent**: ContentView per contenuto singolo tab

#### Altri
- **InvertedBoolConverter**: Converter XAML per invertire booleani

### 3. UI Changes
- **MainPage**: Refactoring completo con tab headers + content area
- **Button "New Conversation"** → **"📂 Seleziona Sessione"**
- **TabView**: Gestione multi-sessione con header scrollabili

### 4. Funzionalità Implementate

#### Boot Logic
- All'avvio, l'applicazione:
  1. Scansiona `~/.claude/projects/` per tutti i file .jsonl
  2. Query database per sessioni con `status='open'`
  3. Crea un tab per ciascuna sessione open
  4. Avvia processo Claude con `--resume <session-id>`

#### Selezione Sessione
- Click su "Seleziona Sessione" apre dialog con:
  - Prima opzione: "➕ Nuova sessione..."
  - Lista di tutte le sessioni esistenti
  - Icone: ✅ (ha nome), ❌ (senza nome)
  - Pulsante "Assegna Nome" per sessioni unnamed

#### Comando Exit
- L'utente digita `exit` nel prompt
- L'applicazione:
  1. Chiede conferma
  2. Invia comando exit al processo Claude
  3. Aggiorna `status='closed'` nel database
  4. Rimuove il tab dall'UI

#### Stop Button
- Click su Stop:
  1. Killa il processo corrente
  2. Riavvia immediatamente con `claude --resume <session-id>`
  3. Mantiene il tab aperto
  4. Utile per "restart" rapido della sessione

## Passi di Migrazione

### Passo 1: Backup Database

```bash
# Esegui backup della tabella conversations
mysqldump -u root -p ClaudeGui conversations > conversations_backup.sql
```

### Passo 2: Esegui Migration Script

**Opzione A: Migration (se esiste già tabella `conversations`)**

```bash
ssh server01
mysql -h 192.168.1.11 -u root -p$(cat /opt/mariadb/secrets/root_password.txt) ClaudeGui < /path/to/migration-to-sessions.sql
```

**Opzione B: Schema Pulito (se tabella `conversations` non esiste)**

```bash
ssh server01
mysql -h 192.168.1.11 -u root -p$(cat /opt/mariadb/secrets/root_password.txt) ClaudeGui < /path/to/database-schema-sessions.sql
```

### Passo 3: Verifica Tabella

```sql
USE ClaudeGui;
DESCRIBE Sessions;

-- Dovrebbe mostrare:
-- id, session_id, name, working_directory, last_activity, status, created_at, updated_at
```

### Passo 4: Verifica Dati Migrati

```sql
SELECT session_id, name, status FROM Sessions;

-- Verifica che:
-- 1. Status sia 'open' o 'closed' (non più 'active', 'running', 'killed')
-- 2. Tutte le sessioni siano presenti
```

### Passo 5: Test Applicazione

1. Avvia ClaudeCodeMAUI
2. Verifica che le sessioni `open` vengano caricate automaticamente
3. Testa "Seleziona Sessione" → Crea nuova sessione
4. Testa "Seleziona Sessione" → Apri sessione esistente
5. Testa "Assegna Nome" per sessione unnamed
6. Testa comando `exit` in una sessione
7. Testa Stop button per restart sessione
8. Chiudi applicazione e riapri → verifica restore sessioni open

## Risoluzione Problemi

### Problema: Sessioni non vengono caricate al boot

**Causa**: Credenziali database non configurate in User Secrets

**Soluzione**:
```bash
cd C:\Sources\ClaudeGui\ClaudeCodeMAUI
dotnet user-secrets set "DatabaseCredentials:Username" "claudegui"
dotnet user-secrets set "DatabaseCredentials:Password" "your-password-here"
```

### Problema: "FolderPicker not found"

**Causa**: .NET MAUI non ha FolderPicker nativo

**Soluzione**: Già implementata - usa `DisplayPromptAsync` per inserimento manuale path

### Problema: Tab non si seleziona correttamente

**Causa**: Implementazione custom TabView con Button headers

**Soluzione**: Verificare che `SwitchToTab(index)` aggiorni correttamente il `CurrentTabContent.Content`

### Problema: Rendering HTML non funziona

**Causa**: Rendering HTML per messaggi JSON non ancora implementato

**Soluzione**: TODO - implementare parsing messaggi "assistant", "tool_use", "tool_result" e rendering in WebView

## File Backup Creati

- `MainPage.xaml.cs.old`: Backup della vecchia MainPage (single-session)
- `MainPage.xaml.cs.backup`: Altro backup per sicurezza

## Prossimi Passi (TODO)

1. **Implementare Rendering HTML**: Parsare messaggi JSON e renderizzarli nel WebView di ogni tab
2. **Context Info per Tab**: Adattare logica Context Info per tab corrente
3. **Terminal per Tab**: Aprire terminale con working directory del tab corrente
4. **Close Tab Button**: Aggiungere bottone X per chiudere tab manualmente
5. **Rename Tab**: Aggiungere dialog per rinominare sessione dal tab
6. **Tab Reordering**: Permettere drag & drop per riordinare tab

## Note Importanti

- **Working Directory**: Ogni sessione ha la propria working directory configurabile
- **Session ID**: Generato da Claude al primo avvio (`system` message in .jsonl)
- **File .jsonl**: Rimangono in `~/.claude/projects/{escaped-path}/{session-id}.jsonl`
- **Backup**: Sempre fare backup prima di migrare database produzione
- **Testing**: Testare prima in ambiente dev/test

## Supporto

Per problemi o domande:
- Verificare logs in `C:\Sources\ClaudeGui\ClaudeCodeMAUI\logs\`
- Controllare console output durante startup
- Verificare che database sia raggiungibile (192.168.1.11:3306)
