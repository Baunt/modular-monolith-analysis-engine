using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Monolith.Analysis;

namespace Monolith.Host;

internal sealed record PollingOptions(TimeSpan Interval);

internal sealed class AnalysisWorker(Engine engine, PollingOptions options, ILogger<AnalysisWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int saved = engine.Tick(stoppingToken);
                if (saved > 0)
                    log.LogInformation("Saved {Count} analysis outcomes", saved);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
            {
                log.LogWarning("Database busy; retrying next cycle");
            }
            await Task.Delay(options.Interval, stoppingToken);
        }
    }
}
