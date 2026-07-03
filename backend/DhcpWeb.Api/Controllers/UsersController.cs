using DhcpWeb.Api.Data;
using DhcpWeb.Api.Models.Dtos;
using DhcpWeb.Api.Models.Entities;
using DhcpWeb.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DhcpWeb.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = Roles.Admin)]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public UsersController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    private string CurrentUser => User.Identity?.Name ?? "embedded";

    private static UserDto ToDto(User u) => new(u.Id, u.Username, u.Role, u.Enabled, u.CreatedAt);

    // 库中当前处于「启用状态的 Admin」数量;用于保护最后一个管理员。
    private Task<int> EnabledAdminCountAsync()
        => _db.Users.CountAsync(u => u.Role == Roles.Admin && u.Enabled);

    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> List()
        => Ok((await _db.Users.OrderBy(u => u.Username).ToListAsync()).Select(ToDto));

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest req)
    {
        var username = (req.Username ?? "").Trim();
        if (username.Length is < 1 or > 64) return BadRequest(new { message = "用户名长度需为 1-64" });
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
            return BadRequest(new { message = "密码长度至少 6 位" });
        if (!Roles.IsValid(req.Role)) return BadRequest(new { message = "角色无效" });
        if (await _db.Users.AnyAsync(u => u.Username == username))
            return BadRequest(new { message = "用户名已存在" });

        var user = new User
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(req.Password),
            Role = req.Role,
            Enabled = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(CurrentUser, "CreateUser", $"{username}({req.Role})", true);
        return Ok(ToDto(user));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> Update(int id, UpdateUserRequest req)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();

        // 保护最后一个启用的 Admin:不能把它降级或停用
        var losingAdmin = user is { Role: Roles.Admin, Enabled: true } &&
                          ((req.Role != null && req.Role != Roles.Admin) || req.Enabled == false);
        if (losingAdmin && await EnabledAdminCountAsync() <= 1)
            return BadRequest(new { message = "不能停用或降级唯一的启用管理员" });

        if (req.Role != null)
        {
            if (!Roles.IsValid(req.Role)) return BadRequest(new { message = "角色无效" });
            user.Role = req.Role;
        }
        if (req.Enabled != null) user.Enabled = req.Enabled.Value;
        if (!string.IsNullOrWhiteSpace(req.Password))
        {
            if (req.Password.Length < 6) return BadRequest(new { message = "密码长度至少 6 位" });
            user.PasswordHash = PasswordHasher.Hash(req.Password);
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync(CurrentUser, "UpdateUser", user.Username, true);
        return Ok(ToDto(user));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();
        if (user.Username == CurrentUser) return BadRequest(new { message = "不能删除当前登录的账号" });
        if (user is { Role: Roles.Admin, Enabled: true } && await EnabledAdminCountAsync() <= 1)
            return BadRequest(new { message = "不能删除唯一的启用管理员" });

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(CurrentUser, "DeleteUser", user.Username, true);
        return Ok(new { message = "用户已删除" });
    }
}
