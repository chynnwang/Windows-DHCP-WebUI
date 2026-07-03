using System.Text.Json;

namespace DhcpAgent;

public class AgentConfig
{
    public string ServerUrl { get; set; } = "";
    public string EnrollmentSecret { get; set; } = "";
    public string AgentId { get; set; } = "";

    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DhcpWebAgent");

    public static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    public static AgentConfig Load()
    {
        if (!File.Exists(ConfigPath)) return new AgentConfig();
        return JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(ConfigPath)) ?? new AgentConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
