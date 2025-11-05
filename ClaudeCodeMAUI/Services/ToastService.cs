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
            System.Diagnostics.Debug.WriteLine("=== ToastService: Inizio ricerca container ===");

            // 1. Prova con Window corrente (raccomandato per .NET 9)
            var window = Application.Current?.Windows?.FirstOrDefault();
            if (window != null)
            {
                System.Diagnostics.Debug.WriteLine($"ToastService: Window trovata: {window.GetType().Name}");

                // Verifica se ci sono dialog modali
                var modalStack = window.Page?.Navigation?.ModalStack;
                if (modalStack != null && modalStack.Count > 0)
                {
                    var modalPage = modalStack.LastOrDefault();
                    System.Diagnostics.Debug.WriteLine($"ToastService: Modal stack contiene {modalStack.Count} pagine, ultima: {modalPage?.GetType().Name}");

                    if (modalPage != null)
                    {
                        // Se il modale è un NavigationPage, cerca nella sua CurrentPage
                        Page targetPage = modalPage;
                        if (modalPage is NavigationPage navPage && navPage.CurrentPage != null)
                        {
                            targetPage = navPage.CurrentPage;
                            System.Diagnostics.Debug.WriteLine($"ToastService: NavigationPage rilevata, uso CurrentPage: {targetPage.GetType().Name}");
                        }

                        var container = FindToastContainerInView(targetPage);
                        if (container != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"ToastService: ✓ Trovato container nella pagina modale: {targetPage.GetType().Name}");
                            return container;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"ToastService: ✗ Container NON trovato nella pagina modale: {targetPage.GetType().Name}");
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ToastService: Nessun dialog modale aperto");
                }

                // Se non ci sono modal o non hanno container, usa la pagina principale
                if (window.Page is Shell shell)
                {
                    var currentPage = shell.CurrentPage;
                    System.Diagnostics.Debug.WriteLine($"ToastService: Shell.CurrentPage: {currentPage?.GetType().Name}");

                    if (currentPage != null)
                    {
                        var container = FindToastContainerInView(currentPage);
                        if (container != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"ToastService: ✓ Trovato container in Shell.CurrentPage");
                            return container;
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"ToastService: Window.Page non è Shell: {window.Page?.GetType().Name}");
                }
            }

            // Fallback: usa il container inizializzato
            if (_toastContainer != null)
            {
                System.Diagnostics.Debug.WriteLine("ToastService: ✓ Uso container inizializzato (fallback)");
                return _toastContainer;
            }

            System.Diagnostics.Debug.WriteLine("ToastService: ✗ Nessun container trovato!");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ToastService: ✗ ERRORE: {ex.Message}\n{ex.StackTrace}");
            return _toastContainer; // Fallback
        }
    }

    /// <summary>
    /// Cerca ricorsivamente un VerticalStackLayout con StyleId="ToastContainer" nella gerarchia visuale
    /// </summary>
    private VerticalStackLayout? FindToastContainerInView(Element view)
    {
        System.Diagnostics.Debug.WriteLine($"  - Analizzando: {view.GetType().Name} (StyleId: {view.StyleId})");

        // Cerca un VerticalStackLayout con StyleId="ToastContainer"
        if (view is VerticalStackLayout vsl && vsl.StyleId == "ToastContainer")
        {
            System.Diagnostics.Debug.WriteLine($"  ✓ TROVATO ToastContainer!");
            return vsl;
        }

        // Cerca nei figli se è un Layout
        if (view is Layout layout)
        {
            System.Diagnostics.Debug.WriteLine($"  → È un Layout con {layout.Children.Count} figli");
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
            System.Diagnostics.Debug.WriteLine($"  → ContentPage, cerco nel Content");
            return FindToastContainerInView(page.Content);
        }

        // Cerca nel Content se è un ScrollView
        if (view is ScrollView scrollView && scrollView.Content != null)
        {
            System.Diagnostics.Debug.WriteLine($"  → ScrollView, cerco nel Content");
            return FindToastContainerInView(scrollView.Content);
        }

        return null;
    }
}
