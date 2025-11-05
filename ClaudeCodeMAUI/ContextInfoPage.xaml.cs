using ClaudeCodeMAUI.Extensions;
using ClaudeCodeMAUI.Models;
using Serilog;

namespace ClaudeCodeMAUI;

/// <summary>
/// Pagina modale per visualizzare le informazioni dettagliate sull'utilizzo del contesto.
/// Mostra barre di progresso colorate per ogni categoria di utilizzo dei token.
/// </summary>
public partial class ContextInfoPage : ContentPage
{
    private ContextInfo? _contextInfo;

    public ContextInfoPage()
    {
        InitializeComponent();
        Log.Information("ContextInfoPage initialized");
    }

    /// <summary>
    /// Imposta i dati del contesto e aggiorna tutti i controlli della pagina.
    /// </summary>
    /// <param name="info">Informazioni sul contesto da visualizzare</param>
    public void SetContextInfo(ContextInfo info)
    {
        if (info == null)
        {
            Log.Error("SetContextInfo called with null info");
            return;
        }

        _contextInfo = info;
        Log.Information("Setting context info for model: {Model}", info.Model);

        // Aggiorna header
        LblModel.Text = $"Model: {info.Model}";
        LblTotalUsage.Text = $"{FormatTokens(info.UsedTokens)} / {FormatTokens(info.TotalTokens)} tokens ({info.UsagePercentage:F1}%)";

        // Aggiorna System Prompt
        ProgressSystemPrompt.Progress = info.SystemPromptPercentage / 100.0;
        LblSystemPrompt.Text = $"{FormatTokens(info.SystemPromptTokens)} tokens ({info.SystemPromptPercentage:F1}%)";

        // Aggiorna System Tools
        ProgressSystemTools.Progress = info.SystemToolsPercentage / 100.0;
        LblSystemTools.Text = $"{FormatTokens(info.SystemToolsTokens)} tokens ({info.SystemToolsPercentage:F1}%)";

        // Aggiorna Memory Files
        ProgressMemoryFiles.Progress = info.MemoryFilesPercentage / 100.0;
        LblMemoryFiles.Text = $"{FormatTokens(info.MemoryFilesTokens)} tokens ({info.MemoryFilesPercentage:F1}%)";

        // Aggiorna Messages
        ProgressMessages.Progress = info.MessagesPercentage / 100.0;
        LblMessages.Text = $"{FormatTokens(info.MessagesTokens)} tokens ({info.MessagesPercentage:F1}%)";

        // Aggiorna Free Space
        ProgressFreeSpace.Progress = info.FreeSpacePercentage / 100.0;
        LblFreeSpace.Text = $"{FormatTokens(info.FreeSpaceTokens)} tokens ({info.FreeSpacePercentage:F1}%)";

        // Aggiorna Autocompact Buffer
        ProgressAutocompact.Progress = info.AutocompactBufferPercentage / 100.0;
        LblAutocompact.Text = $"{FormatTokens(info.AutocompactBufferTokens)} tokens ({info.AutocompactBufferPercentage:F1}%)";

        Log.Information("Context info UI updated successfully");
    }

    /// <summary>
    /// Formatta il numero di token in formato leggibile (es. 2600 -> "2.6k")
    /// </summary>
    /// <param name="tokens">Numero di token</param>
    /// <returns>Stringa formattata</returns>
    private string FormatTokens(int tokens)
    {
        if (tokens >= 1000)
        {
            return $"{tokens / 1000.0:F1}k";
        }
        return tokens.ToString();
    }

    /// <summary>
    /// Handler per il pulsante "Copy Raw Output".
    /// Copia l'output raw del comando /context negli appunti.
    /// </summary>
    private async void OnCopyRawClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_contextInfo == null || string.IsNullOrWhiteSpace(_contextInfo.RawOutput))
            {
                await this.DisplaySelectableAlert("Error", "No raw output available to copy.", "OK");
                return;
            }

            // Copia negli appunti
            await Clipboard.SetTextAsync(_contextInfo.RawOutput);

            Log.Information("Raw output copied to clipboard ({Length} chars)", _contextInfo.RawOutput.Length);

            // Feedback visivo
            var originalText = BtnCopyRaw.Text;
            BtnCopyRaw.Text = "✓ Copied!";
            BtnCopyRaw.BackgroundColor = Colors.Green;

            // Ripristina dopo 2 secondi
            await Task.Delay(2000);
            BtnCopyRaw.Text = originalText;
            BtnCopyRaw.BackgroundColor = Color.FromArgb("#4A4A4A");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy raw output to clipboard");
            await this.DisplaySelectableAlert("Error", $"Failed to copy to clipboard: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Handler per il pulsante "Close".
    /// Chiude la pagina modale.
    /// </summary>
    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        try
        {
            Log.Information("Closing ContextInfoPage");
            await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to close ContextInfoPage");
            await this.DisplaySelectableAlert("Error", $"Failed to close page: {ex.Message}", "OK");
        }
    }
}
