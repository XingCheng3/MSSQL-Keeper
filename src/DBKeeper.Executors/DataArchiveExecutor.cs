using System.Text.Json;
using DBKeeper.Core.Models;
using DBKeeper.Data;

namespace DBKeeper.Executors;

/// <summary>数据归档执行器：按时间字段将生产库单表数据分批迁移到历史库。</summary>
public class DataArchiveExecutor : ITaskExecutor
{
    public string TaskType => "DATA_ARCHIVE";

    public async Task<ExecutionResult> ExecuteAsync(TaskItem task, Connection? connection, CancellationToken cancellationToken = default)
    {
        if (connection == null)
            return ExecutionResult.Fail("数据归档任务缺少数据库连接");

        try
        {
            var config = JsonSerializer.Deserialize<DataArchiveConfig>(task.TaskConfig)!;
            var result = await SqlServerClient.ExecuteDataArchiveAsync(connection, config, cancellationToken);
            return ExecutionResult.Ok(result.ToSummary());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ExecutionResult.Fail($"数据归档失败：{ex.Message}");
        }
    }
}
