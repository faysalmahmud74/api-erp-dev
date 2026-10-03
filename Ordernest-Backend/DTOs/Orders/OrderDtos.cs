using System.ComponentModel.DataAnnotations;
using Ordernest.Backend.Models;

namespace Ordernest.Backend.DTOs.Orders;

public record CreateOrderItemRequest(
    [Range(1, 10_000)] int ProductId,
    [Range(1, 10_000)] int Quantity,
    [Range(0, 1_000_000)] decimal LineDiscount = 0);

// Product ids + quantities ONLY — never prices or totals.
// The server prices the cart from the database.
public record CreateOrderRequest(
    int? CustomerId,
    PaymentMethod PaymentMethod,
    [MaxLength(500)] string? Notes,
    [Required, MinLength(1)] List<CreateOrderItemRequest> Items,
    [Range(0, 1_000_000)] decimal DiscountAmount = 0);

public record OrderItemDto(
    int Id, int? ProductId, string ProductName, string Sku,
    decimal UnitPrice, int Quantity, decimal LineDiscount, decimal LineTotal);

public record OrderDto(
    int Id, string OrderNumber, int? CustomerId, string? CustomerName,
    string UserId, string CashierName,
    decimal SubTotal, decimal DiscountAmount, decimal TaxAmount, decimal TotalAmount,
    string Status, string PaymentMethod, string? Notes,
    DateTime CreatedAt, DateTime? CompletedAt, DateTime? CancelledAt,
    string? CancelReason, IReadOnlyList<OrderItemDto> Items);

public record OrderSummaryDto(
    int Id, string OrderNumber, string? CustomerName, string CashierName,
    decimal TotalAmount, string Status, string PaymentMethod,
    int ItemCount, DateTime CreatedAt);

public record CancelOrderRequest([Required, MaxLength(500)] string Reason);
