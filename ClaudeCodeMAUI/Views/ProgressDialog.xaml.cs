using System.Diagnostics;

namespace ClaudeCodeMAUI.Views;

/// <summary>
/// Dialog per mostrare il progress durante operazioni lunghe (es. import messaggi).
/// Mostra progress bar, conteggio messaggi, velocità e pulsante annulla.
/// </summary>
public partial class ProgressDialog : ContentPage
{
    private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private readonly Stopwatch _stopwatch = new Stopwatch();
    private int _lastProcessedCount = 0;
    private DateTime _lastUpdateTime = DateTime.Now;

    /// <summary>
    /// Token per annullare l'operazione
    /// </summary>
    public CancellationToken CancellationToken => _cancellationTokenSource.Token;

    /// <summary>
    /// TaskCompletionSource per gestire la chiusura del dialog
    /// </summary>
    private TaskCompletionSource<bool> _completionSource = new TaskCompletionSource<bool>();

    /// <summary>
    /// Task che completa quando il dialog viene chiuso
    /// </summary>
    public Task<bool> CompletionTask => _completionSource.Task;

    public ProgressDialog()
    {
        InitializeComponent();
        _stopwatch.Start();
    }

    /// <summary>
    /// Aggiorna il progress del dialog.
    /// </summary>
    /// <param name="current">Numero messaggi processati</param>
    /// <param name="total">Numero totale messaggi (0 se sconosciuto)</param>
    /// <param name="message">Messaggio descrittivo opzionale</param>
    public void UpdateProgress(int current, int total, string? message = null)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Aggiorna contatore
            if (total > 0)
            {
                CounterLabel.Text = $"{current:N0} / {total:N0} messaggi processati";
                ProgressBar.Progress = (double)current / total;
            }
            else
            {
                CounterLabel.Text = $"{current:N0} messaggi processati";
                ProgressBar.Progress = 0; // Indeterminate
            }

            // Aggiorna messaggio se fornito
            if (!string.IsNullOrWhiteSpace(message))
            {
                MessageLabel.Text = message;
            }

            // Calcola e mostra velocità (ogni secondo)
            var now = DateTime.Now;
            var elapsed = (now - _lastUpdateTime).TotalSeconds;
            if (elapsed >= 1.0)
            {
                var messagesPerSecond = (current - _lastProcessedCount) / elapsed;
                SpeedLabel.Text = $"Velocità: {messagesPerSecond:F1} msg/s";

                // Stima tempo rimanente
                if (total > 0 && messagesPerSecond > 0)
                {
                    var remaining = (total - current) / messagesPerSecond;
                    SpeedLabel.Text += $" - Tempo stimato: {TimeSpan.FromSeconds(remaining):mm\\:ss}";
                }

                _lastProcessedCount = current;
                _lastUpdateTime = now;
            }
        });
    }

    /// <summary>
    /// Segna l'operazione come completata con successo.
    /// </summary>
    public void Complete()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _stopwatch.Stop();
            TitleLabel.Text = "✓ Importazione Completata";
            MessageLabel.Text = $"Operazione completata in {_stopwatch.Elapsed:mm\\:ss}";
            ProgressBar.Progress = 1.0;
            CancelButton.Text = "Chiudi";
            CancelButton.BackgroundColor = Colors.Gray;

            // Completa il task
            _completionSource.TrySetResult(true);
        });
    }

    /// <summary>
    /// Segna l'operazione come annullata.
    /// </summary>
    public void SetCancelled()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _stopwatch.Stop();
            TitleLabel.Text = "⚠ Importazione Annullata";
            MessageLabel.Text = "Operazione annullata dall'utente";
            CancelButton.Text = "Chiudi";
            CancelButton.BackgroundColor = Colors.Gray;

            // Completa il task
            _completionSource.TrySetResult(false);
        });
    }

    /// <summary>
    /// Segna l'operazione come fallita.
    /// </summary>
    public void SetError(string errorMessage)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _stopwatch.Stop();
            TitleLabel.Text = "✖ Errore Importazione";
            MessageLabel.Text = errorMessage;
            ProgressBar.ProgressColor = Colors.Red;
            CancelButton.Text = "Chiudi";
            CancelButton.BackgroundColor = Colors.Gray;

            // Completa il task
            _completionSource.TrySetResult(false);
        });
    }

    /// <summary>
    /// Handler per il pulsante Annulla/Chiudi.
    /// </summary>
    private async void OnCancelClicked(object sender, EventArgs e)
    {
        if (CancelButton.Text == "Chiudi")
        {
            // Dialog già completato, chiudi
            await Navigation.PopModalAsync();
        }
        else
        {
            // Richiesta annullamento
            _cancellationTokenSource.Cancel();
            CancelButton.IsEnabled = false;
            CancelButton.Text = "Annullamento...";
            MessageLabel.Text = "Annullamento in corso...";
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _cancellationTokenSource.Cancel();
    }
}
