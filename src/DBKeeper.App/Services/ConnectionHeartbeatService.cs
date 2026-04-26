using DBKeeper.Data;
using DBKeeper.Data.Repositories;
using Serilog;
using System.Collections.Concurrent;

namespace DBKeeper.App.Services;

/// <summary>
/// 后台心跳检测所有连接状态
/// </summary>
public class ConnectionHeartbeatService
{
    private readonly IConnectionRepository _repo;
    private readonly ConcurrentDictionary<int, bool> _statusByConnectionId = new();
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private Timer? _timer;

    /// <summary>连接状态变化事件：key=连接ID, value=是否在线</summary>
    public event Action<int, bool>? StatusChanged;

    public ConnectionHeartbeatService(IConnectionRepository repo)
    {
        _repo = repo;
    }

    public void Start(int intervalSeconds = 60)
    {
        _timer = new Timer(_ => _ = CheckAllAsync(), null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(intervalSeconds));
        Log.Information("心跳检测服务启动，间隔 {Interval}s", intervalSeconds);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public bool? GetStatus(int connectionId)
    {
        return _statusByConnectionId.TryGetValue(connectionId, out var isOnline)
            ? isOnline
            : null;
    }

    public async Task CheckAllAsync()
    {
        if (!await _checkLock.WaitAsync(0))
        {
            Log.Debug("上一次心跳检测仍在执行，跳过本轮");
            return;
        }

        try
        {
            var connections = await _repo.GetAllAsync();
            foreach (var conn in connections)
            {
                var result = await SqlServerClient.TestConnectionAsync(conn);
                _statusByConnectionId[conn.Id] = result.Success;
                StatusChanged?.Invoke(conn.Id, result.Success);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "心跳检测异常");
        }
        finally
        {
            _checkLock.Release();
        }
    }
}
