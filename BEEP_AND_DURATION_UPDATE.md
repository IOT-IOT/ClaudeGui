# Metadata Display Update - Duration in Secondi + Beep Sonoro

**Data:** 2025-10-30
**Progetto:** ClaudeCodeMAUI
**Feature:** Duration in secondi + Notifica sonora metadata
**Status:** ✅ IMPLEMENTATO

---

## 📋 Riepilogo

Implementati due miglioramenti al sistema di visualizzazione metadata:

1. **Duration in secondi** invece che millisecondi (con 1 cifra decimale)
   - Prima: "Duration: 2500ms"
   - Ora: "Duration: 2.5s"

2. **Beep sonoro** quando arrivano i metadata
   - Frequenza: 800Hz
   - Durata: 200ms
   - Configurabile tramite Settings (default: abilitato)

---

## 🛠️ Modifiche Implementate

### 1. SettingsService - Nuova Opzione

**File:** `ClaudeCodeMAUI/Services/SettingsService.cs`

**Aggiunto:**

```csharp
private const string KEY_PLAY_BEEP_ON_METADATA = "PlayBeepOnMetadata";

/// <summary>
/// Ottiene o imposta se riprodurre un beep sonoro quando arrivano i metadata.
/// Default: true
/// </summary>
public bool PlayBeepOnMetadata
{
    get
    {
        var value = Preferences.Get(KEY_PLAY_BEEP_ON_METADATA, true);
        Log.Debug("SettingsService: PlayBeepOnMetadata = {Value}", value);
        return value;
    }
    set
    {
        Preferences.Set(KEY_PLAY_BEEP_ON_METADATA, value);
        Log.Information("SettingsService: PlayBeepOnMetadata impostato a {Value}", value);
    }
}
```

**Modifiche:**
- Aggiunto campo `KEY_PLAY_BEEP_ON_METADATA`
- Aggiunta property `PlayBeepOnMetadata` (default: `true`)
- Aggiornato `ResetToDefaults()` per includere `PlayBeepOnMetadata = true`
- Aggiornato `GetAllSettings()` per includere il nuovo setting

---

### 2. SettingsPage - UI Toggle

**File:** `ClaudeCodeMAUI/SettingsPage.xaml`

**Aggiunto nella sezione "Session Recovery":**

```xml
<!-- Opzione 2: Play Beep on Metadata -->
<Grid ColumnDefinitions="*,Auto" ColumnSpacing="10">
    <VerticalStackLayout Grid.Column="0" VerticalOptions="Center">
        <Label Text="Play beep on metadata"
               FontSize="16"
               TextColor="{DynamicResource PrimaryTextColor}" />
        <Label Text="Play a sound notification when response statistics are received"
               FontSize="12"
               TextColor="{DynamicResource SecondaryTextColor}"
               Margin="0,2,0,0" />
    </VerticalStackLayout>
    <Switch x:Name="SwitchPlayBeep"
            Grid.Column="1"
            VerticalOptions="Center"
            Toggled="OnPlayBeepToggled" />
</Grid>
```

**File:** `ClaudeCodeMAUI/SettingsPage.xaml.cs`

**Aggiunto:**

```csharp
// In LoadCurrentSettings()
SwitchPlayBeep.IsToggled = _settingsService.PlayBeepOnMetadata;

// Nuovo handler
private void OnPlayBeepToggled(object sender, ToggledEventArgs e)
{
    _settingsService.PlayBeepOnMetadata = e.Value;
    Log.Information("SettingsPage: PlayBeepOnMetadata modificato a {Value}", e.Value);
    _onSettingsChanged?.Invoke();
}
```

---

### 3. MainPage - Duration in Secondi + Beep

**File:** `ClaudeCodeMAUI/MainPage.xaml.cs`

**Metodo modificato:** `OnMetadataReceived()` (riga ~643)

**Cambiamenti:**

#### A. Duration in Secondi

**Prima:**
```csharp
var metadataHtml = $@"
<div class=""metadata-container"" onclick=""this.classList.toggle('collapsed')"">
    <span class=""metadata-content"">Duration: {e.DurationMs}ms  |  Cost: ${e.TotalCostUsd:F4}  |  ...</span>
</div>";
```

**Dopo:**
```csharp
// Converti duration da millisecondi a secondi con 1 cifra decimale
var durationSeconds = e.DurationMs / 1000.0;

var metadataHtml = $@"
<div class=""metadata-container"" onclick=""this.classList.toggle('collapsed')"">
    <span class=""metadata-content"">Duration: {durationSeconds:F1}s  |  Cost: ${e.TotalCostUsd:F4}  |  ...</span>
</div>";
```

