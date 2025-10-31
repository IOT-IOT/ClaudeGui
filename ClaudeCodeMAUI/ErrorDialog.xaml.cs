using Serilog;

namespace ClaudeCodeMAUI;

/// <summary>
/// Dialog modale per visualizzare errori con testo copiabile.
/// Permette di copiare facilmente il messaggio di errore negli appunti.
/// </summary>
public partial class ErrorDialog : ContentPage
{
    private string _title = "Error";
    private string _message = string.Empty;

    public ErrorDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Imposta il titolo e il messaggio del dialog.
    /// </summary>
    /// <param name="title">Titolo del dialog</param>
    /// <param name="message">Messaggio da visualizzare</param>
    public void SetError(string title, string message)
    {
        _title = title;
        _message = message;

        LblTitle.Text = title;
        LblMessage.Text = message;

        Log.Information("ErrorDialog set with title: {Title}, message length: {Length}", title, message?.Length ?? 0);
    }

    /// <summary>
    /// Handler per il pulsante "Copy Error".
    /// Copia il messaggio di errore completo negli appunti.
    /// </summary>
    private async void OnCopyClicked(object? sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(_message))
            {
                return;
            }

            // Copia il messaggio completo negli appunti
            // Formato: [Titolo]\n\n[Messaggio]
            var fullText = $"{_title}\n\n{_message}";
            await Clipboard.SetTextAsync(fullText);

            Log.Information("Error message copied to clipboard ({Length} chars)", fullText.Length);

            // Feedback visivo
            var originalText = BtnCopy.Text;
            var originalColor = BtnCopy.BackgroundColor;
            BtnCopy.Text = "✓ Copied!";
            BtnCopy.BackgroundColor = Colors.Green;

            // Ripristina dopo 2 secondi
            await Task.Delay(2000);
            BtnCopy.Text = originalText;
            BtnCopy.BackgroundColor = originalColor;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy error message to clipboard");
            // Non mostrare un altro errore per evitare loop infiniti
        }
    }

    /// <summary>
    /// Handler per il pulsante "OK".
    /// Chiude il dialog modale.
    /// </summary>
    private async void OnOkClicked(object? sender, EventArgs e)
    {
        try
        {
            Log.Information("ErrorDialog closed");
            await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to close ErrorDialog");
        }
    }
}
