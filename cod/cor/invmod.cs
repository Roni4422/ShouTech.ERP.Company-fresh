// cod/cor/invmod.cs
using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ShouTech.Core.Security;

namespace ShouTech.Core.Inv
{
    public class InventoryModule : IInventoryService, IDisposable
    {
        private readonly UnitOfWork _uow;
        private readonly IMemoryCache _cache;
        private readonly ILogger<InventoryModule> _logger;
        private readonly ISecurityEngine _security;

        public InventoryModule(IDbConnection db, IMemoryCache cache, ILogger<InventoryModule> logger, ISecurityEngine security)
        {
            _uow = new UnitOfWork(db);
            _cache = cache;
            _logger = logger;
            _security = security;
        }

        public async Task<bool> ProcessPOSSaleAsync(POSSaleRequest req)
        {
            ArgumentNullException.ThrowIfNull(req);

            if (req.UserId == Guid.Empty || req.CompanyId == Guid.Empty)
                throw new ArgumentException("A valid user and company are required.", nameof(req));
            if (string.IsNullOrWhiteSpace(req.Barcode))
                throw new ArgumentException("A barcode is required.", nameof(req));
            if (req.WarehouseId <= 0)
                throw new ArgumentOutOfRangeException(nameof(req.WarehouseId));
            if (req.Quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(req.Quantity));
            if (req.UnitPrice < 0)
                throw new ArgumentOutOfRangeException(nameof(req.UnitPrice));

            await _security.ValidatePermissionAsync(req.UserId, "POS.ProcessSale");

            // This legacy module has no sale-line, stock, payment, or ledger persistence.
            // Do not open and commit an empty transaction or claim that a sale was posted.
            _logger.LogError(
                "POS sale rejected: persistence is not implemented in the legacy InventoryModule. CompanyId={CompanyId}, UserId={UserId}",
                req.CompanyId, req.UserId);
            throw new NotSupportedException(
                "POS sale processing is unavailable in the legacy InventoryModule because transactional sale persistence is not implemented.");
        }

        public void Dispose() => _uow?.Dispose();
    }
}