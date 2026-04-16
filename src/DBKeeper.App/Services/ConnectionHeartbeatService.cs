using DBKeeper.Data;
using DBKeeper.Data.Repositories;
using Serilog;

namespace DBKeeper.App.Services;

/// <summary>
/// 后台心跳检测所有连接状态
/// </summary>
public class ConnectionHeartbeatService
{
    private readonly IConnectionRepository _repo;
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

    private async Task CheckAllAsync()
    {
        try
        {
            var connections = await _repo.GetAllAsync();
            foreach (var conn in connections)
            {
                var result = await SqlServerClient.TestConnectionAsync(conn);
                StatusChanged?.Invoke(conn.Id, result.Success);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "心跳检测异常");
        }
    }
}
