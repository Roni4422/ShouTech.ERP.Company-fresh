using ShouTech.Domain.Entities;

namespace ShouTech.Domain.Interfaces;

/// <summary>
/// Repository interface for sales representative persistence operations.
/// </summary>
public interface ISalesRepresentativeRepository
{
    /// <summary>
    /// Resolves the company ID from the company code.
    /// </summary>
    Task<Guid> ResolveCompanyIdAsync(string companyCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a new sales representative to the database.
    /// </summary>
    Task<Guid> SaveAsync(SalesRepresentative representative, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a sales representative by ID.
    /// </summary>
    Task<SalesRepresentativeSnapshot?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a sales representative by code within a specific company.
    /// </summary>
    Task<SalesRepresentativeSnapshot?> FindByCodeAsync(Guid companyId, string code, CancellationToken cancellationToken = default);

    /// <summary>Finds a representative by company and code, including soft-deleted records for restore validation.</summary>
    Task<SalesRepresentativeSnapshot?> FindByCodeIncludingDeletedAsync(Guid companyId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all sales representatives for a specific company.
    /// </summary>
    Task<IReadOnlyList<SalesRepresentativeSnapshot>> GetAllAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing sales representative.
    /// </summary>
    Task<bool> UpdateAsync(SalesRepresentative representative, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft deletes a sales representative.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, Guid companyId, string deletedBy, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a soft-deleted sales representative.
    /// </summary>
    Task<bool> RestoreAsync(Guid id, Guid companyId, string restoredBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a sales representative code already exists for a company.
    /// </summary>
    Task<bool> CodeExistsAsync(Guid companyId, string code, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets sales representatives by filter criteria with pagination.
    /// </summary>
    Task<SalesRepresentativeQueryResult> GetByFilterAsync(
        Guid companyId,
        string? code = null,
        string? name = null,
        string? area = null,
        bool? isActive = null,
        bool includeDeleted = false,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches sales representatives by name or code.
    /// </summary>
    Task<IReadOnlyList<SalesRepresentativeSnapshot>> SearchAsync(
        Guid companyId,
        string searchTerm,
        int maxResults = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of sales representatives for a company.
    /// </summary>
    Task<int> CountAsync(Guid companyId, bool includeDeleted = false, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a sales representative query with pagination.
/// </summary>
public sealed class SalesRepresentativeQueryResult
{
    public IReadOnlyList<SalesRepresentativeSnapshot> Representatives { get; }
    public int TotalCount { get; }

    public SalesRepresentativeQueryResult(IReadOnlyList<SalesRepresentativeSnapshot> representatives, int totalCount)
    {
        Representatives = representatives;
        TotalCount = totalCount;
    }
}