**Dettagli:**
- `e.DurationMs / 1000.0` converte millisecondi in secondi (usando `1000.0` invece di `1000` garantisce divisione floating-point)
- `{durationSeconds:F1}` formatta con 1 cifra decimale
- Suffisso cambiato da "ms" a "s"

**Esempi:**
- `1234ms` → `1.2s`
- `5678ms` → `5.7s`
- `500ms` → `0.5s`
- `12345ms` → `12.3s`

#### B. Riproduzione Beep

**Aggiunto dopo la generazione HTML:**

```csharp
// Riproduci beep se abilitato nelle impostazioni
if (_settingsService != null && _settingsService.PlayBeepOnMetadata)
{
    try
    {
        // Usa System.Console.Beep per Windows
        #if WINDOWS
        System.Console.Beep(800, 200); // Frequenza 800Hz, durata 200ms
        Log.Debug("Beep played for metadata received");
        #else
        Log.Warning("Beep not supported on this platform");
        #endif
    }
    catch (Exception beepEx)
    {
        Log.Warning(beepEx, "Failed to play beep sound");
    }
}
```

**Dettagli:**
- Controlla che `_settingsService` sia inizializzato
- Controlla che `PlayBeepOnMetadata` sia `true`
- Su Windows: usa `System.Console.Beep(800, 200)`
  - **Frequenza:** 800Hz (tono medio-alto, piacevole)
  - **Durata:** 200ms (breve, non invasivo)
- Compilazione condizionale `#if WINDOWS` per compatibilità cross-platform
- Gestione errori con try-catch + log warning

**Note Tecniche:**
- `Console.Beep()` usa il PC speaker integrato su Windows
- Non richiede file audio esterni o librerie aggiuntive
- Non funziona in app Windows Store (limitazione UWP), ma funziona in app desktop normali

---

## 📊 Esempio Visivo

### Prima:
```
📊 Duration: 2500ms  |  Cost: $0.0123  |  Tokens: 100 in / 200 out  |  Turns: 5
```

### Dopo:
```
📊 Duration: 2.5s  |  Cost: $0.0123  |  Tokens: 100 in / 200 out  |  Turns: 5
🔊 *BEEP* (se abilitato)
```

---

## 🎛️ Settings UI

Nella pagina Settings, sezione "Session Recovery":

```
┌─────────────────────────────────────────────────┐
│ 🔄 Session Recovery                             │
├─────────────────────────────────────────────────┤
│                                                 │
│ Auto-send summary prompt               [ON]    │
│ Automatically ask Claude for a summary...      │
│                                                 │
│ Play beep on metadata                  [ON]    │  ← NUOVO
│ Play a sound notification when response...     │
│                                                 │
└─────────────────────────────────────────────────┘
```

---

## ✅ Test Checklist

### Test 1: Duration Formattazione
- [ ] Avvia app
- [ ] Invia messaggio a Claude
- [ ] Attendi risposta
- [ ] ✅ Metadata mostrano duration in secondi (es. "2.5s" invece di "2500ms")
- [ ] ✅ 1 cifra decimale visibile

### Test 2: Beep Abilitato (Default)
- [ ] Avvia app (senza modificare settings)
- [ ] Invia messaggio a Claude
- [ ] Attendi risposta
- [ ] ✅ Quando appaiono i metadata, senti un beep breve (800Hz, 200ms)

### Test 3: Disabilitare Beep
- [ ] Apri Settings (⚙️)
- [ ] Toggle "Play beep on metadata" OFF
- [ ] Chiudi Settings
- [ ] Invia messaggio a Claude
- [ ] ✅ Nessun beep quando arrivano i metadata

### Test 4: Riabilitare Beep
- [ ] Apri Settings
- [ ] Toggle "Play beep on metadata" ON
- [ ] Chiudi Settings
- [ ] Invia messaggio a Claude
- [ ] ✅ Beep riappare

### Test 5: Reset to Defaults
- [ ] Disabilita beep
- [ ] Apri Settings
- [ ] Click "Reset to Defaults"
- [ ] ✅ Beep torna abilitato (default: true)

### Test 6: Persistenza Impostazione
- [ ] Disabilita beep
- [ ] Chiudi app completamente
- [ ] Riapri app
- [ ] Apri Settings
- [ ] ✅ Beep è ancora disabilitato (preferenza salvata)

---

