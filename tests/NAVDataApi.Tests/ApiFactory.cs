using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using NAVDataApi.Controllers;
using NAVDataApi.Models;

namespace NAVDataApi.Tests;

// An exported type from the entry assembly avoids changing Program just for tests.
public sealed class ApiFactory : WebApplicationFactory<NavsController>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApiKeys:Clients:0:Name"] = "test-a",
            ["ApiKeys:Clients:0:Key"] = "test-key-a",
            ["ApiKeys:Clients:1:Name"] = "test-b",
            ["ApiKeys:Clients:1:Key"] = "test-key-b",
            ["DataReset:IntervalMinutes"] = "0",
            ["Logging:LogLevel:Default"] = "Error"
        }));
    }

    public HttpClient Client(string? key = "test-key-a")
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        if (key is not null) client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }
}

internal static class Requests
{
    public static NavRecordRequest Valid(int day = 1) => new()
    {
        FundCode = "new", FundName = "New Fund", ShareClass = "A Acc",
        Isin = "ZZ9999999999", Currency = "USD", NavDate = new DateOnly(2027, 1, 1).AddDays(day - 1),
        NavPerShare = 4.5821m, SharesOutstanding = 18500000m
    };

    public static NavRecordRequest From(NavRecord record) => new()
    {
        FundCode = record.FundCode, FundName = record.FundName, ShareClass = record.ShareClass,
        Isin = record.Isin, Currency = record.Currency, NavDate = record.NavDate,
        NavPerShare = record.NavPerShare, SharesOutstanding = record.SharesOutstanding
    };
}
