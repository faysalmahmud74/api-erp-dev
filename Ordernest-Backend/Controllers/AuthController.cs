using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Ordernest.Backend.DTOs.Auth;
using Ordernest.Backend.Extensions;
using Ordernest.Backend.Models;
using Ordernest.Backend.Services;

namespace Ordernest.Backend.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ITokenService tokens) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null)
            return Unauthorized(new { message = "Invalid email or password." });

        // lockoutOnFailure: 5 failed attempts lock the account for 10 minutes.
        var result = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return Unauthorized(new { message = "Invalid email or password." });

        if (!user.IsActive)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Account is deactivated." });

        return Ok(await tokens.CreateTokenAsync(user));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeResponse>> Me()
    {
        var user = await users.FindByIdAsync(User.GetUserId());
        if (user is null)
            return NotFound();

        return Ok(new MeResponse(user.Id, user.Email ?? string.Empty, user.FullName,
            (await users.GetRolesAsync(user)).ToList()));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var user = await users.FindByIdAsync(User.GetUserId());
        if (user is null)
            return NotFound();

        var result = await users.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { message = string.Join("; ", result.Errors.Select(e => e.Description)) });

        return NoContent();
    }

    /// <summary>
    /// Admin provisions staff accounts — a POS has no self-service signup.
    /// </summary>
    [HttpPost("register")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req)
    {
        if (req.Role is not ("Admin" or "Cashier"))
            return BadRequest(new { message = "Role must be 'Admin' or 'Cashier'." });

        var user = new ApplicationUser
        {
            UserName = req.Email,
            Email = req.Email,
            FullName = req.FullName,
            EmailConfirmed = true
        };

        var result = await users.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return BadRequest(new { message = string.Join("; ", result.Errors.Select(e => e.Description)) });

        await users.AddToRoleAsync(user, req.Role);
        return CreatedAtAction(nameof(Me), null, await tokens.CreateTokenAsync(user));
    }
}
