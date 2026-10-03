using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ordernest.Backend.Data;
using Ordernest.Backend.DTOs.Categories;
using Ordernest.Backend.Models;

namespace Ordernest.Backend.Controllers;

[ApiController]
[Route("api/categories")]
[Authorize]
public class CategoriesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetAll()
    {
        var categories = await db.Categories
            .AsNoTracking()
            .Select(c => new CategoryDto(c.Id, c.Name, c.Description, c.IsActive, c.Products.Count))
            .OrderBy(c => c.Name)
            .ToListAsync();

        return Ok(categories);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CategoryDto>> GetById(int id)
    {
        var category = await db.Categories
            .AsNoTracking()
            .Select(c => new CategoryDto(c.Id, c.Name, c.Description, c.IsActive, c.Products.Count))
            .FirstOrDefaultAsync(c => c.Id == id);

        return category is null ? NotFound() : Ok(category);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CategoryDto>> Create(CategoryCreateDto dto)
    {
        if (await db.Categories.AnyAsync(c => c.Name == dto.Name))
            return Conflict(new { message = "A category with this name already exists." });

        var category = new Category { Name = dto.Name, Description = dto.Description };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = category.Id },
            new CategoryDto(category.Id, category.Name, category.Description, category.IsActive, 0));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, CategoryUpdateDto dto)
    {
        var category = await db.Categories.FindAsync(id);
        if (category is null)
            return NotFound();

        if (await db.Categories.AnyAsync(c => c.Name == dto.Name && c.Id != id))
            return Conflict(new { message = "A category with this name already exists." });

        category.Name = dto.Name;
        category.Description = dto.Description;
        category.IsActive = dto.IsActive;
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await db.Categories.FindAsync(id);
        if (category is null)
            return NotFound();

        if (await db.Products.AnyAsync(p => p.CategoryId == id))
            return Conflict(new { message = "Cannot delete a category that still has products." });

        db.Categories.Remove(category);
        await db.SaveChangesAsync();

        return NoContent();
    }
}
