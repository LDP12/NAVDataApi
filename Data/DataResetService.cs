using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace NAVDataApi.Data;

public sealed class DataResetOptions
{
    /// <summary>
    /// Gets the name of the configuration section for data reset options.
    /// </summary>
    public const string SectionName = "DataReset";

    /// <summary>
    /// Gets or sets how often the store is restored to seed data. 0 disables the scheduled reset.
    /// </summary>
    [Range(0, 52560, ErrorMessage = "Reset interval must be 0 (disabled) or at most 52,560 minutes.")]
    public int IntervalMinutes { get; set; } = 0;
}

/// <summary>
/// Optionally restores every client's in-memory dataset to its seed state on a fixed interval.
/// </summary>
public sealed class DataResetService(
    NavSandboxStore sandboxes,
    IOptions<DataResetOptions> options,
    ILogger<DataResetService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = options.Value.IntervalMinutes;
        if (minutes <= 0)
        {
            logger.LogInformation("Scheduled data reset is disabled.");
            return;
        }

        logger.LogInformation("Data will reset to seed state every {Minutes} minute(s).", minutes);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                sandboxes.ResetAll();
                logger.LogInformation("Reset {Count} client dataset(s) to seed state.", sandboxes.SandboxCount);
            }
        }
        catch (OperationCanceledException)
        {
            // Application shutting down.
        }
    }
}
