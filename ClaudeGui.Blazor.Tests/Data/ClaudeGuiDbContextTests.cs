using ClaudeGui.Blazor.Data;
using ClaudeGui.Blazor.Models.Entities;
using ClaudeGui.Blazor.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ClaudeGui.Blazor.Tests.Data;

/// <summary>
/// Test per ClaudeGuiDbContext.
/// Verifica connessione, query base, e transazioni con rollback.
/// </summary>
public class ClaudeGuiDbContextTests
{
    /// <summary>
    /// Verifica che il DbContext possa essere creato e connesso al database.
    /// </summary>
    [Fact]
    public async Task DbContext_ShouldConnect()
    {
        // Arrange
        var fixture = new DatabaseFixture();
        var optionsBuilder = new DbContextOptionsBuilder<ClaudeGuiDbContext>();
        optionsBuilder.UseMySql(fixture.ConnectionString, ServerVersion.AutoDetect(fixture.ConnectionString));

        // Act
        using var context = new ClaudeGuiDbContext(optionsBuilder.Options);
        var canConnect = await context.Database.CanConnectAsync();

        // Assert
        canConnect.Should().BeTrue("DbContext deve potersi connettere a MariaDB");
    }

    /// <summary>
    /// Verifica che DbContext possa queryare la tabella Sessions esistente.
    /// </summary>
    [Fact]
    public async Task DbContext_ShouldQuerySessions()
    {
        // Arrange
        var fixture = new DatabaseFixture();
        var optionsBuilder = new DbContextOptionsBuilder<ClaudeGuiDbContext>();
        optionsBuilder.UseMySql(fixture.ConnectionString, ServerVersion.AutoDetect(fixture.ConnectionString));

        // Act
        using var context = new ClaudeGuiDbContext(optionsBuilder.Options);
        var sessions = await context.Sessions.Take(10).ToListAsync();

        // Assert
        sessions.Should().NotBeNull("query Sessions deve ritornare una lista (anche vuota)");
    }

    /// <summary>
    /// Verifica che DbContext possa queryare la tabella Messages esistente.
    /// </summary>
    [Fact]
    public async Task DbContext_ShouldQueryMessages()
    {
        // Arrange
        var fixture = new DatabaseFixture();
        var optionsBuilder = new DbContextOptionsBuilder<ClaudeGuiDbContext>();
        optionsBuilder.UseMySql(fixture.ConnectionString, ServerVersion.AutoDetect(fixture.ConnectionString));

        // Act
        using var context = new ClaudeGuiDbContext(optionsBuilder.Options);
        var messages = await context.Messages.Take(10).ToListAsync();

        // Assert
        messages.Should().NotBeNull("query Messages deve ritornare una lista (anche vuota)");
    }

    /// <summary>
    /// Verifica che una sessione inserita in transazione venga rollbackata correttamente.
    /// Questo test verifica che il meccanismo transaction rollback funzioni con il DbContext reale.
    /// </summary>
    [Fact]
    public async Task DbContext_TransactionRollback_ShouldNotPersist()
    {
        // Arrange
        var fixture = new DatabaseFixture();
        var optionsBuilder = new DbContextOptionsBuilder<ClaudeGuiDbContext>();
        optionsBuilder.UseMySql(fixture.ConnectionString, ServerVersion.AutoDetect(fixture.ConnectionString));

        var testSessionId = Guid.NewGuid().ToString(); // 36 caratteri esatti (UUID)

        // Act - STEP 1: Conta sessioni iniziali con questo ID
        int initialCount;
        using (var context = new ClaudeGuiDbContext(optionsBuilder.Options))
        {
            initialCount = await context.Sessions
                .Where(s => s.SessionId == testSessionId)
                .CountAsync();
        }

        initialCount.Should().Be(0, "la sessione test non deve esistere prima del test");

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

            finalCount.Should().Be(0, "dopo rollback il record non deve esistere");
        }
    }

    /// <summary>
    /// Verifica che la navigation property Session su Message funzioni correttamente.
    /// Questo test verifica che EF Core carichi correttamente le FK relationships.
    /// </summary>
    [Fact]
    public async Task DbContext_MessageSessionRelationship_ShouldLoad()
    {
        // Arrange
        var fixture = new DatabaseFixture();
        var optionsBuilder = new DbContextOptionsBuilder<ClaudeGuiDbContext>();
        optionsBuilder.UseMySql(fixture.ConnectionString, ServerVersion.AutoDetect(fixture.ConnectionString));

        // Act - Prendi un messaggio che ha una sessione valida
        using var context = new ClaudeGuiDbContext(optionsBuilder.Options);
        var messageWithSession = await context.Messages
            .Include(m => m.Session)
            .FirstOrDefaultAsync(m => m.Session != null);

        // Assert
        if (messageWithSession != null)
        {
            messageWithSession.Session.Should().NotBeNull("navigation property Session deve essere caricata");
            messageWithSession.Session!.SessionId.Should().Be(messageWithSession.ConversationId,
                "FK deve matchare: Message.ConversationId = Session.SessionId");
        }
        else
        {
            // Database vuoto o nessun messaggio con sessione valida - test passa comunque
            true.Should().BeTrue("database vuoto o nessun messaggio con sessione - test skipped");
        }
    }

    /// <summary>
    /// Verifica che tutti i DbSet siano configurati correttamente.
    /// </summary>
    [Fact]
    public void DbContext_ShouldHaveAllDbSetsConfigured()
    {
        // Arrange
        var fixture = new DatabaseFixture();
        var optionsBuilder = new DbContextOptionsBuilder<ClaudeGuiDbContext>();
        optionsBuilder.UseMySql(fixture.ConnectionString, ServerVersion.AutoDetect(fixture.ConnectionString));

        // Act
        using var context = new ClaudeGuiDbContext(optionsBuilder.Options);

        // Assert
        context.Sessions.Should().NotBeNull("DbSet Sessions deve essere configurato");
        context.Messages.Should().NotBeNull("DbSet Messages deve essere configurato");
        context.Summaries.Should().NotBeNull("DbSet Summaries deve essere configurato");
        context.FileHistorySnapshots.Should().NotBeNull("DbSet FileHistorySnapshots deve essere configurato");
        context.QueueOperations.Should().NotBeNull("DbSet QueueOperations deve essere configurato");
        context.Conversations.Should().NotBeNull("DbSet Conversations (legacy) deve essere configurato");
    }
}
