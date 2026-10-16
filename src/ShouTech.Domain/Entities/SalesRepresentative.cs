namespace ShouTech.Domain.Entities;

/// <summary>
/// Represents a sales representative in the ERP system.
/// </summary>
public sealed class SalesRepresentative : EntBase
{
    private const int MaxCodeLength = 50;
    private const int MaxNameLength = 200;
    private const int MaxAreaLength = 100;
    private const int MaxPhoneLength = 30;

    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Area { get; private set; }
    public decimal CommissionRate { get; private set; }
    public string? Phone { get; private set; }
    public bool IsActive { get; private set; } = true;

    public SalesRepresentative(
        Guid companyId,
        string code,
        string name,
        string? area = null,
        decimal commissionRate = 0m,
        string? phone = null,
        bool isActive = true)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company ID is required.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Representative code is required.", nameof(code));
        if (code.Trim().Length > MaxCodeLength)
            throw new ArgumentException($"Representative code cannot exceed {MaxCodeLength} characters.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Representative name is required.", nameof(name));
        if (name.Trim().Length > MaxNameLength)
            throw new ArgumentException($"Representative name cannot exceed {MaxNameLength} characters.", nameof(name));
        if (commissionRate < 0 || commissionRate > 100)
            throw new ArgumentOutOfRangeException(nameof(commissionRate), "Commission rate must be between 0 and 100.");

        CompanyId = companyId;
        Code = code.Trim();
        Name = name.Trim();
        Area = NormalizeOptional(area, MaxAreaLength, nameof(area));
        CommissionRate = commissionRate;
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
        string? area = null,
        decimal? commissionRate = null,
        string? phone = null,
        bool? isActive = null,
        Guid modifiedBy = default)
    {
        if (name != null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Representative name cannot be empty.", nameof(name));
            if (name.Trim().Length > MaxNameLength)
                throw new ArgumentException($"Representative name cannot exceed {MaxNameLength} characters.", nameof(name));
            Name = name.Trim();
        }
        if (area != null)
            Area = NormalizeOptional(area, MaxAreaLength, nameof(area));
        if (commissionRate != null)
        {
            if (commissionRate.Value < 0 || commissionRate.Value > 100)
                throw new ArgumentOutOfRangeException(nameof(commissionRate), "Commission rate must be between 0 and 100.");
            CommissionRate = commissionRate.Value;
        }
        if (phone != null)
            Phone = NormalizeOptional(phone, MaxPhoneLength, nameof(phone));
        if (isActive != null)
            IsActive = isActive.Value;

        if (modifiedBy != default)
            Touch(modifiedBy);
    }
}

/// <summary>
/// Snapshot for reading sales representative data from the database.
/// </summary>
public sealed class SalesRepresentativeSnapshot
{
    public Guid Id { get; }
    public Guid CompanyId { get; }
    public string Code { get; }
    public string Name { get; }
    public string Area { get; }
    public decimal CommissionRate { get; }
    public string Phone { get; }
    public bool IsActive { get; }
    public bool IsDeleted { get; }
    public DateTime CreatedAt { get; }
    public Guid? CreatedBy { get; }
    public DateTime? ModifiedAt { get; }
    public Guid? ModifiedBy { get; }

    public SalesRepresentativeSnapshot(
        Guid id,
        Guid companyId,
        string code,
        string name,
        string area,
        decimal commissionRate,
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
        Area = area;
        CommissionRate = commissionRate;
        Phone = phone;
        IsActive = isActive;
        IsDeleted = isDeleted;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }
}
