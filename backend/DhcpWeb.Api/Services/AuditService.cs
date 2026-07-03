using DhcpWeb.Api.Data;
using DhcpWeb.Api.Models.Entities;

namespace DhcpWeb.Api.Services;

public class AuditService
{
    private readonly AppDbContext _db;
    public AuditService(AppDbContext db) => _db = db;

    public async Task LogAsync(string username, string action, string target, bool success, string? detail = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Username = username,
            Action = action,
            Target = target,
            Success = success,
            Detail = detail
        });
        await _db.SaveChangesAsync();
    }
}
