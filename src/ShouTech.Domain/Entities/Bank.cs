namespace ShouTech.Domain.Entities;

public enum BankType
{
    Bank = 1,
    Cash = 2
}

/// <summary>
/// Represents a bank or cash account in the ERP system.
/// </summary>
public sealed class Bank : EntBase
{
    private const int MaxCodeLength = 50;
    private const int MaxNameLength = 200;
    private const int MaxAccountNumberLength = 100;
    private const int MaxAddressLength = 500;
    private const int MaxPhoneLength = 30;

    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public BankType Type { get; private set; }
    public Guid? GlAccountId { get; private set; }
    public string AccountNumber { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    public Bank(
        Guid companyId,
        string code,
        string name,
        BankType type,
        Guid? glAccountId = null,
        string? accountNumber = null,
        string? address = null,
        string? phone = null,
        bool isActive = true)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company ID is required.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Bank code is required.", nameof(code));
        if (code.Trim().Length > MaxCodeLength)
            throw new ArgumentException($"Bank code cannot exceed {MaxCodeLength} characters.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Bank name is required.", nameof(name));
        if (name.Trim().Length > MaxNameLength)
            throw new ArgumentException($"Bank name cannot exceed {MaxNameLength} characters.", nameof(name));

        CompanyId = companyId;
        Code = code.Trim();
        Name = name.Trim();
        Type = type;
        GlAccountId = glAccountId;
        AccountNumber = NormalizeOptional(accountNumber, MaxAccountNumberLength, nameof(accountNumber));
        Address = NormalizeOptional(address, MaxAddressLength, nameof(address));
        Phone = NormalizeOptional(phone, MaxPhoneLength, nameof(phone));
        IsActive = isActive;
    }

    private static string NormalizeOptional(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new ArgumentException($"{parameterName} cannot exceed {maxLength} characters.", parameterName);
        return normalized;
    }

    public void Update(
        string? name = null,
        BankType? type = null,
        Guid? glAccountId = null,
        string? accountNumber = null,
        string? address = null,
        string? phone = null,
        bool? isActive = null,
        Guid modifiedBy = default)
    {
        if (name != null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Bank name cannot be empty.", nameof(name));
            if (name.Trim().Length > MaxNameLength)
                throw new ArgumentException($"Bank name cannot exceed {MaxNameLength} characters.", nameof(name));
            Name = name.Trim();
        }
        if (type != null) Type = type.Value;
        if (glAccountId != null) GlAccountId = glAccountId;
        if (accountNumber != null) AccountNumber = NormalizeOptional(accountNumber, MaxAccountNumberLength, nameof(accountNumber));
        if (address != null) Address = NormalizeOptional(address, MaxAddressLength, nameof(address));
        if (phone != null) Phone = NormalizeOptional(phone, MaxPhoneLength, nameof(phone));
        if (isActive != null) IsActive = isActive.Value;

        if (modifiedBy != default) Touch(modifiedBy);
    }
}

/// <summary>
/// Snapshot for reading bank data from the database.
/// </summary>
public sealed class BankSnapshot
{
    public Guid Id { get; }
    public Guid CompanyId { get; }
    public string Code { get; }
    public string Name { get; }
    public BankType Type { get; }
    public Guid? GlAccountId { get; }
    public string AccountNumber { get; }
    public string Address { get; }
    public string Phone { get; }
    public bool IsActive { get; }
    public bool IsDeleted { get; }
    public DateTime CreatedAt { get; }
    public Guid? CreatedBy { get; }
    public DateTime? ModifiedAt { get; }
    public Guid? ModifiedBy { get; }

    public BankSnapshot(
        Guid id,
        Guid companyId,
        string code,
        string name,
        BankType type,
        Guid? glAccountId,
        string accountNumber,
        string address,
        string phone,
        bool isActive,
        bool isDeleted,
        DateTime createdAt,
        Guid? createdBy,
        DateTime? modifiedAt,
        Guid? modifiedBy)
    {
        Id = id;
        CompanyId = companyId;
        Code = code;
        Name = name;
        Type = type;
        GlAccountId = glAccountId;
        AccountNumber = accountNumber;
        Address = address;
        Phone = phone;
        IsActive = isActive;
        IsDeleted = isDeleted;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }
}
