using System.Collections.Concurrent;

namespace DhcpWeb.Api.Transport;

/// <summary>在线 Agent 的内存映射:AgentId ↔ SignalR ConnectionId。进程重启后由 Agent 重连重建。</summary>
public class AgentRegistry
{
    private readonly ConcurrentDictionary<string, string> _agentToConn = new();
    private readonly ConcurrentDictionary<string, string> _connToAgent = new();

    public void Add(string agentId, string connectionId)
    {
        _agentToConn[agentId] = connectionId;
        _connToAgent[connectionId] = agentId;
    }

    public void RemoveByConnection(string connectionId)
    {
        if (_connToAgent.TryRemove(connectionId, out var agentId))
        {
            // 仅当当前映射仍指向该连接时移除,避免误删重连后的新连接
            if (_agentToConn.TryGetValue(agentId, out var cur) && cur == connectionId)
                _agentToConn.TryRemove(agentId, out _);
        }
    }

    public string? GetConnectionId(string agentId)
        => _agentToConn.TryGetValue(agentId, out var conn) ? conn : null;

    public bool IsOnline(string agentId) => _agentToConn.ContainsKey(agentId);
}
