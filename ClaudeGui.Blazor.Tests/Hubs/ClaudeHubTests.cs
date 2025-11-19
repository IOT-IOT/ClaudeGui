using ClaudeGui.Blazor.Hubs;
using ClaudeGui.Blazor.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace ClaudeGui.Blazor.Tests.Hubs;

/// <summary>
/// Test per ClaudeHub.
/// Verifica metodi del hub con mock di TerminalManager.
/// </summary>
public class ClaudeHubTests
{
    /// <summary>
    /// Helper per creare ClaudeHub con Context, Groups, e Clients mockati.
    /// </summary>
    private static ClaudeHub CreateHubWithMockedContext(ITerminalManager terminalManager)
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
    /// <summary>
    /// Verifica che CreateSession ritorni un sessionId valido.
    /// </summary>
    [Fact]
    public async Task CreateSession_ShouldReturnSessionId()
    {
        // Arrange
        var mockTerminalManager = new Mock<ITerminalManager>();
        var expectedSessionId = Guid.NewGuid().ToString();

        mockTerminalManager
            .Setup(m => m.CreateSession(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(expectedSessionId);

        mockTerminalManager
            .Setup(m => m.GetSession(expectedSessionId))
            .Returns(new ClaudeProcessManager(null, null, "C:\\Test"));

        var hub = CreateHubWithMockedContext(mockTerminalManager.Object);

        // Act
        var sessionId = await hub.CreateSession("C:\\Test", null);

        // Assert
        sessionId.Should().Be(expectedSessionId, "CreateSession deve ritornare il sessionId creato");
        mockTerminalManager.Verify(m => m.CreateSession("C:\\Test", null), Times.Once);
    }

    /// <summary>
    /// Verifica che CreateSession con existingSessionId passi l'ID al TerminalManager.
    /// </summary>
    [Fact]
    public async Task CreateSession_WithExistingSessionId_ShouldPassToTerminalManager()
    {
        // Arrange
        var mockTerminalManager = new Mock<ITerminalManager>();
        var existingSessionId = Guid.NewGuid().ToString();

        mockTerminalManager
            .Setup(m => m.CreateSession(It.IsAny<string>(), existingSessionId))
            .Returns(existingSessionId);

        mockTerminalManager
            .Setup(m => m.GetSession(existingSessionId))
            .Returns(new ClaudeProcessManager(existingSessionId, existingSessionId, "C:\\Test"));

        var hub = CreateHubWithMockedContext(mockTerminalManager.Object);

        // Act
        var sessionId = await hub.CreateSession("C:\\Test", existingSessionId);

        // Assert
        sessionId.Should().Be(existingSessionId);
        mockTerminalManager.Verify(m => m.CreateSession("C:\\Test", existingSessionId), Times.Once);
    }

    /// <summary>
    /// Verifica che GetSessionInfo ritorni informazioni sulla sessione.
    /// </summary>
    [Fact]
    public async Task GetSessionInfo_ShouldReturnSessionInfo()
    {
        // Arrange
        var mockTerminalManager = new Mock<ITerminalManager>();
        var sessionId = Guid.NewGuid().ToString();

        mockTerminalManager
            .Setup(m => m.SessionExists(sessionId))
            .Returns(true);

        mockTerminalManager
            .Setup(m => m.IsSessionRunning(sessionId))
            .Returns(false);

        var hub = CreateHubWithMockedContext(mockTerminalManager.Object);

        // Act
        var info = await hub.GetSessionInfo(sessionId);

        // Assert
        info.Should().NotBeNull();
        // Usa reflection per accedere alle proprietà dell'oggetto anonimo
        var sessionIdProperty = info.GetType().GetProperty("SessionId");
        var existsProperty = info.GetType().GetProperty("Exists");
        var isRunningProperty = info.GetType().GetProperty("IsRunning");

        sessionIdProperty?.GetValue(info).Should().Be(sessionId);
        existsProperty?.GetValue(info).Should().Be(true);
        isRunningProperty?.GetValue(info).Should().Be(false);
    }

    /// <summary>
    /// Verifica che GetActiveSessions ritorni la lista dal TerminalManager.
    /// </summary>
    [Fact]
    public async Task GetActiveSessions_ShouldReturnActiveSessionIds()
    {
        // Arrange
        var mockTerminalManager = new Mock<ITerminalManager>();
        var sessionIds = new List<string>
        {
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString()
        };

        mockTerminalManager
            .Setup(m => m.GetActiveSessions())
            .Returns(sessionIds);

        mockTerminalManager
            .SetupGet(m => m.ActiveSessionCount)
            .Returns(sessionIds.Count);

        var hub = CreateHubWithMockedContext(mockTerminalManager.Object);

        // Act
        var result = await hub.GetActiveSessions();

        // Assert
        result.Should().BeEquivalentTo(sessionIds);
        mockTerminalManager.Verify(m => m.GetActiveSessions(), Times.Once);
    }

    /// <summary>
    /// Verifica che KillSession chiami TerminalManager.KillSession.
    /// </summary>
    [Fact]
    public async Task KillSession_ShouldCallTerminalManager()
    {
        // Arrange
        var mockTerminalManager = new Mock<ITerminalManager>();
        var sessionId = Guid.NewGuid().ToString();

        var hub = CreateHubWithMockedContext(mockTerminalManager.Object);

        // Act
        await hub.KillSession(sessionId);

        // Assert
        mockTerminalManager.Verify(m => m.KillSession(sessionId), Times.Once);
    }
}
