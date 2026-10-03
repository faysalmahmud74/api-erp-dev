using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ordernest.Backend.DTOs.Users;
using Ordernest.Backend.Extensions;
using Ordernest.Backend.Models;

namespace Ordernest.Backend.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public class UsersController(UserManager<ApplicationUser> users) : ControllerBase
{
    private static readonly string[] ValidRoles = ["Admin", "Cashier"];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll()
    {
        var all = await users.Users
            .OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.Email, u.FullName, u.IsActive, u.CreatedAt })
            .ToListAsync();

        var result = new List<UserDto>(all.Count);
        foreach (var u in all)
        {
            var user = await users.FindByIdAsync(u.Id);
            result.Add(new UserDto(u.Id, u.Email ?? string.Empty, u.FullName,
                (await users.GetRolesAsync(user!)).ToList(), u.IsActive, u.CreatedAt));
        }

        return Ok(result);
    }

    [HttpPut("{id}/roles")]
    public async Task<IActionResult> UpdateRoles(string id, UpdateRolesRequest req)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        if (req.Roles.Any(r => !ValidRoles.Contains(r)))
            return BadRequest(new { message = "Roles must be 'Admin' or 'Cashier'." });

        // Safety: an admin cannot strip their own Admin role (no locked-out store).
        if (user.Id == User.GetUserId() && !req.Roles.Contains("Admin"))
            return BadRequest(new { message = "You cannot remove your own Admin role." });

        var current = await users.GetRolesAsync(user);
        var remove = await users.RemoveFromRolesAsync(user, current);
        if (!remove.Succeeded)
            return BadRequest(new { message = string.Join("; ", remove.Errors.Select(e => e.Description)) });

        var add = await users.AddToRolesAsync(user, req.Roles.Distinct());
        if (!add.Succeeded)
            return BadRequest(new { message = string.Join("; ", add.Errors.Select(e => e.Description)) });

        return NoContent();
    }

    [HttpPost("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        if (user.Id == User.GetUserId())
            return BadRequest(new { message = "You cannot deactivate your own account." });

        user.IsActive = false;
        await users.UpdateAsync(user);
        return NoContent();
    }

    [HttpPost("{id}/activate")]
    public async Task<IActionResult> Activate(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        user.IsActive = true;
        await users.UpdateAsync(user);
        return NoContent();
    }
}
