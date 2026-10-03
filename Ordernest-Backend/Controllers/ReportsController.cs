using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ordernest.Backend.Data;
using Ordernest.Backend.DTOs.Reports;
using Ordernest.Backend.Models;

namespace Ordernest.Backend.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = "Admin")]
public class ReportsController(AppDbContext db) : ControllerBase
{
    // Half-open range: from inclusive, to EXCLUSIVE — never double-counts
    // an order that lands exactly on the boundary.
    [HttpGet("sales/daily")]
    public async Task<ActionResult<IReadOnlyList<DailySalesDto>>> GetDailySales(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toExclusive = to ?? DateTime.UtcNow.AddDays(1);

        // EF cannot translate a GroupBy that projects straight into a record
        // constructor, so aggregate into an anonymous type first, then map in memory.
        var daily = await db.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Completed && o.CreatedAt >= fromDate && o.CreatedAt < toExclusive)
            .GroupBy(o => o.CreatedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                OrderCount = g.Count(),
                Gross = g.Sum(o => o.SubTotal),
                Discounts = g.Sum(o => o.DiscountAmount),
                Net = g.Sum(o => o.TotalAmount),
                Avg = g.Average(o => o.TotalAmount)
            })
            .OrderBy(d => d.Date)
            .ToListAsync();

        return Ok(daily.Select(d => new DailySalesDto(d.Date, d.OrderCount, d.Gross,
            d.Discounts, d.Net, d.Avg)).ToList());
    }

    [HttpGet("sales/monthly")]
    public async Task<ActionResult<IReadOnlyList<MonthlySalesDto>>> GetMonthlySales(
        [FromQuery] int? year = null)
    {
        var targetYear = year ?? DateTime.UtcNow.Year;
        var fromDate = new DateTime(targetYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var toExclusive = fromDate.AddYears(1);

        // Same as daily: aggregate into an anonymous type, then map in memory.
        var monthly = await db.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Completed && o.CreatedAt >= fromDate && o.CreatedAt < toExclusive)
            .GroupBy(o => new { o.CreatedAt.Year, o.CreatedAt.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                OrderCount = g.Count(),
                Gross = g.Sum(o => o.SubTotal),
                Discounts = g.Sum(o => o.DiscountAmount),
                Net = g.Sum(o => o.TotalAmount)
            })
            .OrderBy(m => m.Year).ThenBy(m => m.Month)
            .ToListAsync();

        return Ok(monthly.Select(m => new MonthlySalesDto(m.Year, m.Month, m.OrderCount,
            m.Gross, m.Discounts, m.Net)).ToList());
    }

    // Aggregates the OrderItem SNAPSHOT fields, so history stays correct
    // even after products are renamed or repriced.
    [HttpGet("top-products")]
    public async Task<ActionResult<IReadOnlyList<TopProductDto>>> GetTopProducts(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int take = 10)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toExclusive = to ?? DateTime.UtcNow.AddDays(1);
        take = Math.Clamp(take, 1, 100);

        // Same as daily: aggregate into an anonymous type, then map in memory.
        var top = await db.OrderItems
            .AsNoTracking()
            .Where(i => i.Order!.Status == OrderStatus.Completed &&
                        i.Order.CreatedAt >= fromDate && i.Order.CreatedAt < toExclusive)
            .GroupBy(i => new { i.ProductId, i.ProductName, i.Sku })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.ProductName,
                g.Key.Sku,
                QuantitySold = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.LineTotal)
            })
            .OrderByDescending(t => t.QuantitySold)
            .Take(take)
            .ToListAsync();

        return Ok(top.Select(t => new TopProductDto(t.ProductId, t.ProductName, t.Sku,
            t.QuantitySold, t.Revenue)).ToList());
    }

    [HttpGet("summary")]
    public async Task<ActionResult<SalesSummaryDto>> GetSummary([FromQuery] DateTime? date = null)
    {
        var target = (date ?? DateTime.UtcNow).Date;
        var nextDay = target.AddDays(1);

        var orders = db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Completed && o.CreatedAt >= target && o.CreatedAt < nextDay);

        var todaySales = await orders.SumAsync(o => o.TotalAmount);
        var todayOrders = await orders.CountAsync();
        var average = todayOrders == 0 ? 0 : await orders.AverageAsync(o => o.TotalAmount);
        var lowStockCount = await db.Products.CountAsync(p => p.IsActive && p.StockQuantity <= p.LowStockThreshold);
        var activeProducts = await db.Products.CountAsync(p => p.IsActive);
        var totalCustomers = await db.Customers.CountAsync(c => c.IsActive);

        return Ok(new SalesSummaryDto(target, todaySales, todayOrders, average,
            lowStockCount, activeProducts, totalCustomers));
    }

    [HttpGet("sales/by-cashier")]
    public async Task<ActionResult<IReadOnlyList<CashierSalesDto>>> GetSalesByCashier(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toExclusive = to ?? DateTime.UtcNow.AddDays(1);

        // Same pattern: anonymous type for the translated aggregate, DTO in memory.
        var byCashier = await db.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Completed && o.CreatedAt >= fromDate && o.CreatedAt < toExclusive)
            .GroupBy(o => new { o.UserId, o.User.FullName })
            .Select(g => new
            {
                g.Key.UserId,
                g.Key.FullName,
                OrderCount = g.Count(),
                TotalSales = g.Sum(o => o.TotalAmount)
            })
            .OrderByDescending(c => c.TotalSales)
            .ToListAsync();

        return Ok(byCashier.Select(c => new CashierSalesDto(c.UserId, c.FullName,
            c.OrderCount, c.TotalSales)).ToList());
    }
}
