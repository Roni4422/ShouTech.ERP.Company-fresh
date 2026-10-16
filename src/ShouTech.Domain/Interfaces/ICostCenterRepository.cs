using ShouTech.Domain.Entities;

namespace ShouTech.Domain.Interfaces;

/// <summary>
/// Repository interface for cost center persistence operations.
/// </summary>
public interface ICostCenterRepository
{
    /// <summary>
    /// Resolves the company ID from the company code.
    /// </summary>
    Task<Guid> ResolveCompanyIdAsync(string companyCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a new cost center to the database.
    /// </summary>
    Task<Guid> SaveAsync(CostCenter costCenter, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a cost center by ID.
    /// </summary>
    Task<CostCenterSnapshot?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a cost center by code within a specific company.
    /// </summary>
    Task<CostCenterSnapshot?> FindByCodeAsync(Guid companyId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all cost centers for a specific company.
    /// </summary>
    Task<IReadOnlyList<CostCenterSnapshot>> GetAllAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing cost center.
    /// </summary>
    Task<bool> UpdateAsync(CostCenter costCenter, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft deletes a cost center.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, string deletedBy, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a soft-deleted cost center.
    /// </summary>
    Task<bool> RestoreAsync(Guid id, string restoredBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a cost center code already exists for a company.
    /// </summary>
    Task<bool> CodeExistsAsync(Guid companyId, string code, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cost centers by filter criteria with pagination.
    /// </summary>
    Task<CostCenterQueryResult> GetByFilterAsync(
        Guid companyId,
        string? code = null,
        string? name = null,
        bool? isActive = null,
        bool includeDeleted = false,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches cost centers by name or code.
    /// </summary>
    Task<IReadOnlyList<CostCenterSnapshot>> SearchAsync(
        Guid companyId,
        string searchTerm,
        int maxResults = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of cost centers for a company.
    /// </summary>
    Task<int> CountAsync(Guid companyId, bool includeDeleted = false, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a cost center query with pagination.
/// </summary>
public sealed class CostCenterQueryResult
{
    public IReadOnlyList<CostCenterSnapshot> CostCenters { get; }
    public int TotalCount { get; }

    public CostCenterQueryResult(IReadOnlyList<CostCenterSnapshot> costCenters, int totalCount)
    {
        CostCenters = costCenters;
        TotalCount = totalCount;
    }
}
