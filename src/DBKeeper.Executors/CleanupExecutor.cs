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
    public bool RequiresConnection => false;

    public CleanupExecutor() { }

    public CleanupExecutor(IBackupFileRepository backupRepo)
    {
        _backupRepo = backupRepo;
    }

    public async Task<ExecutionResult> ExecuteAsync(TaskItem task, Connection? connection, CancellationToken cancellationToken = default)
    {
        var config = JsonSerializer.Deserialize<CleanupConfig>(task.TaskConfig)!;
        var dir = config.TargetDir;

        if (!Directory.Exists(dir))
            return ExecutionResult.Fail($"目录不存在: {dir}");

        if (config.RetentionDays <= 0)
        {
            return ExecutionResult.Ok("保留天数小于等于 0，跳过自动清理");
        }

        if (BackupPathGuard.IsRootDirectory(dir))
            return ExecutionResult.Fail("清理目标目录不能是磁盘根目录");

        if (_backupRepo != null)
            return await ExecuteTrackedCleanupAsync(dir, config, cancellationToken);

        return await ExecuteFileSystemCleanupAsync(dir, config, cancellationToken);
    }

    private async Task<ExecutionResult> ExecuteTrackedCleanupAsync(
        string dir,
        CleanupConfig config,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTime.Now.AddDays(-config.RetentionDays);
        var activeFiles = await _backupRepo!.GetAllActiveAsync();
        var recordsByPath = activeFiles
            .Where(file => IsSameOrUnderDirectory(file.FilePath, dir))
            .Where(file => BackupPathGuard.IsAllowedBackupPath(file.FilePath, [dir]))
            .GroupBy(file => GetFullPathOrOriginal(file.FilePath), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var records = group.OrderByDescending(GetRecordCreatedAt).ToList();
                return new CleanupCandidate(group.Key, records[0], records, GetRecordCreatedAt(records[0]));
            })
            .OrderByDescending(candidate => candidate.CreatedAt)
            .ToList();

        var toDelete = recordsByPath
            .Skip(Math.Max(0, config.MinKeepCount))
            .Where(candidate => candidate.CreatedAt < cutoff)
            .ToList();

        var deletedPaths = new List<string>();
        var skippedPinned = 0;
        var failedDeletes = new List<string>();
        var now = DateTime.Now.ToString("O");

        foreach (var candidate in toDelete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.Records.Any(file => file.IsPinned))
            {
                skippedPinned++;
                Log.Information("跳过置顶备份: {Path}", candidate.Path);
                continue;
            }

            try
            {
                DeletePhysicalPath(candidate.Path, dir);
                deletedPaths.Add(candidate.Path);
                foreach (var record in candidate.Records)
                    await _backupRepo.UpdateStatusAsync(record.Id, "DELETED", now);
                Log.Information("清理备份: {Path}", candidate.Path);
            }
            catch (Exception ex)
            {
                failedDeletes.Add($"{candidate.Record.FileName}: {ex.Message}");
                Log.Warning(ex, "清理备份失败: {Path}", candidate.Path);
            }
        }

        return BuildCleanupResult(deletedPaths.Count, failedDeletes, skippedPinned);
    }

    private static Task<ExecutionResult> ExecuteFileSystemCleanupAsync(
        string dir,
        CleanupConfig config,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTime.Now.AddDays(-config.RetentionDays);
        var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
            .Select(file => new FileInfo(file))
            .Where(file => BackupPathGuard.IsAllowedBackupFile(file.FullName, [dir]))
            .OrderByDescending(file => file.CreationTime)
            .ToList();

        var toDelete = files
            .Skip(Math.Max(0, config.MinKeepCount))
            .Where(file => file.CreationTime < cutoff)
            .ToList();

        var deletedCount = 0;
        var failedDeletes = new List<string>();
        foreach (var file in toDelete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                file.Delete();
                deletedCount++;
                Log.Information("清理备份文件: {File}", file.FullName);
            }
            catch (Exception ex)
            {
                failedDeletes.Add($"{file.Name}: {ex.Message}");
                Log.Warning(ex, "清理备份文件失败: {File}", file.FullName);
            }
        }

        return Task.FromResult(BuildCleanupResult(deletedCount, failedDeletes, 0));
    }

    private static void DeletePhysicalPath(string path, string allowedDirectory)
    {
        if (!BackupPathGuard.IsAllowedBackupPath(path, [allowedDirectory]))
            throw new InvalidOperationException("路径不在允许的备份目录内");

        if (File.Exists(path))
        {
            File.Delete(path);
            return;
        }

        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static ExecutionResult BuildCleanupResult(int deletedCount, List<string> failedDeletes, int skippedPinned)
    {
        if (failedDeletes.Count > 0)
        {
            return new ExecutionResult
            {
                Success = false,
                IsWarning = true,
                Summary = $"删除 {deletedCount} 个过期备份，失败 {failedDeletes.Count} 个",
                ErrorDetail = string.Join(Environment.NewLine, failedDeletes)
            };
        }

        return new ExecutionResult
        {
            Success = true,
            Summary = skippedPinned > 0
                ? $"删除 {deletedCount} 个过期备份，跳过置顶 {skippedPinned} 个"
                : $"删除 {deletedCount} 个过期备份"
        };
    }

    private static bool IsSameOrUnderDirectory(string path, string directory)
    {
        try
        {
            var fullPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullPath, fullDirectory, StringComparison.OrdinalIgnoreCase)
                || BackupPathGuard.IsPathUnderDirectory(fullPath, fullDirectory);
        }
        catch
        {
            return false;
        }
    }

    private static string GetFullPathOrOriginal(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    private static DateTime GetRecordCreatedAt(BackupFile file)
    {
        if (DateTime.TryParse(file.CreatedAt, out var createdAt))
            return createdAt;

        try
        {
            if (File.Exists(file.FilePath))
                return File.GetCreationTime(file.FilePath);
            if (Directory.Exists(file.FilePath))
                return Directory.GetCreationTime(file.FilePath);
        }
        catch
        {
            // 使用最早时间，让无效记录优先被清理。
        }

        return DateTime.MinValue;
    }

    private sealed record CleanupCandidate(
        string Path,
        BackupFile Record,
        List<BackupFile> Records,
        DateTime CreatedAt);
}
