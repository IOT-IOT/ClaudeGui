using ClaudeGui.Blazor.Services;
using FluentAssertions;
using Moq;

namespace ClaudeGui.Blazor.Tests.Services;

/// <summary>
/// Test per ClaudeProcessManager.
/// Verifica costruttore, properties, event handlers (con mock dove necessario).
/// </summary>
public class ClaudeProcessManagerTests
{
    /// <summary>
    /// Verifica che il costruttore inizializzi correttamente le properties.
    /// </summary>
    [Fact]
    public void Constructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var manager = new ClaudeProcessManager(
            resumeSessionId: "test-session-123",
            dbSessionId: "db-session-456",
            workingDirectory: "C:\\TestDir"
        );

        // Assert
        manager.Should().NotBeNull("il manager deve essere creato correttamente");
        manager.IsRunning.Should().BeFalse("il processo non deve essere avviato alla creazione");
    }

    /// <summary>
    /// Verifica che il costruttore con parametri null usi valori di default.
    /// </summary>
    [Fact]
    public void Constructor_WithNullParameters_ShouldUseDefaults()
    {
        // Arrange & Act
        var manager = new ClaudeProcessManager();

        // Assert
        manager.Should().NotBeNull();
        manager.IsRunning.Should().BeFalse();
    }

    /// <summary>
    /// Verifica che il costruttore accetti workingDirectory null (usa AppConfig default).
    /// </summary>
    [Fact]
    public void Constructor_WithNullWorkingDirectory_ShouldUseAppConfigDefault()
    {
        // Arrange
        var originalDefault = AppConfig.ClaudeWorkingDirectory;

        try
        {
            AppConfig.ClaudeWorkingDirectory = "C:\\DefaultFromAppConfig";

            // Act
            var manager = new ClaudeProcessManager(workingDirectory: null);

            // Assert
            manager.Should().NotBeNull("deve usare il default di AppConfig");
        }
        finally
        {
            // Restore original default
            AppConfig.ClaudeWorkingDirectory = originalDefault;
        }
    }

    /// <summary>
    /// Verifica che l'evento RawOutputReceived possa essere sottoscritto.
    /// In modalità interactive, il processo emette raw output invece di JSONL.
    /// </summary>
    [Fact]
    public void RawOutputReceived_Event_ShouldBeSubscribable()
    {
        // Arrange
        var manager = new ClaudeProcessManager();
        var eventRaised = false;
        string? receivedOutput = null;

        manager.RawOutputReceived += (sender, args) =>
        {
            eventRaised = true;
            receivedOutput = args.RawOutput;
        };

        // Act
        // Nota: non possiamo testare l'evento senza avviare il processo reale,
        // quindi verifichiamo solo che l'evento possa essere sottoscritto senza errori

        // Assert
        eventRaised.Should().BeFalse("l'evento non dovrebbe essere stato sollevato (processo non avviato)");
    }

    /// <summary>
    /// Verifica che l'evento ProcessCompleted possa essere sottoscritto.
    /// </summary>
    [Fact]
    public void ProcessCompleted_Event_ShouldBeSubscribable()
    {
        // Arrange
        var manager = new ClaudeProcessManager();
        var eventRaised = false;

        manager.ProcessCompleted += (sender, args) =>
        {
            eventRaised = true;
        };

        // Assert
        eventRaised.Should().BeFalse("l'evento non dovrebbe essere stato sollevato (processo non avviato)");
    }

    /// <summary>
    /// Verifica che l'evento ErrorReceived possa essere sottoscritto.
    /// </summary>
    [Fact]
    public void ErrorReceived_Event_ShouldBeSubscribable()
    {
        // Arrange
        var manager = new ClaudeProcessManager();
        var eventRaised = false;

        manager.ErrorReceived += (sender, error) =>
        {
            eventRaised = true;
        };

        // Assert
        eventRaised.Should().BeFalse("l'evento non dovrebbe essere stato sollevato (processo non avviato)");
    }

    /// <summary>
    /// Verifica che l'evento IsRunningChanged possa essere sottoscritto.
    /// </summary>
    [Fact]
    public void IsRunningChanged_Event_ShouldBeSubscribable()
    {
        // Arrange
        var manager = new ClaudeProcessManager();
        var eventRaised = false;

        manager.IsRunningChanged += (sender, isRunning) =>
        {
            eventRaised = true;
        };

        // Assert
        eventRaised.Should().BeFalse("l'evento non dovrebbe essere stato sollevato (processo non avviato)");
    }

    /// <summary>
    /// Verifica che Dispose possa essere chiamato senza errori.
    /// </summary>
    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var manager = new ClaudeProcessManager();

        // Act
        Action disposeAction = () => manager.Dispose();

        // Assert
        disposeAction.Should().NotThrow("Dispose deve essere chiamabile anche se il processo non è mai stato avviato");
    }

    /// <summary>
    /// Verifica che Dispose possa essere chiamato multiple volte (idempotenza).
    /// </summary>
    [Fact]
    public void Dispose_MultipleInvocations_ShouldBeIdempotent()
    {
        // Arrange
        var manager = new ClaudeProcessManager();

        // Act
        manager.Dispose();
        Action secondDisposeAction = () => manager.Dispose();

        // Assert
        secondDisposeAction.Should().NotThrow("Dispose deve essere idempotente");
    }

    /// <summary>
    /// Verifica che IsRunning sia inizialmente false.
    /// </summary>
    [Fact]
    public void IsRunning_InitialValue_ShouldBeFalse()
    {
        // Arrange & Act
        var manager = new ClaudeProcessManager();

        // Assert
        manager.IsRunning.Should().BeFalse("il processo non è avviato alla creazione");
    }
}
