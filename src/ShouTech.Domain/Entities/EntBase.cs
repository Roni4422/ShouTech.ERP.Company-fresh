// shoutech_erp_v0.1.6/src/ShouTech.Domain/Entities/EntBase.cs
using ShouTech.Domain.Entities;

using System.ComponentModel.DataAnnotations;

namespace ShouTech.Domain.Entities;

/// <summary>
/// Base class for all domain entities (GUID + RowVersion + SoftDelete + Audit).
/// </summary>
public abstract class EntBase
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    [Timestamp]
    public byte[] RowVersion { get; protected set; } = Array.Empty<byte>();

    public bool IsDeleted { get; protected set; }
    public DateTime? DeletedAt { get; protected set; }
    public Guid? DeletedBy { get; protected set; }

    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; protected set; }
    public DateTime? ModifiedAt { get; protected set; }
    public Guid? ModifiedBy { get; protected set; }

    public virtual void SoftDelete(Guid deletedBy)
    {
        if (IsDeleted) return;
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        DeletedBy = deletedBy;
        ModifiedAt = DeletedAt;
        ModifiedBy = deletedBy;
    }

    public virtual void Restore(Guid restoredBy)
    {
        if (!IsDeleted) return;
        IsDeleted = false;
        DeletedAt = null;
        DeletedBy = null;
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = restoredBy;
    }

    /// <summary>Restores persisted identity and audit values when loading from storage.</summary>
    public void Rehydrate(Guid id, DateTime createdAt, Guid? createdBy, DateTime? modifiedAt, Guid? modifiedBy)
    {
        Id = id;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }

    protected void Touch(Guid modifiedBy)
    {
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = modifiedBy;
    }
}