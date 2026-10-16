using ShouTech.Domain.Entities;

namespace ShouTech.Domain.Interfaces;

/// <summary>
/// Repository interface for bank persistence operations.
/// </summary>
public interface IBankRepository
{
    /// <summary>
    /// Resolves the company ID from the company code.
    /// </summary>
    Task<Guid> ResolveCompanyIdAsync(string companyCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a new bank to the database.
    /// </summary>
    Task<Guid> SaveAsync(Bank bank, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a bank by ID.
    /// </summary>
    Task<BankSnapshot?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a bank by code within a specific company.
    /// </summary>
    Task<BankSnapshot?> FindByCodeAsync(Guid companyId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all banks for a specific company.
    /// </summary>
    Task<IReadOnlyList<BankSnapshot>> GetAllAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing bank.
    /// </summary>
    Task<bool> UpdateAsync(Bank bank, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft deletes a bank.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, string deletedBy, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a soft-deleted bank.
    /// </summary>
    Task<bool> RestoreAsync(Guid id, string restoredBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a bank code already exists for a company.
    /// </summary>
    Task<bool> CodeExistsAsync(Guid companyId, string code, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets banks by filter criteria with pagination.
    /// </summary>
    Task<BankQueryResult> GetByFilterAsync(
        Guid companyId,
        string? code = null,
        string? name = null,
        BankType? type = null,
        bool? isActive = null,
        bool includeDeleted = false,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches banks by name or code.
    /// </summary>
    Task<IReadOnlyList<BankSnapshot>> SearchAsync(
        Guid companyId,
        string searchTerm,
        int maxResults = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of banks for a company.
    /// </summary>
    Task<int> CountAsync(Guid companyId, bool includeDeleted = false, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a bank query with pagination.
/// </summary>
public sealed class BankQueryResult
{
    public IReadOnlyList<BankSnapshot> Banks { get; }
    public int TotalCount { get; }

    public BankQueryResult(IReadOnlyList<BankSnapshot> banks, int totalCount)
    {
        Banks = banks;
        TotalCount = totalCount;
    }
}
