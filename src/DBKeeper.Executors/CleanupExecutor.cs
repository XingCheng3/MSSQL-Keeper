using System.Text.Json;
using DBKeeper.Core.Models;
using DBKeeper.Data.Repositories;
using Serilog;

namespace DBKeeper.Executors;

/// <summary>
/// 备份清理执行器：按保留天数删除过期文件，并更新 backup_files 表状态
/// </summary>
public class CleanupExecutor : ITaskExecutor
{
    private readonly IBackupFileRepository? _backupRepo;

    public string TaskType => "BACKUP_CLEANUP";

    public CleanupExecutor() { }

    public CleanupExecutor(IBackupFileRepository backupRepo)
    {
        _backupRepo = backupRepo;
    }

    public async Task<ExecutionResult> ExecuteAsync(TaskItem task, Connection connection)
    {
        var config = JsonSerializer.Deserialize<CleanupConfig>(task.TaskConfig)!;
        var dir = config.TargetDir;

        if (!Directory.Exists(dir))
            return ExecutionResult.Fail($"目录不存在: {dir}");

        var cutoff = DateTime.Now.AddDays(-config.RetentionDays);
        var files = Directory.GetFiles(dir, "*.bak")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTime)
            .ToList();

        // 保留最少份数
        var toDelete = files.Skip(config.MinKeepCount)
            .Where(f => f.CreationTime < cutoff)
            .ToList();

        var deletedPaths = new List<string>();
        foreach (var f in toDelete)
        {
            deletedPaths.Add(f.FullName);
            f.Delete();
            Log.Information("清理备份文件: {File}", f.FullName);
        }

        // 更新 backup_files 表中对应记录状态为 DELETED
        if (_backupRepo != null && deletedPaths.Count > 0)
        {
            var activeFiles = await _backupRepo.GetAllActiveAsync();
            var now = DateTime.Now.ToString("O");
            foreach (var dbFile in activeFiles)
            {
                if (deletedPaths.Any(p => string.Equals(p, dbFile.FilePath, StringComparison.OrdinalIgnoreCase)))
                {
                    await _backupRepo.UpdateStatusAsync(dbFile.Id, "DELETED", now);
                    Log.Information("更新备份记录状态为 DELETED: {FileName}", dbFile.FileName);
                }
            }
        }

        return new ExecutionResult
        {
            Success = true,
            Summary = $"删除 {toDelete.Count} 个过期文件"
        };
    }
}
