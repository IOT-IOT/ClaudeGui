# Piano Dettagliato: Migrazione da .NET MAUI a Blazor Server

**Data**: 2025-11-12
**Autore**: Claude Code
**Versione**: 1.0
**Progetto**: ClaudeGui - Terminal-Centric Application con xterm.js

---

## 📋 Indice

1. [Overview Architettura](#1-overview-architettura)
2. [Setup Nuovo Progetto](#2-setup-nuovo-progetto)
3. [Code Reuse Strategy](#3-code-reuse-strategy)
4. [Implementazione Step-by-Step](#4-implementazione-step-by-step)
5. [Integrazione xterm.js + SignalR](#5-integrazione-xtermjs--signalr)
6. [Database Configuration](#6-database-configuration)
7. [Tray Icon & Auto-launch](#7-tray-icon--auto-launch)
8. [Deployment](#8-deployment)
9. [Testing Plan](#9-testing-plan)
10. [Migration Path](#10-migration-path)

---

## 1. Overview Architettura

### 1.1 Stack Tecnologico

| Componente | Tecnologia | Versione |
|------------|-----------|----------|
| Framework | ASP.NET Core Blazor Server | 9.0 |
| Database | MariaDB (remoto) | 11.x |
| ORM | Entity Framework Core + Pomelo.EntityFrameworkCore.MySql | 9.0 |
| Real-time | SignalR | Built-in ASP.NET Core |
| Terminal | xterm.js | 5.3.0 |
| Terminal Addons | xterm-addon-fit, xterm-addon-web-links | Latest |
| Process Management | System.Diagnostics.Process | Built-in .NET |
| Logging | Serilog | 4.x |

### 1.2 Diagramma Architettura

```
┌─────────────────────────────────────────────────────────────────┐
│  CLIENT SIDE - Browser (Edge/Chrome)                            │
│  URL: http://localhost:5000                                     │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Blazor UI (Razor Components)                           │   │
│  │  ┌──────────────────┐  ┌────────────────────────────┐  │   │
│  │  │ SessionSelector  │  │  MainLayout                │  │   │
│  │  │ Component        │  │  ┌──────────────────────┐  │  │   │
│  │  └──────────────────┘  │  │ Session Tabs         │  │  │   │
│  │                        │  └──────────────────────┘  │  │   │
│  │  ┌────────────────────────────────────────────────┐  │  │   │
│  │  │ Terminal.razor Component                       │  │  │   │
│  │  │  - xterm.js instance                           │  │  │   │
│  │  │  - terminal.js (JavaScript interop)            │  │  │   │
│  │  └────────────────────────────────────────────────┘  │  │   │
│  └─────────────────────────────────────────────────────────┘   │
│                           ↕ SignalR WebSocket                   │
└─────────────────────────────────────────────────────────────────┘
                                    ↕
┌─────────────────────────────────────────────────────────────────┐
│  SERVER SIDE - ASP.NET Core Backend (localhost:5000)           │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  SignalR Hub (Hubs/ClaudeHub.cs)                        │   │
│  │  - ReceiveInput(sessionId, input)                       │   │
│  │  - SendOutput(sessionId, output) → Clients             │   │
│  │  - CreateSession(workingDir)                            │   │
│  │  - KillSession(sessionId)                               │   │
│  └─────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  Services (Riutilizzati da MAUI) ♻️                     │   │
│  │  ┌─────────────────────────────────────────────────┐   │   │
│  │  │ ClaudeProcessManager.cs                         │   │   │
│  │  │  - Start(sessionId?, workingDir)                │   │   │
│  │  │  - SendMessageAsync(input)                      │   │   │
│  │  │  - Event: JsonLineReceived                      │   │   │
│  │  │  - Event: ProcessExited                         │   │   │
│  │  └─────────────────────────────────────────────────┘   │   │
│  │  ┌─────────────────────────────────────────────────┐   │   │
│  │  │ DbService.cs                                    │   │   │
│  │  │  - GetSessionsAsync()                           │   │   │
│  │  │  - SaveMessageStandaloneAsync()                 │   │   │
│  │  │  - InsertOrUpdateSessionAsync()                 │   │   │
│  │  └─────────────────────────────────────────────────┘   │   │
│  │  ┌─────────────────────────────────────────────────┐   │   │
│  │  │ SessionScannerService.cs                        │   │   │
│  │  │  - SyncFilesystemWithDatabaseAsync()            │   │   │
│  │  └─────────────────────────────────────────────────┘   │   │
│  │  ┌─────────────────────────────────────────────────┐   │   │
│  │  │ TerminalManager.cs (NUOVO)                      │   │   │
│  │  │  - Dictionary<sessionId, ProcessManager>        │   │   │
│  │  │  - CreateSession() → sessionId                  │   │   │
│  │  │  - GetSession(sessionId) → ProcessManager      │   │   │
│  │  │  - KillSession(sessionId)                       │   │   │
│  │  └─────────────────────────────────────────────────┘   │   │
│  └─────────────────────────────────────────────────────────┘   │
│                           ↕ MySqlConnection                     │
└─────────────────────────────────────────────────────────────────┘
                                    ↕
┌─────────────────────────────────────────────────────────────────┐
│  DATABASE - MariaDB (192.168.1.11:3306)                        │
│  Database: ClaudeGui                                            │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ Tables (stesso schema MAUI):                            │   │
│  │  - sessions                                             │   │
│  │  - messages_from_stdout                                 │   │
│  │  - messages_from_jsonl                                  │   │
│  │  - summaries                                            │   │
│  │  - file_history_snapshots                               │   │
│  │  - queue_operations                                     │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### 1.3 Confronto MAUI vs Blazor Server

| Aspetto | MAUI Attuale | Blazor Server Nuovo |
|---------|--------------|---------------------|
| **UI Rendering** | Native (WinUI3/Avalonia) | Browser (Chromium/Edge) |
| **Terminal** | WebView (limitato) | xterm.js (nativo) |
| **Real-time Comm** | Event handlers | SignalR WebSocket |
| **Code Reuse** | N/A | 70% Services/Models |
| **Deployment** | MSIX/Installer | Self-contained .exe |
| **Startup** | Double-click .exe | Tray icon → browser |
| **Updates** | Manual | Potenziale auto-update |
| **Multi-user** | No | Futuro: Sì (web app) |

---

## 2. Setup Nuovo Progetto

### 2.1 Prerequisiti

- .NET 9 SDK installato
- MariaDB accessibile @ 192.168.1.11:3306
- VS Code o Visual Studio 2022

### 2.2 Creazione Progetto

```bash
# Naviga alla directory del repository
cd C:\sources\claudegui

# Crea nuovo progetto Blazor Server
dotnet new blazorserver -n ClaudeGui.Blazor -o ClaudeGui.Blazor

# Naviga nel nuovo progetto
cd ClaudeGui.Blazor
```

### 2.3 Struttura Cartelle Proposta

```
ClaudeGui.Blazor/
├── wwwroot/
│   ├── css/
│   │   └── terminal.css               # Stili per xterm.js
│   ├── js/
│   │   ├── terminal.js                # Interop xterm.js
│   │   └── xterm/                     # Librerie xterm.js (CDN o locale)
│   │       ├── xterm.js
│   │       ├── xterm.css
│   │       ├── xterm-addon-fit.js
│   │       └── xterm-addon-web-links.js
│   └── favicon.ico
├── Components/
│   ├── Layout/
│   │   ├── MainLayout.razor           # Layout principale con sidebar
│   │   └── NavMenu.razor              # Menu navigazione (opzionale)
│   ├── Pages/
│   │   ├── Index.razor                # Home page con session selector
│   │   ├── Terminal.razor             # Componente terminal principale
│   │   └── Settings.razor             # Pagina settings
│   └── Shared/
│       ├── SessionTab.razor           # Singolo tab sessione
│       └── SessionSelector.razor      # Dialog selezione sessione
├── Hubs/
│   └── ClaudeHub.cs                   # SignalR hub per terminal I/O
├── Services/                          # ♻️ COPIATI DA MAUI
│   ├── ClaudeProcessManager.cs
│   ├── DbService.cs
│   ├── SessionScannerService.cs
│   ├── SettingsService.cs
│   └── TerminalManager.cs             # NUOVO: gestione sessioni attive
├── Models/                            # ♻️ COPIATI DA MAUI
│   └── Entities/
│       ├── Session.cs
│       ├── Message.cs
│       ├── Summary.cs
│       ├── FileHistorySnapshot.cs
│       └── QueueOperation.cs
├── Data/                              # ♻️ COPIATO DA MAUI
│   └── ClaudeGuiDbContext.cs
├── Utilities/                         # ♻️ OPZIONALI DA MAUI
│   ├── MarkdownHtmlRenderer.cs        # Se necessario per future features
│   └── MessageContentExtractor.cs
├── Program.cs                         # Entry point + DI configuration
├── appsettings.json                   # Config (connection string, paths)
└── ClaudeGui.Blazor.csproj
```

### 2.4 NuGet Packages Necessari

```bash
# Naviga nel progetto
cd ClaudeGui.Blazor

# Database (stesso stack MAUI)
dotnet add package Pomelo.EntityFrameworkCore.MySql --version 9.0.0-preview.1
dotnet add package Microsoft.EntityFrameworkCore.Design --version 9.0.0

# Logging (stesso stack MAUI)
dotnet add package Serilog.AspNetCore --version 8.0.0
dotnet add package Serilog.Sinks.Console --version 6.0.0
dotnet add package Serilog.Sinks.File --version 6.0.0

# Tray Icon (Windows-specific)
dotnet add package Hardcodet.NotifyIcon.Wpf --version 1.1.0
# O alternativa cross-platform:
# dotnet add package H.NotifyIcon --version 2.0.0

# Opzionale: Auto-launch browser
dotnet add package Microsoft.AspNetCore.Server.Kestrel --version 9.0.0
```

### 2.5 Configurazione appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.AspNetCore.SignalR": "Debug",
      "Microsoft.AspNetCore.Http.Connections": "Debug"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "ClaudeGuiDb": "Server=192.168.1.11;Port=3306;Database=ClaudeGui;User=claudegui;Password=YOUR_PASSWORD;AllowUserVariables=true;UseAffectedRows=false"
  },
  "ClaudeSettings": {
    "ClaudeExecutablePath": "C:\\Users\\enric\\AppData\\Local\\Programs\\claude\\claude.exe",
    "DefaultWorkingDirectory": "C:\\Sources\\ClaudeGui",
    "ProjectsDirectory": "C:\\Users\\enric\\.claude\\projects"
  },
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5000"
      }
    }
  }
}
```

---

## 3. Code Reuse Strategy

### 3.1 File da Copiare ESATTAMENTE da ClaudeCodeMAUI

**Services (100% riutilizzabili):**
```bash
# Da ClaudeCodeMAUI/Services/ → ClaudeGui.Blazor/Services/
cp ClaudeCodeMAUI/Services/ClaudeProcessManager.cs ClaudeGui.Blazor/Services/
cp ClaudeCodeMAUI/Services/DbService.cs ClaudeGui.Blazor/Services/
cp ClaudeCodeMAUI/Services/SessionScannerService.cs ClaudeGui.Blazor/Services/
cp ClaudeCodeMAUI/Services/SettingsService.cs ClaudeGui.Blazor/Services/
```

**Models/Entities (100% riutilizzabili):**
```bash
# Da ClaudeCodeMAUI/Models/Entities/ → ClaudeGui.Blazor/Models/Entities/
mkdir ClaudeGui.Blazor/Models/Entities
cp ClaudeCodeMAUI/Models/Entities/*.cs ClaudeGui.Blazor/Models/Entities/
```

**Data/DbContext (100% riutilizzabile):**
```bash
# Da ClaudeCodeMAUI/Data/ → ClaudeGui.Blazor/Data/
mkdir ClaudeGui.Blazor/Data
cp ClaudeCodeMAUI/Data/ClaudeGuiDbContext.cs ClaudeGui.Blazor/Data/
```

**Utilities (opzionali):**
```bash
# Se necessario per future features
mkdir ClaudeGui.Blazor/Utilities
cp ClaudeCodeMAUI/Utilities/MessageContentExtractor.cs ClaudeGui.Blazor/Utilities/
```

### 3.2 File NUOVI da Creare

| File | Scopo | Riferimento MAUI |
|------|-------|------------------|
| `Services/TerminalManager.cs` | Gestione dizionario sessioni attive | Simile a gestione `_currentTab` in MainPage |
| `Hubs/ClaudeHub.cs` | SignalR hub per I/O terminal | Equivalente a `OnJsonLineReceived` + `OnSendMessage` |
| `Components/Pages/Terminal.razor` | UI terminal con xterm.js | Equivalente a SessionTabContent.xaml |
| `wwwroot/js/terminal.js` | JavaScript interop xterm.js | Nuovo (non esistente in MAUI) |

### 3.3 Modifiche Minime ai File Copiati

**IMPORTANTE**: I Services copiati **NON richiedono modifiche** se:
- Rimuovi dipendenze da `Microsoft.Maui.*` (se presenti)
- Logging usa Serilog (già presente in MAUI)

**Possibili namespace da aggiornare:**
```csharp
// Prima (MAUI):
using ClaudeCodeMAUI.Models.Entities;
using ClaudeCodeMAUI.Data;

// Dopo (Blazor):
using ClaudeGui.Blazor.Models.Entities;
using ClaudeGui.Blazor.Data;
```

**Tool di automazione:**
```bash
# PowerShell: Replace namespace in tutti i file copiati
cd ClaudeGui.Blazor
Get-ChildItem -Recurse -Filter *.cs | ForEach-Object {
    (Get-Content $_.FullName) -replace 'ClaudeCodeMAUI', 'ClaudeGui.Blazor' | Set-Content $_.FullName
}
```

---

## 4. Implementazione Step-by-Step

### **Strategia Implementativa Micro-Step**

**Decisioni Architetturali:**
- **Code Sharing**: Copia indipendente da ClaudeCodeMAUI → ClaudeGui.Blazor (namespace separato)
- **Testing**: Unit test + Integration test per ogni step (xUnit + Moq + FluentAssertions)
- **Database Test**: ClaudeGui (produzione) con rollback transazioni automatico dopo ogni test
- **Approval Flow**: Micro-step (implementazione 1-2 file → test xUnit → verifica → approvazione manuale)

**Struttura Ogni Micro-Step:**
1. Implementazione (1-2 file massimo)
2. Test xUnit (unit + integration se applicabile)
3. Verifica automatica (`dotnet test`)
4. Pausa per approvazione utente

---

### **MICRO-STEPS DETTAGLIATI**

#### **STEP 1: Project Setup + Test Infrastructure**
- [ ] Creazione progetto `ClaudeGui.Blazor` (Blazor Server template)
- [ ] Creazione progetto `ClaudeGui.Blazor.Tests` (xUnit template)
- [ ] NuGet packages: Pomelo, Serilog, xUnit, Moq, FluentAssertions
- [ ] `appsettings.json` configurazione base (connection string MariaDB)
- [ ] `DatabaseFixture.cs` (helper per transaction rollback nei test)
- **Test**: Verifica connessione DB + rollback mechanism

---

#### **STEP 2: Models/Entities Copy + Adapt**
- [ ] Copia `Models/Entities/*.cs` da ClaudeCodeMAUI
- [ ] Fix namespace: `ClaudeCodeMAUI` → `ClaudeGui.Blazor`
- **Test**: Deserializzazione JSON → Message entity, validazione attributi Required

---

#### **STEP 3: ClaudeGuiDbContext Copy + Adapt**
- [ ] Copia `Data/ClaudeGuiDbContext.cs`
- [ ] Fix namespace + configurazione DbContextFactory
- **Test**: DbContext creation, query sessioni esistenti (con rollback)

---

#### **STEP 4: ClaudeProcessManager Copy + Adapt**
- [ ] Copia `Services/ClaudeProcessManager.cs`
- [ ] Rimuovi dipendenze MAUI (se presenti)
- **Test**: Costruttore, properties, event JsonLineReceived (mock)

---

#### **STEP 5: DbService Copy + Adapt**
- [ ] Copia `Services/DbService.cs`
- **Test**: InsertOrUpdateSessionAsync (INSERT + rollback), SaveMessageStandaloneAsync

---

#### **STEP 6: TerminalManager (NEW)**
- [ ] Implementa `Services/TerminalManager.cs` (gestione dizionario sessioni)
- **Test**: CreateSession, GetSession, KillSession, thread-safety ConcurrentDictionary

---

#### **STEP 7: ClaudeHub (SignalR)**
- [ ] Implementa `Hubs/ClaudeHub.cs`
- [ ] Update `Program.cs` (DI configuration: services, SignalR, DbContextFactory)
- **Test**: CreateSession invocation (mock), SendInput forwarding (mock)

---

#### **STEP 8: JavaScript Interop - terminal.js**
- [ ] Crea `wwwroot/js/terminal.js` (xterm.js wrapper)
- [ ] Crea `wwwroot/css/terminal.css`
- [ ] Update `App.razor` con script CDN xterm.js
- **Test**: Test manuale browser (verifica window.initTerminal presente)

---

#### **STEP 9: Terminal.razor Component**
- [ ] Implementa `Components/Pages/Terminal.razor`
- **Test**: Render component (mock IJSRuntime), SignalR connection lifecycle

---

#### **STEP 10: Session Selector - Index.razor**
- [ ] Implementa `Components/Pages/Index.razor`
- **Test**: Caricamento lista sessioni da DB (rollback), navigazione

---

#### **STEP 11: Integration Test End-to-End**
- [ ] Test scenario completo: CreateSession → SendInput → ReceiveOutput
- [ ] Test multiple sessioni simultanee (3 parallele)

---

#### **STEP 12: Tray Icon + Auto-launch**
- [ ] Update `Program.cs` (tray icon + auto-launch browser)
- **Test**: Test manuale (avvio .exe → verifica tray icon + browser)

---

### **WEEK 1: Backend Foundation (DETTAGLIO ORIGINALE)**

#### **Day 1: Project Setup & Code Copy**

**Obiettivi:**
- [ ] Creare progetto Blazor Server
- [ ] Copiare Services, Models, Data da MAUI
- [ ] Installare NuGet packages
- [ ] Configurare appsettings.json

**Comandi:**
```bash
# Setup progetto
cd C:\sources\claudegui
dotnet new blazorserver -n ClaudeGui.Blazor -o ClaudeGui.Blazor
cd ClaudeGui.Blazor

# Copia code
robocopy ..\ClaudeCodeMAUI\Services Services *.cs /S
robocopy ..\ClaudeCodeMAUI\Models Models *.cs /S
robocopy ..\ClaudeCodeMAUI\Data Data *.cs /S

# Install packages
dotnet add package Pomelo.EntityFrameworkCore.MySql --version 9.0.0-preview.1
dotnet add package Microsoft.EntityFrameworkCore.Design --version 9.0.0
dotnet add package Serilog.AspNetCore --version 8.0.0

# Fix namespaces
powershell -Command "Get-ChildItem -Recurse -Filter *.cs | ForEach-Object { (Get-Content $_.FullName) -replace 'ClaudeCodeMAUI', 'ClaudeGui.Blazor' | Set-Content $_.FullName }"

# Test build
dotnet build
```

**Verifiche:**
- ✅ `dotnet build` successo
- ✅ Nessun errore di namespace
- ✅ ClaudeGuiDbContext riconosciuto da EF Core

---

#### **Day 2: Database Connection Test**

**Obiettivi:**
- [x] Configurare connection string MariaDB
- [x] Testare DbContext connectivity
- [x] Implementare Dependency Injection in Program.cs

**Program.cs - DI Configuration:**
```csharp
using ClaudeGui.Blazor.Data;
using ClaudeGui.Blazor.Services;
using ClaudeGui.Blazor.Hubs;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Configura Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/claudegui-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSignalR();

// Database (DbContextFactory per thread safety con SignalR)
var connectionString = builder.Configuration.GetConnectionString("ClaudeGuiDb");
builder.Services.AddDbContextFactory<ClaudeGuiDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
        mysqlOptions => {
            mysqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
        })
);

// Services (singleton per condivisione state tra SignalR connections)
builder.Services.AddSingleton<TerminalManager>();
builder.Services.AddScoped<DbService>();
builder.Services.AddScoped<SessionScannerService>();
builder.Services.AddSingleton<SettingsService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<ClaudeHub>("/claudehub");

// Test DB connection at startup
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ClaudeGuiDbContext>>();
    using var db = await dbFactory.CreateDbContextAsync();
    var canConnect = await db.Database.CanConnectAsync();
    Log.Information("Database connection: {Status}", canConnect ? "✅ OK" : "❌ FAILED");
}

app.Run();
```

**Test Connection Script:**
```bash
# Verifica connessione DB
dotnet run

# Dovresti vedere nei logs:
# [INFO] Database connection: ✅ OK
```

**Verifiche:**
- ✅ Connessione a MariaDB riuscita
- ✅ DbContextFactory configurato correttamente
- ✅ Services registrati in DI container

---

#### **Day 3-4: TerminalManager & ClaudeHub (SignalR)**

**Obiettivi:**
- [x] Implementare `TerminalManager.cs` (gestione sessioni attive)
- [x] Implementare `ClaudeHub.cs` (SignalR hub)
- [x] Collegare ClaudeProcessManager a SignalR

**Services/TerminalManager.cs:**
```csharp
using System.Collections.Concurrent;
using ClaudeGui.Blazor.Models.Entities;
using Serilog;

namespace ClaudeGui.Blazor.Services;

/// <summary>
/// Gestisce il dizionario di sessioni attive e i loro ProcessManager.
/// Singleton shared tra tutte le SignalR connections.
/// </summary>
public class TerminalManager
{
    private readonly ConcurrentDictionary<string, ClaudeProcessManager> _activeSessions = new();
    private readonly ILogger _logger = Log.ForContext<TerminalManager>();

    /// <summary>
    /// Crea una nuova sessione terminal.
    /// </summary>
    /// <param name="workingDirectory">Working directory per il processo Claude</param>
    /// <param name="sessionId">SessionId esistente (null per nuova sessione)</param>
    /// <returns>Temporary session ID per tracking (prima che Claude generi l'UUID reale)</returns>
    public string CreateSession(string workingDirectory, string? sessionId = null)
    {
        var tempSessionId = sessionId ?? Guid.NewGuid().ToString();

        var processManager = new ClaudeProcessManager(
            resumeSessionId: sessionId,
            dbSessionId: sessionId ?? "",
            workingDirectory: workingDirectory
        );

        if (_activeSessions.TryAdd(tempSessionId, processManager))
        {
            _logger.Information("Created session: {SessionId}, WorkingDir: {WorkingDir}", tempSessionId, workingDirectory);
            return tempSessionId;
        }

        throw new InvalidOperationException($"Session {tempSessionId} already exists");
    }

    /// <summary>
    /// Ottiene il ProcessManager per una sessione.
    /// </summary>
    public ClaudeProcessManager? GetSession(string sessionId)
    {
        _activeSessions.TryGetValue(sessionId, out var manager);
        return manager;
    }

    /// <summary>
    /// Avvia il processo Claude per una sessione (lazy start).
    /// </summary>
    public void StartSession(string sessionId)
    {
        var manager = GetSession(sessionId);
        if (manager == null)
        {
            _logger.Warning("Cannot start session {SessionId}: not found", sessionId);
            return;
        }

        if (!manager.IsRunning)
        {
            manager.Start();
            _logger.Information("Started Claude process for session: {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Termina una sessione e rimuove dal dizionario.
    /// </summary>
    public void KillSession(string sessionId)
    {
        if (_activeSessions.TryRemove(sessionId, out var manager))
        {
            manager.Dispose();
            _logger.Information("Killed session: {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Ottiene tutte le sessioni attive.
    /// </summary>
    public IEnumerable<string> GetActiveSessions()
    {
        return _activeSessions.Keys;
    }

    /// <summary>
    /// Conta sessioni attive.
    /// </summary>
    public int ActiveSessionCount => _activeSessions.Count;
}
```

**Hubs/ClaudeHub.cs:**
```csharp
using Microsoft.AspNetCore.SignalR;
using ClaudeGui.Blazor.Services;
using Serilog;

namespace ClaudeGui.Blazor.Hubs;

/// <summary>
/// SignalR Hub per comunicazione bidirezionale terminal ↔ server.
/// </summary>
public class ClaudeHub : Hub
{
    private readonly TerminalManager _terminalManager;
    private readonly ILogger _logger = Log.ForContext<ClaudeHub>();

    public ClaudeHub(TerminalManager terminalManager)
    {
        _terminalManager = terminalManager;
    }

    /// <summary>
    /// Client crea una nuova sessione terminal.
    /// </summary>
    /// <param name="workingDirectory">Working directory per Claude</param>
    /// <param name="existingSessionId">SessionId esistente per resume (opzionale)</param>
    /// <returns>Temporary session ID</returns>
    public async Task<string> CreateSession(string workingDirectory, string? existingSessionId = null)
    {
        _logger.Information("Client {ConnectionId} creating session, WorkingDir: {WorkingDir}", Context.ConnectionId, workingDirectory);

        var sessionId = _terminalManager.CreateSession(workingDirectory, existingSessionId);

        // Associa connection a sessione (per cleanup su disconnect)
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);

        // Setup event handlers per output
        var processManager = _terminalManager.GetSession(sessionId);
        if (processManager != null)
        {
            processManager.JsonLineReceived += (sender, e) =>
            {
                // Invia output a tutti i client in questo session group
                Clients.Group(sessionId).SendAsync("ReceiveOutput", e.JsonLine);
            };
        }

        return sessionId;
    }

    /// <summary>
    /// Client invia input al terminal (es. comando, Ctrl+C, ecc.).
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <param name="input">Input text/data</param>
    public async Task SendInput(string sessionId, string input)
    {
        var processManager = _terminalManager.GetSession(sessionId);
        if (processManager == null)
        {
            _logger.Warning("SendInput failed: session {SessionId} not found", sessionId);
            return;
        }

        // Avvia processo se non ancora avviato (lazy start)
        if (!processManager.IsRunning)
        {
            _terminalManager.StartSession(sessionId);
            await Task.Delay(1000); // Attendi avvio
        }

        // Invia input al processo Claude
        await processManager.SendMessageAsync(input);
        _logger.Debug("Sent input to session {SessionId}: {Input}", sessionId, input.Length > 50 ? input.Substring(0, 50) + "..." : input);
    }

    /// <summary>
    /// Client richiede terminazione sessione.
    /// </summary>
    public async Task KillSession(string sessionId)
    {
        _logger.Information("Client {ConnectionId} killing session {SessionId}", Context.ConnectionId, sessionId);
        _terminalManager.KillSession(sessionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
    }

    /// <summary>
    /// Cleanup quando client disconnette.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.Information("Client {ConnectionId} disconnected", Context.ConnectionId);
        // Opzionale: kill sessioni associate a questo connection
        // (per ora lasciamo attive per supportare reconnect)
        await base.OnDisconnectedAsync(exception);
    }
}
```

**Verifiche:**
- ✅ TerminalManager gestisce dizionario sessioni
- ✅ ClaudeHub espone metodi CreateSession, SendInput, KillSession
- ✅ Event handlers collegati (JsonLineReceived → SignalR clients)

---

#### **Day 5: Test Backend Integration**

**Obiettivi:**
- [x] Creare test console app per testare Hub
- [x] Verificare output Claude → SignalR
- [x] Verificare input SignalR → Claude

**Test/TestClaudeHub.cs (Console App):**
```csharp
using Microsoft.AspNetCore.SignalR.Client;

var connection = new HubConnectionBuilder()
    .WithUrl("http://localhost:5000/claudehub")
    .Build();

// Handler per output da Claude
connection.On<string>("ReceiveOutput", (output) =>
{
    Console.WriteLine($"[CLAUDE OUTPUT] {output}");
});

await connection.StartAsync();
Console.WriteLine("Connected to ClaudeHub");

// Crea sessione
var sessionId = await connection.InvokeAsync<string>("CreateSession", "C:\\Sources\\ClaudeGui", null);
Console.WriteLine($"Session created: {sessionId}");

// Invia test message
await connection.InvokeAsync("SendInput", sessionId, "Hello Claude!");
Console.WriteLine("Input sent, waiting for output...");

Console.ReadLine();
```

**Comandi test:**
```bash
# Terminal 1: Avvia server
cd ClaudeGui.Blazor
dotnet run

# Terminal 2: Test client
dotnet new console -n TestClient
cd TestClient
dotnet add package Microsoft.AspNetCore.SignalR.Client
# (Crea TestClaudeHub.cs sopra)
dotnet run

# Dovresti vedere output Claude nel Terminal 2
```

**Verifiche:**
- ✅ Client connette a Hub
- ✅ CreateSession ritorna sessionId
- ✅ SendInput invia a ClaudeProcessManager
- ✅ Claude output arriva via ReceiveOutput event

---

### **WEEK 2: Frontend (Razor Components + xterm.js)**

#### **Day 1-2: xterm.js Setup & JavaScript Interop**

**Obiettivi:**
- [x] Integrare xterm.js libraries (CDN o local)
- [x] Creare `terminal.js` per interop
- [x] Testare rendering terminal in browser

**wwwroot/js/terminal.js:**
```javascript
// Globale: dizionario terminals per session ID
window.terminals = {};

/**
 * Inizializza un terminal xterm.js per una sessione.
 * @param {string} sessionId - ID sessione
 * @param {string} elementId - ID elemento DOM container
 */
window.initTerminal = function (sessionId, elementId) {
    console.log(`Initializing terminal for session ${sessionId}`);

    // Crea terminal instance
    const terminal = new Terminal({
        cursorBlink: true,
        fontSize: 14,
        fontFamily: 'Cascadia Code, Consolas, Courier New, monospace',
        theme: {
            background: '#1e1e1e',
            foreground: '#d4d4d4',
            cursor: '#ffffff',
            selection: '#264f78',
            black: '#000000',
            red: '#cd3131',
            green: '#0dbc79',
            yellow: '#e5e510',
            blue: '#2472c8',
            magenta: '#bc3fbc',
            cyan: '#11a8cd',
            white: '#e5e5e5',
            brightBlack: '#666666',
            brightRed: '#f14c4c',
            brightGreen: '#23d18b',
            brightYellow: '#f5f543',
            brightBlue: '#3b8eea',
            brightMagenta: '#d670d6',
            brightCyan: '#29b8db',
            brightWhite: '#ffffff'
        },
        allowProposedApi: true
    });

    // Addon: fit (auto-resize)
    const fitAddon = new FitAddon.FitAddon();
    terminal.loadAddon(fitAddon);

    // Addon: web links (clickable URLs)
    const webLinksAddon = new WebLinksAddon.WebLinksAddon();
    terminal.loadAddon(webLinksAddon);

    // Apri terminal nel container
    const container = document.getElementById(elementId);
    terminal.open(container);

    // Fit terminal to container size
    fitAddon.fit();

    // Auto-resize on window resize
    window.addEventListener('resize', () => {
        fitAddon.fit();
    });

    // Salva terminal in dizionario globale
    window.terminals[sessionId] = {
        instance: terminal,
        fitAddon: fitAddon
    };

    console.log(`Terminal initialized for session ${sessionId}`);
    return true;
};

/**
 * Scrive output nel terminal.
 * @param {string} sessionId - ID sessione
 * @param {string} data - Dati da scrivere
 */
window.writeToTerminal = function (sessionId, data) {
    const term = window.terminals[sessionId];
    if (!term) {
        console.error(`Terminal ${sessionId} not found`);
        return;
    }
    term.instance.write(data);
};

/**
 * Pulisce terminal.
 * @param {string} sessionId - ID sessione
 */
window.clearTerminal = function (sessionId) {
    const term = window.terminals[sessionId];
    if (term) {
        term.instance.clear();
    }
};

/**
 * Distrugge terminal instance.
 * @param {string} sessionId - ID sessione
 */
window.disposeTerminal = function (sessionId) {
    const term = window.terminals[sessionId];
    if (term) {
        term.instance.dispose();
        delete window.terminals[sessionId];
        console.log(`Terminal ${sessionId} disposed`);
    }
};

/**
 * Setup input handler per terminal (invia input a SignalR).
 * @param {string} sessionId - ID sessione
 * @param {object} dotNetHelper - DotNetObjectReference per invoke C#
 */
window.setupTerminalInput = function (sessionId, dotNetHelper) {
    const term = window.terminals[sessionId];
    if (!term) {
        console.error(`Terminal ${sessionId} not found`);
        return;
    }

    // Handler quando utente digita/incolla
    term.instance.onData((data) => {
        // Invia input a C# tramite interop
        dotNetHelper.invokeMethodAsync('OnTerminalInput', data);
    });

    console.log(`Input handler setup for session ${sessionId}`);
};
```

**Components/_Layout.cshtml (o App.razor):**
```html
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <link rel="stylesheet" href="css/bootstrap/bootstrap.min.css" />
    <link rel="stylesheet" href="css/app.css" />

    <!-- xterm.js CSS -->
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/xterm@5.3.0/css/xterm.css" />

    <HeadOutlet />
</head>
<body>
    @RenderBody()

    <!-- xterm.js libraries -->
    <script src="https://cdn.jsdelivr.net/npm/xterm@5.3.0/lib/xterm.js"></script>
    <script src="https://cdn.jsdelivr.net/npm/xterm-addon-fit@0.8.0/lib/xterm-addon-fit.js"></script>
    <script src="https://cdn.jsdelivr.net/npm/xterm-addon-web-links@0.9.0/lib/xterm-addon-web-links.js"></script>

    <!-- SignalR Client -->
    <script src="_framework/blazor.server.js"></script>

    <!-- Custom terminal.js -->
    <script src="js/terminal.js"></script>
</body>
</html>
```

**wwwroot/css/terminal.css:**
```css
.terminal-container {
    width: 100%;
    height: 100%;
    background-color: #1e1e1e;
    padding: 10px;
    border-radius: 4px;
}

.terminal-wrapper {
    height: calc(100vh - 120px); /* Full height minus header/footer */
    display: flex;
    flex-direction: column;
}

.xterm {
    height: 100%;
    width: 100%;
}

.xterm-viewport {
    overflow-y: auto;
    background-color: #1e1e1e !important;
}
```

**Verifiche:**
- ✅ xterm.js carica correttamente
- ✅ `window.initTerminal()` crea terminal visibile
- ✅ `window.writeToTerminal()` scrive output
- ✅ Terminal styling corretto (dark theme)

---

#### **Day 3-4: Razor Component Terminal.razor**

**Obiettivi:**
- [x] Creare componente `Terminal.razor`
- [x] Collegare SignalR client
- [x] Interop con JavaScript per I/O

**Components/Pages/Terminal.razor:**
```razor
@page "/terminal/{SessionId}"
@using Microsoft.AspNetCore.SignalR.Client
@using Microsoft.JSInterop
@inject IJSRuntime JS
@inject NavigationManager Navigation
@implements IAsyncDisposable

<PageTitle>Terminal - @SessionId</PageTitle>

<div class="terminal-wrapper">
    <div class="terminal-header">
        <span>Session: @SessionId</span>
        <span>Working Directory: @WorkingDirectory</span>
        <button @onclick="KillSessionAsync" class="btn btn-danger btn-sm">Kill Session</button>
    </div>

    <div id="terminal-@SessionId" class="terminal-container"></div>
</div>

@code {
    [Parameter]
    public string SessionId { get; set; } = "";

    private string WorkingDirectory { get; set; } = "";
    private HubConnection? hubConnection;
    private DotNetObjectReference<Terminal>? dotNetHelper;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await InitializeTerminalAsync();
        }
    }

    private async Task InitializeTerminalAsync()
    {
        try
        {
            // 1. Connetti a SignalR Hub
            hubConnection = new HubConnectionBuilder()
                .WithUrl(Navigation.ToAbsoluteUri("/claudehub"))
                .Build();

            // 2. Handler per output da Claude
            hubConnection.On<string>("ReceiveOutput", async (output) =>
            {
                await JS.InvokeVoidAsync("writeToTerminal", SessionId, output);
            });

            await hubConnection.StartAsync();

            // 3. Inizializza xterm.js terminal
            await JS.InvokeVoidAsync("initTerminal", SessionId, $"terminal-{SessionId}");

            // 4. Setup input handler (terminal → C#)
            dotNetHelper = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("setupTerminalInput", SessionId, dotNetHelper);

            // 5. Crea sessione su server (o riprendi esistente)
            var createdSessionId = await hubConnection.InvokeAsync<string>(
                "CreateSession",
                WorkingDirectory ?? "C:\\Sources\\ClaudeGui",
                SessionId != "new" ? SessionId : null
            );

            // 6. Invia messaggio iniziale per avviare Claude
            await hubConnection.InvokeAsync("SendInput", SessionId, "");

            StateHasChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Terminal initialization failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Chiamato da JavaScript quando utente digita nel terminal.
    /// </summary>
    [JSInvokable]
    public async Task OnTerminalInput(string data)
    {
        if (hubConnection != null)
        {
            await hubConnection.InvokeAsync("SendInput", SessionId, data);
        }
    }

    private async Task KillSessionAsync()
    {
        if (hubConnection != null)
        {
            await hubConnection.InvokeAsync("KillSession", SessionId);
            Navigation.NavigateTo("/");
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Cleanup
        await JS.InvokeVoidAsync("disposeTerminal", SessionId);
        dotNetHelper?.Dispose();

        if (hubConnection is not null)
        {
            await hubConnection.DisposeAsync();
        }
    }
}
```

**Verifiche:**
- ✅ Terminal si apre in browser
- ✅ xterm.js renderizza correttamente
- ✅ SignalR connette a ClaudeHub
- ✅ Input utente → SignalR → Claude
- ✅ Claude output → SignalR → xterm.js

---

#### **Day 5: Session Selector & Navigation**

**Obiettivi:**
- [x] Creare `SessionSelector.razor` (dialog/page)
- [x] Integrare DbService per caricare sessioni esistenti
- [x] Navigazione tra sessioni

**Components/Pages/Index.razor:**
```razor
@page "/"
@using ClaudeGui.Blazor.Services
@using ClaudeGui.Blazor.Models.Entities
@inject DbService DbService
@inject NavigationManager Navigation

<PageTitle>Claude GUI - Session Selector</PageTitle>

<div class="container mt-5">
    <h1>Select or Create Session</h1>

    @if (sessions == null)
    {
        <p><em>Loading sessions...</em></p>
    }
    else
    {
        <div class="session-list">
            @foreach (var session in sessions)
            {
                <div class="session-card" @onclick="() => OpenSession(session.SessionId)">
                    <h5>@(session.Name ?? $"Session {session.SessionId.Substring(0, 8)}...")</h5>
                    <p>@session.WorkingDirectory</p>
                    <small>Last activity: @session.LastActivity</small>
                </div>
            }
        </div>

        <button class="btn btn-primary mt-3" @onclick="CreateNewSession">
            + New Session
        </button>
    }
</div>

@code {
    private List<Session>? sessions;

    protected override async Task OnInitializedAsync()
    {
        sessions = await DbService.GetSessionsAsync();
    }

    private void OpenSession(string sessionId)
    {
        Navigation.NavigateTo($"/terminal/{sessionId}");
    }

    private void CreateNewSession()
    {
        // TODO: Dialog per selezionare working directory
        Navigation.NavigateTo("/terminal/new");
    }
}
```

**Verifiche:**
- ✅ Index page mostra lista sessioni
- ✅ Click su sessione → naviga a `/terminal/{sessionId}`
- ✅ New Session → crea nuova sessione

---

### **WEEK 3: Polish & Deployment**

#### **Day 1-2: Tray Icon + Auto-launch Browser**

**Obiettivi:**
- [x] Implementare Windows tray icon
- [x] Auto-launch browser all'avvio
- [x] Menu tray (Open, Settings, Exit)

**Program.cs - Tray Icon Integration:**
```csharp
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;

// ... existing code ...

var app = builder.Build();

// Tray Icon (solo per Windows)
if (OperatingSystem.IsWindows())
{
    var trayIcon = new TaskbarIcon
    {
        Icon = new System.Drawing.Icon("wwwroot/favicon.ico"),
        ToolTipText = "ClaudeGui Server"
    };

    trayIcon.TrayMouseDoubleClick += (s, e) =>
    {
        // Open browser
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "http://localhost:5000",
            UseShellExecute = true
        });
    };

    // Context menu
    var contextMenu = new System.Windows.Controls.ContextMenu();

    var openItem = new System.Windows.Controls.MenuItem { Header = "Open Browser" };
    openItem.Click += (s, e) =>
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "http://localhost:5000",
            UseShellExecute = true
        });
    };

    var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
    exitItem.Click += (s, e) =>
    {
        trayIcon.Dispose();
        Environment.Exit(0);
    };

    contextMenu.Items.Add(openItem);
    contextMenu.Items.Add(new System.Windows.Controls.Separator());
    contextMenu.Items.Add(exitItem);

    trayIcon.ContextMenu = contextMenu;

    // Auto-launch browser on startup
    Task.Run(async () =>
    {
        await Task.Delay(2000); // Wait for server start
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "http://localhost:5000",
            UseShellExecute = true
        });
    });
}

app.Run();
```

**Verifiche:**
- ✅ Tray icon appare in system tray
- ✅ Double-click → browser si apre
- ✅ Right-click → menu contestuale
- ✅ Auto-launch browser al startup

---

#### **Day 3-4: Testing & Bug Fixing**

**Test Checklist:**
- [ ] Database connection resilience (retry on disconnect)
- [ ] SignalR reconnection dopo network loss
- [ ] Multiple sessions simultanee (tab multipli)
- [ ] Session resume dopo refresh browser
- [ ] Claude process cleanup on session kill
- [ ] Memory leaks (long-running sessions)
- [ ] Performance test (10+ sessions attive)

---

#### **Day 5: Deployment Package**

**Obiettivi:**
- [x] Self-contained publish
- [x] Installer creation (opzionale)
- [x] Documentation

**Publish Command:**
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64
```

**Installer (Inno Setup Script) - claudegui-installer.iss:**
```inno
[Setup]
AppName=ClaudeGui Server
AppVersion=1.0.0
DefaultDirName={autopf}\ClaudeGui
DefaultGroupName=ClaudeGui
OutputDir=installer
OutputBaseFilename=ClaudeGui-Setup
Compression=lzma2
SolidCompression=yes

[Files]
Source: "publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{group}\ClaudeGui Server"; Filename: "{app}\ClaudeGui.Blazor.exe"
Name: "{commonstartup}\ClaudeGui Server"; Filename: "{app}\ClaudeGui.Blazor.exe"

[Run]
Filename: "{app}\ClaudeGui.Blazor.exe"; Description: "Launch ClaudeGui"; Flags: nowait postinstall skipifsilent
```

---

## 5. Integrazione xterm.js + SignalR

**(Già coperto nelle sezioni precedenti - vedi Day 1-4 Week 2)**

---

## 6. Database Configuration

### Connection String

**appsettings.json:**
```json
{
  "ConnectionStrings": {
    "ClaudeGuiDb": "Server=192.168.1.11;Port=3306;Database=ClaudeGui;User=claudegui;Password=YOUR_PASSWORD;AllowUserVariables=true;UseAffectedRows=false"
  }
}
```

### Nessuna Migration Necessaria

Il database schema è **identico** a quello usato da MAUI. EF Core usa le stesse entity definitions:
- `sessions`
- `messages_from_stdout`
- `summaries`
- `file_history_snapshots`
- `queue_operations`

---

## 7. Tray Icon & Auto-launch

**(Già coperto in Week 3, Day 1-2)**

---

## 8. Deployment

**(Già coperto in Week 3, Day 5)**

---

## 9. Testing Plan

### 9.1 Unit Tests

```bash
# Crea progetto test
dotnet new xunit -n ClaudeGui.Blazor.Tests
cd ClaudeGui.Blazor.Tests
dotnet add reference ../ClaudeGui.Blazor/ClaudeGui.Blazor.csproj
dotnet add package Moq
```

**Tests/TerminalManagerTests.cs:**
```csharp
public class TerminalManagerTests
{
    [Fact]
    public void CreateSession_ShouldReturnSessionId()
    {
        var manager = new TerminalManager();
        var sessionId = manager.CreateSession("C:\\Test");
        Assert.NotNull(sessionId);
        Assert.NotEmpty(sessionId);
    }

    [Fact]
    public void GetSession_ShouldReturnProcessManager()
    {
        var manager = new TerminalManager();
        var sessionId = manager.CreateSession("C:\\Test");
        var processManager = manager.GetSession(sessionId);
        Assert.NotNull(processManager);
    }

    [Fact]
    public void KillSession_ShouldRemoveSession()
    {
        var manager = new TerminalManager();
        var sessionId = manager.CreateSession("C:\\Test");
        manager.KillSession(sessionId);
        var processManager = manager.GetSession(sessionId);
        Assert.Null(processManager);
    }
}
```

### 9.2 Integration Tests

**Tests/ClaudeHubIntegrationTests.cs:**
```csharp
// TODO: Implementare test SignalR con TestServer
```

### 9.3 Manual Testing Checklist

- [ ] Terminal apre e mostra prompt
- [ ] Input utente funziona (echo back)
- [ ] Claude output appare in terminal
- [ ] Multiple sessions (10+ tabs)
- [ ] Session resume dopo refresh browser
- [ ] Kill session cleanup processo Claude
- [ ] Tray icon menu funziona
- [ ] Auto-launch browser al startup

---

## 10. Migration Path

### 10.1 Coesistenza con Progetto MAUI

**Strategia:**
- Mantieni `ClaudeCodeMAUI` per eventuali fallback
- `ClaudeGui.Blazor` usa stesso database MariaDB
- Entrambi possono coesistere (diversi processi, stesso DB)

**Attenzione:**
- Non avviare entrambi simultaneamente sulla stessa sessione (conflitto process lock)

### 10.2 Deprecazione Graduale MAUI

**Timeline proposta:**
1. **Settimana 1-3**: Sviluppo Blazor Server (parallelo a MAUI)
2. **Settimana 4**: User testing Blazor (feedback)
3. **Settimana 5-6**: Fix bug Blazor basati su feedback
4. **Settimana 7**: Promozione Blazor a "produzione"
5. **Settimana 8+**: Depreca MAUI se Blazor soddisfa tutti i requisiti

### 10.3 Data Migration

**Non necessaria** - entrambi usano stesso database schema.

---

## 11. Appendice: Comandi Rapidi

### Setup Iniziale
```bash
cd C:\sources\claudegui
dotnet new blazorserver -n ClaudeGui.Blazor -o ClaudeGui.Blazor
cd ClaudeGui.Blazor
dotnet add package Pomelo.EntityFrameworkCore.MySql --version 9.0.0-preview.1
dotnet add package Microsoft.EntityFrameworkCore.Design --version 9.0.0
dotnet add package Serilog.AspNetCore --version 8.0.0
robocopy ..\ClaudeCodeMAUI\Services Services *.cs /S
robocopy ..\ClaudeCodeMAUI\Models Models *.cs /S
robocopy ..\ClaudeCodeMAUI\Data Data *.cs /S
```

### Build & Run
```bash
dotnet build
dotnet run
```

### Publish
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64
```

### Test Connection
```bash
# Test DB
dotnet ef dbcontext scaffold "Server=192.168.1.11;Database=ClaudeGui;User=claudegui;Password=XXX" Pomelo.EntityFrameworkCore.MySql --context-dir Data --output-dir Models/Entities
```

---

## 12. Risorse Utili

- **xterm.js Docs**: https://xtermjs.org/docs/
- **SignalR Docs**: https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction
- **Blazor Server Docs**: https://learn.microsoft.com/en-us/aspnet/core/blazor/
- **EF Core + Pomelo**: https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql

---

**Fine Piano Dettagliato**

**Prossimi Step**: Iniziare con Day 1 (Project Setup)
