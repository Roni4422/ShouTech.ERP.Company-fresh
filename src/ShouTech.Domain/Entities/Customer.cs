namespace ShouTech.Domain.Entities;

public enum CustomerType
{
    Customer = 1,
    Supplier = 2,
    Both = 3
}

/// <summary>
/// Represents a customer or supplier in the ERP system.
/// </summary>
public sealed class Customer : EntBase
{
    private const int MaxCodeLength = 50;
    private const int MaxNameLength = 200;
    private const int MaxTaxNoLength = 50;
    private const int MaxCrNoLength = 50;
    private const int MaxPhoneLength = 30;
    private const int MaxMobileLength = 30;
    private const int MaxEmailLength = 100;
    private const int MaxAddressLength = 500;
    private const decimal MaxCreditLimit = 999_999_999_999_999.9999m;

    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public CustomerType Type { get; private set; }
    public string TaxNo { get; private set; } = string.Empty;
    public string CrNo { get; private set; } = string.Empty;
    public decimal CreditLimit { get; private set; }
    public int CreditDays { get; private set; }
    public Guid? PriceTierId { get; private set; }
    public Guid? GlAccountId { get; private set; }
    public string Phone { get; private set; } = string.Empty;
    public string Mobile { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    public Customer(
        Guid companyId,
        string code,
        string nameAr,
        string nameEn,
        CustomerType type,
        string? taxNo = null,
        string? crNo = null,
        decimal creditLimit = 0m,
        int creditDays = 30,
        Guid? priceTierId = null,
        Guid? glAccountId = null,
        string? phone = null,
        string? mobile = null,
        string? email = null,
        string? address = null,
        bool isActive = true)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company ID is required.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Customer code is required.", nameof(code));
        if (code.Trim().Length > MaxCodeLength)
            throw new ArgumentException($"Customer code cannot exceed {MaxCodeLength} characters.", nameof(code));
        if (string.IsNullOrWhiteSpace(nameAr))
            throw new ArgumentException("Arabic name is required.", nameof(nameAr));
        if (nameAr.Trim().Length > MaxNameLength)
            throw new ArgumentException($"Arabic name cannot exceed {MaxNameLength} characters.", nameof(nameAr));
        if (string.IsNullOrWhiteSpace(nameEn))
            throw new ArgumentException("English name is required.", nameof(nameEn));
        if (nameEn.Trim().Length > MaxNameLength)
            throw new ArgumentException($"English name cannot exceed {MaxNameLength} characters.", nameof(nameEn));
        if (creditLimit < 0 || creditLimit > MaxCreditLimit || decimal.Round(creditLimit, 4) != creditLimit)
            throw new ArgumentOutOfRangeException(nameof(creditLimit), "Credit limit must be non-negative and fit numeric(19,4).");
        if (creditDays < 0 || creditDays > 3650)
            throw new ArgumentOutOfRangeException(nameof(creditDays), "Credit days must be between 0 and 3650.");

        CompanyId = companyId;
        Code = code.Trim();
        NameAr = nameAr.Trim();
        NameEn = nameEn.Trim();
        Type = type;
        TaxNo = NormalizeOptional(taxNo, MaxTaxNoLength, nameof(taxNo));
        CrNo = NormalizeOptional(crNo, MaxCrNoLength, nameof(crNo));
        CreditLimit = creditLimit;
        CreditDays = creditDays;
        PriceTierId = priceTierId;
        GlAccountId = glAccountId;
        Phone = NormalizeOptional(phone, MaxPhoneLength, nameof(phone));
        Mobile = NormalizeOptional(mobile, MaxMobileLength, nameof(mobile));
        Email = NormalizeOptional(email, MaxEmailLength, nameof(email));
        Address = NormalizeOptional(address, MaxAddressLength, nameof(address));
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
        string? nameAr = null,
        string? nameEn = null,
        CustomerType? type = null,
        string? taxNo = null,
        string? crNo = null,
        decimal? creditLimit = null,
        int? creditDays = null,
        Guid? priceTierId = null,
        Guid? glAccountId = null,
        string? phone = null,
        string? mobile = null,
        string? email = null,
        string? address = null,
        bool? isActive = null,
        Guid modifiedBy = default)
    {
        if (nameAr != null)
        {
            if (string.IsNullOrWhiteSpace(nameAr))
                throw new ArgumentException("Arabic name cannot be empty.", nameof(nameAr));
            if (nameAr.Trim().Length > MaxNameLength)
                throw new ArgumentException($"Arabic name cannot exceed {MaxNameLength} characters.", nameof(nameAr));
            NameAr = nameAr.Trim();
        }
        if (nameEn != null)
        {
            if (string.IsNullOrWhiteSpace(nameEn))
                throw new ArgumentException("English name cannot be empty.", nameof(nameEn));
            if (nameEn.Trim().Length > MaxNameLength)
                throw new ArgumentException($"English name cannot exceed {MaxNameLength} characters.", nameof(nameEn));
            NameEn = nameEn.Trim();
        }
        if (type != null) Type = type.Value;
        if (taxNo != null) TaxNo = NormalizeOptional(taxNo, MaxTaxNoLength, nameof(taxNo));
        if (crNo != null) CrNo = NormalizeOptional(crNo, MaxCrNoLength, nameof(crNo));
        if (creditLimit != null)
        {
            if (creditLimit.Value < 0 || creditLimit.Value > MaxCreditLimit || decimal.Round(creditLimit.Value, 4) != creditLimit.Value)
                throw new ArgumentOutOfRangeException(nameof(creditLimit), "Credit limit must be non-negative and fit numeric(19,4).");
            CreditLimit = creditLimit.Value;
        }
        if (creditDays != null)
        {
            if (creditDays.Value < 0 || creditDays.Value > 3650)
                throw new ArgumentOutOfRangeException(nameof(creditDays), "Credit days must be between 0 and 3650.");
            CreditDays = creditDays.Value;
        }
        if (priceTierId != null) PriceTierId = priceTierId;
        if (glAccountId != null) GlAccountId = glAccountId;
        if (phone != null) Phone = NormalizeOptional(phone, MaxPhoneLength, nameof(phone));
        if (mobile != null) Mobile = NormalizeOptional(mobile, MaxMobileLength, nameof(mobile));
        if (email != null) Email = NormalizeOptional(email, MaxEmailLength, nameof(email));
        if (address != null) Address = NormalizeOptional(address, MaxAddressLength, nameof(address));
        if (isActive != null) IsActive = isActive.Value;

        if (modifiedBy != default) Touch(modifiedBy);
    }
}

