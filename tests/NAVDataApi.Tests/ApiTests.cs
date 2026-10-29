using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NAVDataApi.Models;

namespace NAVDataApi.Tests;

public sealed class ApiTests
{
    [Fact]
    public async Task Post_returns_created_readable_location_and_preserves_prior_records()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        var before = await All(client);
        using var response = await client.PostAsJsonAsync("/api/navs", Requests.Valid());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<NavRecord>())!;
        Assert.Equal($"/api/navs/{created.Id}", response.Headers.Location!.OriginalString);
        Assert.Equal(Requests.Valid().NavPerShare, created.NavPerShare);
        Assert.Equal("NEW", created.FundCode);
        Assert.Equal(created, await client.GetFromJsonAsync<NavRecord>(response.Headers.Location));
        using var second = await client.PostAsJsonAsync("/api/navs", Requests.Valid(2));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var next = (await second.Content.ReadFromJsonAsync<NavRecord>())!;
        Assert.NotEqual(created.Id, next.Id);
        Assert.Equal(created, await client.GetFromJsonAsync<NavRecord>(response.Headers.Location));
        var after = await All(client);
        Assert.Equal(before.Length + 2, after.Length);
        foreach (var record in before) Assert.Contains(record, after);
    }

    [Fact]
    public async Task Put_same_identity_corrects_nav_and_is_idempotent()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        var before = await All(client);
        var old = before[0];
        var request = Requests.From(old);
        request.NavPerShare = 4.582123456789m;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.PutAsJsonAsync($"/api/navs/{old.Id}", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var updated = (await response.Content.ReadFromJsonAsync<NavRecord>())!;
            Assert.Equal(old.Id, updated.Id);
            Assert.Equal(request.NavPerShare, updated.NavPerShare);
            Assert.Equal(updated, await client.GetFromJsonAsync<NavRecord>($"/api/navs/{old.Id}"));
        }
        Assert.Equal(before.Length, (await All(client)).Length);
    }

    [Fact]
    public async Task Put_replaces_all_fields_and_persists_the_response()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        var old = (await All(client))[0];
        var request = Requests.Valid();
        request.FundCode = " xy ";
        request.FundName = " Replaced fund ";
        request.ShareClass = " Z Inc ";
        request.Currency = "GBP";
        request.SharesOutstanding = 0;
        using var response = await client.PutAsJsonAsync($"/api/navs/{old.Id}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<NavRecord>())!;
        Assert.Equal(old.Id, updated.Id);
        Assert.Equal("XY", updated.FundCode);
        Assert.Equal("Replaced fund", updated.FundName);
        Assert.Equal("Z Inc", updated.ShareClass);
        Assert.Equal(request.Isin, updated.Isin);
        Assert.Equal(request.Currency, updated.Currency);
        Assert.Equal(request.NavDate, updated.NavDate);
        Assert.Equal(request.NavPerShare, updated.NavPerShare);
        Assert.Equal(0m, updated.SharesOutstanding);
        Assert.Equal(0m, updated.TotalNetAssets);
        Assert.Equal(updated, await client.GetFromJsonAsync<NavRecord>($"/api/navs/{old.Id}"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public async Task Put_missing_id_returns_404(int id)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        var before = await All(client);
        using var response = await client.PutAsJsonAsync($"/api/navs/{id}", Requests.Valid());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await All(client));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task Conflicts_return_problem_details_without_side_effects(string method)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        var before = await All(client);
        var request = Requests.From(before[0]);
        request.FundCode = "DIFFERENT";
        request.ShareClass = "Other class";
        using var response = method == "POST"
            ? await client.PostAsJsonAsync("/api/navs", request)
            : await client.PutAsJsonAsync($"/api/navs/{before[1].Id}", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(409, problem!["status"]!.GetValue<int>());
        Assert.False(string.IsNullOrWhiteSpace(problem["detail"]!.GetValue<string>()));
        Assert.Equal(before, await All(client));
    }

    public static IEnumerable<object[]> InvalidPayloads()
    {
        var mutations = new List<(string Field, JsonNode? Value, bool Omit)>();
        foreach (var field in new[] { "fundCode", "fundName", "shareClass", "isin", "currency", "navDate", "navPerShare" })
        {
            mutations.Add((field, null, true));
            mutations.Add((field, null, false));
        }
        foreach (var field in new[] { "fundCode", "fundName", "shareClass", "isin", "currency" })
        {
            mutations.Add((field, JsonValue.Create(""), false));
            mutations.Add((field, JsonValue.Create("   "), false));
        }
        foreach (var (field, value) in new[]
        {
            ("fundCode", "X"), ("fundCode", " X "), ("fundCode", new string('X', 21)),
            ("fundName", new string('X', 201)), ("shareClass", new string('X', 51)),
            ("isin", "ZZ000000001"), ("isin", "ZZ000000001A"), ("isin", "120000000011"),
            ("isin", "ZZ00000-0011"), ("isin", "zz0000000011"),
            ("currency", "usd"), ("currency", "US"), ("currency", "USDD"), ("currency", "U1D"),
            ("navDate", "2027-02-30"), ("navDate", "01/02/2027"), ("navDate", "2027-1-1"),
            ("navDate", "2027-01-01T00:00:00Z"), ("navPerShare", "not-a-number")
        }) mutations.Add((field, JsonValue.Create(value), false));
        mutations.Add(("navPerShare", JsonValue.Create(0m), false));
        mutations.Add(("navPerShare", JsonValue.Create(-0.0001m), false));
        mutations.Add(("sharesOutstanding", JsonValue.Create(-1m), false));
        mutations.Add(("sharesOutstanding", null, false));
        foreach (var (field, value, omit) in mutations)
        {
            var payload = JsonSerializer.SerializeToNode(Requests.Valid(), JsonSerializerOptions.Web)!.AsObject();
            if (omit) payload.Remove(field); else payload[field] = value?.DeepClone();
            foreach (var method in new[] { "POST", "PUT" })
                yield return [method, $"{field}:{(omit ? "omitted" : value?.ToJsonString() ?? "null")}", payload.ToJsonString()];
        }
        foreach (var body in new[] { "null", "{}", "[]", "{", "{\"navPerShare\":79228162514264337593543950336}" })
            foreach (var method in new[] { "POST", "PUT" }) yield return [method, body, body];
        var overflow = Requests.Valid();
        overflow.NavPerShare = decimal.MaxValue;
        overflow.SharesOutstanding = 2;
        foreach (var method in new[] { "POST", "PUT" })
            yield return [method, "totalNetAssets overflow", JsonSerializer.Serialize(overflow, JsonSerializerOptions.Web)];
    }

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public async Task Invalid_payload_returns_validation_details_without_mutation(string method, string scenario, string json)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        var before = await All(client);
        using var message = new HttpRequestMessage(new HttpMethod(method), method == "POST" ? "/api/navs" : $"/api/navs/{before[0].Id}")
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        using var response = await client.SendAsync(message);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest,
            $"{method} {scenario}: expected 400, got {(int)response.StatusCode}; {await response.Content.ReadAsStringAsync()}");
        var problem = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(400, problem!["status"]!.GetValue<int>());
        Assert.NotEmpty(problem["errors"]!.AsObject());
        Assert.Equal(before, await All(client));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task Positive_decimal_boundaries_and_zero_shares_are_valid(string method)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        foreach (var nav in new[] { 0.0000000000000000000000000001m, decimal.MaxValue })
        {
            var request = Requests.Valid();
            request.NavPerShare = nav;
            request.SharesOutstanding = 0;
            request.NavDate = nav == decimal.MaxValue ? new DateOnly(2027, 1, 2) : new DateOnly(2027, 1, 1);
            using var response = method == "POST" ? await client.PostAsJsonAsync("/api/navs", request)
                : await client.PutAsJsonAsync("/api/navs/1", request);
            Assert.Equal(method == "POST" ? HttpStatusCode.Created : HttpStatusCode.OK, response.StatusCode);
            var stored = (await response.Content.ReadFromJsonAsync<NavRecord>())!;
            Assert.Equal(nav, stored.NavPerShare);
            Assert.Equal(0m, stored.TotalNetAssets);
            Assert.Equal(stored, await client.GetFromJsonAsync<NavRecord>($"/api/navs/{stored.Id}"));
        }
    }

    [Fact]
    public async Task Read_endpoints_retain_filter_order_latest_and_missing_behaviour()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        var all = await All(client);
        Assert.Equal(60, all.Length);
        Assert.Equal(all.OrderBy(r => r.FundCode).ThenBy(r => r.Isin).ThenBy(r => r.NavDate), all);
        var sample = all[0];
        var filtered = (await client.GetFromJsonAsync<NavRecord[]>($"/api/navs?fundCode={sample.FundCode.ToLowerInvariant()}&isin={sample.Isin.ToLowerInvariant()}&from={sample.NavDate:yyyy-MM-dd}&to={sample.NavDate:yyyy-MM-dd}"))!;
        Assert.Equal(sample, Assert.Single(filtered));
        var latest = (await client.GetFromJsonAsync<NavRecord[]>("/api/navs/latest"))!;
        Assert.Equal(all.GroupBy(r => r.Isin).Select(g => g.MaxBy(r => r.NavDate)!), latest);
        var latestFund = (await client.GetFromJsonAsync<NavRecord[]>($"/api/navs/latest?fundCode={sample.FundCode.ToLowerInvariant()}"))!;
        Assert.Equal(latest.Where(r => r.FundCode == sample.FundCode), latestFund);
        Assert.Equal(sample, await client.GetFromJsonAsync<NavRecord>($"/api/navs/{sample.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/navs/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/navs?from=bad")).StatusCode);
    }

    [Fact]
    public async Task Authenticated_clients_have_isolated_datasets_and_resets()
    {
        using var factory = new ApiFactory();
        using var a = factory.Client();
        using var b = factory.Client("test-key-b");
        var before = await All(b);
        using var create = await a.PostAsJsonAsync("/api/navs", Requests.Valid());
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var added = (await create.Content.ReadFromJsonAsync<NavRecord>())!;
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/navs/{added.Id}")).StatusCode);
        Assert.Equal(before, await All(b));
        using var bCreate = await b.PostAsJsonAsync("/api/navs", Requests.Valid(2));
        Assert.Equal(HttpStatusCode.Created, bCreate.StatusCode);
        var bBeforeReset = await All(b);
        (await a.PostAsync("/api/admin/reset", null)).EnsureSuccessStatusCode();
        Assert.Equal(before, await All(a));
        Assert.Equal(bBeforeReset, await All(b));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Api_requires_valid_key(string? key)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client(key);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/navs")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/navs", Requests.Valid())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync("/api/navs/1", Requests.Valid())).StatusCode);
    }

    [Fact]
    public async Task Authenticated_api_responses_are_not_cacheable_and_vary_by_api_key()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        using var response = await client.GetAsync("/api/navs");
        response.EnsureSuccessStatusCode();
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("private", response.Headers.CacheControl!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("X-Api-Key", response.Headers.Vary);
    }

    [Fact]
    public async Task OpenApi_uses_a_realistic_valid_nav_request_example()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client(key: null);
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadFromJsonAsync<JsonObject>();
        var schema = document!["components"]!["schemas"]!["NavRecordRequest"]!.AsObject();
        var example = schema["example"]!.AsObject();

        Assert.Equal("TEST", example["fundCode"]!.GetValue<string>());
        Assert.Equal("Test Investment Fund", example["fundName"]!.GetValue<string>());
        Assert.Equal("A Acc", example["shareClass"]!.GetValue<string>());
        Assert.Equal("ZZ9999999981", example["isin"]!.GetValue<string>());
        Assert.Equal("USD", example["currency"]!.GetValue<string>());
        Assert.Equal("2026-10-21", example["navDate"]!.GetValue<string>());
        Assert.Equal(125.4321m, example["navPerShare"]!.GetValue<decimal>());
        Assert.Equal(1_000_000m, example["sharesOutstanding"]!.GetValue<decimal>());
    }

    private static async Task<NavRecord[]> All(HttpClient client) =>
        (await client.GetFromJsonAsync<NavRecord[]>("/api/navs"))!;
}
