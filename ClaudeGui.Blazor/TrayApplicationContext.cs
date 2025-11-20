using System.Diagnostics;
using System.Reflection;
using Microsoft.WindowsAPICodePack.Taskbar;

namespace ClaudeGui.Blazor;

/// <summary>
/// Gestisce l'applicazione Windows Forms con icona nella taskbar.
/// Avvia il server Kestrel in background e fornisce un menu contestuale
/// per aprire il browser e chiudere l'applicazione.
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly Form _taskbarForm;
    private readonly NotifyIcon _notifyIcon;
    private readonly string _serverUrl = "http://localhost:5000";
    private readonly IHost _webHost;

    /// <summary>
    /// Costruttore: inizializza la form nascosta con icona taskbar e avvia il server web.
    /// </summary>
    /// <param name="webHost">L'host Kestrel da avviare in background</param>
    public TrayApplicationContext(IHost webHost)
    {
        _webHost = webHost;

        // Crea una form minimizzata con icona nella taskbar
        _taskbarForm = new Form
        {
            // Form normale sempre minimizzata con icona nella taskbar
            ShowInTaskbar = true,
            WindowState = FormWindowState.Minimized,
            FormBorderStyle = FormBorderStyle.FixedSingle,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(300, 200),
            Text = "ClaudeGui",
            Icon = LoadIcon(),
            MaximizeBox = false,
            MinimizeBox = true,
            ShowIcon = true,
            ControlBox = true
        };

        // Crea NotifyIcon nascosto solo per fallback
        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = false,
            Text = "ClaudeGui - Blazor Server"
        };

        // Impedisce il ripristino della finestra (mantiene sempre minimizzata)
        _taskbarForm.Resize += (s, e) =>
        {
            if (_taskbarForm.WindowState != FormWindowState.Minimized)
            {
                _taskbarForm.WindowState = FormWindowState.Minimized;
            }
        };

        // Impedisce la chiusura con Alt+F4, richiede menu "Esci"
        _taskbarForm.FormClosing += (s, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                _taskbarForm.WindowState = FormWindowState.Minimized;
            }
        };

        // Mostra la form minimizzata (icona visibile nella taskbar)
        _taskbarForm.Show();
        _taskbarForm.WindowState = FormWindowState.Minimized;

        // Configura Jump List per il menu contestuale della taskbar
        ConfigureJumpList();

        // Avvia il server Kestrel in background
        Task.Run(async () =>
        {
            try
            {
                Console.WriteLine($"[TaskbarApp] Starting Kestrel server on {_serverUrl}...");
                await _webHost.RunAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TaskbarApp] Error starting server: {ex.Message}");
                _taskbarForm.Invoke((Action)(() =>
                {
                    MessageBox.Show(
                        $"Errore nell'avvio del server:\n{ex.Message}",
                        "ClaudeGui - Errore",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    Application.Exit();
                }));
            }
        });
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
            Console.WriteLine("[TaskbarApp] Icon not found, using default Windows icon");
            return SystemIcons.Application;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TaskbarApp] Error loading icon: {ex.Message}");
            return SystemIcons.Application;
        }
    }

    /// <summary>
    /// Crea il menu contestuale per l'icona nella taskbar.
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
    /// Configura la Jump List per mostrare voci personalizzate nel menu contestuale
    /// quando si fa right-click sull'icona nella taskbar di Windows.
    /// </summary>
    private void ConfigureJumpList()
    {
        try
        {
            // Ottieni il path dell'eseguibile corrente
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                Console.WriteLine("[JumpList] Cannot determine executable path");
                return;
            }

            // Verifica che la piattaforma supporti Jump List (Windows 7+)
            if (!TaskbarManager.IsPlatformSupported)
            {
                Console.WriteLine("[JumpList] Taskbar features not supported on this platform");
                return;
            }

            // Crea una Jump List personalizzata
            var jumpList = JumpList.CreateJumpList();

            // Task: Apri ClaudeGui
            var openTask = new JumpListLink(exePath, "Apri ClaudeGui")
            {
                Arguments = "--open-browser"
            };
            jumpList.AddUserTasks(openTask);

            // Task: Esci
            var exitTask = new JumpListLink(exePath, "Esci")
            {
                Arguments = "--exit"
            };
            jumpList.AddUserTasks(exitTask);

            // Applica la Jump List
            jumpList.Refresh();

            Console.WriteLine("[JumpList] Jump List configured successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JumpList] Error configuring Jump List: {ex.Message}");
        }
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
            Console.Error.WriteLine($"[TaskbarApp] Error opening browser: {ex.Message}");
            MessageBox.Show(
                $"Impossibile aprire il browser:\n{ex.Message}",
                "ClaudeGui - Errore",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    /// <summary>
    /// Chiude l'applicazione fermando il server Kestrel e chiudendo la form.
    /// </summary>
    private async void ExitApplication()
    {
        try
        {
            Console.WriteLine("[TaskbarApp] Shutting down...");

            // Ferma il server Kestrel
            await _webHost.StopAsync(TimeSpan.FromSeconds(5));

            // Chiudi la form e l'applicazione
            _taskbarForm.Close();
            Application.Exit();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TaskbarApp] Error during shutdown: {ex.Message}");
            Application.Exit();
        }
    }

    /// <summary>
    /// Cleanup: rimuove la form quando il context viene distrutto.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon?.Dispose();
            _taskbarForm?.Dispose();
            _webHost?.Dispose();
        }
        base.Dispose(disposing);
    }
}
