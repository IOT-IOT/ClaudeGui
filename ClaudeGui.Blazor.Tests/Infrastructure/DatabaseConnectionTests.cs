using ClaudeGui.Blazor.Data;
using ClaudeGui.Blazor.Models.Entities;
using ClaudeGui.Blazor.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ClaudeGui.Blazor.Tests.Infrastructure;

/// <summary>
/// Test di connessione al database MariaDB e verifica meccanismo rollback transazioni.
/// Questi test verificano che l'infrastruttura database sia configurata correttamente.
/// </summary>
public class DatabaseConnectionTests
{
    /// <summary>
    /// Verifica che la connection string sia presente in appsettings.json.
    /// </summary>
    [Fact]
    public void ConnectionString_ShouldBeConfigured()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory().Replace("ClaudeGui.Blazor.Tests", "ClaudeGui.Blazor"))
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        // Act
        var connectionString = configuration.GetConnectionString("ClaudeGuiDb");

        // Assert
        connectionString.Should().NotBeNullOrEmpty("la connection string deve essere configurata in appsettings.json");
        connectionString.Should().Contain("Server=192.168.1.11", "deve puntare al server MariaDB corretto");
        connectionString.Should().Contain("Database=ClaudeGui", "deve usare il database ClaudeGui");
    }

    /// <summary>
    /// Verifica che sia possibile connettersi al database MariaDB.
    /// </summary>
    [Fact]
    public async Task DatabaseConnection_ShouldSucceed()
    {
        // Arrange
        var fixture = new DatabaseFixture();
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseMySql(fixture.ConnectionString, ServerVersion.AutoDetect(fixture.ConnectionString));

        // Act
        using var context = new TestDbContext(optionsBuilder.Options);
        var canConnect = await context.Database.CanConnectAsync();

        // Assert
        canConnect.Should().BeTrue("la connessione al database MariaDB deve riuscire");
    }

    /// <summary>
    /// Verifica che il meccanismo di rollback transazioni funzioni correttamente.
    /// Questo test inserisce un record temporaneo e verifica che venga rollbackato.
    /// </summary>
    [Fact]
    public async Task TransactionRollback_ShouldNotPersistChanges()
    {
        // Arrange
        var fixture = new DatabaseFixture();
        var optionsBuilder = new DbContextOptionsBuilder<ClaudeGuiDbContext>();
        optionsBuilder.UseMySql(fixture.ConnectionString, ServerVersion.AutoDetect(fixture.ConnectionString));

        var testSessionId = Guid.NewGuid().ToString(); // GUID valido (36 caratteri)

        // Act - STEP 1: Conta sessioni iniziali
        int initialCount;
        using (var context = new ClaudeGuiDbContext(optionsBuilder.Options))
        {
            initialCount = await context.Sessions
                .Where(s => s.SessionId == testSessionId)
                .CountAsync();
        }

        // Act - STEP 2: Inserisci con transazione e rollback
        using (var context = new ClaudeGuiDbContext(optionsBuilder.Options))
        {
            using var scope = new TransactionScope(context);

            var testSession = new Session
            {
                SessionId = testSessionId,
                Name = "Test Rollback Session",
                WorkingDirectory = "C:\\Test",
                Status = "open",
                LastActivity = DateTime.Now,
                CreatedAt = DateTime.Now,
                Processed = true,
                Excluded = false
            };

            context.Sessions.Add(testSession);
            await context.SaveChangesAsync();

            // Verifica che dentro la transazione il record esista
            var countInTransaction = await context.Sessions
                .Where(s => s.SessionId == testSessionId)
                .CountAsync();

            countInTransaction.Should().Be(1, "dentro la transazione il record deve esistere");

            // Scope.Dispose() esegue rollback automatico
        }

        // Assert - STEP 3: Verifica che dopo rollback il record non esista
        using (var context = new ClaudeGuiDbContext(optionsBuilder.Options))
        {
            var finalCount = await context.Sessions
                .Where(s => s.SessionId == testSessionId)
                .CountAsync();

            finalCount.Should().Be(initialCount, "dopo rollback il record non deve esistere (deve tornare al count iniziale)");
        }
    }
}

/// <summary>
/// DbContext minimale per test di connessione.
/// </summary>
public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }
}
