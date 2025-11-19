# Guida: Aggiungere System Tray Icon a ClaudeGui.Blazor

## ⚠️ IMPORTANTE: Blazor rimane identico!

Questa modifica **NON cambia Blazor** - cambia solo il "contenitore" dell'applicazione:

### PRIMA (situazione attuale)
```
┌─────────────────────────────┐
│   Console Application       │
│   ├─ Kestrel Web Server     │
│   │  └─ Blazor Server App   │ ← BLAZOR è qui
│   │     ├─ Pages/*.razor    │
│   │     ├─ SignalR Hubs     │
│   │     └─ Services         │
└─────────────────────────────┘
```

### DOPO (con System Tray)
```
┌─────────────────────────────┐
│   Windows Forms App         │ ← Cambia SOLO questo
│   ├─ System Tray Icon       │ ← NUOVO
│   ├─ Kestrel Web Server     │
│   │  └─ Blazor Server App   │ ← BLAZOR IDENTICO!
│   │     ├─ Pages/*.razor    │ ← Nessuna modifica
│   │     ├─ SignalR Hubs     │ ← Nessuna modifica
│   │     └─ Services         │ ← Nessuna modifica
└─────────────────────────────┘
```

---

## Funzionalità Implementate

✅ **Icona custom nel System Tray**
✅ **Menu contestuale completo**:
- Apri Browser
- Stato Server (URL + sessioni attive)
- Azioni rapide (Nuova Sessione, Lista Sessioni)
- Apri Logs
- Esci

✅ **Notifiche balloon** per eventi importanti
✅ **Nessuna dipendenza esterna** (solo .NET 9 Desktop Runtime)

---

## Step 1: Modifica `ClaudeGui.Blazor.csproj`

### Trova:
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
```

### Sostituisci con:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
```

### Aggiungi DOPO la sezione `<PropertyGroup>`:
```xml
  <ItemGroup>
    <!-- Framework reference per ASP.NET Core (necessario dopo cambio SDK) -->
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <!-- Icona System Tray custom -->
    <Content Include="Resources\trayicon.ico">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
```

### Risultato finale:
Il resto del file rimane identico. Assicurati che tutti i `<PackageReference>` esistenti (Pomelo, Serilog, ecc.) rimangano invariati.

---

## Step 2: Creare l'icona custom

