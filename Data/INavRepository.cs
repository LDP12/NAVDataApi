using NAVDataApi.Models;

namespace NAVDataApi.Data;

public interface INavRepository
{
    /// <summary>
    /// Gets all NAV records, optionally filtered by fund code, ISIN, and/or date range.
    /// </summary>
    /// <param name="fundCode"></param>
    /// <param name="isin"></param>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <returns></returns>
    IReadOnlyList<NavRecord> GetAll(string? fundCode = null, string? isin = null, DateOnly? from = null, DateOnly? to = null);
    /// <summary>
    /// Gets a single NAV record by its unique identifier.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    NavRecord? GetById(int id);
    /// <summary>
    /// Gets the latest NAV records for each fund share class, optionally filtered by fund code.
    /// </summary>
    /// <param name="fundCode"></param>
    /// <returns></returns>
    IReadOnlyList<NavRecord> GetLatest(string? fundCode = null);
    /// <summary>
    /// Creates a new NAV record, or returns conflict if the ISIN and date already exist. Never replaces an existing record.
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    RepositoryResult Create(NavRecordRequest request);
    /// <summary>
    /// Updates an existing NAV record with the given ID using the provided request data. Returns a RepositoryResult indicating success, not found, or conflict.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="request"></param>
    /// <returns></returns>
    RepositoryResult Update(int id, NavRecordRequest request);

    /// <summary>
    /// Discards all changes and restores the original seed data.
    /// </summary>
    void Reset();
    /// <summary>
    /// Gets the timestamp of the last reset in UTC.
    /// </summary>
    DateTimeOffset LastResetUtc { get; }
}
/// <summary>
/// Represents the status of a repository operation, including success, not found, or conflict.
/// </summary>
public enum RepositoryStatus { Success, NotFound, Conflict }
/// <summary>
/// Represents the result of a repository operation, including the status, an optional NAV record, and an optional error message.
/// </summary>
/// <param name="Status"></param>
/// <param name="Record"></param>
/// <param name="Error"></param>
public sealed record RepositoryResult(RepositoryStatus Status, NavRecord? Record = null, string? Error = null);
