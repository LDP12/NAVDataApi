using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace NAVDataApi.Data;

public sealed class SandboxOptions
{
    /// <summary>
    /// Gets the name of the configuration section for sandbox options.
    /// </summary>
    public const string SectionName = "Sandbox";

    /// <summary>
    /// Maximum number of client datasets held in memory at once.
    /// </summary>
    [Range(1, 1000)]
    public int MaxDatasets { get; set; } = 5;

    /// <summary>
    /// Days after creation that a client's dataset is removed. Resets do not extend it.
    /// </summary>
    [Range(0.0001, 3650)]
    public double LifetimeDays { get; set; } = 7;
}
/// <summary>
/// Represents information about a client's sandbox dataset, including the client name, creation timestamp, and expiration timestamp.
/// </summary>
/// <param name="ClientName"></param>
/// <param name="CreatedUtc"></param>
/// <param name="ExpiresUtc"></param>
public sealed record SandboxInfo(string ClientName, DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc);

/// <summary>
/// Thrown when a new client needs a dataset but the store is at capacity.
/// </summary>
public sealed class SandboxCapacityExceededException(int maxDatasets, DateTimeOffset nextSlotUtc)
    : Exception($"All {maxDatasets} sandbox datasets are in use. A slot frees up at {nextSlotUtc:u}.")
{
    /// <summary>
    /// Gets the maximum number of datasets allowed in the store.
    /// </summary>
    public DateTimeOffset NextSlotUtc { get; } = nextSlotUtc;
}

/// <summary>
/// Holds one isolated, seeded in-memory dataset per API client. A client's dataset is
/// created on its first request, so changes made by one client are never visible to another.
/// The number of datasets is capped, and each is removed a fixed time after creation.
/// </summary>
public sealed class NavSandboxStore(IOptions<SandboxOptions> options, TimeProvider time, ILogger<NavSandboxStore> logger)
{
    /// <summary>
    /// Represents a single sandbox dataset, including its in-memory repository and associated information.
    /// </summary>
    /// <param name="Repository"></param>
    /// <param name="Info"></param>
    private sealed record Sandbox(InMemoryNavRepository Repository, SandboxInfo Info);
    /// <summary>
    /// Lock used to synchronize access to the sandboxes dictionary, ensuring thread safety when creating, retrieving, or removing client datasets.
    /// </summary>
    private readonly Lock _gate = new();
    /// <summary>
    /// The in-memory dictionary that stores sandbox datasets, keyed by client name. Each entry contains the client's in-memory NAV repository and associated information.
    /// </summary>
    private readonly Dictionary<string, Sandbox> _sandboxes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the client's dataset, creating it if needed.
    /// </summary>
    /// <exception cref="SandboxCapacityExceededException">The client has no dataset and the store is full.</exception>
    public INavRepository GetFor(string clientName)
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            RemoveExpiredCore(now);

            if (_sandboxes.TryGetValue(clientName, out var existing))
                return existing.Repository;

            var max = options.Value.MaxDatasets;
            if (_sandboxes.Count >= max)
                throw new SandboxCapacityExceededException(max, _sandboxes.Values.Min(s => s.Info.ExpiresUtc));

            var info = new SandboxInfo(clientName, now, now.AddDays(options.Value.LifetimeDays));
            _sandboxes[clientName] = new Sandbox(new InMemoryNavRepository(), info);
            logger.LogInformation("Created dataset for {Client}; expires {ExpiresUtc:u}. {Count}/{Max} in use.",
                clientName, info.ExpiresUtc, _sandboxes.Count, max);
            return _sandboxes[clientName].Repository;
        }
    }
    /// <summary>
    /// Gets information about the client's dataset, or null if it does not exist or has expired.
    /// </summary>
    /// <param name="clientName"></param>
    /// <returns></returns>
    public SandboxInfo? GetInfo(string clientName)
    {
        lock (_gate)
            return _sandboxes.TryGetValue(clientName, out var sandbox) ? sandbox.Info : null;
    }
    /// <summary>
    /// Gets the number of client datasets currently held in memory.
    /// </summary>
    public int SandboxCount
    {
        get { lock (_gate) return _sandboxes.Count; }
    }

    /// <summary>
    /// Removes datasets past their lifetime and returns how many were removed.
    /// </summary>
    public int RemoveExpired()
    {
        lock (_gate)
            return RemoveExpiredCore(time.GetUtcNow());
    }

    /// <summary>
    /// Restores every client's dataset to the original seed data.
    /// </summary>
    public void ResetAll()
    {
        List<InMemoryNavRepository> repositories;
        lock (_gate)
            repositories = _sandboxes.Values.Select(s => s.Repository).ToList();

        foreach (var repository in repositories)
            repository.Reset();
    }
    /// <summary>
    /// Removes datasets past their lifetime and returns how many were removed. This method is not thread-safe and should be called within a lock.
    /// </summary>
    /// <param name="now"></param>
    /// <returns></returns>
    private int RemoveExpiredCore(DateTimeOffset now)
    {
        var expired = _sandboxes.Where(kv => kv.Value.Info.ExpiresUtc <= now).Select(kv => kv.Key).ToList();
        foreach (var clientName in expired)
        {
            _sandboxes.Remove(clientName);
            logger.LogInformation("Removed expired dataset for {Client}.", clientName);
        }
        return expired.Count;
    }
}
