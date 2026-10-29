using System.ComponentModel.DataAnnotations;
using NAVDataApi.Data;
using NAVDataApi.Security;

namespace NAVDataApi.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void Api_clients_require_nonblank_unique_names_and_keys()
    {
        var invalid = new ApiKeyAuthOptions
        {
            Clients =
            [
                new ApiClient { Name = "", Key = "same" },
                new ApiClient { Name = "client", Key = "same" },
                new ApiClient { Name = "CLIENT", Key = "other" }
            ]
        };

        var results = Validate(invalid);
        Assert.Contains(results, r => r.ErrorMessage!.Contains("name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, r => r.ErrorMessage!.Contains("key", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, r => r.ErrorMessage!.Contains("unique", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(52560, true)]
    [InlineData(52561, false)]
    public void Reset_interval_has_an_explicit_supported_range(int minutes, bool valid)
    {
        Assert.Equal(valid, Validate(new DataResetOptions { IntervalMinutes = minutes }).Count == 0);
    }

    private static List<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        return results;
    }
}
