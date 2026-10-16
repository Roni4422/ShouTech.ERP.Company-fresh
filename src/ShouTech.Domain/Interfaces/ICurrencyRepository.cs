using ShouTech.Domain.Entities;

namespace ShouTech.Domain.Interfaces;

public interface ICurrencyRepository
{
    Task<Guid> ResolveCompanyIdAsync(string companyCode, CancellationToken cancellationToken = default);
    Task<string> GetCompanyBaseCurrencyCodeAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<CurrencySnapshot?> LoadAsync(Guid companyId, string currencyCode, CancellationToken cancellationToken = default);
    Task<int> SaveAsync(
        Guid companyId,
        CurrencyDefinition definition,
        IReadOnlyCollection<CurrencyRateDraft> newRates,
        string actor,
        CancellationToken cancellationToken = default);
}
