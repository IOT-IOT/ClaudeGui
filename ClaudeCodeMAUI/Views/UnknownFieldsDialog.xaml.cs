using ClaudeCodeMAUI.Extensions;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Serilog;
using System.Diagnostics;
using System.Text.Json;

namespace ClaudeCodeMAUI.Views;

/// <summary>
/// Dialog per visualizzare campi JSON sconosciuti rilevati durante la scansione dei messaggi.
/// Mostra il JSON completo con syntax highlighting, menu contestuale e pulsanti per continuare/interrompere.
/// </summary>
public partial class UnknownFieldsDialog : ContentPage
{
    private readonly string _jsonLine;
    private string? _tempFilePath;

    /// <summary>
    /// Indica se l'utente ha scelto di continuare la scansione (true) o interromperla (false)
    /// </summary>
    public bool ShouldContinue { get; private set; }

    public UnknownFieldsDialog(string jsonLine, List<string> unknownFields, string uuid)
    {
        InitializeComponent();

        _jsonLine = jsonLine;

        // Popola lista campi sconosciuti
        foreach (var field in unknownFields)
        {
            UnknownFieldsContainer.Children.Add(new Label
            {
                Text = $"• {field}",
                TextColor = Colors.Orange,
                FontFamily = "Consolas"
            });
        }

        // Popola UUID
        UuidEntry.Text = uuid;

        // Popola JSON con syntax highlighting colorato
        JsonLabel.FormattedText = CreateColoredJsonFormattedString(jsonLine);
    }

    /// <summary>
    /// Crea FormattedString con syntax highlighting colorato per JSON
    /// </summary>
    private FormattedString CreateColoredJsonFormattedString(string jsonLine)
    {
        var formattedString = new FormattedString();

        try
        {
            // Formatta JSON con indentazione
            using var doc = JsonDocument.Parse(jsonLine);
            var formattedJson = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });

            // Colori Monokai theme
            var keyColor = Color.FromArgb("#E6DB74");      // giallo oro per chiavi
            var stringColor = Color.FromArgb("#A6E22E");   // verde lime per stringhe
            var numberColor = Color.FromArgb("#AE81FF");   // viola per numeri
            var boolNullColor = Color.FromArgb("#66D9EF"); // cyan per bool/null
            var punctuationColor = Color.FromArgb("#F8F8F2"); // bianco per parentesi

            var lines = formattedJson.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                var indent = line.Substring(0, line.Length - trimmed.Length);

                // Aggiungi indentazione
                if (!string.IsNullOrEmpty(indent))
                    formattedString.Spans.Add(new Span { Text = indent, TextColor = punctuationColor });

                // Parse della linea per colorare correttamente
                if (trimmed.Contains(":"))
                {
                    // Linea con chiave:valore
                    var parts = trimmed.Split(new[] { ':' }, 2);
                    var key = parts[0].Trim();
                    var value = parts.Length > 1 ? parts[1].Trim() : "";

                    // Chiave (con virgolette)
                    formattedString.Spans.Add(new Span { Text = key, TextColor = keyColor });
                    formattedString.Spans.Add(new Span { Text = ": ", TextColor = punctuationColor });

                    // Valore
                    if (value.StartsWith("\""))
                    {
                        // Stringa
                        formattedString.Spans.Add(new Span { Text = value, TextColor = stringColor });
                    }
                    else if (value.StartsWith("true") || value.StartsWith("false") || value.StartsWith("null"))
                    {
                        // Boolean o null
                        formattedString.Spans.Add(new Span { Text = value, TextColor = boolNullColor });
                    }
                    else if (char.IsDigit(value.FirstOrDefault()) || value.StartsWith("-"))
                    {
                        // Numero
                        formattedString.Spans.Add(new Span { Text = value, TextColor = numberColor });
                    }
                    else
                    {
                        // Altro (array/object start)
                        formattedString.Spans.Add(new Span { Text = value, TextColor = punctuationColor });
                    }
                }
                else
                {
                    // Linea con solo parentesi/virgole
                    formattedString.Spans.Add(new Span { Text = trimmed, TextColor = punctuationColor });
                }

