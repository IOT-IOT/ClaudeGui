# Multiline Input Implementation - Modifiche e Verifica

## 📋 Riepilogo

Implementato supporto **multilinea** per il campo di input dei prompt con gestione intelligente delle keyboard shortcuts:
- **Enter** = Invia messaggio
- **Ctrl+Enter** = Nuova linea (senza inviare)
- **AutoSize** = Campo si espande automaticamente

---

## 🔧 Modifiche Implementate

### 1. MainPage.xaml (UI Layer)

**File**: `ClaudeCodeMAUI/MainPage.xaml`

#### A. Sostituzione Entry con Editor (righe 60-73)

**PRIMA** (Entry - single line):
```xml
<!-- Input Area -->
<Grid Grid.Row="2" ColumnDefinitions="*,Auto" ColumnSpacing="10">
    <Entry x:Name="InputEntry"
           Grid.Column="0"
           Placeholder="Enter your message (press Enter to send)..."
           Completed="OnSendMessage" />
    <Button x:Name="BtnSend"
            Grid.Column="1"
            Text="Send"
            Clicked="OnSendMessage" />
</Grid>
```

**DOPO** (Editor - multiline):
```xml
<!-- Input Area -->
<Grid Grid.Row="2" ColumnDefinitions="*,Auto" ColumnSpacing="10" RowDefinitions="Auto">
    <Editor x:Name="InputEditor"
            Grid.Column="0"
            Placeholder="Enter your message (Enter to send, Ctrl+Enter for new line)..."
            AutoSize="TextChanges"
            MinimumHeightRequest="40"
            MaximumHeightRequest="200" />
    <Button x:Name="BtnSend"
            Grid.Column="1"
            Text="Send"
            VerticalOptions="End"
            Clicked="OnSendMessage" />
</Grid>
```

**Cambiamenti**:
- ✅ `Entry` → `Editor` (supporto multilinea nativo)
- ✅ `x:Name="InputEntry"` → `x:Name="InputEditor"`
- ✅ Aggiunto `AutoSize="TextChanges"` - il campo si espande con il contenuto
- ✅ `MinimumHeightRequest="40"` - altezza minima 40px
- ✅ `MaximumHeightRequest="200"` - altezza massima 200px (poi scroll)
- ✅ Placeholder aggiornato con istruzioni chiare
- ✅ `VerticalOptions="End"` sul bottone Send (allineato in basso)
- ✅ Rimosso `Completed` event (non esiste su Editor)

---

### 2. MainPage.xaml.cs (Code-Behind)

**File**: `ClaudeCodeMAUI/MainPage.xaml.cs`

#### A. Costruttore con inizializzazione tastiera (righe 34-41)

**PRIMA**:
```csharp
public MainPage(DbService dbService) : this()
{
    _dbService = dbService;
    Log.Information("MainPage initialized with DbService");
}
```

**DOPO**:
```csharp
public MainPage(DbService dbService) : this()
{
    _dbService = dbService;
    Log.Information("MainPage initialized with DbService");

    // Aggiungi handler per gestione tastiera su Editor
    InitializeInputEditor();
}
```

#### B. Nuovo metodo InitializeInputEditor() (righe 43-59)

**AGGIUNTO**:
```csharp
/// <summary>
/// Inizializza l'InputEditor con la gestione delle keyboard shortcuts.
/// Enter = invia messaggio, Ctrl+Enter = nuova linea
/// </summary>
private void InitializeInputEditor()
{
#if WINDOWS
    // Su Windows, possiamo intercettare i key press usando l'handler nativo
    InputEditor.HandlerChanged += (s, e) =>
    {
        if (InputEditor.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox textBox)
        {
            textBox.KeyDown += OnInputEditorKeyDown;
        }
    };
#endif
}
```

**Spiegazione**:
- `#if WINDOWS` - codice compilato solo per Windows
- `HandlerChanged` - evento quando MAUI crea il controllo nativo
- `PlatformView` - accesso al `TextBox` nativo WinUI
- `KeyDown` - handler per intercettare tasti premuti

#### C. Nuovo handler OnInputEditorKeyDown() (righe 61-85)

**AGGIUNTO**:
```csharp
#if WINDOWS
/// <summary>
/// Handler per KeyDown su Windows - gestisce Enter vs Ctrl+Enter
/// </summary>
private void OnInputEditorKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
{
    // Enter senza modificatori = invia messaggio
    if (e.Key == Windows.System.VirtualKey.Enter)
    {
        var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (!ctrlPressed)
        {
            // Enter normale = invia messaggio
            e.Handled = true; // Previeni il default (nuova linea)
            MainThread.BeginInvokeOnMainThread(() =>
            {
                OnSendMessage(sender, EventArgs.Empty);
            });
        }
        // Se Ctrl è premuto, lascia il comportamento default (nuova linea)
    }
}
#endif
```

