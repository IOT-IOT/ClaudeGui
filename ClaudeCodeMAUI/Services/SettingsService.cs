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
    private const string KEY_PLAY_BEEP_ON_METADATA = "PlayBeepOnMetadata";
    private const string KEY_SHOW_RESUME_DIALOG = "ShowResumeDialog";
    private const string KEY_HISTORY_MESSAGE_COUNT = "HistoryMessageCount";
    private const string KEY_WINDOW_X = "WindowX";
    private const string KEY_WINDOW_Y = "WindowY";
    private const string KEY_WINDOW_WIDTH = "WindowWidth";
    private const string KEY_WINDOW_HEIGHT = "WindowHeight";

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
    /// Ottiene o imposta se riprodurre un beep sonoro quando arrivano i metadata.
    /// Default: true
    /// </summary>
    public bool PlayBeepOnMetadata
    {
        get
        {
            var value = Preferences.Get(KEY_PLAY_BEEP_ON_METADATA, true);
            Log.Debug("SettingsService: PlayBeepOnMetadata = {Value}", value);
            return value;
        }
        set
        {
            Preferences.Set(KEY_PLAY_BEEP_ON_METADATA, value);
            Log.Information("SettingsService: PlayBeepOnMetadata impostato a {Value}", value);
        }
    }

    /// <summary>
    /// Ottiene o imposta se mostrare il dialog di ripristino sessione all'avvio.
    /// Se false, la sessione viene ripristinata automaticamente senza chiedere conferma.
    /// Default: false
    /// </summary>
    public bool ShowResumeDialog
    {
        get
        {
            var value = Preferences.Get(KEY_SHOW_RESUME_DIALOG, false);
            Log.Debug("SettingsService: ShowResumeDialog = {Value}", value);
            return value;
        }
        set
        {
            Preferences.Set(KEY_SHOW_RESUME_DIALOG, value);
            Log.Information("SettingsService: ShowResumeDialog impostato a {Value}", value);
        }
    }

    /// <summary>
    /// Ottiene o imposta il numero di messaggi storici da visualizzare al ripristino sessione.
    /// Valore compreso tra 0 (nessun messaggio storico) e 50 (massimo).
    /// Default: 10
    /// </summary>
    public int HistoryMessageCount
    {
        get
        {
            var value = Preferences.Get(KEY_HISTORY_MESSAGE_COUNT, 10);
            Log.Debug("SettingsService: HistoryMessageCount = {Value}", value);
            return value;
        }
        set
        {
            // Clamp il valore tra 0 e 50
            var clampedValue = Math.Max(0, Math.Min(50, value));
            Preferences.Set(KEY_HISTORY_MESSAGE_COUNT, clampedValue);
            Log.Information("SettingsService: HistoryMessageCount impostato a {Value}", clampedValue);
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
        PlayBeepOnMetadata = true;
        ShowResumeDialog = false;
        HistoryMessageCount = 10;
    }

    /// <summary>
    /// Salva la posizione della finestra principale.
    /// </summary>
    /// <param name="x">Coordinata X (distanza dal bordo sinistro dello schermo)</param>
    /// <param name="y">Coordinata Y (distanza dal bordo superiore dello schermo)</param>
    public void SaveWindowPosition(double x, double y)
    {
        
        //if (x < 0)
        //{
        //    x = 0;

        //}

        //if (y < 0)
        //{
        //    y = 0;

        //}
        Preferences.Set(KEY_WINDOW_X, x);
        Preferences.Set(KEY_WINDOW_Y, y);
        Log.Debug("SettingsService: Posizione finestra salvata - X={X}, Y={Y}", x, y);
    }

    /// <summary>
    /// Salva le dimensioni della finestra principale.
    /// </summary>
    /// <param name="width">Larghezza della finestra</param>
    /// <param name="height">Altezza della finestra</param>
    public void SaveWindowSize(double width, double height)
    {
        Preferences.Set(KEY_WINDOW_WIDTH, width);
        Preferences.Set(KEY_WINDOW_HEIGHT, height);
        Log.Debug("SettingsService: Dimensioni finestra salvate - Width={Width}, Height={Height}", width, height);
    }

    /// <summary>
    /// Recupera la posizione salvata della finestra.
    /// Restituisce null se non è mai stata salvata.
    /// </summary>
    /// <returns>Tupla (X, Y) se salvata, altrimenti null</returns>
    public (double X, double Y)? GetWindowPosition()
    {
        if (Preferences.ContainsKey(KEY_WINDOW_X) && Preferences.ContainsKey(KEY_WINDOW_Y))
        {
            var x = Preferences.Get(KEY_WINDOW_X, 0.0);
            var y = Preferences.Get(KEY_WINDOW_Y, 0.0);
            Log.Debug("SettingsService: Posizione finestra recuperata - X={X}, Y={Y}", x, y);
            return (x, y);
        }
        Log.Debug("SettingsService: Nessuna posizione finestra salvata");
        return null;
    }

    /// <summary>
    /// Recupera le dimensioni salvate della finestra.
    /// Restituisce null se non sono mai state salvate.
    /// </summary>
    /// <returns>Tupla (Width, Height) se salvata, altrimenti null</returns>
    public (double Width, double Height)? GetWindowSize()
    {
        if (Preferences.ContainsKey(KEY_WINDOW_WIDTH) && Preferences.ContainsKey(KEY_WINDOW_HEIGHT))
        {
            var width = Preferences.Get(KEY_WINDOW_WIDTH, 0.0);
            var height = Preferences.Get(KEY_WINDOW_HEIGHT, 0.0);
            Log.Debug("SettingsService: Dimensioni finestra recuperate - Width={Width}, Height={Height}", width, height);
            return (width, height);
        }
        Log.Debug("SettingsService: Nessuna dimensione finestra salvata");
        return null;
    }

    /// <summary>
    /// Ottiene tutte le impostazioni correnti come dizionario (per debug/logging).
    /// </summary>
    public Dictionary<string, object> GetAllSettings()
    {
        return new Dictionary<string, object>
        {
            { KEY_AUTO_SEND_SUMMARY_PROMPT, AutoSendSummaryPrompt },
            { KEY_THEME, IsDarkTheme },
            { KEY_PLAY_BEEP_ON_METADATA, PlayBeepOnMetadata },
            { KEY_SHOW_RESUME_DIALOG, ShowResumeDialog },
            { KEY_HISTORY_MESSAGE_COUNT, HistoryMessageCount }
        };
    }
}
