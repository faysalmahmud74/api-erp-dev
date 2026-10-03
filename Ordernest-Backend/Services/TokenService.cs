using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Ordernest.Backend.Models;

namespace Ordernest.Backend.Services;

public class TokenService(UserManager<ApplicationUser> users, IConfiguration config) : ITokenService
{
    public async Task<AuthResponse> CreateTokenAsync(ApplicationUser user)
    {
        var roles = await users.GetRolesAsync(user);
        var minutes = int.Parse(config["Jwt:ExpiryMinutes"] ?? "720");
        var expires = DateTime.UtcNow.AddMinutes(minutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("name", user.FullName),
            new(JwtRegisteredClaimNames.Email, user.Email ?? "")
        };
        // One "role" claim per role — matches RoleClaimType configured in JwtBearer.
        claims.AddRange(roles.Select(r => new Claim("role", r)));

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return new AuthResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            expires,
            user.Id,
            user.Email ?? string.Empty,
            user.FullName,
            roles.ToList());
    }
}
