namespace ShouTech.Domain.Entities;

public enum CashVoucherKind
{
    Receipt = 1,
    Payment = 2
}

public enum CashVoucherStatus
{
    Draft = 0,
    Posted = 1,
    Reversed = 2
}

/// <summary>A single account allocation within a receipt or payment voucher.</summary>
public sealed record CashVoucherLine
{
    private const decimal MaximumAmount = 999_999_999_999_999.9999m;

    public Guid AccountId { get; }
    public decimal Amount { get; }
    public string? CostCenter { get; }
    public string? InvoiceReference { get; }
    public string? Note { get; }

    public CashVoucherLine(
        Guid accountId,
        decimal amount,
        string? costCenter = null,
        string? invoiceReference = null,
        string? note = null)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("An allocation account is required.", nameof(accountId));
        if (amount <= 0 || amount > MaximumAmount || decimal.Round(amount, 4) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount), "Allocation amounts must be positive and fit numeric(19,4).");

        CostCenter = NormalizeOptional(costCenter, 100, nameof(costCenter));
        InvoiceReference = NormalizeOptional(invoiceReference, 100, nameof(invoiceReference));
        Note = NormalizeOptional(note, 500, nameof(note));
        AccountId = accountId;
        Amount = amount;
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

/// <summary>
/// Immutable voucher request with the accounting invariants required before persistence.
/// Posted state is managed exclusively by the repository's transactional workflow.
/// </summary>
public sealed class CashVoucher
{
    private const decimal MaximumAmount = 999_999_999_999_999.9999m;
    private readonly List<CashVoucherLine> _lines;

    public Guid Id { get; } = Guid.NewGuid();
    public CashVoucherKind Kind { get; }
    public DateOnly VoucherDate { get; }
    public string VoucherNumber { get; }
    public Guid TreasuryAccountId { get; }
    public string? PartyName { get; }
    public decimal TotalAmount { get; }
    public string CurrencyCode { get; }
    public decimal ExchangeRate { get; }
    public string? ReferenceNumber { get; }
    public string? CostCenter { get; }
    public string? Memo { get; }
    public IReadOnlyList<CashVoucherLine> Lines => _lines.AsReadOnly();

    public CashVoucher(
        CashVoucherKind kind,
        DateOnly voucherDate,
        string voucherNumber,
        Guid treasuryAccountId,
        string? partyName,
        decimal totalAmount,
        string currencyCode,
        decimal exchangeRate,
        IEnumerable<CashVoucherLine> lines,
        string? referenceNumber = null,
        string? costCenter = null,
        string? memo = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (string.IsNullOrWhiteSpace(voucherNumber))
            throw new ArgumentException("Voucher number is required.", nameof(voucherNumber));
        var normalizedNumber = voucherNumber.Trim();
        if (normalizedNumber.Length > 100)
            throw new ArgumentException("Voucher number cannot exceed 100 characters.", nameof(voucherNumber));
        if (treasuryAccountId == Guid.Empty)
            throw new ArgumentException("A treasury/bank account is required.", nameof(treasuryAccountId));
        if (totalAmount <= 0 || totalAmount > MaximumAmount || decimal.Round(totalAmount, 4) != totalAmount)
            throw new ArgumentOutOfRangeException(nameof(totalAmount), "Voucher totals must be positive and fit numeric(19,4).");
        if (string.IsNullOrWhiteSpace(currencyCode) ||
            currencyCode.Trim().Length != 3 ||
            !currencyCode.Trim().All(char.IsAsciiLetter))
            throw new ArgumentException("Currency code must be a three-letter ASCII ISO code.", nameof(currencyCode));
        if (exchangeRate <= 0 || exchangeRate > 99_999_999_999.99999999m ||
            decimal.Round(exchangeRate, 8) != exchangeRate)
            throw new ArgumentOutOfRangeException(nameof(exchangeRate), "Exchange rate must be positive and fit numeric(19,8).");

        ArgumentNullException.ThrowIfNull(lines);
        _lines = lines.ToList();
        if (_lines.Count == 0)
            throw new InvalidOperationException("A cash voucher requires at least one allocation line.");
        if (_lines.Any(line => line is null))
            throw new ArgumentException("Voucher lines cannot contain null entries.", nameof(lines));
        if (_lines.Any(line => line.AccountId == treasuryAccountId))
            throw new InvalidOperationException("The allocation account cannot be the same as the treasury/bank account.");
        if (_lines.Sum(line => line.Amount) != totalAmount)
            throw new InvalidOperationException("Voucher total must equal the sum of all allocation lines.");

        var normalizedParty = NormalizeOptional(partyName, 250, nameof(partyName));
        var normalizedReference = NormalizeOptional(referenceNumber, 100, nameof(referenceNumber));
        var normalizedCostCenter = NormalizeOptional(costCenter, 100, nameof(costCenter));
        var normalizedMemo = NormalizeOptional(memo, 500, nameof(memo));

        Kind = kind;
        VoucherDate = voucherDate;
        VoucherNumber = normalizedNumber;
        TreasuryAccountId = treasuryAccountId;
        PartyName = normalizedParty;
        TotalAmount = totalAmount;
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        ExchangeRate = exchangeRate;
        ReferenceNumber = normalizedReference;
        CostCenter = normalizedCostCenter;
        Memo = normalizedMemo;
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

public sealed record CashVoucherSnapshot(
    Guid Id,
    Guid CompanyId,
    CashVoucherKind Kind,
    DateOnly VoucherDate,
    string VoucherNumber,
    Guid TreasuryAccountId,
    string TreasuryAccountCode,
    string TreasuryAccountName,
    string? PartyName,
    decimal TotalAmount,
    string CurrencyCode,
    decimal ExchangeRate,
    string? ReferenceNumber,
    string? CostCenter,
    string? Memo,
    Guid? FiscalPeriodId,
    Guid? JournalEntryId,
    Guid? ReversalJournalEntryId,
    CashVoucherStatus Status,
    IReadOnlyList<CashVoucherLineSnapshot> Lines);

public sealed record CashVoucherLineSnapshot(
    int LineNumber,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    decimal Amount,
    string? CostCenter,
    string? InvoiceReference,
    string? Note);
