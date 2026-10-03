using Microsoft.AspNetCore.Identity;

namespace Ordernest.Backend.Models;

/// <summary>
/// The POS staff account. Inherits IdentityUser, which already provides
/// Id (string GUID), UserName, Email, PasswordHash, SecurityStamp,
/// LockoutEnd, and the other identity plumbing.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
