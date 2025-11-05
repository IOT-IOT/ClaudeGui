using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using Serilog;
using Serilog.Extensions.Logging;
using ClaudeCodeMAUI.Services;
using CommunityToolkit.Maui;

namespace ClaudeCodeMAUI;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
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
			var dbService = new DbService(username, password);
			builder.Services.AddSingleton(dbService);
			builder.Services.AddSingleton(sp => new SessionScannerService(dbService));
		}
		else
		{
			Log.Warning("Database credentials not found in User Secrets");
		}

		// Registra le pagine con factory per dependency injection
		builder.Services.AddSingleton<MainPage>(sp =>
		{
			var config = sp.GetRequiredService<IConfiguration>();
			var dbService = sp.GetService<DbService>();
			var sessionScanner = sp.GetService<SessionScannerService>();
			return new MainPage(config, dbService, sessionScanner);
		});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}