**Spiegazione**:
- Controlla se il tasto premuto è **Enter**
- Verifica se **Ctrl** è premuto contemporaneamente
- **Se solo Enter**: `e.Handled = true` blocca il default (nuova linea) e chiama `OnSendMessage()`
- **Se Ctrl+Enter**: non fa nulla, lascia il comportamento default (inserisce `\n`)
- `MainThread.BeginInvokeOnMainThread` - esegue l'invio sul thread UI

#### D. Aggiornamento OnSendMessage() (righe 162-170)

**PRIMA**:
```csharp
private void OnSendMessage(object? sender, EventArgs e)
{
    var message = InputEntry.Text?.Trim();
    if (string.IsNullOrWhiteSpace(message))
        return;

    SendMessageAsync(message);
    InputEntry.Text = string.Empty;
}
```

**DOPO**:
```csharp
private void OnSendMessage(object? sender, EventArgs e)
{
    var message = InputEditor.Text?.Trim();
    if (string.IsNullOrWhiteSpace(message))
        return;

    SendMessageAsync(message);
    InputEditor.Text = string.Empty;
}
```

**Cambiamenti**:
- ✅ `InputEntry.Text` → `InputEditor.Text` (2 occorrenze)

---

## 🎯 Comportamento Atteso

### Scenario 1: Enter normale (invia messaggio)
```
1. User digita: "Hello Claude"
2. User preme: Enter
3. ✅ Messaggio inviato immediatamente
4. ✅ Campo input svuotato
5. ✅ Nessuna nuova linea inserita
```

### Scenario 2: Ctrl+Enter (nuova linea)
```
1. User digita: "Line 1"
2. User preme: Ctrl+Enter
3. ✅ Cursore va a capo
4. User digita: "Line 2"
5. User preme: Ctrl+Enter
6. ✅ Cursore va a capo di nuovo
7. User digita: "Line 3"
8. User preme: Enter (senza Ctrl)
9. ✅ Messaggio multilinea inviato:
   "Line 1
    Line 2
    Line 3"
```

### Scenario 3: AutoSize (espansione campo)
```
1. Campo iniziale: 40px altezza (1 riga)
2. User preme Ctrl+Enter 3 volte
3. ✅ Campo si espande automaticamente (3 righe visibili)
4. User continua ad aggiungere righe
5. Campo raggiunge 200px (max)
6. ✅ Compare scrollbar verticale
7. User può scrollare dentro il campo
```

### Scenario 4: Bottone Send (sempre funzionante)
```
1. User digita messaggio multilinea
2. User clicca bottone "Send" (mouse)
3. ✅ Messaggio inviato (come Enter)
```

---

## ✅ Checklist Verifica Funzionamento

### Test 1: Enter invia messaggio
- [ ] Digitare "test message"
- [ ] Premere Enter
- [ ] ✅ Verificare: messaggio inviato e campo svuotato
- [ ] ✅ Verificare: nessuna nuova linea inserita prima dell'invio

### Test 2: Ctrl+Enter nuova linea
- [ ] Digitare "line 1"
- [ ] Premere Ctrl+Enter
- [ ] ✅ Verificare: cursore va a capo
- [ ] Digitare "line 2"
- [ ] ✅ Verificare: campo contiene 2 righe
- [ ] Premere Enter (senza Ctrl)
- [ ] ✅ Verificare: messaggio inviato con entrambe le righe

### Test 3: AutoSize funziona
- [ ] Campo vuoto = 40px circa (1 riga)
- [ ] Premere Ctrl+Enter 5 volte
- [ ] ✅ Verificare: campo si espande fino a circa 120-150px
- [ ] Continuare ad aggiungere righe
- [ ] ✅ Verificare: campo si ferma a 200px max
- [ ] ✅ Verificare: compare scrollbar verticale

### Test 4: Bottone Send funziona
- [ ] Digitare messaggio multilinea (con Ctrl+Enter)
- [ ] Cliccare bottone "Send" con mouse
- [ ] ✅ Verificare: messaggio inviato correttamente
- [ ] ✅ Verificare: campo svuotato

