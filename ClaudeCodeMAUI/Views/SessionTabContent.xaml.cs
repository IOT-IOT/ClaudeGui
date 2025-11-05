using ClaudeCodeMAUI.Models;

namespace ClaudeCodeMAUI.Views
{
    /// <summary>
    /// ContentView che rappresenta il contenuto di un singolo tab sessione.
    /// Ogni tab ha il proprio WebView, barre di stato, e informazioni di contesto.
    /// </summary>
    public partial class SessionTabContent : ContentView
    {
        private SessionTabItem? _sessionTabItem;

        public SessionTabContent()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Imposta il SessionTabItem associato a questo tab content.
        /// Aggiorna le label e il WebView con i dati della sessione.
        /// </summary>
        public void SetSessionTabItem(SessionTabItem sessionTabItem)
        {
            _sessionTabItem = sessionTabItem;

            // Aggiorna le informazioni di sessione
            WorkingDirectoryLabel.Text = $"Working Directory: {sessionTabItem.WorkingDirectory}";
            ContextIdLabel.Text = $"Session ID: {sessionTabItem.SessionId}";

            // Il TokenBudgetLabel verrà aggiornato dinamicamente quando arrivano messaggi "result"
            TokenBudgetLabel.Text = "Context: Ready";
        }

        /// <summary>
        /// Aggiorna la Token Budget Label con le informazioni di contesto.
        /// </summary>
        public void UpdateTokenBudget(string text)
        {
            TokenBudgetLabel.Text = text;
        }

        /// <summary>
        /// Accesso al WebView per aggiornare il contenuto HTML.
        /// </summary>
        public WebView WebView => ConversationWebView;

        /// <summary>
        /// Inizializza la WebView con una pagina HTML completa (inclusi messaggi storici).
        /// </summary>
        /// <param name="fullHtmlPage">Pagina HTML completa da visualizzare</param>
        public void InitializeWebView(string fullHtmlPage)
        {
            ConversationWebView.Source = new HtmlWebViewSource
            {
                Html = fullHtmlPage
            };
        }
    }
}
