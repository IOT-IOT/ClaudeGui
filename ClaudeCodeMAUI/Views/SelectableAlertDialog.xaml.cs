namespace ClaudeCodeMAUI.Views;

/// <summary>
/// Dialog personalizzato con testo selezionabile e copiabile.
/// Sostituisce DisplayAlert che non permette selezione del testo.
/// </summary>
public partial class SelectableAlertDialog : ContentPage
{
    private readonly TaskCompletionSource<string?> _taskCompletionSource = new();

    /// <summary>
    /// Crea un dialog con testo selezionabile
    /// </summary>
    /// <param name="title">Titolo del dialog</param>
    /// <param name="message">Messaggio (selezionabile e copiabile)</param>
    /// <param name="buttons">Etichette dei pulsanti (1 o 2)</param>
    public SelectableAlertDialog(string title, string message, params string[] buttons)
    {
        InitializeComponent();

        TitleLabel.Text = title;
        MessageLabel.Text = message;

        // Aggiungi pulsanti dinamicamente
        if (buttons == null || buttons.Length == 0)
        {
            buttons = new[] { "OK" };
        }

        foreach (var buttonText in buttons)
        {
            var button = new Button
            {
                Text = buttonText,
                BackgroundColor = GetButtonColor(buttonText),
                TextColor = Colors.White,
                CornerRadius = 5,
                Padding = new Thickness(20, 10),
                MinimumWidthRequest = 100
            };

            button.Clicked += (s, e) => OnButtonClicked(buttonText);
            ButtonsContainer.Children.Add(button);
        }
    }

    /// <summary>
    /// Restituisce il colore del pulsante in base al testo
    /// </summary>
    private Color GetButtonColor(string buttonText)
    {
        var lowerText = buttonText.ToLower();

        if (lowerText.Contains("ok") || lowerText.Contains("sì") || lowerText.Contains("yes") || lowerText.Contains("continua"))
            return Color.FromArgb("#4CAF50"); // Verde

        if (lowerText.Contains("cancel") || lowerText.Contains("annulla") || lowerText.Contains("no") || lowerText.Contains("interrompi"))
            return Color.FromArgb("#F44336"); // Rosso

        return Color.FromArgb("#2196F3"); // Blu (default)
    }

    /// <summary>
    /// Handler per il click su un pulsante
    /// </summary>
    private async void OnButtonClicked(string buttonText)
    {
        _taskCompletionSource.TrySetResult(buttonText);
        await Navigation.PopModalAsync();
    }

    /// <summary>
    /// Mostra il dialog e attende la risposta dell'utente
    /// </summary>
    /// <returns>Testo del pulsante premuto</returns>
    public Task<string?> ShowAsync()
    {
        return _taskCompletionSource.Task;
    }
}
