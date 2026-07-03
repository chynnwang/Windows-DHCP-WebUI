using DhcpAgent;

// 子命令:install / uninstall,其余(含 run)以服务/前台方式运行
if (args.Length > 0)
{
    switch (args[0].ToLowerInvariant())
    {
        case "install":
            return await Installer.InstallAsync(args);
        case "uninstall":
            return await Installer.UninstallAsync();
    }
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = Installer.ServiceName);
builder.Services.AddHostedService<AgentWorker>();
var host = builder.Build();
host.Run();
return 0;