/// <summary>
/// Snapshot for reading customer data from the database.
/// </summary>
public sealed class CustomerSnapshot
{
    public Guid Id { get; }
    public Guid CompanyId { get; }
    public string Code { get; }
    public string NameAr { get; }
    public string NameEn { get; }
    public CustomerType Type { get; }
    public string TaxNo { get; }
    public string CrNo { get; }
    public decimal CreditLimit { get; }
    public int CreditDays { get; }
    public Guid? PriceTierId { get; }
    public Guid? GlAccountId { get; }
    public string Phone { get; }
    public string Mobile { get; }
    public string Email { get; }
    public string Address { get; }
    public bool IsActive { get; }
    public bool IsDeleted { get; }
    public DateTime CreatedAt { get; }
    public Guid? CreatedBy { get; }
    public DateTime? ModifiedAt { get; }
    public Guid? ModifiedBy { get; }

    public CustomerSnapshot(
        Guid id,
        Guid companyId,
        string code,
        string nameAr,
        string nameEn,
        CustomerType type,
        string taxNo,
        string crNo,
        decimal creditLimit,
        int creditDays,
        Guid? priceTierId,
        Guid? glAccountId,
        string phone,
        string mobile,
        string email,
        string address,
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
        NameAr = nameAr;
        NameEn = nameEn;
        Type = type;
        TaxNo = taxNo;
        CrNo = crNo;
        CreditLimit = creditLimit;
        CreditDays = creditDays;
        PriceTierId = priceTierId;
        GlAccountId = glAccountId;
        Phone = phone;
        Mobile = mobile;
        Email = email;
        Address = address;
        IsActive = isActive;
        IsDeleted = isDeleted;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }
}
