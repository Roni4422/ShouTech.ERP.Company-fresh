namespace ShouTech.Domain.Entities;

/// <summary>
/// Represents a cost center in the ERP system.
/// </summary>
public sealed class CostCenter : EntBase
{
    private const int MaxCodeLength = 50;
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 500;

    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;

    public CostCenter(
        Guid companyId,
        string code,
        string nameAr,
        string nameEn,
        string? description = null,
        bool isActive = true)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company ID is required.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Cost center code is required.", nameof(code));
        if (code.Trim().Length > MaxCodeLength)
            throw new ArgumentException($"Cost center code cannot exceed {MaxCodeLength} characters.", nameof(code));
        if (string.IsNullOrWhiteSpace(nameAr))
            throw new ArgumentException("Arabic name is required.", nameof(nameAr));
        if (nameAr.Trim().Length > MaxNameLength)
            throw new ArgumentException($"Arabic name cannot exceed {MaxNameLength} characters.", nameof(nameAr));
        if (string.IsNullOrWhiteSpace(nameEn))
            throw new ArgumentException("English name is required.", nameof(nameEn));
        if (nameEn.Trim().Length > MaxNameLength)
            throw new ArgumentException($"English name cannot exceed {MaxNameLength} characters.", nameof(nameEn));

        CompanyId = companyId;
        Code = code.Trim();
        NameAr = nameAr.Trim();
        NameEn = nameEn.Trim();
        Description = NormalizeOptional(description, MaxDescriptionLength, nameof(description));
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
        string? description = null,
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
        if (description != null)
            Description = NormalizeOptional(description, MaxDescriptionLength, nameof(description));
        if (isActive != null)
            IsActive = isActive.Value;

        if (modifiedBy != default)
            Touch(modifiedBy);
    }
}

/// <summary>
/// Snapshot for reading cost center data from the database.
/// </summary>
public sealed class CostCenterSnapshot
{
    public Guid Id { get; }
    public Guid CompanyId { get; }
    public string Code { get; }
    public string NameAr { get; }
    public string NameEn { get; }
    public string Description { get; }
    public bool IsActive { get; }
    public bool IsDeleted { get; }
    public DateTime CreatedAt { get; }
    public Guid? CreatedBy { get; }
    public DateTime? ModifiedAt { get; }
    public Guid? ModifiedBy { get; }

    public CostCenterSnapshot(
        Guid id,
        Guid companyId,
        string code,
        string nameAr,
        string nameEn,
        string description,
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
        Description = description;
        IsActive = isActive;
        IsDeleted = isDeleted;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }
}