## 🐛 Troubleshooting

### Issue 1: Beep non si sente

**Possibili cause:**
1. Volume PC a zero o altoparlanti spenti
2. App compilata come Windows Store app (UWP) invece di desktop app
3. Driver audio non installati

**Debug:**
- Controlla log: "Beep played for metadata received"
- Se log appare ma non senti nulla → problema hardware/driver
- Se log non appare → `PlayBeepOnMetadata` è false o c'è exception

**Workaround:**
Sostituire `Console.Beep()` con riproduzione file WAV:
```csharp
var player = new System.Media.SoundPlayer("beep.wav");
player.Play();
```

### Issue 2: Beep troppo forte/fastidioso

**Soluzione:**
Modificare parametri in `OnMetadataReceived()`:

```csharp
// Beep più basso e più breve
System.Console.Beep(500, 100); // 500Hz, 100ms

// Beep più alto e più lungo
System.Console.Beep(1200, 300); // 1200Hz, 300ms
```

**Valori consigliati:**
- Frequenza: 400-1200 Hz (udibile e piacevole)
- Durata: 50-500 ms (più breve = meno invasivo)

### Issue 3: Duration mostra "0.0s" per risposte velocissime

**Causa:** Risposta completata in meno di 50ms

**Comportamento:**
- Corretto: 0.0s è accurato
- Se vuoi mostrare almeno 0.1s: `Math.Max(durationSeconds, 0.1)`

### Issue 4: Duration con più di 1 cifra decimale

**Causa:** Formattazione errata

**Fix:**
Verifica che usi `{durationSeconds:F1}` e non `{durationSeconds}` o `{durationSeconds:F2}`

---

## 🚀 Miglioramenti Futuri

### Opzione 1: Beep Personalizzabili

Aggiungere settings per frequenza e durata:

```csharp
public int BeepFrequency
{
    get => Preferences.Get("BeepFrequency", 800);
    set => Preferences.Set("BeepFrequency", value);
}

public int BeepDurationMs
{
    get => Preferences.Get("BeepDurationMs", 200);
    set => Preferences.Set("BeepDurationMs", value);
}
```

UI: Slider nella SettingsPage

### Opzione 2: Diversi Suoni per Eventi

- Beep 1 (alto): Metadata ricevuti
- Beep 2 (basso): Errore
- Beep 3 (medio): Tool call completato

### Opzione 3: File Audio Personalizzati

Permettere all'utente di scegliere un file WAV:

```csharp
var soundFile = Preferences.Get("MetadataBeepFile", "default");
if (soundFile == "default")
{
    Console.Beep(800, 200);
}
else
{
    var player = new System.Media.SoundPlayer(soundFile);
    player.Play();
}
```

---

## 📝 File Modificati

| File | Righe Modificate/Aggiunte | Descrizione |
|------|---------------------------|-------------|
| `Services/SettingsService.cs` | +26 righe | Aggiunta property `PlayBeepOnMetadata` |
| `SettingsPage.xaml` | +17 righe | Aggiunto toggle UI per beep |
| `SettingsPage.xaml.cs` | +12 righe | Handler `OnPlayBeepToggled()` |
| `MainPage.xaml.cs:643-684` | +15 righe | Duration in secondi + beep logic |

**Totale righe aggiunte:** ~70

---

## 📚 Riferimenti

### API Usate
- `System.Console.Beep(int frequency, int duration)` - [MSDN](https://learn.microsoft.com/en-us/dotnet/api/system.console.beep)
- `Preferences.Get/Set` - [MAUI Docs](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/preferences)

### Alternative per Beep
- `System.Media.SoundPlayer` - Per file WAV
- `System.Media.SystemSounds.Beep.Play()` - Suono di sistema predefinito
- `Microsoft.Toolkit.Uwp.Notifications` - Per toast notifications con suoni

---

## ✅ Conclusioni

**Modifiche Implementate:** ✅
**Codice Compila:** ✅
**Duration in Secondi:** ✅
**Beep Funziona:** ✅ (da testare manualmente su Windows)
**Settings Aggiornati:** ✅
**Default Abilitato:** ✅

**Prossimi Step:**
1. Chiudi app se in esecuzione
2. Ricompila: `dotnet build`
3. Avvia app
4. Testa i 6 test case sopra
5. Regola frequenza/durata beep se necessario

---

**Documento creato:** 2025-10-30
**Versione:** 1.0
**Autore:** Claude (Anthropic)
**Feature:** Metadata Duration in Secondi + Beep Notification
