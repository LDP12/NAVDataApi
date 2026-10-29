namespace NAVDataApi.Models;

/// <summary>
/// A single Net Asset Value (NAV) observation for a fund share class on a given date.
/// </summary>
public sealed record NavRecord
{
    /// <summary>
    /// Gets or sets the unique identifier for the NAV record.
    /// </summary>
    public required int Id { get; init; }
    /// <summary>
    /// Gets or sets the fund code associated with the NAV record.
    /// </summary>
    public required string FundCode { get; init; }
    /// <summary>
    /// Gets or sets the fund name associated with the NAV record.
    /// </summary>
    public required string FundName { get; init; }
    /// <summary>
    /// Gets or sets the share class associated with the NAV record.
    /// </summary>
    public required string ShareClass { get; init; }
    /// <summary>
    /// Gets or sets the International Securities Identification Number (ISIN) associated with the NAV record.
    /// </summary>
    public required string Isin { get; init; }
    /// <summary>
    /// Gets or sets the currency in which the NAV is denominated.
    /// </summary>
    public required string Currency { get; init; }
    /// <summary>
    /// Gets or sets the date of the NAV observation.
    /// </summary>
    public required DateOnly NavDate { get; init; }
    /// <summary>
    /// Gets or sets the Net Asset Value (NAV) per share for the fund share class on the given date.
    /// </summary>
    public required decimal NavPerShare { get; init; }
    /// <summary>
    /// Gets or sets the total number of shares outstanding for the fund share class on the given date.
    /// </summary>
    public required decimal SharesOutstanding { get; init; }
    /// <summary>
    /// Gets the total net assets for the fund share class on the given date, calculated as NAV per share multiplied by shares outstanding, rounded to two decimal places.
    /// </summary>

    public decimal TotalNetAssets => Math.Round(NavPerShare * SharesOutstanding, 2);
}
