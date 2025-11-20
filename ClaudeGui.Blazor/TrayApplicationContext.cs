using System.Diagnostics;
using System.Reflection;

namespace ClaudeGui.Blazor;

/// <summary>
/// Gestisce l'applicazione Windows Forms per la system tray icon.
/// Avvia il server Kestrel in background e fornisce un menu contestuale
/// per aprire il browser e chiudere l'applicazione.
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly string _serverUrl = "http://localhost:5000";
    private readonly IHost _webHost;

    /// <summary>
    /// Costruttore: inizializza la system tray icon e avvia il server web.
    /// </summary>
    /// <param name="webHost">L'host Kestrel da avviare in background</param>
    public TrayApplicationContext(IHost webHost)
    {
        _webHost = webHost;

        // Crea l'icona nella system tray
        _trayIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            ContextMenuStrip = CreateContextMenu(),
            Visible = true,
            Text = "ClaudeGui - Blazor Server"
        };

        // Double-click sull'icona apre il browser
        _trayIcon.DoubleClick += (s, e) => OpenBrowser();

        // Avvia il server Kestrel in background
        Task.Run(async () =>
        {
            try
            {
                Console.WriteLine($"[TrayApp] Starting Kestrel server on {_serverUrl}...");
                await _webHost.RunAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TrayApp] Error starting server: {ex.Message}");
                MessageBox.Show(
                    $"Errore nell'avvio del server:\n{ex.Message}",
                    "ClaudeGui - Errore",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                Application.Exit();
            }
        });

        // Messaggio di avvio
        _trayIcon.ShowBalloonTip(
            3000,
            "ClaudeGui Avviato",
            $"Server in ascolto su {_serverUrl}\nClicca l'icona per aprire il browser.",
            ToolTipIcon.Info
        );
    }

    /// <summary>
    /// Carica l'icona dell'applicazione. Se non trova claudegui.ico,
    /// usa l'icona di default del sistema.
    /// </summary>
    private Icon LoadIcon()
    {
        try
        {
            // Prova a caricare l'icona embedded resource
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "ClaudeGui.Blazor.Resources.claudegui.ico";
            using var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream != null)
            {
                return new Icon(stream);
            }

            // Fallback: cerca file fisico nella directory Resources
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "claudegui.ico");
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }

            // Fallback finale: icona di default Windows
            Console.WriteLine("[TrayApp] Icon not found, using default Windows icon");
            return SystemIcons.Application;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TrayApp] Error loading icon: {ex.Message}");
            return SystemIcons.Application;
        }
    }

    /// <summary>
    /// Crea il menu contestuale della tray icon con le opzioni disponibili.
    /// </summary>
    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        // Voce: Apri ClaudeGui
        var openItem = new ToolStripMenuItem("Apri ClaudeGui", null, (s, e) => OpenBrowser())
        {
            Font = new Font(menu.Font, FontStyle.Bold)
        };
        menu.Items.Add(openItem);

        // Separatore
        menu.Items.Add(new ToolStripSeparator());

        // Voce: Esci
        var exitItem = new ToolStripMenuItem("Esci", null, (s, e) => ExitApplication());
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>
    /// Apre il browser sull'URL del server Blazor.
    /// </summary>
    private void OpenBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _serverUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TrayApp] Error opening browser: {ex.Message}");
            MessageBox.Show(
                $"Impossibile aprire il browser:\n{ex.Message}",
                "ClaudeGui - Errore",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    /// <summary>
    /// Chiude l'applicazione fermando il server Kestrel e rimuovendo la tray icon.
    /// </summary>
    private async void ExitApplication()
    {
        try
        {
            Console.WriteLine("[TrayApp] Shutting down...");

            // Nascondi l'icona dalla tray
            _trayIcon.Visible = false;

            // Ferma il server Kestrel
            await _webHost.StopAsync(TimeSpan.FromSeconds(5));

            // Chiudi l'applicazione Windows Forms
            Application.Exit();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TrayApp] Error during shutdown: {ex.Message}");
            Application.Exit();
        }
    }

    /// <summary>
    /// Cleanup: rimuove la tray icon quando il context viene distrutto.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon?.Dispose();
            _webHost?.Dispose();
        }
        base.Dispose(disposing);
    }
}
