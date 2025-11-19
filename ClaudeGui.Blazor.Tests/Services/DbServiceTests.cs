using ClaudeGui.Blazor.Data;
using ClaudeGui.Blazor.Services;
using ClaudeGui.Blazor.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ClaudeGui.Blazor.Tests.Services;

/// <summary>
/// Test per DbService.
/// Verifica costruttore e operazioni base.
/// </summary>
public class DbServiceTests
{
    /// <summary>
    /// Verifica che DbService possa essere creato con credenziali valide.
    /// </summary>
    [Fact]
    public void Constructor_WithValidCredentials_ShouldSucceed()
    {
        // Arrange
        var fixture = new DatabaseFixture();
        var optionsBuilder = new DbContextOptionsBuilder<ClaudeGuiDbContext>();
        optionsBuilder.UseMySql(fixture.ConnectionString, ServerVersion.AutoDetect(fixture.ConnectionString));

        var dbContextFactory = new TestDbContextFactory(optionsBuilder.Options);

        // Act
        var dbService = new DbService("root", "Prikko%%45WE", dbContextFactory);

        // Assert
        dbService.Should().NotBeNull("DbService deve essere creato correttamente");
    }

    /// <summary>
    /// Verifica che il costruttore lanci eccezione con credenziali vuote.
    /// </summary>
    [Fact]
    public void Constructor_WithEmptyCredentials_ShouldThrow()
    {
        // Arrange
        var fixture = new DatabaseFixture();
        var optionsBuilder = new DbContextOptionsBuilder<ClaudeGuiDbContext>();
        optionsBuilder.UseMySql(fixture.ConnectionString, ServerVersion.AutoDetect(fixture.ConnectionString));

        var dbContextFactory = new TestDbContextFactory(optionsBuilder.Options);

        // Act
        Action act = () => new DbService("", "", dbContextFactory);

        // Assert
        act.Should().Throw<ArgumentException>("credenziali vuote devono lanciare eccezione");
    }

    /// <summary>
    /// Verifica che GetLastMessagesAsync ritorni lista messaggi (anche vuota).
    /// </summary>
    [Fact]
    public async Task GetLastMessagesAsync_ShouldReturnList()
    {
        // Arrange
        var fixture = new DatabaseFixture();
        var optionsBuilder = new DbContextOptionsBuilder<ClaudeGuiDbContext>();
        optionsBuilder.UseMySql(fixture.ConnectionString, ServerVersion.AutoDetect(fixture.ConnectionString));

        var dbContextFactory = new TestDbContextFactory(optionsBuilder.Options);
        var dbService = new DbService("root", "Prikko%%45WE", dbContextFactory);

        var testSessionId = Guid.NewGuid().ToString();

        // Act
        var messages = await dbService.GetLastMessagesAsync(testSessionId, count: 10);

        // Assert
        messages.Should().NotBeNull("GetLastMessagesAsync deve ritornare una lista");
        messages.Count.Should().Be(0, "per una sessione inesistente deve ritornare lista vuota");
    }
}

/// <summary>
/// Factory helper per creare DbContext nei test.
/// </summary>
public class TestDbContextFactory : IDbContextFactory<ClaudeGuiDbContext>
{
    private readonly DbContextOptions<ClaudeGuiDbContext> _options;

    public TestDbContextFactory(DbContextOptions<ClaudeGuiDbContext> options)
    {
        _options = options;
    }

    public ClaudeGuiDbContext CreateDbContext()
    {
        return new ClaudeGuiDbContext(_options);
    }
}
