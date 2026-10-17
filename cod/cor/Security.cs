// cod/cor/Security.cs
using System;
using System.Threading.Tasks;

namespace ShouTech.Core.Security
{
    public interface ISecurityEngine
    {
        Task ValidatePermissionAsync(Guid userId, string permissionKey);
    }

    /// <summary>
    /// Compatibility placeholder that deliberately fails closed.
    /// It must never be registered as a production authorization provider.
    /// </summary>
    [Obsolete("DummySecurityEngine cannot authorize requests. Register a real security engine.")]
    public sealed class DummySecurityEngine : ISecurityEngine
    {
        public Task ValidatePermissionAsync(Guid userId, string permissionKey)
        {
            if (userId == Guid.Empty)
                throw new UnauthorizedAccessException("A valid user identity is required.");

            if (string.IsNullOrWhiteSpace(permissionKey))
                throw new ArgumentException("A permission key is required.", nameof(permissionKey));

            throw new UnauthorizedAccessException(
                "Authorization is unavailable because DummySecurityEngine is not a real security provider.");
        }
    }
}