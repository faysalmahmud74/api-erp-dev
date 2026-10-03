using System.ComponentModel.DataAnnotations;

namespace Ordernest.Backend.DTOs.Customers;

public record CustomerDto(
    int Id, string FullName, string? Phone, string? Email, string? Address,
    string? Notes, int LoyaltyPoints, bool IsActive, DateTime CreatedAt);

public record CustomerCreateDto(
    [Required, MaxLength(150)] string FullName,
    [MaxLength(30)] string? Phone,
    [EmailAddress, MaxLength(200)] string? Email,
    [MaxLength(300)] string? Address,
    [MaxLength(500)] string? Notes);

public record CustomerUpdateDto(
    [Required, MaxLength(150)] string FullName,
    [MaxLength(30)] string? Phone,
    [EmailAddress, MaxLength(200)] string? Email,
    [MaxLength(300)] string? Address,
    [MaxLength(500)] string? Notes,
    bool IsActive);
