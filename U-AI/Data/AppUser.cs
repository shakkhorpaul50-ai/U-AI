using Microsoft.AspNetCore.Identity;

namespace UAI.Data;

public sealed class AppUser : IdentityUser
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ChatSession> Sessions { get; set; } = [];
}
