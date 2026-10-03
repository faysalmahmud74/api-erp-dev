using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ordernest.Backend.Data;
using Ordernest.Backend.DTOs.Common;
using Ordernest.Backend.DTOs.Products;
using Ordernest.Backend.Models;

namespace Ordernest.Backend.Controllers;

[ApiController]
[Route("api/products")]
[Authorize]
public class ProductsController(AppDbContext db) : ControllerBase
{
    private static ProductDto ToDto(Product p) =>
        new(p.Id, p.Name, p.Sku, p.Barcode, p.Price, p.CostPrice, p.StockQuantity,
            p.LowStockThreshold, p.CategoryId, p.Category.Name, p.IsActive,
            p.CreatedAt, p.UpdatedAt);

    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductDto>>> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] int? categoryId = null,
        [FromQuery] bool lowStock = false,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Products.AsNoTracking().AsQueryable();

        if (!includeInactive)
            query = query.Where(p => p.IsActive);

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        if (lowStock)
            query = query.Where(p => p.StockQuantity <= p.LowStockThreshold);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || p.Sku.Contains(search));

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductDto(p.Id, p.Name, p.Sku, p.Barcode, p.Price, p.CostPrice,
                p.StockQuantity, p.LowStockThreshold, p.CategoryId, p.Category.Name, p.IsActive,
                p.CreatedAt, p.UpdatedAt))
            .ToListAsync();

        return Ok(new PagedResult<ProductDto>(items, page, pageSize, totalCount));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDto>> GetById(int id)
    {
        var product = await db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id);

        return product is null ? NotFound() : Ok(ToDto(product));
    }

    // POS scanner lookup: fast, indexed, returns just the product.
    [HttpGet("barcode/{barcode}")]
    public async Task<ActionResult<ProductDto>> GetByBarcode(string barcode)
    {
        var product = await db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Barcode == barcode && p.IsActive);

        return product is null ? NotFound() : Ok(ToDto(product));
    }

    [HttpGet("low-stock")]
    public async Task<ActionResult<IReadOnlyList<ProductDto>>> GetLowStock()
    {
        var items = await db.Products
            .AsNoTracking()
            .Where(p => p.IsActive && p.StockQuantity <= p.LowStockThreshold)
            .OrderBy(p => p.Name)
            .Select(p => new ProductDto(p.Id, p.Name, p.Sku, p.Barcode, p.Price, p.CostPrice,
                p.StockQuantity, p.LowStockThreshold, p.CategoryId, p.Category.Name, p.IsActive,
                p.CreatedAt, p.UpdatedAt))
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ProductDto>> Create(ProductCreateDto dto)
    {
        if (await db.Products.AnyAsync(p => p.Sku == dto.Sku))
            return Conflict(new { message = "A product with this SKU already exists." });

        if (dto.Barcode is not null && await db.Products.AnyAsync(p => p.Barcode == dto.Barcode))
            return Conflict(new { message = "A product with this barcode already exists." });

        if (!await db.Categories.AnyAsync(c => c.Id == dto.CategoryId))
            return BadRequest(new { message = "Category not found." });

        var product = new Product
        {
            Name = dto.Name,
            Sku = dto.Sku,
            Barcode = dto.Barcode,
            Price = dto.Price,
            CostPrice = dto.CostPrice,
            StockQuantity = dto.StockQuantity,
            LowStockThreshold = dto.LowStockThreshold,
            CategoryId = dto.CategoryId
        };

        db.Products.Add(product);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = product.Id },
            new ProductDto(product.Id, product.Name, product.Sku, product.Barcode, product.Price,
                product.CostPrice, product.StockQuantity, product.LowStockThreshold,
                product.CategoryId, (await db.Categories.FindAsync(dto.CategoryId))!.Name,
                product.IsActive, product.CreatedAt, product.UpdatedAt));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, ProductUpdateDto dto)
    {
        var product = await db.Products.FindAsync(id);
        if (product is null)
            return NotFound();

        if (await db.Products.AnyAsync(p => p.Sku == dto.Sku && p.Id != id))
            return Conflict(new { message = "A product with this SKU already exists." });

        if (dto.Barcode is not null && await db.Products.AnyAsync(p => p.Barcode == dto.Barcode && p.Id != id))
            return Conflict(new { message = "A product with this barcode already exists." });

        if (!await db.Categories.AnyAsync(c => c.Id == dto.CategoryId))
            return BadRequest(new { message = "Category not found." });

        product.Name = dto.Name;
        product.Sku = dto.Sku;
        product.Barcode = dto.Barcode;
        product.Price = dto.Price;
        product.CostPrice = dto.CostPrice;
        product.LowStockThreshold = dto.LowStockThreshold;
        product.CategoryId = dto.CategoryId;
        product.IsActive = dto.IsActive;
        product.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return NoContent();
    }

    // Soft delete: a product with sales history must never be hard-deleted.
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await db.Products.FindAsync(id);
        if (product is null)
            return NotFound();

        product.IsActive = false;
        product.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return NoContent();
    }

    // The ONLY way stock changes outside of sales: +10 restock, -3 breakage.
    // Negative adjustments are allowed down to zero — never below.
    [HttpPost("{id:int}/stock")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ProductDto>> AdjustStock(int id, StockAdjustmentDto dto)
    {
        var product = await db.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product is null)
            return NotFound();

        if (product.StockQuantity + dto.Adjustment < 0)
            return BadRequest(new { message = "Stock cannot go below zero." });

        product.StockQuantity += dto.Adjustment;
        product.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(ToDto(product));
    }
}
