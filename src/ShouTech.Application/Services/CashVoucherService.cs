using ShouTech.Domain.Entities;
using ShouTech.Domain.Interfaces;

namespace ShouTech.Application.Services;

/// <summary>Application boundary for database-backed receipt/payment voucher operations.</summary>
public sealed class CashVoucherService
{
    private readonly ICashVoucherRepository _repository;

    public CashVoucherService(ICashVoucherRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public Task<Guid> ResolveCompanyIdAsync(string companyCode, CancellationToken cancellationToken = default) =>
        _repository.ResolveCompanyIdAsync(companyCode, cancellationToken);

    public Task<Guid> ResolveAccountIdAsync(Guid companyId, string accountCode, CancellationToken cancellationToken = default) =>
        _repository.ResolveAccountIdAsync(companyId, accountCode, cancellationToken);

    public Task<string> ResolveAccountNameAsync(Guid companyId, string accountCode, CancellationToken cancellationToken = default) =>
        _repository.ResolveAccountNameAsync(companyId, accountCode, cancellationToken);

    public Task<Guid> CreateDraftAsync(Guid companyId, CashVoucher voucher, string userId, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));
        ArgumentNullException.ThrowIfNull(voucher);
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User is required.", nameof(userId));
        return _repository.SaveDraftAsync(companyId, voucher, userId, cancellationToken);
    }

    public Task<CashVoucherSnapshot?> FindByDocumentNumberAsync(
        Guid companyId,
        CashVoucherKind kind,
        string voucherNumber,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(voucherNumber)) throw new ArgumentException("Voucher number is required.", nameof(voucherNumber));
        return _repository.FindByDocumentNumberAsync(companyId, kind, voucherNumber.Trim(), cancellationToken);
    }

    public Task<Guid> PostAsync(Guid companyId, CashVoucherKind kind, Guid voucherId, string userId, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));
        if (voucherId == Guid.Empty) throw new ArgumentException("Voucher is required.", nameof(voucherId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User is required.", nameof(userId));
        return _repository.PostAsync(companyId, kind, voucherId, userId, cancellationToken);
    }

    public Task<Guid> ReverseAsync(Guid companyId, CashVoucherKind kind, Guid voucherId, string userId, string reason, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));
        if (voucherId == Guid.Empty) throw new ArgumentException("Voucher is required.", nameof(voucherId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reversal reason is required.", nameof(reason));
        return _repository.ReverseAsync(companyId, kind, voucherId, userId, reason.Trim(), cancellationToken);
    }
}
