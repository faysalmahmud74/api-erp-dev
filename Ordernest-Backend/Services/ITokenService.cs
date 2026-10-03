using Ordernest.Backend.Models;

namespace Ordernest.Backend.Services;

public record AuthResponse(
    string Token,
    DateTime ExpiresAtUtc,
    string UserId,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles);

public interface ITokenService
{
    Task<AuthResponse> CreateTokenAsync(ApplicationUser user);
}
