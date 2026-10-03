using System.ComponentModel.DataAnnotations;

namespace Ordernest.Backend.DTOs.Auth;

public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);

public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password,
    [Required, MaxLength(150)] string FullName,
    [Required] string Role); // "Admin" | "Cashier"

public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, MinLength(8)] string NewPassword);

public record MeResponse(string Id, string Email, string FullName, IReadOnlyList<string> Roles);
