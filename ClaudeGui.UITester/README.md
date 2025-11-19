# ClaudeGui UI Screenshot Tool

Tool per catturare screenshot automatici dell'applicazione ClaudeGui usando Playwright.

## Requisiti

- .NET 9.0+
- ClaudeGui.Blazor in esecuzione (default: `https://localhost:7140`)

## Installazione

Il progetto è già configurato con Microsoft.Playwright. Al primo utilizzo, i browser necessari verranno scaricati automaticamente.

## Utilizzo

### 1. Avvia l'applicazione ClaudeGui

```bash
cd C:\Sources\ClaudeGui\ClaudeGui.Blazor
dotnet run
```

Assicurati che l'applicazione sia accessibile su `https://localhost:7140` (o annota la porta utilizzata).

### 2. Esegui lo screenshot tool

```bash
cd C:\Sources\ClaudeGui\ClaudeGui.UITester
dotnet run
```

### Con URL personalizzato

```bash
dotnet run https://localhost:5001
```

## Output

Gli screenshot vengono salvati in `ClaudeGui.UITester/Screenshots/` con timestamp:

- `fullpage_{timestamp}.png` - Pagina intera
- `header_{timestamp}.png` - Sezione header
- `session-view_{timestamp}.png` - Layout 3-panel (Claude | Editor | PowerShell)
- `markdown-toolbar_{timestamp}.png` - Toolbar del Markdown Editor
- `editor-panel_{timestamp}.png` - Pannello editor completo

## Esempio Output

```
===========================================
  ClaudeGui UI Screenshot Tool
===========================================
Base URL: https://localhost:7140
Screenshot Dir: C:\Sources\ClaudeGui\ClaudeGui.UITester\Screenshots

[1/5] Installazione browser Playwright...
✓ Browser Chromium pronto

[2/5] Avvio Playwright...
[3/5] Lancio browser Chromium headless...
[4/5] Navigazione a https://localhost:7140...
✓ Pagina caricata

[5/5] Cattura screenshot...

  ✓ Pagina intera: fullpage_20251119_153045.png
  ✓ Header: header_20251119_153045.png
  ✓ Session View (3-panel): session-view_20251119_153045.png
  ✓ Markdown Toolbar: markdown-toolbar_20251119_153045.png
  ✓ Editor Panel: editor-panel_20251119_153045.png

===========================================
✓ Screenshot completati!
  Directory: C:\Sources\ClaudeGui\ClaudeGui.UITester\Screenshots
===========================================
```

## Note

- **Primo avvio**: Il download di Chromium (~150MB) può richiedere alcuni minuti
- **HTTPS**: Il tool accetta automaticamente certificati self-signed per localhost
- **Sessioni**: Per catturare session-view e markdown-toolbar, apri una sessione Claude prima di eseguire il tool
- **Dimensione viewport**: 1920x1080 (configurabile in `Program.cs`)

## Troubleshooting

### Errore: "Impossibile connettersi"
- Verifica che ClaudeGui.Blazor sia in esecuzione
- Controlla che la porta sia corretta (`netstat -an | findstr 7140`)

### Errore: "Playwright not found"
- Esegui: `dotnet build` nel progetto UITester
- Il build scaricherà automaticamente i binari necessari

### Screenshot vuoti/neri
- Aumenta il delay dopo `WaitForLoadStateAsync` (linea 69)
- Blazor potrebbe richiedere più tempo per il rendering iniziale

## Integrazione nel Workflow

Usa questo tool per:
1. **Verificare modifiche CSS** - Confronta screenshot prima/dopo
2. **Validare UI** - Controlla che layout sia corretto dopo refactoring
3. **Documentazione** - Screenshot per README o issue tracking
4. **Testing visivo** - Baseline per test di regressione visiva

## Estensioni Future

- [ ] Screenshot comparativi automatici (diff)
- [ ] Testing su Firefox e WebKit
- [ ] Screenshot responsive (mobile/tablet)
- [ ] Integrazione CI/CD
- [ ] Video recording delle interazioni
