using Serilog;
using ClaudeCodeMAUI.Services;

namespace ClaudeCodeMAUI;

public partial class App : Application
{
	private DbService? _dbService;
	private string? _currentSessionId;

	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
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
	/// Chiamato quando l'applicazione va in background o viene chiusa.
	/// Marca la sessione corrente come "closed" nel database per recovery futuro.
	/// </summary>
	protected override void OnSleep()
	{
		base.OnSleep();

		Log.Information("App: OnSleep called");

		// Marca la sessione corrente come closed se presente
		if (_dbService != null && !string.IsNullOrEmpty(_currentSessionId))
		{
			try
			{
				Log.Information("App: Marking session {SessionId} as closed", _currentSessionId);
				// Operazione sincrona perché OnSleep deve completare velocemente
				_dbService.UpdateStatusAsync(_currentSessionId, "closed").Wait(TimeSpan.FromSeconds(2));
				Log.Information("App: Session {SessionId} marked as closed successfully", _currentSessionId);
			}
			catch (Exception ex)
			{
				Log.Error(ex, "App: Failed to mark session as closed");
			}
		}
		else
		{
			Log.Information("App: No active session to close");
		}
	}
}