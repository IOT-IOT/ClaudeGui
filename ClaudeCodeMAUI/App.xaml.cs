using Serilog;
using ClaudeCodeMAUI.Services;

namespace ClaudeCodeMAUI;

public partial class App : Application
{
	private DbService? _dbService;
	private string? _currentSessionId;
	private SettingsService _settingsService;
	private Window? _mainWindow;

	public App()
	{
		InitializeComponent();
		_settingsService = new SettingsService();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var appShell = new AppShell();

		_mainWindow = new Window(appShell);

		// Inizializza il ToastService (il container sarà aggiunto a MainPage)
		// Per ora lascia vuoto, verrà inizializzato quando MainPage viene creata
		ToastService.Instance.Initialize(null);

		// Carica e applica la posizione e dimensione salvate della finestra
		RestoreWindowBounds();

		// Sottoscrivi agli eventi per salvare posizione/dimensione quando cambiano
		SubscribeToWindowEvents();

		return _mainWindow;
	}

	/// <summary>
	/// Imposta il DbService e il session ID corrente per gestire la chiusura graceful.
	/// Chiamato dal MainPage quando viene inizializzata una sessione.
	/// </summary>
	public void SetCurrentSession(DbService dbService, string? sessionId)
	{
		_dbService = dbService;
		_currentSessionId = sessionId;
		Log.Information("App: Current session set to {SessionId}", sessionId ?? "null");
	}

	/// <summary>
	/// Ripristina la posizione e dimensione della finestra dalle impostazioni salvate.
	/// Se non ci sono impostazioni salvate, usa i valori di default.
	/// </summary>
	private void RestoreWindowBounds()
	{
		if (_mainWindow == null) return;

		try
		{
			// Recupera la posizione salvata
			var position = _settingsService.GetWindowPosition();
			if (position.HasValue)
			{
				_mainWindow.X = position.Value.X;
				_mainWindow.Y = position.Value.Y;
				Log.Information("App: Posizione finestra ripristinata - X={X}, Y={Y}", position.Value.X, position.Value.Y);
			}

			// Recupera le dimensioni salvate
			var size = _settingsService.GetWindowSize();
			if (size.HasValue)
			{
				_mainWindow.Width = Math.Max(size.Value.Width, 200);
				_mainWindow.Height = Math.Max(size.Value.Height, 200);
				Log.Information("App: Dimensioni finestra ripristinate - Width={Width}, Height={Height}", size.Value.Width, size.Value.Height);
			}
			else
			{
				// Dimensioni di default se non ci sono impostazioni salvate
				_mainWindow.Width = 1400;
				_mainWindow.Height = 900;
				Log.Information("App: Usate dimensioni di default - Width=1400, Height=900");
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex, "App: Errore durante il ripristino dei bounds della finestra");
		}
	}

	/// <summary>
	/// Sottoscrivi agli eventi della finestra per salvare posizione e dimensione quando cambiano.
	/// </summary>
	private void SubscribeToWindowEvents()
	{
		if (_mainWindow == null) return;

		// Salva la posizione quando cambia
		_mainWindow.PropertyChanged += (sender, e) =>
		{
			if (e.PropertyName == nameof(Window.X) || e.PropertyName == nameof(Window.Y))
			{
				SaveWindowPosition();
			}
			else if (e.PropertyName == nameof(Window.Width) || e.PropertyName == nameof(Window.Height))
			{
				SaveWindowSize();
			}
		};

		Log.Information("App: Sottoscritto agli eventi della finestra per il salvataggio automatico");
	}

	/// <summary>
	/// Salva la posizione corrente della finestra nelle impostazioni.
	/// Non salva se la finestra è minimizzata (valori X/Y < -1000).
	/// </summary>
	private void SaveWindowPosition()
	{
        // Non salvare se la finestra è minimizzata (coordinate negative anomale)
        if (_mainWindow == null ||_mainWindow.X < -10000 || _mainWindow.Y < -10000) return;

		try
		{
			

			_settingsService.SaveWindowPosition(_mainWindow.X, _mainWindow.Y);
		}
		catch (Exception ex)
		{
			Log.Error(ex, "App: Errore durante il salvataggio della posizione della finestra");
		}
	}

	/// <summary>
	/// Salva le dimensioni correnti della finestra nelle impostazioni.
	/// </summary>
	private void SaveWindowSize()
	{
        // Non salvare se la finestra è minimizzata (coordinate negative anomale)
        if (_mainWindow == null ||_mainWindow.X < -10000 || _mainWindow.Y < -10000) return;

		try
		{
			_settingsService.SaveWindowSize(_mainWindow.Width, _mainWindow.Height);
		}
		catch (Exception ex)
		{
			Log.Error(ex, "App: Errore durante il salvataggio delle dimensioni della finestra");
		}
	}

	/// <summary>
	/// Chiamato quando l'applicazione va in background o viene chiusa.
	/// Marca la sessione corrente come "closed" nel database per recovery futuro.
	/// </summary>
	protected override void OnSleep()
	{
		base.OnSleep();

		Log.Information("App: OnSleep called");

		// Salva la posizione e dimensione finali prima di chiudere
		SaveWindowPosition();
		SaveWindowSize();




		//Commentato perchè una sessione può essere chiusa solo dall'utente
		//// Marca la sessione corrente come closed se presente
		//if (_dbService != null && !string.IsNullOrEmpty(_currentSessionId))
		//{
		//	try
		//	{
		//		Log.Information("App: Marking session {SessionId} as closed", _currentSessionId);
		//		// Operazione sincrona perché OnSleep deve completare velocemente
		//		_dbService.UpdateStatusAsync(_currentSessionId, "closed").Wait(TimeSpan.FromSeconds(2));
		//		Log.Information("App: Session {SessionId} marked as closed successfully", _currentSessionId);
		//	}
		//	catch (Exception ex)
		//	{
		//		Log.Error(ex, "App: Failed to mark session as closed");
		//	}
		//}
		//else
		//{
		//	Log.Information("App: No active session to close");
		//}
	}
}