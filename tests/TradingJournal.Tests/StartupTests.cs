using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingJournal.Application;
using TradingJournal.Infrastructure;
using TradingJournal.Infrastructure.Persistence;
using Xunit;

namespace TradingJournal.Tests;

public sealed class StartupTests
{
    [Fact]
    public async Task ApiStartsAndServesOpenApiInDevelopment()
    {
        await using var factory = new AuthApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.StartsWith("3.", document.RootElement.GetProperty("openapi").GetString());
    }

    [Fact]
    public void DependencyInjectionResolvesConfiguredSqlServerContextWithinScope()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TradingJournal"] =
                    "Server=(localdb)\\MSSQLLocalDB;Database=TradingJournal;Trusted_Connection=True"
            }).Build();

        using var provider = new ServiceCollection()
            .AddApplication()
            .AddInfrastructure(configuration)
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var context = firstScope.ServiceProvider.GetRequiredService<TradingJournalDbContext>();

        Assert.Same(context, firstScope.ServiceProvider.GetRequiredService<TradingJournalDbContext>());
        Assert.NotSame(context, secondScope.ServiceProvider.GetRequiredService<TradingJournalDbContext>());
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
        Assert.Equal(7, context.Model.GetEntityTypes().Count());
    }
}
