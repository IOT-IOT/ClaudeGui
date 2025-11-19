using ClaudeGui.Blazor.Data;
using ClaudeGui.Blazor.Hubs;
using ClaudeGui.Blazor.Services;
using ClaudeGui.Blazor.Models.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ClaudeGui.Blazor.Tests.Integration;

/// <summary>
/// Test di integrazione end-to-end per verificare il flusso completo:
/// TerminalManager → ClaudeHub → Database.
///
/// Questi test verificano l'integrazione tra i vari componenti senza
/// richiedere un browser reale o un server in esecuzione.
/// </summary>
public class FullStackIntegrationTests : IDisposable
{
    private readonly IDbContextFactory<ClaudeGuiDbContext> _dbContextFactory;
    private readonly ITerminalManager _terminalManager;
    private readonly ClaudeGuiDbContext _testDb;

    public FullStackIntegrationTests()
    {
        // Setup in-memory database per i test
        var options = new DbContextOptionsBuilder<ClaudeGuiDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
            .Options;

        _testDb = new ClaudeGuiDbContext(options);

        // Mock del factory che ritorna sempre lo stesso context
        var mockFactory = new Mock<IDbContextFactory<ClaudeGuiDbContext>>();
        mockFactory.Setup(f => f.CreateDbContextAsync(default))
            .ReturnsAsync(_testDb);
        _dbContextFactory = mockFactory.Object;

        // Istanza reale di TerminalManager
        _terminalManager = new TerminalManager();
    }

    /// <summary>
    /// Test 1: Verifica che TerminalManager possa creare una sessione.
    /// </summary>
    [Fact]
    public void Integration_TerminalManager_CreateSession_ShouldSucceed()
    {
        // Arrange
        var workingDirectory = "C:\\Test";

        // Act
        var sessionId = _terminalManager.CreateSession(workingDirectory);

        // Assert
        sessionId.Should().NotBeNullOrEmpty("deve ritornare un sessionId valido");
        Guid.TryParse(sessionId, out _).Should().BeTrue("sessionId deve essere un GUID valido");

        var session = _terminalManager.GetSession(sessionId);
        session.Should().NotBeNull("la sessione deve esistere in TerminalManager");
    }

    /// <summary>
    /// Test 2: Verifica il flusso completo Create → Get → Kill sessione.
    /// </summary>
    [Fact]
    public void Integration_TerminalManager_FullSessionLifecycle_ShouldWork()
    {
        // Arrange
        var workingDirectory = "C:\\Test";

        // Act 1: Crea sessione
        var sessionId = _terminalManager.CreateSession(workingDirectory);
        sessionId.Should().NotBeNullOrEmpty();

        // Act 2: Verifica esistenza
        var exists = _terminalManager.SessionExists(sessionId);
        exists.Should().BeTrue("la sessione appena creata deve esistere");

        // Act 3: Kill sessione
        _terminalManager.KillSession(sessionId);

        // Assert: Sessione non deve più esistere
        var existsAfterKill = _terminalManager.SessionExists(sessionId);
        existsAfterKill.Should().BeFalse("la sessione killata non deve più esistere");
    }

    /// <summary>
    /// Test 3: Verifica che ClaudeHub possa interagire con TerminalManager.
    /// </summary>
    [Fact]
    public async Task Integration_ClaudeHub_CreateSession_ShouldInteractWithTerminalManager()
    {
        // Arrange
        var hub = CreateMockedHub(_terminalManager);
        var workingDirectory = "C:\\Test";

        // Act
        var sessionId = await hub.CreateSession(workingDirectory, null);

        // Assert
        sessionId.Should().NotBeNullOrEmpty("ClaudeHub deve ritornare un sessionId");
        _terminalManager.SessionExists(sessionId).Should().BeTrue("la sessione deve esistere in TerminalManager");

        // Cleanup
        _terminalManager.KillSession(sessionId);
    }

