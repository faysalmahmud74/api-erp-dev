namespace Ordernest.Backend.Models;

/// <summary>
/// A line on an order — deliberately a SNAPSHOT of the product at sale time.
/// ProductName, Sku and UnitPrice are copied, not joined, so receipts and
/// reports stay correct even after the product is renamed, repriced or removed.
/// ProductId is nullable so history survives even a hard product delete.
/// </summary>
public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineDiscount { get; set; }
    public decimal LineTotal { get; set; }

    public Order Order { get; set; } = null!;
    public Product? Product { get; set; }
}
