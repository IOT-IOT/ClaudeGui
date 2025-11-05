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
        // Esegui su UI thread
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // Trova il container appropriato (dialog modale o MainPage)
            var container = FindAppropriateToastContainer();

            if (container == null)
            {
                System.Diagnostics.Debug.WriteLine("ToastService: Nessun container disponibile per mostrare toast.");
                return;
            }

            var toast = new ToastNotification(message, type, durationMs);

            // Aggiungi al container (in cima per ordine corretto: più recente in alto)
            container.Children.Insert(0, toast);

            // Mostra con animazione
            await toast.ShowAsync();

            // Rimuovi dal container dopo l'animazione
            container.Children.Remove(toast);
        });
    }

    /// <summary>
    /// Trova il ToastContainer appropriato cercando nella pagina corrente (inclusi dialog modali)
    /// o fallback al container inizializzato
    /// </summary>
    private VerticalStackLayout? FindAppropriateToastContainer()
    {
        try
        {
            // 1. Verifica se c'è un dialog modale aperto
            var currentPage = Application.Current?.MainPage?.Navigation?.ModalStack?.LastOrDefault();

            // 2. Se non ci sono modal, usa la pagina corrente
            if (currentPage == null)
            {
                var shell = Application.Current?.MainPage as Shell;
                currentPage = shell?.CurrentPage;
            }

            // 3. Cerca un ToastContainer nella pagina corrente
            if (currentPage != null)
            {
                var container = FindToastContainerInView(currentPage);
                if (container != null)
                {
                    System.Diagnostics.Debug.WriteLine($"ToastService: Trovato container nella pagina corrente: {currentPage.GetType().Name}");
                    return container;
                }
            }

            // 4. Fallback: usa il container inizializzato (MainPage)
            if (_toastContainer != null)
            {
                System.Diagnostics.Debug.WriteLine("ToastService: Uso container inizializzato (MainPage)");
                return _toastContainer;
            }

            System.Diagnostics.Debug.WriteLine("ToastService: Nessun container trovato");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ToastService: Errore nel trovare container: {ex.Message}");
            return _toastContainer; // Fallback
        }
    }

    /// <summary>
    /// Cerca ricorsivamente un VerticalStackLayout con StyleId="ToastContainer" nella gerarchia visuale
    /// </summary>
    private VerticalStackLayout? FindToastContainerInView(Element view)
    {
        // Cerca un VerticalStackLayout con StyleId="ToastContainer"
        if (view is VerticalStackLayout vsl && vsl.StyleId == "ToastContainer")
            return vsl;

        // Cerca nei figli se è un Layout
        if (view is Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child is Element element)
                {
                    var result = FindToastContainerInView(element);
                    if (result != null) return result;
                }
            }
        }

        // Cerca nel Content se è una ContentPage
        if (view is ContentPage page && page.Content != null)
        {
            return FindToastContainerInView(page.Content);
        }

        // Cerca nel Content se è un ScrollView
        if (view is ScrollView scrollView && scrollView.Content != null)
        {
            return FindToastContainerInView(scrollView.Content);
        }

        return null;
    }
}
