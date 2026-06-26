// GoF: Entity
namespace PoMemeVideo.Api.Entities;

public class UserIdentity
{
    public Guid IdentityId { get; init; } = Guid.NewGuid();
    public required string IdentityType { get; init; } // "Microsoft" | "ANON"
    public required string DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
