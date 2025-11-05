using ClaudeCodeMAUI.Extensions;
using ClaudeCodeMAUI.Services;
using Serilog;

namespace ClaudeCodeMAUI.Views
{
    /// <summary>
    /// Dialog per assegnare un nome a una sessione esistente.
    /// Utilizzato dalla SessionSelectorPage per dare un nome descrittivo alle sessioni senza nome.
    /// </summary>
    public partial class AssignNameDialog : ContentPage
    {
        private readonly string _sessionId;
        private readonly DbService _dbService;

        /// <summary>
        /// Indica se l'utente ha assegnato un nome (true) o ha cancellato (false)
        /// </summary>
        public bool WasNameAssigned { get; private set; } = false;

        /// <summary>
        /// Nome assegnato alla sessione (null se l'utente ha cancellato)
        /// </summary>
        public string? AssignedName { get; private set; }

        public AssignNameDialog(string sessionId, DbService dbService)
        {
            InitializeComponent();

            _sessionId = sessionId;
            _dbService = dbService;

            // Mostra il Session ID nella UI per riferimento
            SessionIdLabel.Text = $"Session ID: {sessionId}";

            // Focus automatico sul campo di input
            _ = Task.Run(async () =>
            {
                await Task.Delay(300); // Piccolo delay per permettere il rendering della pagina
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    NameEntry.Focus();
                });
            });
        }

        /// <summary>
        /// Handler per il cambio di testo nel campo Nome.
        /// Abilita il pulsante "Salva" solo se il nome non è vuoto.
        /// </summary>
        private void OnNameEntryTextChanged(object? sender, TextChangedEventArgs e)
        {
            // Abilita il pulsante Salva solo se il nome non è vuoto
            SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(NameEntry.Text);
        }

        /// <summary>
        /// Handler per il pulsante "Salva".
        /// Aggiorna il nome della sessione nel database e chiude il dialog.
        /// </summary>
        private async void OnSaveClicked(object? sender, EventArgs e)
        {
            try
            {
                var name = NameEntry.Text?.Trim();

                // Validazione: nome obbligatorio
                if (string.IsNullOrWhiteSpace(name))
                {
                    await this.DisplaySelectableAlert("Errore", "Devi inserire un nome per la sessione.", "OK");
                    return;
                }

                Log.Information("Assigning name '{Name}' to session: {SessionId}", name, _sessionId);

                // Aggiorna il nome nel database
                await _dbService.UpdateSessionNameAsync(_sessionId, name);

                Log.Information("Session name updated successfully");

                // Notifica successo all'utente
                await this.DisplaySelectableAlert("Successo", $"Il nome '{name}' è stato assegnato alla sessione.", "OK");

                // Imposta i valori di ritorno
                WasNameAssigned = true;
                AssignedName = name;

                // Chiudi il dialog
                await Navigation.PopModalAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to assign name to session: {SessionId}", _sessionId);
                await this.DisplaySelectableAlert("Errore", $"Impossibile assegnare il nome:\n{ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Handler per il pulsante "Annulla".
        /// Chiude il dialog senza salvare alcun nome.
        /// </summary>
        private async void OnCancelClicked(object? sender, EventArgs e)
        {
            Log.Information("Assign name dialog cancelled by user");
            WasNameAssigned = false;
            AssignedName = null;
            await Navigation.PopModalAsync();
        }
    }
}