                // New line
                formattedString.Spans.Add(new Span { Text = "\n" });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create colored JSON FormattedString");
            // Fallback: testo semplice bianco
            formattedString.Spans.Add(new Span { Text = jsonLine, TextColor = Colors.White });
        }

        return formattedString;
    }

    private async void OnCopyJsonClicked(object sender, EventArgs e)
    {
        await Clipboard.SetTextAsync(_jsonLine);
        await this.DisplaySelectableAlert("Copiato", "JSON copiato negli appunti", "OK");
    }

    private async void OnContinueClicked(object sender, EventArgs e)
    {
        ShouldContinue = true;
        CleanupTempFile();
        await Navigation.PopModalAsync();
    }

    private async void OnStopClicked(object sender, EventArgs e)
    {
        ShouldContinue = false;
        CleanupTempFile();
        await Navigation.PopModalAsync();
    }

    private void OnJsonTapped(object sender, EventArgs e)
    {
        // Placeholder per eventuali azioni su tap (attualmente il menu contestuale è già gestito)
    }

    private async void OnOpenWithVSCodeClicked(object sender, EventArgs e)
    {
        await OpenWithExternalEditor("vscode");
    }

    private async void OnOpenWithNotepadPPClicked(object sender, EventArgs e)
    {
        await OpenWithExternalEditor("notepad++");
    }

    private async void OnOpenWithDefaultEditorClicked(object sender, EventArgs e)
    {
        await OpenWithExternalEditor("default");
    }

    /// <summary>
    /// Salva JSON in file temporaneo e apre con editor specificato
    /// </summary>
    private async Task OpenWithExternalEditor(string editorType)
    {
        try
        {
            // Salva in file temporaneo
            _tempFilePath = Path.Combine(Path.GetTempPath(), $"unknown_fields_{Guid.NewGuid()}.json");

            // Formatta JSON prima di salvare
            using var doc = JsonDocument.Parse(_jsonLine);
            var formattedJson = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_tempFilePath, formattedJson);

            Log.Information("Saved JSON to temp file: {TempFile}", _tempFilePath);

            // Apri con editor
            ProcessStartInfo psi;

            switch (editorType.ToLower())
            {
                case "vscode":
                    // Cerca VS Code
                    var vscodePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Programs", "Microsoft VS Code", "Code.exe");

                    if (File.Exists(vscodePath))
                    {
                        psi = new ProcessStartInfo
                        {
                            FileName = vscodePath,
                            Arguments = $"\"{_tempFilePath}\"",
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                        Log.Information("Opened with VS Code: {Path}", vscodePath);
                    }
                    else
                    {
                        await this.DisplaySelectableAlert("VS Code Non Trovato",
                            "VS Code non è installato o non è stato trovato nel percorso predefinito.\n" +
                            "Percorso cercato: " + vscodePath,
                            "OK");
                    }
                    break;

                case "notepad++":
                    // Cerca Notepad++
                    var notepadPath = @"C:\Program Files\Notepad++\notepad++.exe";
                    if (!File.Exists(notepadPath))
                        notepadPath = @"C:\Program Files (x86)\Notepad++\notepad++.exe";

                    if (File.Exists(notepadPath))
                    {
                        psi = new ProcessStartInfo
                        {
                            FileName = notepadPath,
                            Arguments = $"\"{_tempFilePath}\"",
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                        Log.Information("Opened with Notepad++: {Path}", notepadPath);
                    }
                    else
                    {
                        await this.DisplaySelectableAlert("Notepad++ Non Trovato",
                            "Notepad++ non è installato o non è stato trovato nel percorso predefinito.",
                            "OK");
                    }
                    break;

                case "default":
                default:
                    // Usa associazione predefinita Windows per .json
                    psi = new ProcessStartInfo
                    {
                        FileName = _tempFilePath,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    Log.Information("Opened with default editor");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open JSON with external editor");
            await this.DisplaySelectableAlert("Errore", $"Impossibile aprire l'editor:\n{ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Pulisce il file temporaneo quando il dialog viene chiuso
    /// </summary>
    private void CleanupTempFile()
    {
        if (!string.IsNullOrEmpty(_tempFilePath) && File.Exists(_tempFilePath))
        {
            try
            {
                File.Delete(_tempFilePath);
                Log.Debug("Deleted temp file: {TempFile}", _tempFilePath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to delete temp file: {TempFile}", _tempFilePath);
            }
        }
    }
}
