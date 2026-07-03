using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DhcpWeb.Api.Models.Entities;
using Microsoft.IdentityModel.Tokens;

namespace DhcpWeb.Api.Services;

/// <summary>按用户签发 JWT。密钥/签发者/有效期从 Jwt:* 配置读取。</summary>
public class JwtIssuer
{
    private readonly string _key;
    private readonly string _issuer;
    private readonly int _expireHours;

    public JwtIssuer(IConfiguration config)
    {
        _key = config["Jwt:Key"] ?? "dev-only-insecure-key-change-me-please-0123456789";
        _issuer = config["Jwt:Issuer"] ?? "dhcpweb";
        _expireHours = config.GetValue("Jwt:ExpireHours", 12);
    }

    public (string token, DateTime expires) Issue(User user)
    {
        var expires = DateTime.UtcNow.AddHours(_expireHours);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Username),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: _issuer, audience: _issuer, claims: claims,
            expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}
