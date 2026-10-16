using ShouTech.Domain.Entities;
using ShouTech.Domain.Interfaces;

namespace ShouTech.Application.Services;

/// <summary>Application boundary for currency master data and append-only, company-scoped rate history.</summary>
public sealed class CurrencyService
{
    private readonly ICurrencyRepository _repository;

    public CurrencyService(ICurrencyRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public Task<Guid> ResolveCompanyIdAsync(string companyCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(companyCode))
            throw new ArgumentException("Company code is required.", nameof(companyCode));
        return _repository.ResolveCompanyIdAsync(companyCode, cancellationToken);
    }

    public Task<string> GetCompanyBaseCurrencyCodeAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company is required.", nameof(companyId));
        return _repository.GetCompanyBaseCurrencyCodeAsync(companyId, cancellationToken);
    }

    public Task<CurrencySnapshot?> LoadAsync(Guid companyId, string currencyCode, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company is required.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new ArgumentException("Currency code is required.", nameof(currencyCode));
        return _repository.LoadAsync(companyId, currencyCode.Trim(), cancellationToken);
    }

    public Task<int> SaveAsync(
        Guid companyId,
        CurrencyDefinition definition,
        IReadOnlyCollection<CurrencyRateDraft> newRates,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company is required.", nameof(companyId));
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(newRates);
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Actor is required.", nameof(actor));
        return _repository.SaveAsync(companyId, definition, newRates, actor, cancellationToken);
    }
}
