using ClaudeGui.Blazor.Data;
using ClaudeGui.Blazor.Services;
using ClaudeGui.Blazor.Hubs;
using ClaudeGui.Blazor;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Configura Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/claudegui-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// SignalR
builder.Services.AddSignalR();

// Database (DbContextFactory per thread safety con SignalR)
var connectionString = builder.Configuration.GetConnectionString("ClaudeGuiDb");
if (connectionString != null)
{
    builder.Services.AddDbContextFactory<ClaudeGuiDbContext>(options =>
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
            mysqlOptions =>
            {
                mysqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
            })
    );
}

// Services (singleton per condivisione state tra SignalR connections)
builder.Services.AddSingleton<ITerminalManager, TerminalManager>();
builder.Services.AddSingleton<SessionEventService>();

// DbService requires credentials from configuration
var dbUsername = builder.Configuration["ClaudeSettings:DbUsername"] ?? "root";
var dbPassword = builder.Configuration["ClaudeSettings:DbPassword"] ?? "";

builder.Services.AddScoped(sp =>
{
    var dbContextFactory = sp.GetService<IDbContextFactory<ClaudeGuiDbContext>>();
    return new DbService(dbUsername, dbPassword, dbContextFactory);
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapHub<ClaudeHub>("/claudehub");
app.MapFallbackToPage("/_Host");

// Test DB connection at startup
if (connectionString != null)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ClaudeGuiDbContext>>();
        using var db = await dbFactory.CreateDbContextAsync();
        var canConnect = await db.Database.CanConnectAsync();
        Log.Information("Database connection: {Status}", canConnect ? "✅ OK" : "❌ FAILED");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Database connection test failed");
    }
}

Log.Information("ClaudeGui Blazor Server starting on http://localhost:5000");

// Avvia l'applicazione Windows Forms con system tray icon
// invece di bloccare il thread con app.Run()
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.Run(new TrayApplicationContext(app));