### Opzione A: Creare icona da zero
1. Usa uno strumento online come [favicon.io](https://favicon.io/) o [ICO Convert](https://icoconvert.com/)
2. Crea un'icona 256x256px con tema appropriato (es. logo ClaudeGui, terminal icon, ecc.)
3. Salva come `trayicon.ico`

### Opzione B: Usare icona esistente
1. Cerca su [IconArchive](https://www.iconarchive.com/) o [Icons8](https://icons8.com/)
2. Scarica un'icona .ico appropriata
3. Rinominala in `trayicon.ico`

### Posizionamento
1. Crea cartella `ClaudeGui.Blazor\Resources\` se non esiste
2. Copia `trayicon.ico` dentro `Resources\`
3. Verifica che il path sia: `ClaudeGui.Blazor\Resources\trayicon.ico`

---

## Step 3: Creare `Services\ITrayNotificationService.cs`

Crea nuovo file: `ClaudeGui.Blazor\Services\ITrayNotificationService.cs`

```csharp
namespace ClaudeGui.Blazor.Services;

/// <summary>
/// Servizio per inviare notifiche balloon dal System Tray.
/// </summary>
public interface ITrayNotificationService
{
    /// <summary>
    /// Mostra una notifica balloon nel System Tray.
    /// </summary>
    /// <param name="title">Titolo della notifica</param>
    /// <param name="message">Messaggio della notifica</param>
    /// <param name="icon">Tipo di icona (Info, Warning, Error)</param>
    void ShowNotification(string title, string message, NotificationType icon = NotificationType.Info);

    /// <summary>
    /// Aggiorna il conteggio delle sessioni attive visualizzato nel menu.
    /// </summary>
    /// <param name="count">Numero di sessioni attive</param>
    void UpdateSessionCount(int count);
}

/// <summary>
/// Tipo di icona per le notifiche balloon.
/// </summary>
public enum NotificationType
{
    Info,
    Warning,
    Error
}
```

---

## Step 4: Creare `Services\TrayNotificationService.cs`

Crea nuovo file: `ClaudeGui.Blazor\Services\TrayNotificationService.cs`

```csharp
using System.Windows.Forms;

namespace ClaudeGui.Blazor.Services;

/// <summary>
/// Implementazione del servizio di notifiche System Tray.
/// Thread-safe per chiamate da thread diversi (SignalR, background tasks).
/// </summary>
public class TrayNotificationService : ITrayNotificationService
{
    private NotifyIcon? _notifyIcon;
    private int _sessionCount;

    /// <summary>
    /// Registra il NotifyIcon da usare per le notifiche.
    /// Chiamato da TrayApplicationContext all'avvio.
    /// </summary>
    public void RegisterNotifyIcon(NotifyIcon notifyIcon)
    {
        _notifyIcon = notifyIcon;
    }

    /// <summary>
    /// Mostra una notifica balloon nel System Tray.
    /// Thread-safe: può essere chiamato da qualsiasi thread.
    /// </summary>
    public void ShowNotification(string title, string message, NotificationType icon = NotificationType.Info)
    {
        if (_notifyIcon == null) return;

        // Converti NotificationType a ToolTipIcon
        var toolTipIcon = icon switch
        {
            NotificationType.Warning => ToolTipIcon.Warning,
            NotificationType.Error => ToolTipIcon.Error,
            _ => ToolTipIcon.Info
        };

        // Invoke su UI thread se necessario
        if (_notifyIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            _notifyIcon.ContextMenuStrip.Invoke(() =>
            {
                _notifyIcon.ShowBalloonTip(3000, title, message, toolTipIcon);
            });
        }
        else
        {
            _notifyIcon.ShowBalloonTip(3000, title, message, toolTipIcon);
        }
    }

    /// <summary>
    /// Aggiorna il conteggio delle sessioni attive.
    /// </summary>
    public void UpdateSessionCount(int count)
    {
        _sessionCount = count;
    }

    /// <summary>
    /// Ottiene il numero di sessioni attive corrente.
    /// </summary>
    public int GetSessionCount() => _sessionCount;
}
```

---

## Step 5: Creare `TrayApplicationContext.cs`

Crea nuovo file: `ClaudeGui.Blazor\TrayApplicationContext.cs`

```csharp
using System.Diagnostics;
using System.Windows.Forms;
using ClaudeGui.Blazor.Services;

namespace ClaudeGui.Blazor;

/// <summary>
/// Contesto applicazione per la gestione del System Tray icon.
/// Gestisce l'icona, il menu contestuale e le notifiche balloon.
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly string _serverUrl;
    private readonly TrayNotificationService _notificationService;
    private readonly ITerminalManager _terminalManager;

    // Menu items che devono essere aggiornati dinamicamente
    private ToolStripMenuItem _statusMenuItem;
    private ToolStripMenuItem _sessionCountMenuItem;

    public TrayApplicationContext(string serverUrl, TrayNotificationService notificationService, ITerminalManager terminalManager)
    {
        _serverUrl = serverUrl;
        _notificationService = notificationService;
        _terminalManager = terminalManager;

        // Configura tray icon
        _trayIcon = new NotifyIcon
        {
            Icon = new Icon("Resources/trayicon.ico"),
            Text = "ClaudeGui Blazor Server",
            Visible = true,
            ContextMenuStrip = CreateContextMenu()
        };

        _trayIcon.DoubleClick += OnTrayIconDoubleClick;

        // Registra l'icona nel servizio notifiche
        _notificationService.RegisterNotifyIcon(_trayIcon);

        // Timer per aggiornare lo stato periodicamente
        var updateTimer = new System.Windows.Forms.Timer { Interval = 2000 }; // Ogni 2 secondi
        updateTimer.Tick += (s, e) => UpdateStatus();
        updateTimer.Start();
    }

    /// <summary>
    /// Crea il menu contestuale del System Tray icon.
    /// </summary>
    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        // Header: Apri ClaudeGui
        var openItem = new ToolStripMenuItem("🌐 Apri ClaudeGui", null, (s, e) => OpenBrowser())
        {
            Font = new Font(menu.Font, FontStyle.Bold)
        };
        menu.Items.Add(openItem);

        menu.Items.Add(new ToolStripSeparator());

        // Sezione: Stato Server
        var statusLabel = new ToolStripMenuItem("📊 Stato Server")
        {
            Enabled = false,
            Font = new Font(menu.Font, FontStyle.Italic)
        };
        menu.Items.Add(statusLabel);

        _statusMenuItem = new ToolStripMenuItem($"   Server: {_serverUrl}")
        {
            Enabled = false
        };
        menu.Items.Add(_statusMenuItem);

        _sessionCountMenuItem = new ToolStripMenuItem("   Sessioni attive: 0")
        {
            Enabled = false
        };
        menu.Items.Add(_sessionCountMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        // Sezione: Azioni Rapide
        var actionsLabel = new ToolStripMenuItem("⚡ Azioni Rapide")
        {
            Enabled = false,
            Font = new Font(menu.Font, FontStyle.Italic)
        };
        menu.Items.Add(actionsLabel);

        menu.Items.Add("   Nuova Sessione", null, (s, e) => OpenBrowserWithNewSession());
        menu.Items.Add("   Lista Sessioni", null, (s, e) => OpenBrowserSessions());

        menu.Items.Add(new ToolStripSeparator());

        // Utility
        menu.Items.Add("📂 Apri Logs", null, (s, e) => OpenLogsFolder());

        menu.Items.Add(new ToolStripSeparator());

        // Esci
        menu.Items.Add("❌ Esci", null, (s, e) => ExitApplication());

        return menu;
    }

    /// <summary>
    /// Aggiorna lo stato del server visualizzato nel menu.
    /// Chiamato periodicamente dal timer.
    /// </summary>
    private void UpdateStatus()
    {
        try
        {
            var sessionCount = _terminalManager.ActiveSessionCount;
            _sessionCountMenuItem.Text = $"   Sessioni attive: {sessionCount}";
            _notificationService.UpdateSessionCount(sessionCount);
        }
        catch (Exception ex)
        {
            // Ignora errori durante l'aggiornamento status
            Debug.WriteLine($"Error updating tray status: {ex.Message}");
        }
    }

    /// <summary>
    /// Gestisce il doppio click sull'icona: apre il browser.
    /// </summary>
    private void OnTrayIconDoubleClick(object? sender, EventArgs e)
    {
        OpenBrowser();
    }

    /// <summary>
    /// Apre il browser all'URL del server.
    /// </summary>
    private void OpenBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_serverUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Impossibile aprire il browser: {ex.Message}",
                "Errore",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Apre il browser per creare una nuova sessione.
    /// </summary>
    private void OpenBrowserWithNewSession()
    {
        try
        {
            // Assumendo che la pagina principale permetta di creare nuove sessioni
            Process.Start(new ProcessStartInfo($"{_serverUrl}/?new=true")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Impossibile aprire il browser: {ex.Message}",
                "Errore",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Apre il browser alla pagina di lista sessioni.
    /// </summary>
    private void OpenBrowserSessions()
    {
        try
        {
            // TODO: Adattare all'URL corretto per la lista sessioni
            Process.Start(new ProcessStartInfo($"{_serverUrl}/sessions")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Impossibile aprire il browser: {ex.Message}",
                "Errore",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Apre la cartella dei logs in Windows Explorer.
    /// </summary>
    private void OpenLogsFolder()
    {
        try
        {
            var logsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

            // Crea la cartella se non esiste
            if (!Directory.Exists(logsPath))
            {
                Directory.CreateDirectory(logsPath);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = logsPath,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Impossibile aprire la cartella logs: {ex.Message}",
                "Errore",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Chiude l'applicazione in modo graceful.
    /// </summary>
    private void ExitApplication()
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }

    /// <summary>
    /// Cleanup risorse.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
```

---

## Step 6: Modificare `Program.cs`

### Trova queste righe alla fine del file:
```csharp
Log.Information("ClaudeGui Blazor Server starting on http://localhost:5000");

app.Run();
```

### Sostituiscile con:
```csharp
// Configurazione Windows Forms + System Tray
const string serverUrl = "http://localhost:5000";
Log.Information("ClaudeGui Blazor Server starting on {Url}", serverUrl);

// Configura Windows Forms
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

// Registra servizio notifiche tray
var trayNotificationService = new TrayNotificationService();
builder.Services.AddSingleton<ITrayNotificationService>(trayNotificationService);

// Ottieni ITerminalManager per passarlo al TrayApplicationContext
using var scope = app.Services.CreateScope();
var terminalManager = scope.ServiceProvider.GetRequiredService<ITerminalManager>();

// Avvia Kestrel in background (non blocca il thread)
var kestrelTask = app.RunAsync(serverUrl);

// Mostra notifica di avvio
trayNotificationService.ShowNotification(
    "ClaudeGui Avviato",
    $"Server in ascolto su {serverUrl}",
    NotificationType.Info);

// Avvia l'applicazione Windows Forms con il tray icon
Application.Run(new TrayApplicationContext(serverUrl, trayNotificationService, terminalManager));

// Quando l'utente esce dal tray, ferma Kestrel gracefully
Log.Information("Stopping Kestrel server...");
await app.StopAsync();
await kestrelTask;

Log.Information("ClaudeGui Blazor Server stopped");
```

### Aggiungi using all'inizio del file (se non presenti):
```csharp
using System.Windows.Forms;
using ClaudeGui.Blazor.Services;
```

---

## Step 7: Modificare `Services\TerminalManager.cs`

### Trova il costruttore:
```csharp
public TerminalManager(IHubContext<ClaudeHub> hubContext, IServiceScopeFactory serviceScopeFactory)
{
    _hubContext = hubContext;
    _serviceScopeFactory = serviceScopeFactory;
}
```

### Sostituisci con:
```csharp
private readonly ITrayNotificationService? _trayNotificationService;

public TerminalManager(
    IHubContext<ClaudeHub> hubContext,
    IServiceScopeFactory serviceScopeFactory,
    ITrayNotificationService? trayNotificationService = null)
{
    _hubContext = hubContext;
    _serviceScopeFactory = serviceScopeFactory;
    _trayNotificationService = trayNotificationService;
}
```

### Nel metodo `CreateSession`, dopo la linea `processManager.Start();`, aggiungi:
```csharp
// Notifica tray icon
_trayNotificationService?.ShowNotification(
    "Nuova Sessione",
    $"Sessione creata in: {workingDirectory}",
    NotificationType.Info);
```

### Nel metodo `RegisterEventHandlers`, nel catch del handler `SessionIdDetected`, dopo la linea del log error, aggiungi:
```csharp
// Notifica errore critico via tray
_trayNotificationService?.ShowNotification(
    "Errore Critico",
    "Impossibile salvare la sessione nel database",
    NotificationType.Error);
```

### Nel metodo `KillSession`, dopo la linea `manager.Dispose();`, aggiungi:
```csharp
// Notifica chiusura sessione
_trayNotificationService?.ShowNotification(
    "Sessione Chiusa",
    $"Sessione {sessionId} terminata",
    NotificationType.Info);
```

### Aggiungi using all'inizio del file:
```csharp
using ClaudeGui.Blazor.Services;
```

---

## Step 8: Compilazione e Test

### 1. Compila il progetto:
```bash
dotnet build ClaudeGui.Blazor\ClaudeGui.Blazor.csproj
```

### 2. Se ottieni errori:
- Verifica che tutti i file siano stati creati correttamente
- Controlla che `Resources\trayicon.ico` esista
- Verifica che tutti i `using` siano stati aggiunti

### 3. Esegui l'applicazione:
```bash
dotnet run --project ClaudeGui.Blazor\ClaudeGui.Blazor.csproj
```

### 4. Verifica:
- ✅ Dovresti vedere l'icona nel System Tray (area notifiche, angolo in basso a destra)
- ✅ Dovresti vedere una notifica balloon "ClaudeGui Avviato"
- ✅ Right-click sull'icona → Menu contestuale completo
- ✅ Double-click sull'icona → Si apre il browser
- ✅ "Stato Server" → Mostra URL e numero sessioni attive (si aggiorna automaticamente)
- ✅ "Apri Logs" → Si apre la cartella logs
- ✅ "Esci" → L'applicazione si chiude gracefully

---

## Troubleshooting

### Problema: Icona non si vede nel System Tray
**Soluzione**:
- Verifica che il file `Resources\trayicon.ico` esista
- Verifica che il path nel codice sia corretto: `new Icon("Resources/trayicon.ico")`
- Prova a usare icona di sistema temporaneamente: `Icon = SystemIcons.Application`

### Problema: Errore "Cannot find Microsoft.AspNetCore.App"
**Soluzione**:
- Verifica che `<FrameworkReference Include="Microsoft.AspNetCore.App" />` sia presente nel .csproj
- Reinstalla .NET 9.0 SDK se necessario

### Problema: Menu contestuale non si aggiorna
**Soluzione**:
- Verifica che il timer in `TrayApplicationContext` sia avviato
- Controlla i log per errori nel metodo `UpdateStatus()`

### Problema: Notifiche balloon non appaiono
**Soluzione**:
- Verifica le impostazioni di Windows (Impostazioni → Sistema → Notifiche)
- Controlla che le notifiche siano abilitate per l'applicazione
- Alcune versioni di Windows hanno notifiche disabilitate di default

### Problema: Applicazione non si chiude correttamente
**Soluzione**:
- Verifica che `app.StopAsync()` venga chiamato
- Aggiungi logging per tracciare il shutdown
- Usa Task Manager per verificare che il processo termini

---

## Possibili Miglioramenti Futuri

### 1. Icona animata per indicare attività
```csharp
// Cambia icona quando ci sono sessioni attive
if (_terminalManager.ActiveSessionCount > 0)
{
    _trayIcon.Icon = new Icon("Resources/trayicon-active.ico");
}
else
{
    _trayIcon.Icon = new Icon("Resources/trayicon.ico");
}
```

### 2. Avvio automatico con Windows
```csharp
// Aggiungi chiave registro in fase di installazione
using Microsoft.Win32;

var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
key?.SetValue("ClaudeGui", Application.ExecutablePath);
```

### 3. Tooltip dinamico con info sessioni
```csharp
_trayIcon.Text = $"ClaudeGui - {sessionCount} sessioni attive";
```

### 4. Context menu con lista sessioni dinamica
```csharp
// Aggiungi menu items per ogni sessione attiva
foreach (var sessionId in _terminalManager.GetActiveSessions())
{
    menu.Items.Add($"📌 {sessionId}", null, (s, e) => OpenSession(sessionId));
}
```

### 5. Configurazione porta tramite settings
```csharp
// Leggi porta da appsettings.json
var port = builder.Configuration.GetValue<int>("ServerPort", 5000);
var serverUrl = $"http://localhost:{port}";
```

---

## Note Finali

### Architettura
Questa implementazione mantiene la **separazione delle responsabilità**:
- **TrayApplicationContext**: Gestisce solo UI (icona, menu)
- **TrayNotificationService**: Servizio singleton per notifiche cross-thread
- **TerminalManager**: Business logic (invariato, solo aggiunta notifiche)
- **Blazor Server**: Completamente invariato

### Performance
- Il timer di aggiornamento stato (2 secondi) è leggero
- Le notifiche balloon non bloccano il thread principale
- Il menu contestuale si genera on-demand (no overhead)

### Deployment
- L'applicazione richiede **.NET 9.0 Desktop Runtime** (non solo ASP.NET Core Runtime)
- Distribuisci con `dotnet publish -c Release -r win-x64 --self-contained`
- Includi `Resources\trayicon.ico` nella distribuzione

### Compatibilità
- ✅ Windows 10/11
- ❌ Linux/macOS (Windows Forms non supportato)
- Se serve cross-platform, valuta Avalonia UI o Electron wrapper

---

## Checklist Implementazione

Prima di iniziare:
- [ ] Backup del progetto corrente
- [ ] Commit Git dello stato attuale

Durante l'implementazione:
- [ ] Step 1: Modificato `.csproj`
- [ ] Step 2: Creata icona custom in `Resources\trayicon.ico`
- [ ] Step 3: Creato `ITrayNotificationService.cs`
- [ ] Step 4: Creato `TrayNotificationService.cs`
- [ ] Step 5: Creato `TrayApplicationContext.cs`
- [ ] Step 6: Modificato `Program.cs`
- [ ] Step 7: Modificato `TerminalManager.cs`
- [ ] Step 8: Compilato con successo
- [ ] Step 8: Testato tutte le funzionalità

Dopo l'implementazione:
- [ ] Commit delle modifiche
- [ ] Testing estensivo (avvio, sessioni, notifiche, chiusura)
- [ ] Aggiornamento documentazione progetto

---

**Tempo stimato totale**: 2-3 ore
**Difficoltà**: Media
**Impatto su codice esistente**: Minimo (solo TerminalManager tocca logica business)

Buon lavoro! 🚀
