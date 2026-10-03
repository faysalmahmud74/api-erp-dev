using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Ordernest.Backend.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The authenticated user's id. With MapInboundClaims = false the claim
    /// keeps its short name ("sub"), so read it via JwtRegisteredClaimNames.Sub
    /// (NOT ClaimTypes.NameIdentifier).
    /// </summary>
    public static string GetUserId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new UnauthorizedAccessException("Token has no sub claim.");
}
