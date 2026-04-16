using System.Text.Json;
using DBKeeper.Core.Models;
using DBKeeper.Data;

namespace DBKeeper.Executors;

/// <summary>
/// 存储过程执行器
/// </summary>
public class ProcedureExecutor : ITaskExecutor
{
    public string TaskType => "PROCEDURE";

    public async Task<ExecutionResult> ExecuteAsync(TaskItem task, Connection connection)
    {
        var config = JsonSerializer.Deserialize<ProcedureConfig>(task.TaskConfig)!;
        var parameters = config.Parameters?.ToDictionary(p => p.Name, p => p.Value);
        var result = await SqlServerClient.ExecuteProcedureAsync(
            connection, config.DatabaseName, config.ProcedureName, parameters, config.TimeoutSec);
        return ExecutionResult.Ok(result ?? "执行完成");
    }
}
