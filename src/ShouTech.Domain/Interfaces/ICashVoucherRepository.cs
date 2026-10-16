using ShouTech.Domain.Entities;

namespace ShouTech.Domain.Interfaces;

public interface ICashVoucherRepository
{
    Task<Guid> ResolveCompanyIdAsync(string companyCode, CancellationToken cancellationToken = default);
    Task<Guid> ResolveAccountIdAsync(Guid companyId, string accountCode, CancellationToken cancellationToken = default);
    Task<string> ResolveAccountNameAsync(Guid companyId, string accountCode, CancellationToken cancellationToken = default);
    Task<Guid> SaveDraftAsync(Guid companyId, CashVoucher voucher, string userId, CancellationToken cancellationToken = default);
    Task<CashVoucherSnapshot?> FindByDocumentNumberAsync(Guid companyId, CashVoucherKind kind, string voucherNumber, CancellationToken cancellationToken = default);
    Task<Guid> PostAsync(Guid companyId, CashVoucherKind kind, Guid voucherId, string userId, CancellationToken cancellationToken = default);
    Task<Guid> ReverseAsync(Guid companyId, CashVoucherKind kind, Guid voucherId, string userId, string reason, CancellationToken cancellationToken = default);
}
