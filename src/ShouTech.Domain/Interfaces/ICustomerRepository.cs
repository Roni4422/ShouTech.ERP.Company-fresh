using ShouTech.Domain.Entities;

namespace ShouTech.Domain.Interfaces;

/// <summary>
/// Repository interface for customer persistence operations.
/// </summary>
public interface ICustomerRepository
{
    /// <summary>
    /// Resolves the company ID from the company code.
    /// </summary>
    Task<Guid> ResolveCompanyIdAsync(string companyCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a new customer to the database.
    /// </summary>
    Task<Guid> SaveAsync(Customer customer, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a customer by ID.
    /// </summary>
    Task<CustomerSnapshot?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a customer by code within a specific company.
    /// </summary>
    Task<CustomerSnapshot?> FindByCodeAsync(Guid companyId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all customers for a specific company.
    /// </summary>
    Task<IReadOnlyList<CustomerSnapshot>> GetAllAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing customer.
    /// </summary>
    Task<bool> UpdateAsync(Customer customer, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft deletes a customer.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, string deletedBy, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a soft-deleted customer.
    /// </summary>
    Task<bool> RestoreAsync(Guid id, string restoredBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a customer code already exists for a company.
    /// </summary>
    Task<bool> CodeExistsAsync(Guid companyId, string code, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets customers by filter criteria with pagination.
    /// </summary>
    Task<CustomerQueryResult> GetByFilterAsync(
        Guid companyId,
        string? code = null,
        string? name = null,
        CustomerType? type = null,
        bool? isActive = null,
        bool includeDeleted = false,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches customers by name or code.
    /// </summary>
    Task<IReadOnlyList<CustomerSnapshot>> SearchAsync(
        Guid companyId,
        string searchTerm,
        int maxResults = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of customers for a company.
    /// </summary>
    Task<int> CountAsync(Guid companyId, bool includeDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets customer summary statistics for a company.
    /// </summary>
    Task<CustomerSummary> GetSummaryAsync(Guid companyId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a customer query with pagination.
/// </summary>
public sealed class CustomerQueryResult
{
    public IReadOnlyList<CustomerSnapshot> Customers { get; }
    public int TotalCount { get; }

    public CustomerQueryResult(IReadOnlyList<CustomerSnapshot> customers, int totalCount)
    {
        Customers = customers;
        TotalCount = totalCount;
    }
}

/// <summary>
/// Summary statistics for customers.
/// </summary>
public sealed class CustomerSummary
{
    public int TotalCustomers { get; }
    public int ActiveCustomers { get; }
    public int Suppliers { get; }
    public int Both { get; }
    public decimal TotalCreditLimit { get; }

    public CustomerSummary(
        int totalCustomers,
        int activeCustomers,
        int suppliers,
        int both,
        decimal totalCreditLimit)
    {
        TotalCustomers = totalCustomers;
        ActiveCustomers = activeCustomers;
        Suppliers = suppliers;
        Both = both;
        TotalCreditLimit = totalCreditLimit;
    }
}
