using System.Text.Json;
using DBKeeper.Core.Helpers;
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

    public async Task<ExecutionResult> ExecuteAsync(TaskItem task, Connection connection, CancellationToken cancellationToken = default)
    {
        var config = JsonSerializer.Deserialize<CleanupConfig>(task.TaskConfig)!;
        var dir = config.TargetDir;

        if (!Directory.Exists(dir))
            return ExecutionResult.Fail($"目录不存在: {dir}");

        if (config.RetentionDays <= 0)
        {
            return ExecutionResult.Ok("保留天数小于等于 0，跳过自动清理");
        }

        var cutoff = DateTime.Now.AddDays(-config.RetentionDays);
        var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
            .Select(f => new FileInfo(f))
            .Where(f => BackupPathGuard.IsAllowedBackupFile(f.FullName, [dir]))
            .OrderByDescending(f => f.CreationTime)
            .ToList();

        // 保留最少份数
        var toDelete = files.Skip(config.MinKeepCount)
            .Where(f => f.CreationTime < cutoff)
            .ToList();

        var pinnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_backupRepo != null)
        {
            var pinnedSourceFiles = await _backupRepo.GetAllActiveAsync();
            foreach (var file in pinnedSourceFiles.Where(f => f.IsPinned))
                pinnedPaths.Add(Path.GetFullPath(file.FilePath));
        }

        var deletedPaths = new List<string>();
        var skippedPinned = 0;
        var failedDeletes = new List<string>();
        var now = DateTime.Now.ToString("O");
        var activeFiles = _backupRepo != null
            ? await _backupRepo.GetAllActiveAsync()
            : [];
        foreach (var f in toDelete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(f.FullName);
            if (!BackupPathGuard.IsAllowedBackupFile(fullPath, [dir]))
            {
                Log.Warning("跳过不受允许规则约束的文件: {File}", fullPath);
                continue;
            }

            if (pinnedPaths.Contains(fullPath))
            {
                skippedPinned++;
                Log.Information("跳过置顶备份文件: {File}", fullPath);
                continue;
            }

            try
            {
                f.Delete();
                deletedPaths.Add(fullPath);
                Log.Information("清理备份文件: {File}", fullPath);

                if (_backupRepo != null)
                {
                    var dbFile = activeFiles.FirstOrDefault(item =>
                        string.Equals(Path.GetFullPath(item.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));
                    if (dbFile != null)
                    {
                        await _backupRepo.UpdateStatusAsync(dbFile.Id, "DELETED", now);
                        Log.Information("更新备份记录状态为 DELETED: {FileName}", dbFile.FileName);
                    }
                }
            }
            catch (Exception ex)
            {
                failedDeletes.Add($"{f.Name}: {ex.Message}");
                Log.Warning(ex, "清理备份文件失败: {File}", fullPath);
            }
        }

        if (failedDeletes.Count > 0)
        {
            return new ExecutionResult
            {
                Success = false,
                IsWarning = true,
                Summary = $"删除 {deletedPaths.Count} 个过期文件，失败 {failedDeletes.Count} 个",
                ErrorDetail = string.Join(Environment.NewLine, failedDeletes)
            };
        }

        return new ExecutionResult
        {
            Success = true,
            Summary = skippedPinned > 0
                ? $"删除 {deletedPaths.Count} 个过期文件，跳过置顶 {skippedPinned} 个"
                : $"删除 {deletedPaths.Count} 个过期文件"
        };
    }
}
