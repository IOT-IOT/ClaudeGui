using ClaudeGui.Blazor.Services;
using FluentAssertions;

namespace ClaudeGui.Blazor.Tests.Services;

/// <summary>
/// Test per TerminalManager.
/// Verifica gestione dizionario sessioni, creazione, recupero, terminazione.
/// </summary>
public class TerminalManagerTests
{
    /// <summary>
    /// Verifica che CreateSession ritorni un sessionId valido.
    /// </summary>
    [Fact]
    public void CreateSession_ShouldReturnSessionId()
    {
        // Arrange
        var manager = new TerminalManager();

        // Act
        var sessionId = manager.CreateSession("C:\\Test");

        // Assert
        sessionId.Should().NotBeNullOrEmpty("CreateSession deve ritornare un sessionId valido");
        Guid.TryParse(sessionId, out _).Should().BeTrue("sessionId deve essere un GUID valido");
    }

    /// <summary>
    /// Verifica che CreateSession con sessionId esistente usi quel sessionId.
    /// </summary>
    [Fact]
    public void CreateSession_WithExistingSessionId_ShouldUseProvidedId()
    {
        // Arrange
        var manager = new TerminalManager();
        var existingSessionId = Guid.NewGuid().ToString();

        // Act
        var sessionId = manager.CreateSession("C:\\Test", existingSessionId);

        // Assert
        sessionId.Should().Be(existingSessionId, "deve usare il sessionId fornito");
    }

    /// <summary>
    /// Verifica che GetSession ritorni il ProcessManager per una sessione esistente.
    /// </summary>
    [Fact]
    public void GetSession_ExistingSession_ShouldReturnProcessManager()
    {
        // Arrange
        var manager = new TerminalManager();
        var sessionId = manager.CreateSession("C:\\Test");

        // Act
        var processManager = manager.GetSession(sessionId);

        // Assert
        processManager.Should().NotBeNull("GetSession deve ritornare il ProcessManager per una sessione esistente");
    }

    /// <summary>
    /// Verifica che GetSession ritorni null per una sessione inesistente.
    /// </summary>
    [Fact]
    public void GetSession_NonExistingSession_ShouldReturnNull()
    {
        // Arrange
        var manager = new TerminalManager();
        var nonExistingSessionId = Guid.NewGuid().ToString();

        // Act
        var processManager = manager.GetSession(nonExistingSessionId);

        // Assert
        processManager.Should().BeNull("GetSession deve ritornare null per una sessione inesistente");
    }

    /// <summary>
    /// Verifica che KillSession rimuova la sessione dal dizionario.
    /// </summary>
    [Fact]
    public void KillSession_ShouldRemoveSession()
    {
        // Arrange
        var manager = new TerminalManager();
        var sessionId = manager.CreateSession("C:\\Test");

        // Act
        manager.KillSession(sessionId);

        // Assert
        var processManager = manager.GetSession(sessionId);
        processManager.Should().BeNull("dopo KillSession la sessione non deve più esistere");
    }

    /// <summary>
    /// Verifica che GetActiveSessions ritorni la lista di sessioni attive.
    /// </summary>
    [Fact]
    public void GetActiveSessions_ShouldReturnActiveSessionIds()
    {
        // Arrange
        var manager = new TerminalManager();
        var sessionId1 = manager.CreateSession("C:\\Test1");
        var sessionId2 = manager.CreateSession("C:\\Test2");

        // Act
        var activeSessions = manager.GetActiveSessions().ToList();

        // Assert
        activeSessions.Should().Contain(sessionId1);
        activeSessions.Should().Contain(sessionId2);
        activeSessions.Count.Should().Be(2, "devono esserci esattamente 2 sessioni attive");
    }

    /// <summary>
    /// Verifica che ActiveSessionCount ritorni il numero corretto di sessioni.
    /// </summary>
    [Fact]
    public void ActiveSessionCount_ShouldReturnCorrectCount()
    {
        // Arrange
        var manager = new TerminalManager();
        manager.CreateSession("C:\\Test1");
        manager.CreateSession("C:\\Test2");
        manager.CreateSession("C:\\Test3");

        // Act
        var count = manager.ActiveSessionCount;

        // Assert
        count.Should().Be(3, "devono esserci esattamente 3 sessioni attive");
    }

    /// <summary>
    /// Verifica che SessionExists ritorni true per una sessione esistente.
    /// </summary>
    [Fact]
    public void SessionExists_ExistingSession_ShouldReturnTrue()
    {
        // Arrange
        var manager = new TerminalManager();
        var sessionId = manager.CreateSession("C:\\Test");

        // Act
        var exists = manager.SessionExists(sessionId);

        // Assert
        exists.Should().BeTrue("SessionExists deve ritornare true per una sessione esistente");
    }

    /// <summary>
    /// Verifica che SessionExists ritorni false per una sessione inesistente.
    /// </summary>
    [Fact]
    public void SessionExists_NonExistingSession_ShouldReturnFalse()
    {
        // Arrange
        var manager = new TerminalManager();
        var nonExistingSessionId = Guid.NewGuid().ToString();

        // Act
        var exists = manager.SessionExists(nonExistingSessionId);

        // Assert
        exists.Should().BeFalse("SessionExists deve ritornare false per una sessione inesistente");
    }

    /// <summary>
    /// Verifica che IsSessionRunning ritorni false per una sessione appena creata (non avviata).
    /// </summary>
    [Fact]
    public void IsSessionRunning_NewSession_ShouldReturnFalse()
    {
        // Arrange
        var manager = new TerminalManager();
        var sessionId = manager.CreateSession("C:\\Test");

        // Act
        var isRunning = manager.IsSessionRunning(sessionId);

        // Assert
        isRunning.Should().BeFalse("una sessione appena creata non è ancora running");
    }

    /// <summary>
    /// Verifica che CreateSession lanci eccezione per sessionId duplicato.
    /// </summary>
    [Fact]
    public void CreateSession_DuplicateSessionId_ShouldThrow()
    {
        // Arrange
        var manager = new TerminalManager();
        var sessionId = Guid.NewGuid().ToString();
        manager.CreateSession("C:\\Test1", sessionId);

        // Act
        Action act = () => manager.CreateSession("C:\\Test2", sessionId);

        // Assert
        act.Should().Throw<InvalidOperationException>("sessionId duplicato deve lanciare eccezione");
    }

    /// <summary>
    /// Verifica thread-safety: creazione simultanea di multiple sessioni.
    /// </summary>
    [Fact]
    public void CreateSession_ConcurrentCreation_ShouldBeThreadSafe()
    {
        // Arrange
        var manager = new TerminalManager();
        var tasks = new List<Task<string>>();

        // Act - Crea 10 sessioni in parallelo
        for (int i = 0; i < 10; i++)
        {
            var i1 = i;
            tasks.Add(Task.Run(() => manager.CreateSession($"C:\\Test{i1}")));
        }

        Task.WaitAll(tasks.ToArray());
        var sessionIds = tasks.Select(t => t.Result).ToList();

        // Assert
        sessionIds.Should().HaveCount(10, "devono essere state create 10 sessioni");
        sessionIds.Should().OnlyHaveUniqueItems("tutti i sessionId devono essere univoci");
        manager.ActiveSessionCount.Should().Be(10, "devono esserci 10 sessioni attive");
    }
}
