using PoMemeVideo.Domain.Entities;

namespace PoMemeVideo.Domain.Interfaces;

public interface IUserIdentityRepository
{
    Task<UserIdentity> CreateAsync(UserIdentity identity, CancellationToken cancellationToken = default);
    Task<UserIdentity?> GetByIdAsync(Guid identityId, string identityType, CancellationToken cancellationToken = default);
}
