using Serilog;

namespace ClaudeCodeMAUI.Services;

/// <summary>
/// Servizio per la gestione delle impostazioni dell'applicazione.
/// Utilizza le Preferences di MAUI per persistere le configurazioni.
/// </summary>
public class SettingsService
{
    // ===== CHIAVI PER LE PREFERENCES =====
    private const string KEY_AUTO_SEND_SUMMARY_PROMPT = "AutoSendSummaryPrompt";
    private const string KEY_THEME = "IsDarkTheme";

    /// <summary>
    /// Ottiene o imposta se il prompt di riassunto deve essere inviato automaticamente
    /// quando una sessione viene ripristinata.
    /// Default: true
    /// </summary>
    public bool AutoSendSummaryPrompt
    {
        get
        {
            var value = Preferences.Get(KEY_AUTO_SEND_SUMMARY_PROMPT, true);
            Log.Debug("SettingsService: AutoSendSummaryPrompt = {Value}", value);
            return value;
        }
        set
        {
            Preferences.Set(KEY_AUTO_SEND_SUMMARY_PROMPT, value);
            Log.Information("SettingsService: AutoSendSummaryPrompt impostato a {Value}", value);
        }
    }

    /// <summary>
    /// Ottiene o imposta se il tema scuro è attivo.
    /// Default: true
    /// </summary>
    public bool IsDarkTheme
    {
        get
        {
            var value = Preferences.Get(KEY_THEME, true);
            Log.Debug("SettingsService: IsDarkTheme = {Value}", value);
            return value;
        }
        set
        {
            Preferences.Set(KEY_THEME, value);
            Log.Information("SettingsService: IsDarkTheme impostato a {Value}", value);
        }
    }

    /// <summary>
    /// Resetta tutte le impostazioni ai valori di default.
    /// </summary>
    public void ResetToDefaults()
    {
        Log.Information("SettingsService: Reset di tutte le impostazioni ai valori di default");
        AutoSendSummaryPrompt = true;
        IsDarkTheme = true;
    }

    /// <summary>
    /// Ottiene tutte le impostazioni correnti come dizionario (per debug/logging).
    /// </summary>
    public Dictionary<string, object> GetAllSettings()
    {
        return new Dictionary<string, object>
        {
            { KEY_AUTO_SEND_SUMMARY_PROMPT, AutoSendSummaryPrompt },
            { KEY_THEME, IsDarkTheme }
        };
    }
}
