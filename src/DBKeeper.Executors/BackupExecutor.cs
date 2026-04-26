using System.Text.Json;
using DBKeeper.Core.Models;
using DBKeeper.Data;
using Serilog;

namespace DBKeeper.Executors;

/// <summary>
/// 备份执行器：执行 BACKUP DATABASE，含磁盘空间预检
/// </summary>
public class BackupExecutor : ITaskExecutor
{
    /// <summary>所需磁盘空间 = 数据库大小 × 此倍率</summary>
    private const double RequiredSpaceMultiplier = 1.2;

    public string TaskType => "BACKUP";

    public async Task<ExecutionResult> ExecuteAsync(TaskItem task, Connection connection)
    {
        var config = JsonSerializer.Deserialize<BackupConfig>(task.TaskConfig)!;
        var dbName = config.DatabaseName;
        var backupDir = config.BackupDir;

        // 文件名变量替换：{DATE} 只表示日期，{TIME} 只表示时间，避免生成重复时间戳。
        var now = DateTime.Now;
        var fileName = (config.FileNameTemplate ?? "{DB}_{DATE}_{TIME}.bak")
            .Replace("{DB}", dbName)
            .Replace("{DATE}", now.ToString("yyyyMMdd"))
            .Replace("{TIME}", now.ToString("HHmmss"));
        fileName = SanitizeFileName(fileName);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = $"{SanitizeFileName(dbName)}_{now:yyyyMMdd_HHmmss}.bak";
        var filePath = System.IO.Path.Combine(backupDir, fileName);

        // 确保备份目录存在
        Directory.CreateDirectory(backupDir);

        // 磁盘空间预检
        var driveInfo = new DriveInfo(System.IO.Path.GetPathRoot(filePath)!);
        var dbSizeMb = await SqlServerClient.GetDatabaseSizeMbAsync(connection, dbName);
        var requiredMb = (long)(dbSizeMb * RequiredSpaceMultiplier);

        if (driveInfo.AvailableFreeSpace / (1024 * 1024) < requiredMb)
        {
            var freeMb = driveInfo.AvailableFreeSpace / (1024 * 1024);
            var msg = $"磁盘空间不足: 数据库大小={dbSizeMb}MB, 需要={requiredMb}MB, 剩余={freeMb}MB";
            Log.Warning("备份跳过 - {Message}, 数据库={Db}", msg, dbName);
            return ExecutionResult.Warn(msg, msg);
        }

        // 执行备份
        var timeoutSec = config.TimeoutSec > 0 ? config.TimeoutSec : 600;
        await SqlServerClient.ExecuteBackupAsync(connection, dbName, filePath, config.UseCompression, timeoutSec, config.BackupType);

        // 校验
        if (config.VerifyAfterBackup)
            await SqlServerClient.ExecuteVerifyAsync(connection, filePath);

        // 获取文件大小
        var fileInfo = new FileInfo(filePath);
        var sizeMb = fileInfo.Length / (1024.0 * 1024);

        Log.Information("备份完成: {Db} → {FilePath}, 大小={SizeMB:F1}MB", dbName, filePath, sizeMb);
        return new ExecutionResult
        {
            Success = true,
            Summary = $"{dbName} → {fileName}, {sizeMb:F1}MB",
            Metadata = new Dictionary<string, object>
            {
                ["FilePath"] = filePath,
                ["FileName"] = fileName,
                ["FileSizeBytes"] = fileInfo.Length
            }
        };
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        if (!string.Equals(fileName, sanitized, StringComparison.Ordinal))
            Log.Warning("备份文件名包含非法字符，已自动替换: {Original} -> {Sanitized}", fileName, sanitized);
        return sanitized;
    }
}
