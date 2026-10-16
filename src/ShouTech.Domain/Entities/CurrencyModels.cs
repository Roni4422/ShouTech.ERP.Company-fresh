namespace ShouTech.Domain.Entities;

/// <summary>A shared ISO-4217 currency definition. Exchange-rate history remains company-scoped.</summary>
public sealed record CurrencyDefinition
{
    public string Code { get; }
    public string NameArabic { get; }
    public string? NameEnglish { get; }
    public string? Symbol { get; }
    public string? FractionName { get; }
    public int DecimalPlaces { get; }

    public CurrencyDefinition(
        string code,
        string nameArabic,
        string? nameEnglish = null,
        string? symbol = null,
        string? fractionName = null,
        int decimalPlaces = 2)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length != 3 || !code.Trim().All(char.IsAsciiLetter))
            throw new ArgumentException("Currency code must be a three-letter ASCII ISO code.", nameof(code));
        if (string.IsNullOrWhiteSpace(nameArabic))
            throw new ArgumentException("Arabic currency name is required.", nameof(nameArabic));
        if (decimalPlaces is < 0 or > 6)
            throw new ArgumentOutOfRangeException(nameof(decimalPlaces), "Currency decimal places must be between zero and six.");

        Code = code.Trim().ToUpperInvariant();
        NameArabic = NormalizeRequired(nameArabic, 100, nameof(nameArabic));
        NameEnglish = NormalizeOptional(nameEnglish, 100, nameof(nameEnglish));
        Symbol = NormalizeOptional(symbol, 10, nameof(symbol));
        FractionName = NormalizeOptional(fractionName, 100, nameof(fractionName));
        DecimalPlaces = decimalPlaces;
    }

    private static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
        return normalized;
    }
}

/// <summary>An append-only daily exchange-rate observation.</summary>
public sealed record CurrencyRateDraft
{
    public DateOnly EffectiveDate { get; }
    public decimal Rate { get; }
    public decimal BuyRate { get; }
    public decimal SellRate { get; }
    public decimal MinRate { get; }
    public decimal MaxRate { get; }
    public string? Notes { get; }

    public CurrencyRateDraft(
        DateOnly effectiveDate,
        decimal rate,
        decimal buyRate,
        decimal sellRate,
        decimal minRate,
        decimal maxRate,
        string? notes = null)
    {
        ValidateRate(rate, nameof(rate));
        ValidateRate(buyRate, nameof(buyRate));
        ValidateRate(sellRate, nameof(sellRate));
        ValidateRate(minRate, nameof(minRate));
        ValidateRate(maxRate, nameof(maxRate));
        if (minRate > rate || rate > maxRate)
            throw new ArgumentException("The central exchange rate must be within the minimum and maximum limits.");
        if (buyRate > sellRate)
            throw new ArgumentException("Buy rate cannot exceed sell rate.");

        if (!string.IsNullOrWhiteSpace(notes) && notes.Trim().Length > 500)
            throw new ArgumentException("Rate notes cannot exceed 500 characters.", nameof(notes));

        EffectiveDate = effectiveDate;
        Rate = rate;
        BuyRate = buyRate;
        SellRate = sellRate;
        MinRate = minRate;
        MaxRate = maxRate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    private static void ValidateRate(decimal value, string parameterName)
    {
        if (value <= 0 || value > 99_999_999_999.99999999m || decimal.Round(value, 8) != value)
            throw new ArgumentOutOfRangeException(parameterName, "Exchange rates must be positive and fit numeric(19,8).");
    }
}

public sealed record CurrencyRateSnapshot(
    long RateId,
    DateOnly EffectiveDate,
    decimal Rate,
    decimal BuyRate,
    decimal SellRate,
    decimal MinRate,
    decimal MaxRate,
    string? Notes,
    string CreatedBy);

public sealed record CurrencySnapshot(
    CurrencyDefinition Definition,
    string CompanyBaseCurrencyCode,
    IReadOnlyList<CurrencyRateSnapshot> Rates);