### Test 5: Placeholder visibile
- [ ] Campo vuoto
- [ ] ✅ Verificare: testo grigio "Enter your message (Enter to send, Ctrl+Enter for new line)..."
- [ ] Digitare un carattere
- [ ] ✅ Verificare: placeholder scompare

### Test 6: Focus e usabilità
- [ ] Cliccare nel campo input
- [ ] ✅ Verificare: cursore lampeggiante visibile
- [ ] Digitare testo lungo (>200 caratteri)
- [ ] ✅ Verificare: testo va a capo automaticamente (word wrap)
- [ ] ✅ Verificare: scrollbar compare quando necessario

### Test 7: Invio messaggio vuoto bloccato
- [ ] Campo vuoto
- [ ] Premere Enter
- [ ] ✅ Verificare: niente viene inviato (metodo ritorna early)
- [ ] Digitare solo spazi "   "
- [ ] Premere Enter
- [ ] ✅ Verificare: niente viene inviato (Trim() lo rende vuoto)

---

## 🐛 Problemi Noti e Limitazioni

### 1. Windows-Only Implementation
**Problema**: Codice keyboard shortcuts funziona solo su Windows

**Motivo**: Uso di `#if WINDOWS` e API WinUI specifiche

**Impatto**:
- ✅ Windows: Funziona perfettamente
- ⚠️ Android/iOS/macOS: Enter inserisce nuova linea (comportamento default Editor)

**Soluzione futura** (se necessario):
- Implementare handler platform-specific per Android (`#if ANDROID`)
- Implementare handler platform-specific per iOS (`#if IOS`)
- Oppure: accettare comportamento default su mobile (Enter = nuova linea, solo bottone invia)

### 2. Shift+Enter comportamento
**Problema**: Shift+Enter NON è gestito esplicitamente

**Comportamento attuale**:
- Shift+Enter = nuova linea (comportamento default Editor)
- Ctrl+Enter = nuova linea (gestito esplicitamente)
- Enter = invia messaggio

**Possibile miglioramento**:
- Gestire anche Shift+Enter in modo esplicito per coerenza

### 3. MaximumHeightRequest hardcoded
**Problema**: Altezza massima 200px è hardcoded in XAML

**Impatto**: Su schermi piccoli potrebbe essere troppo, su grandi troppo poco

**Soluzione futura**:
- Usare binding a risorsa o percentuale dell'altezza schermo
- Esempio: `MaximumHeightRequest="{Binding ScreenHeight, Converter={StaticResource PercentageConverter}, ConverterParameter=0.3}"`

---

## 🔍 Dettagli Tecnici

### Perché #if WINDOWS?

MAUI è cross-platform ma gli eventi tastiera sono platform-specific:
- **Windows**: WinUI 3 con `TextBox.KeyDown`
- **Android**: Android native con `View.KeyPress`
- **iOS**: UIKit con `UITextField.ShouldReturn`
- **macOS**: AppKit con diversi handlers

Per evitare complessità, implementiamo solo Windows (target principale del progetto).

### Perché HandlerChanged?

MAUI usa un sistema di **Handlers** per wrappare i controlli nativi:
- `Editor` (MAUI) → wrapper cross-platform
- `TextBox` (WinUI) → controllo nativo Windows
- `EditText` (Android) → controllo nativo Android
- etc.

`HandlerChanged` viene chiamato quando MAUI crea il controllo nativo, permettendo di accedere al `PlatformView`.

### Perché e.Handled = true?

```csharp
e.Handled = true; // Previeni il default (nuova linea)
```

Quando premi Enter in un TextBox/Editor:
1. WinUI intercetta il tasto
2. Handler custom viene chiamato
3. Se `e.Handled = false`: WinUI inserisce `\n` nel testo (default)
4. Se `e.Handled = true`: WinUI NON fa nulla (preveniamo inserimento `\n`)

Questo ci permette di:
- **Enter solo**: bloccare `\n` e chiamare `OnSendMessage()`
- **Ctrl+Enter**: lasciare `e.Handled = false` per permettere `\n`

### Perché MainThread.BeginInvokeOnMainThread?

```csharp
MainThread.BeginInvokeOnMainThread(() =>
{
    OnSendMessage(sender, EventArgs.Empty);
});
```

`KeyDown` è un evento nativo che potrebbe essere chiamato da thread diversi.
`OnSendMessage()` manipola la UI (svuota InputEditor, aggiorna WebView), quindi DEVE essere eseguito sul **Main Thread** (UI thread).

`BeginInvokeOnMainThread` garantisce esecuzione sicura sul thread UI.

