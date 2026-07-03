using DhcpWeb.Api.Data;
using DhcpWeb.Api.Models.Dtos;
using DhcpWeb.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DhcpWeb.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtIssuer _jwt;
    private readonly AuditService _audit;

    public AuthController(AppDbContext db, JwtIssuer jwt, AuditService audit)
    {
        _db = db;
        _jwt = jwt;
        _audit = audit;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest req)
    {
        var username = (req.Username ?? "").Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null || !user.Enabled || !PasswordHasher.Verify(req.Password ?? "", user.PasswordHash))
        {
            await _audit.LogAsync(username, "Login", "failed", false);
            return Unauthorized(new { message = "用户名或密码错误,或账号已停用" });
        }

        var (token, expires) = _jwt.Issue(user);
        await _audit.LogAsync(user.Username, "Login", "ok", true);
        return Ok(new LoginResponse(token, user.Username, user.Role, expires));
    }

    [Authorize]
    [HttpGet("me")]
    public ActionResult<MeDto> Me()
    {
        var name = User.Identity?.Name ?? "";
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        return Ok(new MeDto(name, role));
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<ActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var name = User.Identity?.Name ?? "";
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == name);
        if (user == null) return Unauthorized();
        if (!PasswordHasher.Verify(req.OldPassword ?? "", user.PasswordHash))
            return BadRequest(new { message = "原密码不正确" });
        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 6)
            return BadRequest(new { message = "新密码长度至少 6 位" });

        user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(user.Username, "ChangePassword", user.Username, true);
        return Ok(new { message = "密码已修改" });
    }
}
