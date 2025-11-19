using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace ClaudeGui.Blazor.Tests.Helpers;

/// <summary>
/// Fixture per test database con rollback automatico delle transazioni.
/// Utilizzare come IClassFixture per garantire che ogni test non modifichi il database in modo permanente.
/// </summary>
public class DatabaseFixture : IDisposable
{
    /// <summary>
    /// Connection string MariaDB da appsettings.json
    /// </summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Transazione corrente (per rollback automatico)
    /// </summary>
    private IDbContextTransaction? _transaction;

    public DatabaseFixture()
    {
        // Leggi connection string da appsettings.json del progetto Blazor
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory().Replace("ClaudeGui.Blazor.Tests", "ClaudeGui.Blazor"))
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        ConnectionString = configuration.GetConnectionString("ClaudeGuiDb")
            ?? throw new InvalidOperationException("Connection string 'ClaudeGuiDb' non trovata in appsettings.json");
    }

    /// <summary>
    /// Crea un DbContext con transazione attiva (per rollback automatico).
    /// Ogni test deve chiamare BeginTransaction() e al termine Dispose() verrà eseguito rollback.
    /// </summary>
    public TContext CreateDbContext<TContext>() where TContext : DbContext
    {
        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
        optionsBuilder.UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString));

        var context = (TContext)Activator.CreateInstance(typeof(TContext), optionsBuilder.Options)!;
        return context;
    }

    /// <summary>
    /// Avvia una transazione che verrà rollbackata automaticamente al Dispose.
    /// </summary>
    public IDbContextTransaction BeginTransaction(DbContext context)
    {
        _transaction = context.Database.BeginTransaction();
        return _transaction;
    }

    /// <summary>
    /// Rollback automatico della transazione.
    /// </summary>
    public void Dispose()
    {
        if (_transaction != null)
        {
            _transaction.Rollback();
            _transaction.Dispose();
        }
    }
}

/// <summary>
/// Helper per test transazionali con rollback automatico.
/// Utilizzo: using var scope = new TransactionScope(dbContext, fixture);
/// </summary>
public class TransactionScope : IDisposable
{
    private readonly IDbContextTransaction _transaction;

    public TransactionScope(DbContext context)
    {
        _transaction = context.Database.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
    }
}
