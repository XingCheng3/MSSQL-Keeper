using System.Text.Json;
using DBKeeper.Core.Models;
using DBKeeper.Data;

namespace DBKeeper.Executors;

/// <summary>
/// 自定义 SQL 执行器
/// </summary>
public class SqlExecutor : ITaskExecutor
{
    public string TaskType => "CUSTOM_SQL";

    public async Task<ExecutionResult> ExecuteAsync(TaskItem task, Connection connection, CancellationToken cancellationToken = default)
    {
        var config = JsonSerializer.Deserialize<SqlConfig>(task.TaskConfig)!;
        var result = await SqlServerClient.ExecuteSqlAsync(
            connection, config.DatabaseName, config.SqlContent, config.TimeoutSec, cancellationToken);
        return ExecutionResult.Ok(result ?? "执行完成");
    }
}
