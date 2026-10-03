using System.ComponentModel.DataAnnotations;

namespace Ordernest.Backend.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public decimal Price { get; set; }
    public decimal? CostPrice { get; set; }
    public int StockQuantity { get; set; }
    public int LowStockThreshold { get; set; } = 5;
    public int CategoryId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Optimistic-concurrency token maintained by SQL Server (rowversion column).
    /// Every UPDATE becomes "WHERE RowVersion = @original", so two cashiers
    /// selling the last unit concurrently cannot both succeed silently.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public Category Category { get; set; } = null!;
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
