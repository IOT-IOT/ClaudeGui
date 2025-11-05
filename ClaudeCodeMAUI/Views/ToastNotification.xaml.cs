namespace ClaudeCodeMAUI.Views;

/// <summary>
/// Tipi di toast notification disponibili
/// </summary>
public enum ToastType
{
    Success,  // Verde con ✓
    Error,    // Rosso con ✗
    Info,     // Blu con ℹ
    Warning   // Giallo con ⚠
}

/// <summary>
/// Toast notification non bloccante che appare temporaneamente e scompare con animazione.
/// Utilizzato per feedback visivo di operazioni (es. "JSON copiato", "Sessione salvata", ecc.)
/// </summary>
public partial class ToastNotification : Border
{
    private const int ANIMATION_DURATION = 300; // ms per fade-in/out
    private readonly int _displayDuration;      // ms di visualizzazione
    private CancellationTokenSource? _dismissCts;

    /// <summary>
    /// Crea un nuovo toast notification
    /// </summary>
    /// <param name="message">Messaggio da visualizzare</param>
    /// <param name="type">Tipo di toast (Success, Error, Info, Warning)</param>
    /// <param name="durationMs">Durata visualizzazione in millisecondi (default: 2500ms)</param>
    public ToastNotification(string message, ToastType type = ToastType.Success, int durationMs = 2500)
    {
        InitializeComponent();

        _displayDuration = durationMs;

        // Imposta il messaggio
        MessageLabel.Text = message;

        // Configura colori e icona in base al tipo
        ConfigureToastStyle(type);

        // Imposta stato iniziale per animazione (invisibile, traslato a destra)
        this.Opacity = 0;
        this.TranslationX = 50;
    }

    /// <summary>
    /// Configura colori, icona e stile in base al tipo di toast
    /// </summary>
    private void ConfigureToastStyle(ToastType type)
    {
        switch (type)
        {
            case ToastType.Success:
                BackgroundColor = Color.FromArgb("#4CAF50"); // Verde
                IconLabel.Text = "✓";
                IconLabel.TextColor = Colors.White;
                MessageLabel.TextColor = Colors.White;
                break;

            case ToastType.Error:
                BackgroundColor = Color.FromArgb("#F44336"); // Rosso
                IconLabel.Text = "✗";
                IconLabel.TextColor = Colors.White;
                MessageLabel.TextColor = Colors.White;
                break;

            case ToastType.Info:
                BackgroundColor = Color.FromArgb("#2196F3"); // Blu
                IconLabel.Text = "ℹ";
                IconLabel.TextColor = Colors.White;
                MessageLabel.TextColor = Colors.White;
                break;

            case ToastType.Warning:
                BackgroundColor = Color.FromArgb("#FF9800"); // Giallo/Arancione
                IconLabel.Text = "⚠";
                IconLabel.TextColor = Colors.Black;
                MessageLabel.TextColor = Colors.Black;
                break;
        }
    }

    /// <summary>
    /// Mostra il toast con animazione fade-in e lo nasconde automaticamente dopo la durata specificata
    /// </summary>
    public async Task ShowAsync()
    {
        // Fade-in + slide-in da destra
        await Task.WhenAll(
            this.FadeTo(1, ANIMATION_DURATION, Easing.CubicOut),
            this.TranslateTo(0, 0, ANIMATION_DURATION, Easing.CubicOut)
        );

        // Attendi durata visualizzazione
        _dismissCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(_displayDuration, _dismissCts.Token);
        }
        catch (TaskCanceledException)
        {
            // Dismissione anticipata richiesta
            return;
        }

        // Fade-out
        await this.FadeTo(0, ANIMATION_DURATION, Easing.CubicIn);
    }

    /// <summary>
    /// Nasconde il toast anticipatamente (prima della scadenza automatica)
    /// </summary>
    public void Dismiss()
    {
        _dismissCts?.Cancel();
    }
}
