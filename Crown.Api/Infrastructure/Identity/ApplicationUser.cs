using Microsoft.AspNetCore.Identity;

namespace Crown.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime? LastActivityAtUtc { get; set; }
}
