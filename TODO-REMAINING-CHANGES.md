# Modifiche Rimanenti da Completare

## ✅ Completato Finora

1. ✅ Commit e push modifiche precedenti
2. ✅ Creato script SQL `add-message-metadata-columns.sql`
3. ✅ Modificato `DbService.cs` con:
   - `GetKnownJsonFields()` - lista campi JSON noti
   - `DetectUnknownFields()` - rilevamento ricorsivo campi sconosciuti
   - `ReadLastLinesFromFileAsync()` - legge ultimi 32KB del file .jsonl
   - `ImportMessagesFromJsonlAsync()` - import batch messaggi da file
   - `SaveMessageAsync()` overload con metadata completi
4. ✅ Creato `UnknownFieldsDialog.xaml` e `.xaml.cs`

---

## ⚠️ DA COMPLETARE MANUALMENTE

### 1. **Eseguire Script SQL**
```bash
# Connettiti al database
mysql -h 192.168.1.11 -u <user> -p ClaudeGui

# Esegui lo script
source add-message-metadata-columns.sql
```

### 2. **MainPage.xaml.cs** - Modificare OnJsonLineReceived

**Posizione**: Riga ~874-901

**Modifiche necessarie**:
- ❌ NON usare il JSON da stdout direttamente
- ✅ Usa OnJsonLineReceived solo come **trigger**
- ✅ Leggi dal file .jsonl (source of truth)
- ✅ Rileva campi sconosciuti e mostra dialog
- ✅ Salva TUTTI i tipi di messaggio (no filtro)

**Pseudo-codice**:
```csharp
private DateTime? _lastProcessedTimestamp = null;

private async void OnJsonLineReceived(SessionTabItem tabItem, string jsonLine)
{
    // IGNORA stdout jsonLine - usa solo come trigger
    var filePath = GetSessionFilePath(tabItem.SessionId, tabItem.WorkingDirectory);

    // Leggi ultime righe dal file con retry (32KB buffer)
    var lastLines = await _dbService.ReadLastLinesFromFileAsync(filePath, maxLines: 20);

    foreach (var line in lastLines)
    {
        await ProcessMessageLineFromFileAsync(tabItem, line);
    }
}

private async Task ProcessMessageLineFromFileAsync(SessionTabItem tabItem, string jsonLine)
{
    var json = JsonDocument.Parse(jsonLine);
    var timestamp = ExtractTimestamp(json);

    // Skip duplicati
    if (_lastProcessedTimestamp.HasValue && timestamp <= _lastProcessedTimestamp)
        return;

    _lastProcessedTimestamp = timestamp;

    // Rileva campi sconosciuti
    var unknownFields = _dbService.DetectUnknownFields(json.RootElement, _dbService.GetKnownJsonFields());
    if (unknownFields.Count > 0)
    {
        await ShowUnknownFieldsDialogAsync(jsonLine, unknownFields, ExtractUuid(json));
        return; // INTERROMPI
    }

    // Salva nel DB - TUTTI i tipi (no filtro)
    await SaveMessageFromJson(tabItem.SessionId, json);
}

private string GetSessionFilePath(string sessionId, string workingDirectory)
{
    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var encodedDir = workingDirectory.Replace(":\\", "--").Replace("\\", "-");
    return Path.Combine(userProfile, ".claude", "projects", encodedDir, $"{sessionId}.jsonl");
}

private async Task ShowUnknownFieldsDialogAsync(string jsonLine, List<string> unknownFields, string uuid)
{
    var dialog = new UnknownFieldsDialog(jsonLine, unknownFields, uuid);
    await Navigation.PushModalAsync(new NavigationPage(dialog));
}
```

### 3. **MainPage.xaml.cs** - Validazione Nome Obbligatorio

**Posizione**: Metodo `OnSelectSessionClicked()` (circa riga 530)

**Aggiungere PRIMA di aprire il tab**:
```csharp
// Validazione nome obbligatorio
if (selected != null && string.IsNullOrWhiteSpace(selected.Name))
{
    await DisplayAlert("Nome Mancante",
        "Questa sessione non ha un nome assegnato.\n\n" +
        "Assegna un nome prima di aprirla utilizzando il pulsante 'Assegna Nome' " +
        "o modificando direttamente il campo nella tabella.",
        "OK");
    return;
}
```

### 4. **SessionSelectorPage.xaml** - Modifiche UI

**A. Rinominare pulsanti** (righe ~225-235):
```xml
<Button Text="Aggiorna Vista"
        ToolTipProperties.Text="Ricarica la lista dal database senza scansionare il filesystem"
        BackgroundColor="#4A4A4A"
        TextColor="White"
        Clicked="OnRefreshClicked"
        MinimumWidthRequest="120" />

<Button Text="Sincronizza Filesystem"
        ToolTipProperties.Text="Scansiona le directory .claude/projects e sincronizza con il database"
        BackgroundColor="#CC6600"
        TextColor="White"
        Clicked="OnRescanClicked"
        MinimumWidthRequest="180" />
```

