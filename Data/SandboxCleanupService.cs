namespace NAVDataApi.Data;

/// <summary>
/// Removes expired client datasets so their memory and capacity slots are released even
/// when the owning client never calls again.
/// </summary>
public sealed class SandboxCleanupService(
    NavSandboxStore sandboxes,
    TimeProvider time,
    ILogger<SandboxCleanupService> logger) : BackgroundService
{
    /// <summary>
    /// The interval at which the cleanup service checks for expired datasets.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    /// <summary>
    /// Executes the cleanup process at regular intervals.
    /// </summary>
    /// <param name="stoppingToken"></param>
    /// <returns></returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var removed = sandboxes.RemoveExpired();
                if (removed > 0)
                    logger.LogInformation("Cleanup removed {Removed} expired dataset(s).", removed);
            }
        }
        catch (OperationCanceledException)
        {
            // Application shutting down.
        }
    }
}