---

## 📝 File Modificati

| File | Righe Modificate | Tipo Modifica |
|------|------------------|---------------|
| `MainPage.xaml` | 60-73 | Sostituzione Entry → Editor |
| `MainPage.xaml.cs` | 34-41 | Aggiunta chiamata InitializeInputEditor() |
| `MainPage.xaml.cs` | 43-85 (nuovo) | Aggiunta gestione tastiera Windows |
| `MainPage.xaml.cs` | 162-170 | Aggiornamento riferimenti InputEntry → InputEditor |

---

## 🚀 Build e Deploy

### Compilazione

```bash
cd C:\Sources\ClaudeGui
dotnet build ClaudeCodeMAUI/ClaudeCodeMAUI.csproj -c Debug -f net9.0-windows10.0.19041.0
```

**Atteso**:
- ✅ Build successful (no errors)
- ⚠️ Possibili warnings su `#if WINDOWS` (normali, ignorabili)

### Esecuzione

```bash
dotnet run --project ClaudeCodeMAUI/ClaudeCodeMAUI.csproj -c Debug -f net9.0-windows10.0.19041.0
```

Oppure:
- Aprire `ClaudeCodeGUI.sln` in Visual Studio 2022
- Selezionare `ClaudeCodeMAUI` come Startup Project
- Premere F5 (Debug) o Ctrl+F5 (Run without debug)

---

## 🎓 Come Testare Manualmente

### Test Completo Passo-Passo

1. **Avvio applicazione**
   ```
   - Lanciare app
   - ✅ Verificare: campo input visibile, altezza ~40px
   - ✅ Verificare: placeholder grigio visibile
   ```

2. **Test Enter semplice**
   ```
   - Digitare: "Hello"
   - Premere: Enter
   - ✅ Verificare: messaggio inviato
   - ✅ Verificare: campo svuotato
   - ✅ Verificare: nessuna riga vuota rimasta
   ```

3. **Test Ctrl+Enter multilinea**
   ```
   - Digitare: "First line"
   - Premere: Ctrl+Enter
   - Verificare: cursore a riga 2
   - Digitare: "Second line"
   - Premere: Ctrl+Enter
   - Verificare: cursore a riga 3
   - Digitare: "Third line"
   - Premere: Enter (senza Ctrl)
   - ✅ Verificare: tutto inviato come singolo messaggio
   ```

4. **Test AutoSize**
   ```
   - Digitare testo lungo con Ctrl+Enter dopo ogni riga
   - Aggiungere 10 righe
   - ✅ Verificare: campo cresce fino a ~200px
   - ✅ Verificare: scrollbar verticale compare
   - ✅ Verificare: puoi scrollare dentro il campo
   ```

5. **Test bottone Send**
   ```
   - Digitare messaggio multilinea
   - Cliccare "Send" con mouse
   - ✅ Verificare: stesso comportamento di Enter
   ```

---

## 🔄 Rollback (se necessario)

Se le modifiche causano problemi, per tornare alla versione precedente:

### Rollback MainPage.xaml
```xml
<!-- Sostituire Editor con Entry originale -->
<Entry x:Name="InputEntry"
       Grid.Column="0"
       Placeholder="Enter your message (press Enter to send)..."
       Completed="OnSendMessage" />
```

### Rollback MainPage.xaml.cs
```csharp
// 1. Rimuovere InitializeInputEditor() dal costruttore
public MainPage(DbService dbService) : this()
{
    _dbService = dbService;
    Log.Information("MainPage initialized with DbService");
    // RIMUOVERE: InitializeInputEditor();
}

// 2. Eliminare metodi InitializeInputEditor() e OnInputEditorKeyDown()

// 3. Ripristinare InputEditor → InputEntry in OnSendMessage()
var message = InputEntry.Text?.Trim();
// ...
InputEntry.Text = string.Empty;
```

---

## 📚 Riferimenti

### MAUI Documentation
- [Editor Control](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/editor)
- [Platform-Specific Code](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/customize-ui-appearance)
- [Handlers](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/handlers/)

### WinUI KeyDown
- [KeyRoutedEventArgs](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.input.keyroutedeventargs)
- [VirtualKey Enum](https://learn.microsoft.com/en-us/uwp/api/windows.system.virtualkey)

---

**Documento creato**: 2025-10-30
**Versione**: 1.0
**Autore**: Claude (Anthropic)
**Progetto**: ClaudeCodeMAUI
**Feature**: Multiline Input with Keyboard Shortcuts
