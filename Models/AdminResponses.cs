namespace NAVDataApi.Models;
/// <summary>
/// Represents the response to a reset request, including a message and the timestamp of the reset in UTC.
/// </summary>
/// <param name="Message"></param>
/// <param name="ResetAtUtc"></param>
public sealed record ResetResponse(string Message, DateTimeOffset ResetAtUtc);

/// <summary>
/// Represents the status of the NAV dataset, including the client name, record count, and timestamps for the last reset, dataset creation, and dataset expiration.
/// </summary>
/// <param name="Client"></param>
/// <param name="RecordCount"></param>
/// <param name="LastResetUtc"></param>
/// <param name="DatasetCreatedUtc"></param>
/// <param name="DatasetExpiresUtc"></param>
public sealed record DataStatusResponse(
    string? Client,
    int RecordCount,
    DateTimeOffset LastResetUtc,
    DateTimeOffset? DatasetCreatedUtc,
    DateTimeOffset? DatasetExpiresUtc);
