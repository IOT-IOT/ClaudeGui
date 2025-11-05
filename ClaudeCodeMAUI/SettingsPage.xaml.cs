using ClaudeCodeMAUI.Extensions;
using ClaudeCodeMAUI.Services;
using Serilog;

namespace ClaudeCodeMAUI;

/// <summary>
/// Pagina delle impostazioni dell'applicazione.
/// Permette di configurare comportamenti come l'invio automatico del prompt di riassunto
/// e il tema dell'interfaccia.
/// </summary>
public partial class SettingsPage : ContentPage
{
    private readonly SettingsService _settingsService;
    private readonly Action? _onSettingsChanged;

    /// <summary>
    /// Costruttore della pagina Settings.
    /// </summary>
    /// <param name="settingsService">Il servizio per gestire le impostazioni</param>
    /// <param name="onSettingsChanged">Callback opzionale da chiamare quando le impostazioni cambiano</param>
    public SettingsPage(SettingsService settingsService, Action? onSettingsChanged = null)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _onSettingsChanged = onSettingsChanged;

        Log.Information("SettingsPage: Inizializzata");

        // Carica le impostazioni correnti e aggiorna l'UI
        LoadCurrentSettings();

        // Leggi la versione dal BuildVersion.txt
        LoadVersion();
    }

    /// <summary>
    /// Carica le impostazioni correnti dal SettingsService e aggiorna i controlli UI.
    /// </summary>
    private void LoadCurrentSettings()
    {
        SwitchShowResumeDialog.IsToggled = _settingsService.ShowResumeDialog;
        SwitchAutoSummary.IsToggled = _settingsService.AutoSendSummaryPrompt;
        SwitchDarkTheme.IsToggled = _settingsService.IsDarkTheme;
        SwitchPlayBeep.IsToggled = _settingsService.PlayBeepOnMetadata;
        SliderHistoryCount.Value = _settingsService.HistoryMessageCount;
        LblHistoryCount.Text = _settingsService.HistoryMessageCount.ToString();

        Log.Debug("SettingsPage: Impostazioni caricate - ShowResumeDialog={ShowResumeDialog}, AutoSummary={AutoSummary}, DarkTheme={DarkTheme}, PlayBeep={PlayBeep}, HistoryCount={HistoryCount}",
            SwitchShowResumeDialog.IsToggled, SwitchAutoSummary.IsToggled, SwitchDarkTheme.IsToggled, SwitchPlayBeep.IsToggled, _settingsService.HistoryMessageCount);
    }

    /// <summary>
    /// Legge la versione dal file BuildVersion.txt nella root del progetto.
    /// </summary>
    private void LoadVersion()
    {
        try
        {
            // Il file BuildVersion.txt dovrebbe essere nella root del progetto
            var versionFile = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "BuildVersion.txt");

            if (File.Exists(versionFile))
            {
                var version = File.ReadAllText(versionFile).Trim();
                LblVersion.Text = $"Versione: {version}";
                Log.Information("SettingsPage: Versione caricata: {Version}", version);
            }
            else
            {
                Log.Warning("SettingsPage: File BuildVersion.txt non trovato in {Path}", versionFile);
                LblVersion.Text = "Versione: Sconosciuta";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsPage: Errore durante il caricamento della versione");
            LblVersion.Text = "Versione: Errore";
        }
    }

    /// <summary>
    /// Handler per il toggle dell'impostazione "Show Resume Dialog".
    /// </summary>
    private void OnShowResumeDialogToggled(object sender, ToggledEventArgs e)
    {
        _settingsService.ShowResumeDialog = e.Value;
        Log.Information("SettingsPage: ShowResumeDialog modificato a {Value}", e.Value);

        // Notifica il cambiamento
        _onSettingsChanged?.Invoke();
    }

    /// <summary>
    /// Handler per il toggle dell'impostazione "Auto Send Summary Prompt".
    /// </summary>
    private void OnAutoSummaryToggled(object sender, ToggledEventArgs e)
    {
        _settingsService.AutoSendSummaryPrompt = e.Value;
        Log.Information("SettingsPage: AutoSendSummaryPrompt modificato a {Value}", e.Value);

        // Notifica il cambiamento
        _onSettingsChanged?.Invoke();
    }

    /// <summary>
    /// Handler per il toggle dell'impostazione "Dark Theme".
    /// </summary>
    private void OnDarkThemeToggled(object sender, ToggledEventArgs e)
    {
        _settingsService.IsDarkTheme = e.Value;
        Log.Information("SettingsPage: IsDarkTheme modificato a {Value}", e.Value);

        // Notifica il cambiamento (la MainPage dovrà aggiornare il tema della WebView)
        _onSettingsChanged?.Invoke();
    }

    /// <summary>
    /// Handler per il toggle dell'impostazione "Play Beep on Metadata".
    /// </summary>
    private void OnPlayBeepToggled(object sender, ToggledEventArgs e)
    {
        _settingsService.PlayBeepOnMetadata = e.Value;
        Log.Information("SettingsPage: PlayBeepOnMetadata modificato a {Value}", e.Value);

        // Notifica il cambiamento
        _onSettingsChanged?.Invoke();
    }

    /// <summary>
    /// Handler per il cambio del valore dello slider "History Message Count".
    /// Aggiorna sia l'impostazione che la label con il valore corrente.
    /// </summary>
    private void OnHistoryCountChanged(object sender, ValueChangedEventArgs e)
    {
        // Arrotonda il valore a un intero
        int newValue = (int)Math.Round(e.NewValue);

        // Aggiorna la label con il nuovo valore
        LblHistoryCount.Text = newValue.ToString();

        // Salva il nuovo valore nelle impostazioni
        _settingsService.HistoryMessageCount = newValue;

        Log.Information("SettingsPage: HistoryMessageCount modificato a {Value}", newValue);

        // Notifica il cambiamento
        _onSettingsChanged?.Invoke();
    }

    /// <summary>
    /// Handler per il pulsante "Reset to Defaults".
    /// Ripristina tutte le impostazioni ai valori di default.
    /// </summary>
    private async void OnResetToDefaults(object sender, EventArgs e)
    {
        // Chiedi conferma all'utente
        bool confirm = await this.DisplaySelectableAlert(
            "Ripristina impostazioni",
            "Sei sicuro di voler ripristinare tutte le impostazioni ai valori predefiniti?",
            "Sì",
            "Annulla");

        if (confirm)
        {
            _settingsService.ResetToDefaults();
            LoadCurrentSettings();
            _onSettingsChanged?.Invoke();

            await this.DisplaySelectableAlert("Successo", "Le impostazioni sono state ripristinate ai valori predefiniti.", "OK");
            Log.Information("SettingsPage: Impostazioni resettate ai valori di default");
        }
    }

    /// <summary>
    /// Handler per il pulsante "Close".
    /// Chiude la pagina delle impostazioni e torna alla MainPage.
    /// </summary>
    private async void OnClose(object sender, EventArgs e)
    {
        Log.Information("SettingsPage: Chiusura richiesta");
        await Navigation.PopModalAsync();
    }
}
