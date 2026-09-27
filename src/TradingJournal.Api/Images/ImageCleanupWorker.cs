using Microsoft.Extensions.Options;
using TradingJournal.Infrastructure.Images;

namespace TradingJournal.Api.Images;

public sealed class ImageCleanupWorker(IServiceScopeFactory scopes, IOptions<S3ImageOptions> options, ILogger<ImageCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled || !options.Value.CleanupEnabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ImageCleanup>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception)
            {
                // Deliberately no exception payloads, object keys, credentials or URLs in logs.
                logger.LogWarning("Image cleanup deferred; verify database and storage availability.");
            }
        }
    }
}
