using ClaudeCodeMAUI.Views;

namespace ClaudeCodeMAUI.Services;

/// <summary>
/// Servizio singleton per mostrare toast notification non bloccanti.
/// I toast appaiono in basso a destra, si sovrappongono se multipli e scompaiono automaticamente.
/// </summary>
public class ToastService
{
    private static ToastService? _instance;
    private static readonly object _lock = new object();

    private VerticalStackLayout? _toastContainer;

    /// <summary>
    /// Ottiene l'istanza singleton del ToastService
    /// </summary>
    public static ToastService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new ToastService();
                    }
                }
            }
            return _instance;
        }
    }

    private ToastService()
    {
        // Costruttore privato per pattern Singleton
    }

    /// <summary>
    /// Inizializza il servizio con il container dove verranno mostrati i toast.
    /// Deve essere chiamato all'avvio dell'applicazione (es. da App.xaml.cs o MainPage)
    /// </summary>
    /// <param name="container">Container VerticalStackLayout dove inserire i toast (può essere null)</param>
    public void Initialize(VerticalStackLayout? container)
    {
        _toastContainer = container;
    }

    /// <summary>
    /// Verifica se il servizio è stato inizializzato con un container valido
    /// </summary>
    public bool IsInitialized => _toastContainer != null;

    /// <summary>
    /// Mostra un toast di successo (verde con ✓)
    /// </summary>
    /// <param name="message">Messaggio da visualizzare</param>
    /// <param name="durationMs">Durata in millisecondi (default: 2500ms)</param>
    public void ShowSuccess(string message, int durationMs = 2500)
    {
        Show(message, ToastType.Success, durationMs);
    }

    /// <summary>
    /// Mostra un toast di errore (rosso con ✗)
    /// </summary>
    /// <param name="message">Messaggio da visualizzare</param>
    /// <param name="durationMs">Durata in millisecondi (default: 2500ms)</param>
    public void ShowError(string message, int durationMs = 2500)
    {
        Show(message, ToastType.Error, durationMs);
    }

    /// <summary>
    /// Mostra un toast informativo (blu con ℹ)
    /// </summary>
    /// <param name="message">Messaggio da visualizzare</param>
    /// <param name="durationMs">Durata in millisecondi (default: 2500ms)</param>
    public void ShowInfo(string message, int durationMs = 2500)
    {
        Show(message, ToastType.Info, durationMs);
    }

    /// <summary>
    /// Mostra un toast di warning (giallo con ⚠)
    /// </summary>
    /// <param name="message">Messaggio da visualizzare</param>
    /// <param name="durationMs">Durata in millisecondi (default: 2500ms)</param>
    public void ShowWarning(string message, int durationMs = 2500)
    {
        Show(message, ToastType.Warning, durationMs);
    }

    /// <summary>
    /// Mostra un toast generico con tipo personalizzato
    /// </summary>
    /// <param name="message">Messaggio da visualizzare</param>
    /// <param name="type">Tipo di toast (Success, Error, Info, Warning)</param>
    /// <param name="durationMs">Durata in millisecondi (default: 2500ms)</param>
    public void Show(string message, ToastType type = ToastType.Success, int durationMs = 2500)
    {
        if (_toastContainer == null)
        {
            // Container non inizializzato, log errore o ignora
            System.Diagnostics.Debug.WriteLine("ToastService: Container non inizializzato. Impossibile mostrare toast.");
            return;
        }

        // Esegui su UI thread
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var toast = new ToastNotification(message, type, durationMs);

            // Aggiungi al container (in cima per ordine corretto: più recente in alto)
            _toastContainer.Children.Insert(0, toast);

            // Mostra con animazione
            await toast.ShowAsync();

            // Rimuovi dal container dopo l'animazione
            _toastContainer.Children.Remove(toast);
        });
    }
}
