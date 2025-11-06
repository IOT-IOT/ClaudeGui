using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using Serilog;
using Serilog.Extensions.Logging;
using ClaudeCodeMAUI.Services;
using ClaudeCodeMAUI.Models.Entities;
using CommunityToolkit.Maui;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

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
			// Costruisci connection string per MariaDB
			var connectionString = $"Server=192.168.1.11;Port=3306;Database=ClaudeGui;User={username};Password={password};CharSet=utf8mb4;";

			// Registra Entity Framework Core DbContextFactory (per uso con Singleton services)
			builder.Services.AddDbContextFactory<ClaudeGuiDbContext>(options =>
			{
				options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), mySqlOptions =>
				{
					// Configurazioni opzionali per performance e resilienza
					mySqlOptions.EnableRetryOnFailure(
						maxRetryCount: 3,
						maxRetryDelay: TimeSpan.FromSeconds(5),
						errorNumbersToAdd: null);
					mySqlOptions.CommandTimeout(30);
				});

				// Abilita logging dettagliato per SQL queries in DEBUG
#if DEBUG
				options.EnableSensitiveDataLogging();
				options.EnableDetailedErrors();
#endif
			});

			// Registra DbService con DbContextFactory per migrazione graduale
			// TODO: Rimuovere DbService quando migrazione EF Core sarà completa
			builder.Services.AddSingleton<DbService>(sp =>
			{
				var dbContextFactory = sp.GetRequiredService<IDbContextFactory<ClaudeGuiDbContext>>();
				return new DbService(username, password, dbContextFactory);
			});

			builder.Services.AddSingleton(sp => new SessionScannerService(sp.GetRequiredService<DbService>()));

			Log.Information("Database services registered (EF Core + DbService legacy)");
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

