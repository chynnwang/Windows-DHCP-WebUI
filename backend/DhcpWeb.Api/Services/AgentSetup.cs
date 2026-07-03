namespace DhcpWeb.Api.Services;

/// <summary>Agent 接入相关的共享常量。</summary>
public static class AgentSetup
{
    /// <summary>Settings 表中「Agent 连接密钥」覆盖值的键;为空时回退 appsettings 的 Agent:EnrollmentSecret。</summary>
    public const string EnrollmentSecretKey = "AgentEnrollmentSecret";

    /// <summary>Settings 表中「平台对外地址」覆盖值的键。</summary>
    public const string PlatformUrlKey = "PlatformBaseUrl";
}
