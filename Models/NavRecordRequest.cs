using System.ComponentModel.DataAnnotations;

namespace NAVDataApi.Models;

/// <summary>
/// Payload used to create or replace a NAV record.
/// </summary>
public sealed class NavRecordRequest : IValidatableObject
{
    /// <summary>
    /// Gets or sets the fund code associated with the NAV record.
    /// </summary>
    [Required, StringLength(20, MinimumLength = 2)]
    public string FundCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the fund name associated with the NAV record.
    /// </summary>
    [Required, StringLength(200)]
    public string FundName { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the share class associated with the NAV record.
    /// </summary>
    [Required, StringLength(50)]
    public string ShareClass { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the International Securities Identification Number (ISIN) associated with the NAV record.
    /// </summary>
    [Required, RegularExpression("^[A-Z]{2}[A-Z0-9]{9}[0-9]$", ErrorMessage = "ISIN must be 12 characters: 2-letter country code, 9 alphanumerics, 1 check digit.")]
    public string Isin { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the currency in which the NAV is denominated.
    /// </summary>
    [Required, RegularExpression("^[A-Z]{3}$", ErrorMessage = "Currency must be a 3-letter ISO 4217 code.")]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the date of the NAV observation.
    /// </summary>
    [Required]
    public DateOnly? NavDate { get; set; }

    /// <summary>
    /// Gets or sets the Net Asset Value (NAV) per share for the fund share class on the given date.
    /// </summary>
    [Range(typeof(decimal), "0", "79228162514264337593543950335", MinimumIsExclusive = true,
        ErrorMessage = "NAV per share must be greater than zero.")]
    public decimal NavPerShare { get; set; }

    /// <summary>
    /// Gets or sets the total number of shares outstanding for the fund share class on the given date.
    /// </summary>
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal SharesOutstanding { get; set; }
    /// <summary>
    /// Converts the request to a NAV record.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public NavRecord ToRecord(int id) => new()
    {
        Id = id,
        FundCode = FundCode.Trim().ToUpperInvariant(),
        FundName = FundName.Trim(),
        ShareClass = ShareClass.Trim(),
        Isin = Isin,
        Currency = Currency,
        NavDate = NavDate!.Value,
        NavPerShare = NavPerShare,
        SharesOutstanding = SharesOutstanding
    };

    /// <summary>Validates normalized values and the computed response before any write.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var errors = new List<ValidationResult>();
        if (FundCode?.Trim().Length is < 2)
            errors.Add(new ValidationResult("Fund code must contain at least 2 characters after trimming.",
                [nameof(FundCode)]));

        // TotalNetAssets is a decimal product. Reject unrepresentable products here,
        // rather than persisting a record that subsequently fails JSON serialization.
        try
        {
            _ = NavPerShare * SharesOutstanding;
        }
        catch (OverflowException)
        {
            errors.Add(new ValidationResult("NAV per share multiplied by shares outstanding exceeds the supported decimal range.",
                [nameof(NavPerShare), nameof(SharesOutstanding)]));
        }

        return errors;
    }
}
