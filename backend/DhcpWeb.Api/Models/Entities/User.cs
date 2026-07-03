namespace DhcpWeb.Api.Models.Entities;

/// <summary>平台登录用户。Role 取 "Admin"(全权)或 "Viewer"(只读)。</summary>
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.Viewer;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Viewer = "Viewer";

    public static bool IsValid(string? role) => role is Admin or Viewer;
}
