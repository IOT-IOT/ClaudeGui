using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using Serilog;
using Serilog.Extensions.Logging;
using ClaudeCodeMAUI.Services;

namespace ClaudeCodeMAUI;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Configura Configuration con appsettings.json e User Secrets
		var assembly = Assembly.GetExecutingAssembly();
		using var stream = assembly.GetManifestResourceStream("ClaudeCodeMAUI.appsettings.json");

		var config = new ConfigurationBuilder()
			.AddJsonStream(stream)
			.AddUserSecrets(assembly)
			.Build();

		builder.Configuration.AddConfiguration(config);

		// Configura Serilog
		Log.Logger = new LoggerConfiguration()
			.ReadFrom.Configuration(config)
			.CreateLogger();

		builder.Logging.ClearProviders();
		builder.Logging.AddSerilog(Log.Logger);

		Log.Information("========================================");
		Log.Information("ClaudeCodeMAUI Application Starting");
		Log.Information("========================================");

		// Registra servizi in Dependency Injection
		var username = config["DatabaseCredentials:Username"];
		var password = config["DatabaseCredentials:Password"];

		if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
		{
			builder.Services.AddSingleton(sp => new DbService(username, password));
		}
		else
		{
			Log.Warning("Database credentials not found in User Secrets");
		}

		// Registra le pagine
		builder.Services.AddSingleton<MainPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}