    /// <summary>
    /// Test 4: Verifica che GetActiveSessions ritorni le sessioni corrette.
    /// </summary>
    [Fact]
    public async Task Integration_ClaudeHub_GetActiveSessions_ShouldReturnCreatedSessions()
    {
        // Arrange
        var hub = CreateMockedHub(_terminalManager);
        var session1 = await hub.CreateSession("C:\\Test1", null);
        var session2 = await hub.CreateSession("C:\\Test2", null);

        // Act
        var activeSessions = await hub.GetActiveSessions();

        // Assert
        activeSessions.Should().Contain(session1, "la prima sessione deve essere attiva");
        activeSessions.Should().Contain(session2, "la seconda sessione deve essere attiva");
        activeSessions.Count().Should().BeGreaterOrEqualTo(2, "devono esserci almeno 2 sessioni attive");

        // Cleanup
        _terminalManager.KillSession(session1);
        _terminalManager.KillSession(session2);
    }

    /// <summary>
    /// Test 5: Verifica che KillSession rimuova effettivamente la sessione.
    /// </summary>
    [Fact]
    public async Task Integration_ClaudeHub_KillSession_ShouldRemoveFromTerminalManager()
    {
        // Arrange
        var hub = CreateMockedHub(_terminalManager);
        var sessionId = await hub.CreateSession("C:\\Test", null);
        _terminalManager.SessionExists(sessionId).Should().BeTrue("pre-condizione: sessione deve esistere");

        // Act
        await hub.KillSession(sessionId);

        // Assert
        _terminalManager.SessionExists(sessionId).Should().BeFalse("la sessione deve essere stata rimossa");
    }

    /// <summary>
    /// Test 6: Verifica che GetSessionInfo ritorni le informazioni corrette.
    /// </summary>
    [Fact]
    public async Task Integration_ClaudeHub_GetSessionInfo_ShouldReturnCorrectInfo()
    {
        // Arrange
        var hub = CreateMockedHub(_terminalManager);
        var sessionId = await hub.CreateSession("C:\\Test", null);

        // Act
        var info = await hub.GetSessionInfo(sessionId);

        // Assert
        info.Should().NotBeNull();
        var sessionIdProperty = info.GetType().GetProperty("SessionId");
        var existsProperty = info.GetType().GetProperty("Exists");

        sessionIdProperty?.GetValue(info).Should().Be(sessionId);
        existsProperty?.GetValue(info).Should().Be(true);

        // Cleanup
        _terminalManager.KillSession(sessionId);
    }

    /// <summary>
    /// Test 7: Verifica che il database possa salvare una sessione (in-memory).
    /// </summary>
    [Fact]
    public async Task Integration_Database_SaveSession_ShouldPersist()
    {
        // Arrange
        var session = new Session
        {
            SessionId = Guid.NewGuid().ToString(),
            Name = "Test Session",
            WorkingDirectory = "C:\\Test",
            LastActivity = DateTime.UtcNow,
            Status = "open",
            CreatedAt = DateTime.UtcNow
        };

        // Act
        _testDb.Sessions.Add(session);
        await _testDb.SaveChangesAsync();

        // Assert
        var retrieved = await _testDb.Sessions
            .FirstOrDefaultAsync(s => s.SessionId == session.SessionId);

        retrieved.Should().NotBeNull("la sessione deve essere stata salvata");
        retrieved!.Name.Should().Be("Test Session");
        retrieved.WorkingDirectory.Should().Be("C:\\Test");
    }

    /// <summary>
    /// Test 8: Verifica che il database possa salvare messaggi associati a una sessione.
    /// </summary>
    [Fact]
    public async Task Integration_Database_SaveMessageWithSession_ShouldLink()
    {
        // Arrange
        var sessionId = Guid.NewGuid().ToString();
        var session = new Session
        {
            SessionId = sessionId,
            Name = "Test Session",
            WorkingDirectory = "C:\\Test",
            LastActivity = DateTime.UtcNow,
            Status = "open",
            CreatedAt = DateTime.UtcNow
        };

        var message = new Message
        {
            ConversationId = sessionId,
            Uuid = Guid.NewGuid().ToString(),
            MessageType = "text",
            Content = "Test message",
            Timestamp = DateTime.UtcNow
        };

        // Act
        _testDb.Sessions.Add(session);
        _testDb.Messages.Add(message);
        await _testDb.SaveChangesAsync();

        // Assert
        var retrievedSession = await _testDb.Sessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);
        var retrievedMessage = await _testDb.Messages
            .FirstOrDefaultAsync(m => m.ConversationId == sessionId);

