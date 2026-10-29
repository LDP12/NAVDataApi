using NAVDataApi.Models;

namespace NAVDataApi.Data;

/// <summary>
/// Thread-safe, non-persistent store. Changes live only in process memory and are
/// discarded on <see cref="Reset"/> or application restart.
/// </summary>
public sealed class InMemoryNavRepository : INavRepository
{
    /// <summary>
    /// A lock object to ensure thread safety for concurrent access to the in-memory NAV records.
    /// </summary>
    private readonly Lock _gate = new();
    /// <summary>
    /// The in-memory dictionary that stores NAV records, keyed by their unique identifier.
    /// </summary>
    private Dictionary<int, NavRecord> _records = [];
    /// <summary>
    /// The next unique identifier to be assigned to a new NAV record.
    /// </summary>
    private int _nextId;
    /// <summary>
    /// Gets the timestamp of the last reset in UTC. This property is updated whenever the repository is reset to its seed state.
    /// </summary>
    public DateTimeOffset LastResetUtc { get; private set; }
    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryNavRepository"/> class and populates it with seed data.
    /// </summary>
    public InMemoryNavRepository() => Reset();
    /// <summary>
    /// Gets all NAV records, optionally filtered by fund code, ISIN, and/or date range. The results are ordered by fund code, ISIN, and NAV date.
    /// </summary>
    /// <param name="fundCode"></param>
    /// <param name="isin"></param>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <returns></returns>
    public IReadOnlyList<NavRecord> GetAll(string? fundCode = null, string? isin = null, DateOnly? from = null, DateOnly? to = null)
    {
        lock (_gate)
        {
            IEnumerable<NavRecord> query = _records.Values;

            if (!string.IsNullOrWhiteSpace(fundCode))
                query = query.Where(r => r.FundCode.Equals(fundCode, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(isin))
                query = query.Where(r => r.Isin.Equals(isin, StringComparison.OrdinalIgnoreCase));
            if (from is not null)
                query = query.Where(r => r.NavDate >= from);
            if (to is not null)
                query = query.Where(r => r.NavDate <= to);

            return query.OrderBy(r => r.FundCode).ThenBy(r => r.Isin).ThenBy(r => r.NavDate).ToList();
        }
    }
    /// <summary>
    /// Gets a single NAV record by its unique identifier. Returns null if the record does not exist.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public NavRecord? GetById(int id)
    {
        lock (_gate)
            return _records.GetValueOrDefault(id);
    }
    /// <summary>
    /// Gets the latest NAV records for each fund share class (ISIN), optionally filtered by fund code. The latest record is determined by the most recent NAV date.
    /// </summary>
    /// <param name="fundCode"></param>
    /// <returns></returns>
    public IReadOnlyList<NavRecord> GetLatest(string? fundCode = null) =>
        GetAll(fundCode)
            .GroupBy(r => r.Isin)
            .Select(g => g.MaxBy(r => r.NavDate)!)
            .ToList();

    /// <summary>
    /// Creates a new NAV record, or returns conflict if the ISIN and date already exist. Never replaces an existing record.
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    public RepositoryResult Create(NavRecordRequest request)
    {
        lock (_gate)
        {
            if (IsDuplicate(request, excludeId: null))
                return DuplicateResult(request);

            var record = request.ToRecord(_nextId);
            _records.Add(record.Id, record);
            _nextId++;
            return new RepositoryResult(RepositoryStatus.Success, record);
        }
    }
    /// <summary>
    /// Updates an existing NAV record with the given ID using the provided request data. Returns a RepositoryResult indicating success, not found, or conflict.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="request"></param>
    /// <returns></returns>
    public RepositoryResult Update(int id, NavRecordRequest request)
    {
        lock (_gate)
        {
            if (!_records.ContainsKey(id))
                return new RepositoryResult(RepositoryStatus.NotFound);
            if (IsDuplicate(request, excludeId: id))
                return DuplicateResult(request);

            var record = request.ToRecord(id);
            _records[id] = record;
            return new RepositoryResult(RepositoryStatus.Success, record);
        }
    }
    /// <summary>
    /// Discards all changes and restores the original seed data. This method is thread-safe and updates the LastResetUtc timestamp to the current UTC time.
    /// </summary>
    public void Reset()
    {
        var seed = SeedData.Create();
        lock (_gate)
        {
            _records = seed.ToDictionary(r => r.Id);
            _nextId = seed.Count == 0 ? 1 : seed.Max(r => r.Id) + 1;
            LastResetUtc = DateTimeOffset.UtcNow;
        }
    }

    // One NAV per share class (ISIN) per date.
    /// <summary>
    /// Determines whether a NAV record with the same ISIN and NAV date already exists in the repository, excluding a specific record ID if provided. This method is used to prevent duplicate records when creating or updating NAV records.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="excludeId"></param>
    /// <returns></returns>
    private bool IsDuplicate(NavRecordRequest request, int? excludeId) =>
        _records.Values.Any(r =>
            r.Id != excludeId &&
            r.Isin.Equals(request.Isin, StringComparison.OrdinalIgnoreCase) &&
            r.NavDate == request.NavDate);

    /// <summary>
    /// Creates a RepositoryResult indicating a conflict due to a duplicate NAV record for the same ISIN and NAV date. This method is used when attempting to create or update a NAV record that would result in a duplicate entry.
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    private static RepositoryResult DuplicateResult(NavRecordRequest request) =>
        new(RepositoryStatus.Conflict, Error: $"A NAV for ISIN {request.Isin} on {request.NavDate:yyyy-MM-dd} already exists.");
}
