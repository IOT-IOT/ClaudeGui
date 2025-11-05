using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace ClaudeCodeMAUI.Views;

/// <summary>
/// Dialog per visualizzare campi JSON sconosciuti rilevati durante la scansione dei messaggi.
/// Mostra il JSON completo con testo selezionabile e copiabile.
/// </summary>
public partial class UnknownFieldsDialog : ContentPage
{
    private readonly string _jsonLine;

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

        // Popola JSON (formattato se possibile)
        JsonEditor.Text = FormatJson(jsonLine);
    }

    private string FormatJson(string jsonLine)
    {
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(jsonLine);
            return System.Text.Json.JsonSerializer.Serialize(json, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch
        {
            return jsonLine;  // Return raw se non parsabile
        }
    }

    private async void OnCopyJsonClicked(object sender, EventArgs e)
    {
        await Clipboard.SetTextAsync(_jsonLine);
        await DisplayAlert("Copied", "JSON copiato negli appunti", "OK");
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
