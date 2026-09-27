using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TradingJournal.Infrastructure.Persistence;

namespace TradingJournal.Api.Persistence;

// Migrations need database configuration, never a JWT signing secret or a running API.
public sealed class TradingJournalDbContextFactory : IDesignTimeDbContextFactory<TradingJournalDbContext>
{
    public TradingJournalDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var builder = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true);
        if (environment == "Development") builder.AddUserSecrets<Program>(optional: true);
        var configuration = builder.AddEnvironmentVariables().Build();
        var connection = configuration.GetConnectionString("TradingJournal");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings:TradingJournal must be configured for migrations.");
        return new TradingJournalDbContext(new DbContextOptionsBuilder<TradingJournalDbContext>()
            .UseSqlServer(connection).Options);
    }
}
