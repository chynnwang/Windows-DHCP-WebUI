using System.Diagnostics;
using System.Text;

namespace DhcpAgent;

/// <summary>Agent 端执行 PowerShell 的返回结构,须与平台端 AgentExecResult 字段一致。</summary>
public record AgentExecResult(bool Success, string? Stdout, string? Error);

public static class PowerShellRunner
{
    public static async Task<AgentExecResult> RunAsync(string script, CancellationToken ct = default)
    {
        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-EncodedCommand");
            psi.ArgumentList.Add(encoded);

            using var proc = new Process { StartInfo = psi };
            proc.Start();
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
                return new AgentExecResult(false, stdout, stderr.Trim());
            return new AgentExecResult(true, stdout, null);
        }
        catch (Exception ex)
        {
            return new AgentExecResult(false, null, ex.Message);
        }
    }
}
