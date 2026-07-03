namespace DhcpAgent;

public static class Installer
{
    public const string ServiceName = "DhcpWebAgent";

    public static async Task<int> InstallAsync(string[] args)
    {
        var server = GetArg(args, "--server");
        var secret = GetArg(args, "--secret");

        if (string.IsNullOrWhiteSpace(server))
        {
            Console.Write("请输入平台地址(如 http://192.168.1.5:5280): ");
            server = Console.ReadLine()?.Trim();
        }
        if (string.IsNullOrWhiteSpace(secret))
        {
            Console.Write("请输入注册密钥: ");
            secret = Console.ReadLine()?.Trim();
        }
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(secret))
        {
            Console.Error.WriteLine("平台地址和注册密钥不能为空。");
            return 1;
        }

        var cfg = AgentConfig.Load();
        cfg.ServerUrl = server!;
        cfg.EnrollmentSecret = secret!;
        if (string.IsNullOrWhiteSpace(cfg.AgentId))
            cfg.AgentId = Guid.NewGuid().ToString("N");
        cfg.Save();
        Console.WriteLine($"配置已保存: {AgentConfig.ConfigPath}");
        Console.WriteLine($"AgentId: {cfg.AgentId}");

        var exe = Environment.ProcessPath!;
        var binPath = $"\"{exe}\" run";
        // 先删除同名旧服务(忽略失败),再创建
        await Ps($"sc.exe stop {ServiceName} 2>$null; sc.exe delete {ServiceName} 2>$null; Start-Sleep -Milliseconds 500");
        var create = await Ps(
            $"New-Service -Name '{ServiceName}' -BinaryPathName '{binPath.Replace("'", "''")}' " +
            $"-DisplayName 'DHCP Web Agent' -Description 'DHCP Web 管理平台 Agent' -StartupType Automatic");
        if (!create.Success)
        {
            Console.Error.WriteLine($"创建服务失败(需以管理员身份运行): {create.Error}");
            return 2;
        }
        var start = await Ps($"Start-Service {ServiceName}");
        if (!start.Success)
        {
            Console.Error.WriteLine($"服务已创建但启动失败: {start.Error}");
            return 3;
        }
        Console.WriteLine($"服务 {ServiceName} 已安装并启动。请到平台查看,该服务器应已自动出现。");
        return 0;
    }

    public static async Task<int> UninstallAsync()
    {
        await Ps($"Stop-Service {ServiceName} -ErrorAction SilentlyContinue");
        var del = await Ps($"sc.exe delete {ServiceName}");
        Console.WriteLine(del.Success ? $"服务 {ServiceName} 已卸载。" : $"卸载失败: {del.Error}");
        return del.Success ? 0 : 1;
    }

    private static Task<AgentExecResult> Ps(string script) => PowerShellRunner.RunAsync(script);

    private static string? GetArg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
