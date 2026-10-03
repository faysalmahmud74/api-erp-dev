using System.ComponentModel.DataAnnotations;

namespace Ordernest.Backend.DTOs.Users;

public record UserDto(
    string Id, string Email, string FullName,
    IReadOnlyList<string> Roles, bool IsActive, DateTime CreatedAt);

public record UpdateRolesRequest([Required, MinLength(1)] List<string> Roles);
