using Microsoft.EntityFrameworkCore;
using Ordernest.Backend.Data;
using Ordernest.Backend.DTOs.Orders;
using Ordernest.Backend.Models;

namespace Ordernest.Backend.Services;

public class OrderService(AppDbContext db) : IOrderService
{
    /// <summary>
    /// Creates an order and decrements stock in ONE transaction.
    /// Prices/totals are computed here from DB prices — client totals are never trusted.
    /// </summary>
    public async Task<(OrderDto? Order, string? Error)> CreateAsync(CreateOrderRequest req, string userId)
    {
        await using var tx = await db.Database.BeginTransactionAsync();

        // 1. Load all products in ONE query (never per-item round trips).
        var ids = req.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products
            .Where(p => ids.Contains(p.Id) && p.IsActive)
            .ToDictionaryAsync(p => p.Id);

        var order = new Order
        {
            CustomerId = req.CustomerId,
            UserId = userId,
            PaymentMethod = req.PaymentMethod,
            DiscountAmount = req.DiscountAmount,
            Notes = req.Notes,
            Status = OrderStatus.Completed,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };

        foreach (var line in req.Items)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
                return (null, $"Product {line.ProductId} not found or inactive.");

            if (product.StockQuantity < line.Quantity)
                return (null, $"Insufficient stock for '{product.Name}': " +
                              $"{product.StockQuantity} left, {line.Quantity} requested.");

            product.StockQuantity -= line.Quantity; // decrement stock

            // Snapshot name/sku/price at sale time so history survives repricing.
            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Sku = product.Sku,
                UnitPrice = product.Price,
                Quantity = line.Quantity,
                LineDiscount = line.LineDiscount,
                LineTotal = product.Price * line.Quantity - line.LineDiscount
            });
        }

        // 2. Totals computed HERE, from DB prices.
        order.SubTotal = order.Items.Sum(i => i.UnitPrice * i.Quantity);
        order.TotalAmount = order.SubTotal - order.DiscountAmount + order.TaxAmount;
        if (order.TotalAmount < 0)
            return (null, "Discount exceeds order total.");

        db.Orders.Add(order);
        await db.SaveChangesAsync(); // gets the identity Id

        // 3. Human-readable number derived from the Id — no race condition possible.
        order.OrderNumber = $"ORD-{order.CreatedAt:yyyyMMdd}-{order.Id:D4}";
        await db.SaveChangesAsync();

        await tx.CommitAsync();

        var created = await GetByIdAsync(order.Id, userId, isAdmin: true);
        return (created, null);
    }

    /// <summary>
    /// Loads an order with its lines and projects it to OrderDto.
    /// Cashiers may only see their own orders (enforced by the caller).
    /// </summary>
    public async Task<OrderDto?> GetByIdAsync(int id, string userId, bool isAdmin)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.User)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null || (!isAdmin && order.UserId != userId))
            return null;

        return new OrderDto(
            order.Id, order.OrderNumber, order.CustomerId, order.Customer?.FullName,
            order.UserId, order.User.FullName,
            order.SubTotal, order.DiscountAmount, order.TaxAmount, order.TotalAmount,
            order.Status.ToString(), order.PaymentMethod.ToString(), order.Notes,
            order.CreatedAt, order.CompletedAt, order.CancelledAt, order.CancelReason,
            order.Items
                .OrderBy(i => i.Id)
                .Select(i => new OrderItemDto(i.Id, i.ProductId, i.ProductName, i.Sku,
                    i.UnitPrice, i.Quantity, i.LineDiscount, i.LineTotal))
                .ToList());
    }

    /// <summary>
    /// Cancels a completed order and restores stock in ONE transaction.
    /// </summary>
    public async Task<(OrderDto? Order, string? Error)> CancelAsync(int id, string reason)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null)
            return (null, "Order not found.");

        if (order.Status != OrderStatus.Completed)
            return (null, $"Only completed orders can be cancelled (current status: {order.Status}).");

        await using var tx = await db.Database.BeginTransactionAsync();

        // Restore stock for every line that still points at a product.
        var productIds = order.Items.Where(i => i.ProductId.HasValue)
            .Select(i => i.ProductId!.Value).Distinct().ToList();
        var products = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        foreach (var item in order.Items)
        {
            if (item.ProductId.HasValue && products.TryGetValue(item.ProductId.Value, out var product))
                product.StockQuantity += item.Quantity;
        }

        order.Status = OrderStatus.Cancelled;
        order.CancelledAt = DateTime.UtcNow;
        order.CancelReason = reason;

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        var updated = await GetByIdAsync(id, order.UserId, isAdmin: true);
        return (updated, null);
    }
}
