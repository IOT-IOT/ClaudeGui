using Microsoft.Playwright;
using FluentAssertions;

namespace ClaudeGui.Blazor.Tests.E2E;

/// <summary>
/// Test end-to-end con Playwright per verificare il terminal web.
/// Questi test richiedono che l'applicazione Blazor sia in esecuzione.
///
/// Per eseguire:
/// 1. Avvia l'applicazione: cd ClaudeGui.Blazor && dotnet run
/// 2. Run test: dotnet test --filter "FullyQualifiedName~E2E"
/// </summary>
[Collection("E2E")]
public class TerminalE2ETests : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private const string BASE_URL = "http://localhost:5000";

    /// <summary>
    /// Setup eseguito una sola volta prima di tutti i test della classe.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Installa Playwright se non già fatto
        // Command: playwright install chromium

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Headless = true, // true per CI/CD, false per debugging
            SlowMo = 100 // Slow motion per vedere cosa succede
        });
    }

    /// <summary>
    /// Cleanup eseguito dopo tutti i test della classe.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_browser != null)
            await _browser.DisposeAsync();

        _playwright?.Dispose();
    }

    /// <summary>
    /// Test 1: Verifica che la homepage si carichi correttamente.
    /// </summary>
    [Fact(Skip = "Requires Blazor server running on localhost:5000")]
    public async Task Homepage_ShouldLoadSuccessfully()
    {
        // Arrange
        var page = await _browser!.NewPageAsync();

        try
        {
            // Act
            await page.GotoAsync(BASE_URL, new() { WaitUntil = WaitUntilState.NetworkIdle });

            // Assert
            var title = await page.TitleAsync();
            title.Should().Contain("ClaudeGui", "il titolo deve contenere ClaudeGui");

            var heading = await page.Locator("h1").TextContentAsync();
            heading.Should().Contain("ClaudeGui", "l'heading deve contenere ClaudeGui");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>
    /// Test 2: Verifica che il form "New Session" sia presente.
    /// </summary>
    [Fact(Skip = "Requires Blazor server running on localhost:5000")]
    public async Task Homepage_ShouldShowNewSessionForm()
    {
        // Arrange
        var page = await _browser!.NewPageAsync();

        try
        {
            // Act
            await page.GotoAsync(BASE_URL, new() { WaitUntil = WaitUntilState.NetworkIdle });

            // Assert
            var workingDirInput = page.Locator("input[id='workingDir']");
            (await workingDirInput.IsVisibleAsync()).Should().BeTrue();

            var createButton = page.Locator("button:has-text('Create New Session')");
            (await createButton.IsVisibleAsync()).Should().BeTrue();
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>
    /// Test 3: Verifica che si possa creare una nuova sessione e vedere il terminal.
    /// </summary>
    [Fact(Skip = "Requires Blazor server running on localhost:5000 and claude.exe installed")]
    public async Task CreateSession_ShouldShowTerminal()
    {
        // Arrange
        var page = await _browser!.NewPageAsync();

        try
        {
            // Act 1: Vai alla homepage
            await page.GotoAsync(BASE_URL, new() { WaitUntil = WaitUntilState.NetworkIdle });

            // Act 2: Inserisci working directory
            var workingDirInput = page.Locator("input[id='workingDir']");
            await workingDirInput.FillAsync("C:\\Temp");

            // Act 3: Clicca "Create New Session"
            var createButton = page.Locator("button:has-text('Create New Session')");
            await createButton.ClickAsync();

            // Act 4: Attendi che il terminal appaia
            await page.WaitForSelectorAsync(".terminal-container", new() { Timeout = 10000 });

            // Assert: Verifica che il terminal sia visibile
            var terminalContainer = page.Locator(".terminal-container");
            (await terminalContainer.IsVisibleAsync()).Should().BeTrue();

            // Assert: Verifica che ci sia il pulsante "Close"
            var closeButton = page.Locator("button:has-text('Close')");
            (await closeButton.IsVisibleAsync()).Should().BeTrue();
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>
    /// Test 4: Verifica che il terminal possa ricevere input.
    /// </summary>
    [Fact(Skip = "Requires Blazor server running on localhost:5000 and claude.exe installed")]
    public async Task Terminal_ShouldAcceptInput()
    {
        // Arrange
        var page = await _browser!.NewPageAsync();

        try
        {
            // Act 1: Crea sessione e apri terminal
            await page.GotoAsync(BASE_URL);
            await page.Locator("input[id='workingDir']").FillAsync("C:\\Temp");
            await page.Locator("button:has-text('Create New Session')").ClickAsync();
            await page.WaitForSelectorAsync(".terminal-container", new() { Timeout = 10000 });

            // Act 2: Attendi che xterm.js sia pronto
            await page.WaitForTimeoutAsync(2000);

            // Act 3: Simula input nel terminal (focus + type)
            var terminalElement = page.Locator(".terminal-container .xterm");
            await terminalElement.ClickAsync(); // Focus sul terminal
            await page.Keyboard.TypeAsync("hello world");
            await page.Keyboard.PressAsync("Enter");

            // Assert: Verifica che l'input sia stato inviato
            // Nota: in modalità interactive, l'output dipende da Claude
            // Per ora verifichiamo solo che non ci siano errori JavaScript
            var consoleErrors = new List<string>();
            page.Console += (_, msg) =>
            {
                if (msg.Type == "error")
                    consoleErrors.Add(msg.Text);
            };

            await page.WaitForTimeoutAsync(1000);
            consoleErrors.Should().BeEmpty("non devono esserci errori JavaScript nel console");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}

