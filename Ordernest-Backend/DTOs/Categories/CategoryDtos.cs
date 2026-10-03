using System.ComponentModel.DataAnnotations;

namespace Ordernest.Backend.DTOs.Categories;

public record CategoryDto(int Id, string Name, string? Description, bool IsActive, int ProductCount);

public record CategoryCreateDto(
    [Required, MaxLength(100)] string Name,
    [MaxLength(500)] string? Description);

public record CategoryUpdateDto(
    [Required, MaxLength(100)] string Name,
    [MaxLength(500)] string? Description,
    bool IsActive);
