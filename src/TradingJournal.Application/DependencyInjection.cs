using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradingJournal.Application.Auth;

namespace TradingJournal.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AuthService>();
        services.AddScoped<Trades.TradeService>();
        services.AddScoped<Images.TradeImageService>();
        return services;
    }
}
