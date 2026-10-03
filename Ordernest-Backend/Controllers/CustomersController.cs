using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ordernest.Backend.Data;
using Ordernest.Backend.DTOs.Common;
using Ordernest.Backend.DTOs.Customers;
using Ordernest.Backend.DTOs.Orders;
using Ordernest.Backend.Models;

namespace Ordernest.Backend.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomersController(AppDbContext db) : ControllerBase
{
    private static CustomerDto ToDto(Customer c) =>
        new(c.Id, c.FullName, c.Phone, c.Email, c.Address, c.Notes,
            c.LoyaltyPoints, c.IsActive, c.CreatedAt);

    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerDto>>> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Customers.AsNoTracking().AsQueryable();

        // POS staff search by name OR phone.
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.FullName.Contains(search) || (c.Phone != null && c.Phone.Contains(search)));

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(c => c.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CustomerDto(c.Id, c.FullName, c.Phone, c.Email, c.Address, c.Notes,
                c.LoyaltyPoints, c.IsActive, c.CreatedAt))
            .ToListAsync();

        return Ok(new PagedResult<CustomerDto>(items, page, pageSize, totalCount));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomerDto>> GetById(int id)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        return customer is null ? NotFound() : Ok(ToDto(customer));
    }

    [HttpGet("{id:int}/orders")]
    public async Task<ActionResult<IReadOnlyList<OrderSummaryDto>>> GetOrders(int id)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == id))
            return NotFound();

        var orders = await db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == id)
            .OrderByDescending(o => o.CreatedAt)
            .Take(20)
            .Select(o => new OrderSummaryDto(o.Id, o.OrderNumber, o.Customer!.FullName, o.User.FullName,
                o.TotalAmount, o.Status.ToString(), o.PaymentMethod.ToString(),
                o.Items.Count, o.CreatedAt))
            .ToListAsync();

        return Ok(orders);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Cashier")]
    public async Task<ActionResult<CustomerDto>> Create(CustomerCreateDto dto)
    {
        var customer = new Customer
        {
            FullName = dto.FullName,
            Phone = dto.Phone,
            Email = dto.Email,
            Address = dto.Address,
            Notes = dto.Notes
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = customer.Id }, ToDto(customer));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Cashier")]
    public async Task<IActionResult> Update(int id, CustomerUpdateDto dto)
    {
        var customer = await db.Customers.FindAsync(id);
        if (customer is null)
            return NotFound();

        customer.FullName = dto.FullName;
        customer.Phone = dto.Phone;
        customer.Email = dto.Email;
        customer.Address = dto.Address;
        customer.Notes = dto.Notes;
        customer.IsActive = dto.IsActive;

        await db.SaveChangesAsync();
        return NoContent();
    }

    // Soft delete: order history keeps its CustomerId.
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var customer = await db.Customers.FindAsync(id);
        if (customer is null)
            return NotFound();

        customer.IsActive = false;
        await db.SaveChangesAsync();

        return NoContent();
    }
}
