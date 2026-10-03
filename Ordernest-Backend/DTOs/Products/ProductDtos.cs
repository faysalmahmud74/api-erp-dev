using System.ComponentModel.DataAnnotations;

namespace Ordernest.Backend.DTOs.Products;

public record ProductDto(
    int Id, string Name, string Sku, string? Barcode,
    decimal Price, decimal? CostPrice, int StockQuantity, int LowStockThreshold,
    int CategoryId, string CategoryName, bool IsActive,
    DateTime CreatedAt, DateTime? UpdatedAt);

public record ProductCreateDto(
    [Required, MaxLength(150)] string Name,
    [Required, MaxLength(50)] string Sku,
    [MaxLength(50)] string? Barcode,
    [Range(0.01, 1_000_000)] decimal Price,
    [Range(0, 1_000_000)] decimal? CostPrice,
    [Range(0, 1_000_000)] int StockQuantity,
    [Range(0, 100_000)] int LowStockThreshold,
    [Range(1, int.MaxValue)] int CategoryId);

// StockQuantity is deliberately ABSENT here: after creation, stock only moves
// through sales or the explicit stock-adjustment endpoint.
public record ProductUpdateDto(
    [Required, MaxLength(150)] string Name,
    [Required, MaxLength(50)] string Sku,
    [MaxLength(50)] string? Barcode,
    [Range(0.01, 1_000_000)] decimal Price,
    [Range(0, 1_000_000)] decimal? CostPrice,
    [Range(0, 100_000)] int LowStockThreshold,
    [Range(1, int.MaxValue)] int CategoryId,
    bool IsActive);

public record StockAdjustmentDto(
    int Adjustment, // +10 restock, -3 breakage
    [MaxLength(200)] string? Reason);