        retrievedSession.Should().NotBeNull("la sessione deve essere salvata");
        retrievedMessage.Should().NotBeNull("il messaggio deve essere salvato");
        retrievedMessage!.ConversationId.Should().Be(sessionId, "il messaggio deve essere linkato alla sessione");
    }

    /// <summary>
    /// Test 9: Verifica concorrenza - creazione simultanea di multiple sessioni.
    /// </summary>
    [Fact]
    public async Task Integration_TerminalManager_ConcurrentSessionCreation_ShouldBeThreadSafe()
    {
        // Arrange
        var tasks = new List<Task<string>>();

        // Act - Crea 10 sessioni in parallelo
        for (int i = 0; i < 10; i++)
        {
            var i1 = i;
            tasks.Add(Task.Run(() => _terminalManager.CreateSession($"C:\\Test{i1}")));
        }

        await Task.WhenAll(tasks);
        var sessionIds = tasks.Select(t => t.Result).ToList();

        // Assert
        sessionIds.Should().HaveCount(10, "devono essere state create 10 sessioni");
        sessionIds.Should().OnlyHaveUniqueItems("tutti i sessionId devono essere univoci");
        _terminalManager.ActiveSessionCount.Should().Be(10, "devono esserci 10 sessioni attive");

        // Cleanup
        foreach (var sessionId in sessionIds)
        {
            _terminalManager.KillSession(sessionId);
        }
    }

    /// <summary>
    /// Test 10: Verifica che multiple chiamate a ClaudeHub funzionino correttamente.
    /// </summary>
    [Fact]
    public async Task Integration_ClaudeHub_MultipleCalls_ShouldWorkCorrectly()
    {
        // Arrange
        var hub = CreateMockedHub(_terminalManager);

        // Act - Crea 3 sessioni
        var session1 = await hub.CreateSession("C:\\Test1", null);
        var session2 = await hub.CreateSession("C:\\Test2", null);
        var session3 = await hub.CreateSession("C:\\Test3", null);

        // Verifica info per ogni sessione
        var info1 = await hub.GetSessionInfo(session1);
        var info2 = await hub.GetSessionInfo(session2);
        var info3 = await hub.GetSessionInfo(session3);

        // Kill una sessione
        await hub.KillSession(session2);

        // Verifica sessioni attive
        var activeSessions = await hub.GetActiveSessions();

        // Assert
        info1.Should().NotBeNull();
        info2.Should().NotBeNull();
        info3.Should().NotBeNull();

        activeSessions.Should().Contain(session1);
        activeSessions.Should().NotContain(session2, "session2 è stata killata");
        activeSessions.Should().Contain(session3);

        // Cleanup
        _terminalManager.KillSession(session1);
        _terminalManager.KillSession(session3);
    }

    /// <summary>
    /// Helper per creare ClaudeHub con Context mockato.
    /// </summary>
    private static ClaudeHub CreateMockedHub(ITerminalManager terminalManager)
    {
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(c => c.ConnectionId).Returns("test-connection-id");

        var mockGroups = new Mock<IGroupManager>();
        mockGroups.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default))
            .Returns(Task.CompletedTask);
        mockGroups.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default))
            .Returns(Task.CompletedTask);

        var mockClients = new Mock<IHubCallerClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        var mockSingleClientProxy = new Mock<ISingleClientProxy>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        mockClients.Setup(c => c.Caller).Returns(mockSingleClientProxy.Object);

        var hub = new ClaudeHub(terminalManager)
        {
            Context = mockContext.Object,
            Groups = mockGroups.Object,
            Clients = mockClients.Object
        };

        return hub;
    }

    public void Dispose()
    {
        // Cleanup: rimuovi tutte le sessioni attive
        var activeSessions = _terminalManager.GetActiveSessions().ToList();
        foreach (var sessionId in activeSessions)
        {
            _terminalManager.KillSession(sessionId);
        }

        _testDb.Dispose();
    }
}
