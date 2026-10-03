using Ordernest.Backend.DTOs.Orders;

namespace Ordernest.Backend.Services;

public interface IOrderService
{
    Task<(OrderDto? Order, string? Error)> CreateAsync(CreateOrderRequest req, string userId);

    Task<OrderDto?> GetByIdAsync(int id, string userId, bool isAdmin);

    Task<(OrderDto? Order, string? Error)> CancelAsync(int id, string reason);
}
