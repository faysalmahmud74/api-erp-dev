using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ordernest.Backend.Data;
using Ordernest.Backend.DTOs.Common;
using Ordernest.Backend.DTOs.Orders;
using Ordernest.Backend.Extensions;
using Ordernest.Backend.Models;
using Ordernest.Backend.Services;

namespace Ordernest.Backend.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController(AppDbContext db, IOrderService orders) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Admin,Cashier")]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderRequest req)
    {
        try
        {
            var (order, error) = await orders.CreateAsync(req, User.GetUserId());
            if (error is not null)
                return BadRequest(new { message = error });

            return CreatedAtAction(nameof(GetById), new { id = order!.Id }, order);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two cashiers sold the last unit concurrently — the RowVersion
            // check caught it. The stock has changed; ask the cashier to retry.
            return Conflict(new { message = "Stock changed while processing, please retry." });
        }
    }

    /// <summary>
    /// Date filters are half-open: from is inclusive, to is EXCLUSIVE
    /// (use 2026-10-04 to include all of Oct 3).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderSummaryDto>>> GetAll(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] OrderStatus? status = null,
        [FromQuery] int? customerId = null,
        [FromQuery] string? userId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Orders.AsNoTracking().AsQueryable();

        // Cashiers can only see their own sales; admins see everything.
        if (!User.IsInRole("Admin"))
        {
            var ownId = User.GetUserId();
            query = query.Where(o => o.UserId == ownId);
            if (userId is not null && userId != ownId)
                return Forbid();
        }
        else if (userId is not null)
        {
            query = query.Where(o => o.UserId == userId);
        }

        if (from.HasValue)
            query = query.Where(o => o.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(o => o.CreatedAt < to.Value);
        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);
        if (customerId.HasValue)
            query = query.Where(o => o.CustomerId == customerId.Value);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderSummaryDto(o.Id, o.OrderNumber, o.Customer!.FullName, o.User.FullName,
                o.TotalAmount, o.Status.ToString(), o.PaymentMethod.ToString(),
                o.Items.Count, o.CreatedAt))
            .ToListAsync();

        return Ok(new PagedResult<OrderSummaryDto>(items, page, pageSize, totalCount));
    }

    [HttpGet("my-sales")]
    [Authorize(Roles = "Admin,Cashier")]
    public async Task<ActionResult<IReadOnlyList<OrderSummaryDto>>> GetMySales(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var ownId = User.GetUserId();
        var query = db.Orders.AsNoTracking().Where(o => o.UserId == ownId);

        if (from.HasValue)
            query = query.Where(o => o.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(o => o.CreatedAt < to.Value);

        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderSummaryDto(o.Id, o.OrderNumber, o.Customer!.FullName, o.User.FullName,
                o.TotalAmount, o.Status.ToString(), o.PaymentMethod.ToString(),
                o.Items.Count, o.CreatedAt))
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDto>> GetById(int id)
    {
        var isAdmin = User.IsInRole("Admin");
        var order = await orders.GetByIdAsync(id, User.GetUserId(), isAdmin);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<OrderDto>> Cancel(int id, CancelOrderRequest req)
    {
        var (order, error) = await orders.CancelAsync(id, req.Reason);
        if (error is not null)
            return BadRequest(new { message = error });

        return Ok(order);
    }
}
