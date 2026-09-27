using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using TradingJournal.Infrastructure.Persistence;
using Xunit;

namespace TradingJournal.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerFixture.ConnectionVariable)))
            Skip = $"Set {SqlServerFixture.ConnectionVariable} to enable real SQL Server tests.";
    }
}

public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string ConnectionVariable = "TRADINGJOURNAL_SQLSERVER_TEST_CONNECTION";
    private string? connectionString;
    private bool databaseCreated;
    public string ConnectionString => connectionString ?? throw new InvalidOperationException("SQL Server tests are disabled.");

    public TradingJournalDbContext CreateContext() => new(
        new DbContextOptionsBuilder<TradingJournalDbContext>()
            .UseSqlServer(connectionString ?? throw new InvalidOperationException("SQL Server tests are disabled."))
            .Options);

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(configured)) return;
        var builder = new SqlConnectionStringBuilder(configured);
        if (!string.IsNullOrEmpty(builder.AttachDBFilename))
            throw new InvalidOperationException("Test connections must not specify AttachDBFilename.");

        // Never migrate or delete the configured database: use an isolated, newly created database.
        builder.InitialCatalog = $"TradingJournal_Tests_{Guid.NewGuid():N}";
        builder.Pooling = false;
        connectionString = builder.ConnectionString;
        await using var context = CreateContext();
        var creator = context.GetService<IRelationalDatabaseCreator>();
        if (await creator.ExistsAsync())
            throw new InvalidOperationException("The isolated test database unexpectedly already exists.");
        await creator.CreateAsync();
        databaseCreated = true;
        try
        {
            await context.Database.MigrateAsync();
            await context.Database.MigrateAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (!databaseCreated) return;
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        databaseCreated = false;
    }
}