**B. Campo Name editabile** (riga ~171):
```xml
<!-- Colonna 3: Name (EDITABILE) -->
<Entry Grid.Column="2"
       Text="{Binding DisplayName}"
       FontSize="12"
       TextColor="White"
       BackgroundColor="Transparent"
       IsReadOnly="False"
       VerticalOptions="Center"
       Unfocused="OnNameEntryUnfocused" />
```
**NOTA**: Rimuovere `InputTransparent="True"` solo da questa colonna, non dalle altre!

**C. Menu contestuale** (righe ~140-143):
```xml
<FlyoutBase.ContextFlyout>
    <MenuFlyout>
        <MenuFlyoutItem Text="Apri con Notepad++" Clicked="OnOpenWithNotepadClicked" />
        <MenuFlyoutSeparator />
        <MenuFlyoutItem Text="Aggiorna Messaggi" Clicked="OnUpdateMessagesClicked" />
    </MenuFlyout>
</FlyoutBase.ContextFlyout>
```

### 5. **SessionSelectorPage.xaml.cs** - Nuovi Handlers

**A. Handler modifica nome inline**:
```csharp
private async void OnNameEntryUnfocused(object sender, FocusEventArgs e)
{
    if (sender is Entry entry && entry.BindingContext is SessionDisplayItem item)
    {
        var newName = entry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(newName))
        {
            await DisplayAlert("Nome Invalido", "Il nome non può essere vuoto.", "OK");
            entry.Text = item.DisplayName;
            return;
        }

        if (newName == item.DisplayName)
            return;

        await _dbService.UpdateSessionNameAsync(item.SessionId, newName);
        item.DisplayName = newName;
        UpdateCurrentSessions();
    }
}
```

**B. Handler "Aggiorna Messaggi"**:
```csharp
private async void OnUpdateMessagesClicked(object? sender, EventArgs e)
{
    if (sender is MenuFlyoutItem menuItem && menuItem.BindingContext is SessionDisplayItem selectedItem)
    {
        var filePath = GetSessionFilePath(selectedItem.SessionId, selectedItem.WorkingDirectory);

        if (!File.Exists(filePath))
        {
            await DisplayAlert("File non trovato", $"Il file non esiste:\n{filePath}", "OK");
            return;
        }

        bool confirm = await DisplayAlert(
            "Aggiorna Messaggi",
            $"Importare tutti i messaggi dal file .jsonl nel database?\n\n" +
            $"Sessione: {selectedItem.DisplayName}\n" +
            $"Eventuali messaggi duplicati (stesso UUID) verranno ignorati.",
            "Sì, Importa",
            "Annulla");

        if (!confirm)
            return;

        var (imported, unknownFields, errorUuid) = await _dbService.ImportMessagesFromJsonlAsync(
            selectedItem.SessionId,
            filePath);

        if (unknownFields.Count > 0)
        {
            string? errorLine = await FindJsonLineByUuidAsync(filePath, errorUuid);
            var dialog = new UnknownFieldsDialog(errorLine ?? "", unknownFields, errorUuid ?? "unknown");
            await Navigation.PushModalAsync(new NavigationPage(dialog));

            await DisplayAlert("Import Interrotto",
                $"Import interrotto dopo {imported} messaggi.\n\n" +
                $"Trovati {unknownFields.Count} campi sconosciuti.",
                "OK");
        }
        else
        {
            await DisplayAlert("Import Completato",
                $"Importati con successo {imported} messaggi nel database.",
                "OK");
        }
    }
}

private async Task<string?> FindJsonLineByUuidAsync(string filePath, string? uuid)
{
    if (string.IsNullOrEmpty(uuid))
        return null;

    using var reader = new StreamReader(filePath);
    while (!reader.EndOfStream)
    {
        var line = await reader.ReadLineAsync();
        if (line?.Contains($"\"uuid\":\"{uuid}\"") == true)
            return line;
    }
    return null;
}
```

---

## 📋 Checklist Finale

- [ ] Eseguire script SQL sul database
- [ ] Modificare MainPage.xaml.cs (OnJsonLineReceived + validazione nome)
- [ ] Modificare SessionSelectorPage.xaml (pulsanti + campo Name + menu)
- [ ] Modificare SessionSelectorPage.xaml.cs (handlers)
- [ ] Build e test
- [ ] Commit finale

---

## 🔑 Punti Chiave

1. **File .jsonl = Source of Truth**: stdout è solo trigger
2. **Buffer 32KB**: performance ottimale
3. **Nessun filtro type**: salva TUTTI i tipi di messaggio
4. **Campi sconosciuti**: interrompe e mostra dialog
5. **Nome obbligatorio**: validazione all'apertura sessione
