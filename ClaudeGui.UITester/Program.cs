using Microsoft.Playwright;

namespace ClaudeGui.UITester;

/// <summary>
/// Tool per catturare screenshot dell'applicazione ClaudeGui usando Playwright.
/// Utile per verifiche visive durante lo sviluppo.
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        var baseUrl = args.Length > 0 ? args[0] : "https://localhost:7140";
        var screenshotDir = Path.Combine(Directory.GetCurrentDirectory(), "Screenshots");

        // Crea directory se non esiste
        if (!Directory.Exists(screenshotDir))
        {
            Directory.CreateDirectory(screenshotDir);
        }

        Console.WriteLine("===========================================");
        Console.WriteLine("  ClaudeGui UI Screenshot Tool");
        Console.WriteLine("===========================================");
        Console.WriteLine($"Base URL: {baseUrl}");
        Console.WriteLine($"Screenshot Dir: {screenshotDir}");
        Console.WriteLine();

        Console.WriteLine("[1/5] Installazione browser Playwright...");
        // Installa browser se necessario (automatico al primo utilizzo)
        var exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
        if (exitCode != 0 && exitCode != 1) // 1 = già installato
        {
            Console.WriteLine($"⚠ Playwright install returned code {exitCode}");
        }
        else
        {
            Console.WriteLine("✓ Browser Chromium pronto");
        }

        Console.WriteLine("\n[2/5] Avvio Playwright...");
        using var playwright = await Playwright.CreateAsync();

        Console.WriteLine("[3/5] Lancio browser Chromium headless...");
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            IgnoreHTTPSErrors = true // Per localhost con certificato self-signed
        });

        var page = await context.NewPageAsync();

        try
        {
            Console.WriteLine($"\n[4/5] Navigazione a {baseUrl}...");
            await page.GotoAsync(baseUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30000
            });

            // Attendi caricamento completo
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Task.Delay(2000); // Attesa extra per rendering Blazor

            Console.WriteLine("✓ Pagina caricata");

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            Console.WriteLine("\n[5/5] Cattura screenshot...\n");

            // Screenshot 1: Pagina intera
            var fullPagePath = Path.Combine(screenshotDir, $"fullpage_{timestamp}.png");
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = fullPagePath,
                FullPage = true
            });
            Console.WriteLine($"  ✓ Pagina intera: {Path.GetFileName(fullPagePath)}");

            // Screenshot 2: Header
            try
            {
                var header = await page.QuerySelectorAsync(".header-section");
                if (header != null)
                {
                    var headerPath = Path.Combine(screenshotDir, $"header_{timestamp}.png");
                    await header.ScreenshotAsync(new ElementHandleScreenshotOptions { Path = headerPath });
                    Console.WriteLine($"  ✓ Header: {Path.GetFileName(headerPath)}");
                }
                else
                {
                    Console.WriteLine("  ⚠ Header non trovato (.header-section)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠ Impossibile catturare header: {ex.Message}");
            }

            // Screenshot 3: Session View (3-panel layout)
            try
            {
                var sessionView = await page.QuerySelectorAsync(".session-view");
                if (sessionView != null)
                {
                    var sessionViewPath = Path.Combine(screenshotDir, $"session-view_{timestamp}.png");
                    await sessionView.ScreenshotAsync(new ElementHandleScreenshotOptions { Path = sessionViewPath });
                    Console.WriteLine($"  ✓ Session View (3-panel): {Path.GetFileName(sessionViewPath)}");
                }
                else
                {
                    Console.WriteLine("  ⚠ Session View non trovato (.session-view) - probabilmente nessuna sessione aperta");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠ Impossibile catturare session view: {ex.Message}");
            }

            // Screenshot 4: Markdown Editor Toolbar
            try
            {
                var toolbar = await page.QuerySelectorAsync(".editor-toolbar");
                if (toolbar != null)
                {
                    var toolbarPath = Path.Combine(screenshotDir, $"markdown-toolbar_{timestamp}.png");
                    await toolbar.ScreenshotAsync(new ElementHandleScreenshotOptions { Path = toolbarPath });
                    Console.WriteLine($"  ✓ Markdown Toolbar: {Path.GetFileName(toolbarPath)}");
                }
                else
                {
                    Console.WriteLine("  ⚠ Markdown Toolbar non trovato (.editor-toolbar) - probabilmente nessuna sessione aperta");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠ Impossibile catturare toolbar: {ex.Message}");
            }

            // Screenshot 5: Editor Panel completo
            try
            {
                var editorPanel = await page.QuerySelectorAsync(".editor-panel");
                if (editorPanel != null)
                {
                    var editorPanelPath = Path.Combine(screenshotDir, $"editor-panel_{timestamp}.png");
                    await editorPanel.ScreenshotAsync(new ElementHandleScreenshotOptions { Path = editorPanelPath });
                    Console.WriteLine($"  ✓ Editor Panel: {Path.GetFileName(editorPanelPath)}");
                }
                else
                {
                    Console.WriteLine("  ⚠ Editor Panel non trovato (.editor-panel)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠ Impossibile catturare editor panel: {ex.Message}");
            }

            Console.WriteLine("\n===========================================");
            Console.WriteLine($"✓ Screenshot completati!");
            Console.WriteLine($"  Directory: {screenshotDir}");
            Console.WriteLine("===========================================");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Errore: {ex.Message}");
            Console.WriteLine("\nAssicurati che:");
            Console.WriteLine($"  1. L'applicazione ClaudeGui sia in esecuzione su {baseUrl}");
            Console.WriteLine("  2. Il certificato HTTPS sia accettato");
            Console.WriteLine("  3. La porta sia corretta");
            throw;
        }
        finally
        {
            await context.CloseAsync();
        }
    }
}
