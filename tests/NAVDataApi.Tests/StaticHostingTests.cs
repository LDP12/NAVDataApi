using System.Net;

namespace NAVDataApi.Tests;

public sealed class StaticHostingTests
{
    [Fact]
    public async Task Dashboard_is_public_html_while_api_stays_protected()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client(key: null);
        using var page = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<html", await page.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/navs")).StatusCode);
    }

    [Fact]
    public async Task Dashboard_assets_are_public_while_api_stays_protected()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client(key: null);
        using var asset = await client.GetAsync("/reporting.mjs");
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Contains("javascript", asset.Content.Headers.ContentType?.MediaType ?? "");
        Assert.False(string.IsNullOrWhiteSpace(await asset.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/navs")).StatusCode);
    }
}
