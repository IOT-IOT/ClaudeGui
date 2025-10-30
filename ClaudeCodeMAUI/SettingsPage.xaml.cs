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
        SwitchAutoSummary.IsToggled = _settingsService.AutoSendSummaryPrompt;
        SwitchDarkTheme.IsToggled = _settingsService.IsDarkTheme;
        SwitchPlayBeep.IsToggled = _settingsService.PlayBeepOnMetadata;

        Log.Debug("SettingsPage: Impostazioni caricate - AutoSummary={AutoSummary}, DarkTheme={DarkTheme}, PlayBeep={PlayBeep}",
            SwitchAutoSummary.IsToggled, SwitchDarkTheme.IsToggled, SwitchPlayBeep.IsToggled);
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
                LblVersion.Text = $"Version: {version}";
                Log.Information("SettingsPage: Versione caricata: {Version}", version);
            }
            else
            {
                Log.Warning("SettingsPage: File BuildVersion.txt non trovato in {Path}", versionFile);
                LblVersion.Text = "Version: Unknown";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsPage: Errore durante il caricamento della versione");
            LblVersion.Text = "Version: Error";
        }
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
    /// Handler per il pulsante "Reset to Defaults".
    /// Ripristina tutte le impostazioni ai valori di default.
    /// </summary>
    private async void OnResetToDefaults(object sender, EventArgs e)
    {
        // Chiedi conferma all'utente
        bool confirm = await DisplayAlert(
            "Reset Settings",
            "Are you sure you want to reset all settings to their default values?",
            "Yes",
            "Cancel");

        if (confirm)
        {
            _settingsService.ResetToDefaults();
            LoadCurrentSettings();
            _onSettingsChanged?.Invoke();

            await DisplayAlert("Success", "Settings have been reset to defaults.", "OK");
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
