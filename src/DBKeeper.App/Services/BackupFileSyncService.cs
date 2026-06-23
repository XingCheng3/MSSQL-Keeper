using System.IO;
using DBKeeper.Data.Repositories;
using Serilog;

namespace DBKeeper.App.Services;

/// <summary>
/// 后台定时扫描备份目录，比对 backup_files 表与磁盘实际文件。
/// 表中有但磁盘无 → 更新 status='DELETED'，有删除变更时写入 execution_logs。
/// </summary>
public class BackupFileSyncService
{
    private readonly IBackupFileRepository _backupRepo;
    private readonly IExecutionLogRepository _logRepo;
    private readonly ITaskRepository _taskRepo;
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private Timer? _timer;

    public BackupFileSyncService(
        IBackupFileRepository backupRepo,
        IExecutionLogRepository logRepo,
        ITaskRepository taskRepo)
    {
        _backupRepo = backupRepo;
        _logRepo = logRepo;
        _taskRepo = taskRepo;
    }

    public void Start(int? intervalMinutes = null)
    {
        var intervalMin = Math.Clamp(intervalMinutes ?? 30, 1, 1440);
        _timer = new Timer(_ => _ = ScanNowAsync(), null,
            TimeSpan.FromMinutes(intervalMin),
            TimeSpan.FromMinutes(intervalMin));
        Log.Information("备份文件同步服务启动，间隔 {Interval} 分钟", intervalMin);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Restart(int intervalMinutes)
    {
        Stop();
        Start(intervalMinutes);
    }

    public async Task<BackupFileSyncResult> ScanNowAsync()
    {
        if (!await _scanLock.WaitAsync(0))
        {
            return new BackupFileSyncResult(0, 0, "已有备份文件同步扫描正在执行");
        }

        try
        {
            return await ScanCoreAsync();
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private async Task<BackupFileSyncResult> ScanCoreAsync()
    {
        int logId = 0;
        try
        {
            int totalChecked = 0;
            int totalDeleted = 0;

            var allTasks = await _taskRepo.GetAllAsync();
            var backupTasks = allTasks.Where(t => t.TaskType is "BACKUP" or "BACKUP_CLEANUP" or "DIRECTORY_SYNC");

            // 收集所有备份目录
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var task in backupTasks)
            {
                if (!string.IsNullOrEmpty(task.TaskConfig))
                {
                    try
                    {
                        var config = System.Text.Json.JsonDocument.Parse(task.TaskConfig);
                        string? dir = null;
                        if (config.RootElement.TryGetProperty("BackupDir", out var dirElem))
                            dir = dirElem.GetString();
                        else if (config.RootElement.TryGetProperty("TargetDir", out var targetElem))
                            dir = targetElem.GetString();
                        if (!string.IsNullOrEmpty(dir))
                            directories.Add(dir);
                    }
                    catch { /* 忽略解析错误 */ }
                }
            }

            // 扫描每个目录
            foreach (var dir in directories)
            {
                if (!System.IO.Directory.Exists(dir)) continue;

                var activeFiles = await _backupRepo.GetActiveByDirAsync(dir);
                foreach (var file in activeFiles)
                {
                    totalChecked++;
                    if (!PathExists(file.FilePath))
                    {
                        await _backupRepo.UpdateStatusAsync(file.Id, "DELETED", DateTime.Now.ToString("O"));
                        totalDeleted++;
                        Log.Information("备份文件同步: {FileName} 已从磁盘删除，更新状态为 DELETED", file.FileName);
                    }
                }
            }

            // 同时扫描没有匹配到目录但有活跃记录的文件
            var allActive = await _backupRepo.GetAllActiveAsync();
            foreach (var file in allActive)
            {
                // 跳过已在目录扫描中检查过的
                if (directories.Any(d => IsSameOrUnderDirectory(file.FilePath, d)))
                    continue;

                totalChecked++;
                if (!PathExists(file.FilePath))
                {
                    await _backupRepo.UpdateStatusAsync(file.Id, "DELETED", DateTime.Now.ToString("O"));
                    totalDeleted++;
                    Log.Information("备份文件同步: {FileName} 已从磁盘删除，更新状态为 DELETED", file.FileName);
                }
            }

            var summary = $"扫描完成: 检查 {totalChecked} 个文件，标记 {totalDeleted} 个为 DELETED";
            if (totalDeleted > 0)
            {
                var startedAt = DateTime.Now.ToString("O");
                logId = await _logRepo.InsertAsync(new Core.Models.ExecutionLog
                {
                    TaskId = null,
                    TaskName = "备份文件同步扫描",
                    TaskType = "SYSTEM",
                    TriggerType = "SYSTEM",
                    StartedAt = startedAt,
                    Status = "RUNNING"
                });

                await _logRepo.UpdateFinishAsync(logId, "SUCCESS", 0, summary, null);
            }
            Log.Information("备份文件同步扫描完成: {Summary}", summary);
            return new BackupFileSyncResult(totalChecked, totalDeleted, summary);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "备份文件同步扫描异常");
            if (logId > 0)
            {
                try { await _logRepo.UpdateFinishAsync(logId, "FAILED", 0, null, ex.ToString()); }
                catch { /* 避免二次异常 */ }
            }

            return new BackupFileSyncResult(0, 0, $"扫描失败: {ex.Message}");
        }
    }

    private static bool IsSameOrUnderDirectory(string filePath, string directory)
    {
        try
        {
            var fullFilePath = Path.GetFullPath(filePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullDirectoryWithoutSeparator = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return string.Equals(fullFilePath, fullDirectoryWithoutSeparator, StringComparison.OrdinalIgnoreCase)
                || fullFilePath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool PathExists(string path)
    {
        return System.IO.File.Exists(path) || System.IO.Directory.Exists(path);
    }
}

public record BackupFileSyncResult(int CheckedCount, int DeletedCount, string Summary);
