namespace ShouTech.Domain.Entities;

public sealed class JournalEntry
{
    private readonly List<JournalLine> _lines = new();

    public Guid Id { get; } = Guid.NewGuid();
    public DateOnly EntryDate { get; }
    public string Description { get; }
    public string? DocumentNumber { get; }
    public string? ReferenceNumber { get; }
    public string CurrencyCode { get; }
    public decimal ExchangeRate { get; }
    public JournalEntryStatus Status { get; private set; } = JournalEntryStatus.Draft;
    public IReadOnlyCollection<JournalLine> Lines => _lines.AsReadOnly();

    public JournalEntry(DateOnly entryDate, string description, string? referenceNumber = null, string currencyCode = "SAR", decimal exchangeRate = 1m, string? documentNumber = null)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required.", nameof(description));
        var normalizedDescription = description.Trim();
        if (normalizedDescription.Length > 500)
            throw new ArgumentException("Description cannot exceed 500 characters.", nameof(description));
        if (string.IsNullOrWhiteSpace(currencyCode) ||
            currencyCode.Trim().Length != 3 ||
            !currencyCode.Trim().All(char.IsAsciiLetter))
            throw new ArgumentException("Currency code must be a three-letter ASCII ISO code.", nameof(currencyCode));
        if (exchangeRate <= 0 ||
            exchangeRate > 99_999_999_999.99999999m ||
            decimal.Round(exchangeRate, 8) != exchangeRate)
            throw new ArgumentOutOfRangeException(nameof(exchangeRate),
                "Exchange rate must be positive and fit the database precision numeric(19,8).");

        var normalizedDocumentNumber = string.IsNullOrWhiteSpace(documentNumber) ? null : documentNumber.Trim();
        if (normalizedDocumentNumber?.Length > 100)
            throw new ArgumentException("Document number cannot exceed 100 characters.", nameof(documentNumber));
        var normalizedReferenceNumber = string.IsNullOrWhiteSpace(referenceNumber) ? null : referenceNumber.Trim();
        if (normalizedReferenceNumber?.Length > 100)
            throw new ArgumentException("Reference number cannot exceed 100 characters.", nameof(referenceNumber));

        EntryDate = entryDate;
        Description = normalizedDescription;
        DocumentNumber = normalizedDocumentNumber;
        ReferenceNumber = normalizedReferenceNumber;
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        ExchangeRate = exchangeRate;
    }

    public void AddLine(Guid accountId, decimal debit, decimal credit, string? note = null, string? costCenter = null)
    {
        var line = new JournalLine(accountId, debit, credit, note, costCenter);
        _lines.Add(line);
    }

    public void EnsureBalanced()
    {
        if (_lines.Count < 2)
            throw new InvalidOperationException("A journal entry requires at least two lines.");

        var totalDebit = _lines.Sum(line => line.Debit);
        var totalCredit = _lines.Sum(line => line.Credit);

        if (totalDebit != totalCredit)
            throw new InvalidOperationException("Journal entry debit and credit totals must balance.");

        if (totalDebit == 0)
            throw new InvalidOperationException("A journal entry must have a non-zero amount.");
    }

    public void Post()
    {
        if (Status != JournalEntryStatus.Draft)
            throw new InvalidOperationException("Only draft journal entries can be posted.");

        EnsureBalanced();
        Status = JournalEntryStatus.Posted;
    }

    public void Reverse()
    {
        if (Status != JournalEntryStatus.Posted)
            throw new InvalidOperationException("Only posted journal entries can be reversed.");

        Status = JournalEntryStatus.Reversed;
    }
}

public enum JournalEntryStatus
{
    Draft,
    Posted,
    Reversed
}

public sealed class JournalLine
{
    public Guid AccountId { get; }
    public decimal Debit { get; }
    public decimal Credit { get; }
    public string? Note { get; }
    public string? CostCenter { get; }

    private static void ValidateMoneyPrecision(decimal amount, string parameterName)
    {
        const decimal maximum = 999_999_999_999_999.9999m;
        if (amount > maximum || decimal.Round(amount, 4) != amount)
            throw new ArgumentOutOfRangeException(parameterName,
                "Journal amounts must fit the database precision numeric(19,4).");
    }

    public JournalLine(Guid accountId, decimal debit, decimal credit, string? note = null, string? costCenter = null)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account is required.", nameof(accountId));
        if (debit < 0)
            throw new ArgumentOutOfRangeException(nameof(debit), "Debit amounts cannot be negative.");
        if (credit < 0)
            throw new ArgumentOutOfRangeException(nameof(credit), "Credit amounts cannot be negative.");
        ValidateMoneyPrecision(debit, nameof(debit));
        ValidateMoneyPrecision(credit, nameof(credit));
        if (debit > 0 && credit > 0)
            throw new ArgumentException("A line cannot contain both debit and credit.");
        if (debit == 0 && credit == 0)
            throw new ArgumentException("A line must contain a debit or credit amount.");

        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (normalizedNote?.Length > 500)
            throw new ArgumentException("Line note cannot exceed 500 characters.", nameof(note));
        var normalizedCostCenter = string.IsNullOrWhiteSpace(costCenter) ? null : costCenter.Trim();
        if (normalizedCostCenter?.Length > 100)
            throw new ArgumentException("Cost center cannot exceed 100 characters.", nameof(costCenter));

        AccountId = accountId;
        Debit = debit;
        Credit = credit;
        Note = normalizedNote;
        CostCenter = normalizedCostCenter;
    }
}