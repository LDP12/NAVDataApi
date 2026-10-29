using NAVDataApi.Models;

namespace NAVDataApi.Data;

/// <summary>
/// Deterministic seed data. Every call returns an identical, fresh data set so a reset
/// always restores the API to exactly the same state.
/// All funds and identifiers are fictional ("ZZ" ISIN prefix is not an assigned country code).
/// </summary>
public static class SeedData
{
    /// <summary>
    /// Represents a share class and its initial NAV and shares outstanding for seeding purposes.
    /// </summary>
    /// <param name="FundCode"></param>
    /// <param name="FundName"></param>
    /// <param name="ShareClass"></param>
    /// <param name="Isin"></param>
    /// <param name="Currency"></param>
    /// <param name="StartNav"></param>
    /// <param name="SharesOutstanding"></param>
    private sealed record ShareClassSeed(
        string FundCode, string FundName, string ShareClass, string Isin,
        string Currency, decimal StartNav, decimal SharesOutstanding);
    /// <summary>
    /// The share classes used to generate the seed data, including their initial NAV and shares outstanding.
    /// </summary>
    private static readonly ShareClassSeed[] ShareClasses =
    [
        new("GEF", "Global Equity Fund", "A Acc", "ZZ0000000011", "USD", 125.4300m, 1_250_000m),
        new("GEF", "Global Equity Fund", "I Inc", "ZZ0000000029", "USD", 1_032.1500m, 85_000m),
        new("EBF", "European Bond Fund", "A Acc", "ZZ0000000037", "EUR", 98.7600m, 2_400_000m),
        new("EBF", "European Bond Fund", "B Inc", "ZZ0000000045", "EUR", 101.2200m, 640_000m),
        new("UKS", "UK Smaller Companies Fund", "R Acc", "ZZ0000000052", "GBP", 4.5821m, 18_500_000m),
        new("EMD", "Emerging Markets Debt Fund", "I Acc", "ZZ0000000060", "USD", 87.9100m, 920_000m),
    ];

    // Fixed anchor date keeps the seed reproducible regardless of when the API is started.
    /// <summary>
    /// The last NAV date used for generating the seed data. The seed data includes 10 business days of NAV records leading up to this date.
    /// </summary>
    private static readonly DateOnly LastNavDate = new(2026, 9, 30);
    /// <summary>
    /// The number of business days of NAV records to generate for each share class in the seed data.
    /// </summary>
    private const int BusinessDays = 10;
    /// <summary>
    /// Creates a list of NAV records based on the defined share classes and their initial NAV and shares outstanding, generating records for the specified number of business days leading up to the last NAV date.
    /// </summary>
    /// <returns></returns>
    public static List<NavRecord> Create()
    {
        var dates = GetBusinessDays(LastNavDate, BusinessDays);
        var random = new Random(20260930);
        var records = new List<NavRecord>(ShareClasses.Length * dates.Count);
        var id = 1;

        foreach (var sc in ShareClasses)
        {
            var nav = sc.StartNav;
            foreach (var date in dates)
            {
                records.Add(new NavRecord
                {
                    Id = id++,
                    FundCode = sc.FundCode,
                    FundName = sc.FundName,
                    ShareClass = sc.ShareClass,
                    Isin = sc.Isin,
                    Currency = sc.Currency,
                    NavDate = date,
                    NavPerShare = nav,
                    SharesOutstanding = sc.SharesOutstanding
                });

                // Daily move between -1.5% and +1.5%.
                var change = (decimal)(random.NextDouble() * 0.03 - 0.015);
                nav = Math.Round(nav * (1 + change), 4);
            }
        }

        return records;
    }
    /// <summary>
    /// Returns a list of the most recent business days leading up to the specified end date, excluding weekends. The list is ordered from oldest to newest.
    /// </summary>
    /// <param name="endDate"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    private static List<DateOnly> GetBusinessDays(DateOnly endDate, int count)
    {
        var dates = new List<DateOnly>(count);
        for (var d = endDate; dates.Count < count; d = d.AddDays(-1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                dates.Add(d);
        }
        dates.Reverse();
        return dates;
    }
}
