namespace PoMemeVideo.Shared.Models;

public class UserIdentityDto
{
    public Guid IdentityId { get; init; }
    public string IdentityType { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}
