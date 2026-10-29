using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace NAVDataApi.Security;

public sealed class ApiClient
{
    /// <summary>
    /// Stable identifier for the client; also the key of its isolated dataset.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// The secret API key used to authenticate the client. This should be a long, random string and must be kept confidential.
    /// </summary>
    public string Key { get; set; } = string.Empty;
}
/// <summary>
/// Options for API key authentication, including the list of valid clients and their associated keys.
/// </summary>
public sealed class ApiKeyAuthOptions : IValidatableObject
{
    /// <summary>
    /// Gets the name of the configuration section for API key authentication options.
    /// </summary>
    public const string SectionName = "ApiKeys";
    /// <summary>
    /// Gets the name of the HTTP header used to pass the API key. The default is <c>X-Api-Key</c>.
    /// </summary>
    public const string HeaderName = "X-Api-Key";
    /// <summary>
    /// Gets or sets the list of valid API clients, each with a name and secret key. Requests must present a valid key in the <c>X-Api-Key</c> header to be authenticated.
    /// </summary>
    public List<ApiClient> Clients { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Clients.Any(client => string.IsNullOrWhiteSpace(client.Name)))
            yield return new ValidationResult("Every API client requires a nonblank name.", [nameof(Clients)]);
        if (Clients.Any(client => string.IsNullOrWhiteSpace(client.Key)))
            yield return new ValidationResult("Every API client requires a nonblank key.", [nameof(Clients)]);
        if (Clients.Where(client => !string.IsNullOrWhiteSpace(client.Name))
            .GroupBy(client => client.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            yield return new ValidationResult("API client names must be unique (case-insensitive).", [nameof(Clients)]);
        if (Clients.Where(client => !string.IsNullOrWhiteSpace(client.Key))
            .GroupBy(client => client.Key, StringComparer.Ordinal).Any(group => group.Count() > 1))
            yield return new ValidationResult("API client keys must be unique.", [nameof(Clients)]);
    }
}

/// <summary>
/// Authenticates requests by the <c>X-Api-Key</c> header. The authenticated user's name is the
/// client name, which selects that client's isolated sandbox dataset.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    IOptionsMonitor<ApiKeyAuthOptions> apiKeyOptions,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    /// <summary>
    /// Gets the name of the authentication scheme used for API key authentication. This is also the name of the authentication handler.
    /// </summary>
    public const string SchemeName = "ApiKey";
    /// <summary>
    /// Handles the authentication of incoming requests by checking for a valid API key in the <c>X-Api-Key</c> header. If a valid key is found, the request is authenticated and associated with the corresponding client; otherwise, authentication fails.
    /// </summary>
    /// <returns></returns>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthOptions.HeaderName, out var values) || string.IsNullOrWhiteSpace(values))
            return Task.FromResult(AuthenticateResult.NoResult());

        var client = FindClient(values.ToString());
        if (client is null)
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, client.Name)], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
    /// <summary>
    /// Finds the API client associated with the given presented key by performing a constant-time comparison against all configured clients. Returns the matching client if found; otherwise, returns null.
    /// </summary>
    /// <param name="presentedKey"></param>
    /// <returns></returns>
    private ApiClient? FindClient(string presentedKey)
    {
        var presented = Encoding.UTF8.GetBytes(presentedKey);
        ApiClient? match = null;

        // Check every key with a constant-time comparison so timing does not reveal partial matches.
        foreach (var client in apiKeyOptions.CurrentValue.Clients)
        {
            if (string.IsNullOrEmpty(client.Key))
                continue;
            if (CryptographicOperations.FixedTimeEquals(presented, Encoding.UTF8.GetBytes(client.Key)))
                match = client;
        }

        return match;
    }
}
