using DBKeeper.Core.Models;

namespace DBKeeper.Executors;

/// <summary>
/// 任务执行器统一接口
/// </summary>
public interface ITaskExecutor
{
    /// <summary>支持的任务类型</summary>
    string TaskType { get; }

    /// <summary>执行任务并返回结果</summary>
    Task<ExecutionResult> ExecuteAsync(TaskItem task, Connection connection, CancellationToken cancellationToken = default);
}

/// <summary>
/// 执行结果
/// </summary>
public class ExecutionResult
{
    public bool Success { get; set; }
    public string? Summary { get; set; }
    public string? ErrorDetail { get; set; }

    /// <summary>是否为警告（非致命性失败，如磁盘空间预检失败）</summary>
    public bool IsWarning { get; set; }

    /// <summary>附加数据（如实际文件路径等），用于在执行器与调度服务间传递信息</summary>
    public Dictionary<string, object>? Metadata { get; set; }

    public static ExecutionResult Ok(string summary) => new() { Success = true, Summary = summary };
    public static ExecutionResult Fail(string error) => new() { Success = false, ErrorDetail = error };
    public static ExecutionResult Warn(string summary, string? detail = null) => new()
    {
        Success = false,
        IsWarning = true,
        Summary = summary,
        ErrorDetail = detail ?? summary
    };
}
