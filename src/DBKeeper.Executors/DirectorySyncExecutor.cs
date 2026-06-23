using System.Diagnostics;
using System.IO.Enumeration;
using System.IO.Compression;
using System.Text.Json;
using DBKeeper.Core.Helpers;
using DBKeeper.Core.Models;
using Serilog;

namespace DBKeeper.Executors;

/// <summary>目录同步执行器：支持差量、全量和压缩归档。</summary>
public class DirectorySyncExecutor : ITaskExecutor
{
    public string TaskType => "DIRECTORY_SYNC";
    public bool RequiresConnection => false;

    public async Task<ExecutionResult> ExecuteAsync(TaskItem task, Connection? connection, CancellationToken cancellationToken = default)
    {
        var config = JsonSerializer.Deserialize<DirectorySyncConfig>(task.TaskConfig)!;
        try
        {
            ValidateConfig(config);
            Directory.CreateDirectory(config.TargetDir);

            using var timeoutCts = config.TimeoutSec > 0
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;
            if (timeoutCts != null)
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSec));
            var effectiveToken = timeoutCts?.Token ?? cancellationToken;

            var mode = Normalize(config.SyncMode, "DIFF");
            return mode switch
            {
                "FULL" => await ExecuteFullSyncAsync(config, effectiveToken),
                "ARCHIVE" => await ExecuteArchiveAsync(config, effectiveToken),
                _ => await ExecuteDiffSyncAsync(config, effectiveToken)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "目录同步失败: {SourceDir} -> {TargetDir}", config.SourceDir, config.TargetDir);
            return ExecutionResult.Fail($"目录同步失败：{ex.Message}");
        }
    }

    private static async Task<ExecutionResult> ExecuteDiffSyncAsync(DirectorySyncConfig config, CancellationToken cancellationToken)
    {
        var files = EnumerateSourceFiles(config).ToList();
        var copiedCount = 0;
        var skippedCount = 0;
        long copiedBytes = 0;
        var failedFiles = new List<string>();

        foreach (var sourceFile in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(config.SourceDir, sourceFile);
            var targetFile = Path.Combine(config.TargetDir, relativePath);

            try
            {
                var sourceInfo = new FileInfo(sourceFile);
                var targetInfo = new FileInfo(targetFile);
                var shouldCopy = !targetInfo.Exists
                    || (config.OverwriteChangedFiles
                        && (sourceInfo.Length != targetInfo.Length
                            || sourceInfo.LastWriteTimeUtc > targetInfo.LastWriteTimeUtc.AddSeconds(1)));

                if (!shouldCopy)
                {
                    skippedCount++;
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                await using var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                await using var targetStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None);
                await sourceStream.CopyToAsync(targetStream, cancellationToken);
                File.SetLastWriteTimeUtc(targetFile, sourceInfo.LastWriteTimeUtc);
                copiedCount++;
                copiedBytes += sourceInfo.Length;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedFiles.Add($"{relativePath}: {ex.Message}");
            }
        }

        return BuildSyncResult(config, "DIR_DIFF", copiedCount, skippedCount, copiedBytes, failedFiles);
    }

    private static async Task<ExecutionResult> ExecuteFullSyncAsync(DirectorySyncConfig config, CancellationToken cancellationToken)
    {
        ClearTargetDirectory(config.TargetDir);
        var result = await ExecuteDiffSyncAsync(config, cancellationToken);
        if (result.Metadata != null)
            result.Metadata["BackupType"] = "DIR_FULL";
        if (result.Success)
            result.Summary = result.Summary?.Replace("DIR_DIFF", "DIR_FULL", StringComparison.Ordinal);
        return result;
    }

    private static async Task<ExecutionResult> ExecuteArchiveAsync(DirectorySyncConfig config, CancellationToken cancellationToken)
    {
        var archiveFormat = Normalize(config.ArchiveFormat, "ZIP");
        var extension = archiveFormat == "7Z" ? "7z" : "zip";
        var archiveName = BuildArchiveFileName(config, extension);
        var archivePath = Path.Combine(config.TargetDir, archiveName);

        if (File.Exists(archivePath))
            File.Delete(archivePath);

        if (archiveFormat == "7Z")
            await Create7zArchiveAsync(config, archivePath, cancellationToken);
        else
            await CreateZipArchiveAsync(config, archivePath, cancellationToken);

        var fileInfo = new FileInfo(archivePath);
        var backupType = archiveFormat == "7Z" ? "DIR_7Z" : "DIR_ZIP";
        return new ExecutionResult
        {
            Success = true,
            Summary = $"{backupType} → {archiveName}, {fileInfo.Length / (1024.0 * 1024):F1}MB",
            Metadata = new Dictionary<string, object>
            {
                ["BackupCreated"] = true,
                ["IsDirectoryBackup"] = true,
                ["FilePath"] = archivePath,
                ["FileName"] = archiveName,
                ["BackupType"] = backupType,
                ["SourceType"] = "DIRECTORY",
                ["SourceName"] = new DirectoryInfo(config.SourceDir).Name,
                ["FileSizeBytes"] = fileInfo.Length,
                ["IsVerified"] = true
            }
        };
    }

    private static async Task CreateZipArchiveAsync(DirectorySyncConfig config, string archivePath, CancellationToken cancellationToken)
    {
        var level = Normalize(config.CompressionLevel, "BALANCED") switch
        {
            "FAST" => CompressionLevel.Fastest,
            "SMALLEST" => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal
        };

        await using var zipFile = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(zipFile, ZipArchiveMode.Create);
        foreach (var file in EnumerateSourceFiles(config))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(config.SourceDir, file).Replace('\\', '/');
            var entry = archive.CreateEntry(relativePath, level);
            await using var entryStream = entry.Open();
            await using var sourceStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            await sourceStream.CopyToAsync(entryStream, cancellationToken);
        }
    }

    private static async Task Create7zArchiveAsync(DirectorySyncConfig config, string archivePath, CancellationToken cancellationToken)
    {
        var sevenZip = Find7ZipExecutable();
        if (sevenZip == null)
            throw new InvalidOperationException("未找到 7z.exe。请先安装 7-Zip，或选择 ZIP 压缩格式。");

        var compressionSwitch = Normalize(config.CompressionLevel, "BALANCED") switch
        {
            "FAST" => "-mx=1",
            "SMALLEST" => "-mx=9",
            _ => "-mx=5"
        };
        var includeSubdirectories = config.IncludeSubdirectories ? "-r" : string.Empty;
        var excludeArgs = Build7zExcludeArgs(config);
        var arguments = $"a -t7z {compressionSwitch} {includeSubdirectories} -y \"{archivePath}\" \"{Path.Combine(config.SourceDir, "*")}\" {excludeArgs}".Trim();

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = sevenZip,
            Arguments = arguments,
            WorkingDirectory = config.SourceDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"7Z 压缩失败：{error}{Environment.NewLine}{output}".Trim());
    }

    private static ExecutionResult BuildSyncResult(
        string backupType,
        DirectorySyncConfig config,
        int copiedCount,
        int skippedCount,
        long copiedBytes,
        List<string> failedFiles)
    {
        var metadata = new Dictionary<string, object>
        {
            ["BackupCreated"] = true,
            ["IsDirectoryBackup"] = true,
            ["FilePath"] = config.TargetDir,
            ["FileName"] = new DirectoryInfo(config.TargetDir).Name,
            ["BackupType"] = backupType,
            ["SourceType"] = "DIRECTORY",
            ["SourceName"] = new DirectoryInfo(config.SourceDir).Name,
            ["FileSizeBytes"] = GetDirectorySize(config.TargetDir),
            ["CopiedCount"] = copiedCount,
            ["SkippedCount"] = skippedCount,
            ["FailedCount"] = failedFiles.Count,
            ["CopiedBytes"] = copiedBytes,
            ["IsVerified"] = failedFiles.Count == 0
        };

        var summary = $"{backupType} → 复制 {copiedCount} 个，跳过 {skippedCount} 个，失败 {failedFiles.Count} 个";
        if (failedFiles.Count == 0)
            return new ExecutionResult { Success = true, Summary = summary, Metadata = metadata };

        return new ExecutionResult
        {
            Success = false,
            IsWarning = true,
            Summary = summary,
            ErrorDetail = string.Join(Environment.NewLine, failedFiles.Take(20)),
            Metadata = metadata
        };
    }

    private static IEnumerable<string> EnumerateSourceFiles(DirectorySyncConfig config)
    {
        var option = config.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var patterns = ParseExcludePatterns(config.ExcludePatterns);
        return Directory.EnumerateFiles(config.SourceDir, "*", option)
            .Where(file => !IsExcluded(file, config.SourceDir, patterns));
    }

    private static bool IsExcluded(string filePath, string sourceDir, List<string> patterns)
    {
        if (patterns.Count == 0)
            return false;

        var fileName = Path.GetFileName(filePath);
        var relativePath = Path.GetRelativePath(sourceDir, filePath);
        return patterns.Any(pattern =>
            FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true)
            || FileSystemName.MatchesSimpleExpression(pattern, relativePath, ignoreCase: true));
    }

    private static List<string> ParseExcludePatterns(string? value)
    {
        return (value ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .ToList();
    }

    private static string BuildArchiveFileName(DirectorySyncConfig config, string extension)
    {
        var sourceName = new DirectoryInfo(config.SourceDir).Name;
        var now = DateTime.Now;
        var template = string.IsNullOrWhiteSpace(config.FileNameTemplate)
            ? "{NAME}_{DATE}_{TIME}.{EXT}"
            : config.FileNameTemplate;
        var fileName = template
            .Replace("{NAME}", sourceName, StringComparison.Ordinal)
            .Replace("{DATE}", now.ToString("yyyyMMdd"), StringComparison.Ordinal)
            .Replace("{TIME}", now.ToString("HHmmss"), StringComparison.Ordinal)
            .Replace("{EXT}", extension, StringComparison.Ordinal);
        return SanitizeFileName(fileName);
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(fileName.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
    }

    private static string? Find7ZipExecutable()
    {
        var candidates = new[]
        {
            "7z.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe")
        };

        return candidates.FirstOrDefault(candidate =>
        {
            try
            {
                if (Path.IsPathFullyQualified(candidate))
                    return File.Exists(candidate);

                var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                return paths.Any(path => File.Exists(Path.Combine(path, candidate)));
            }
            catch
            {
                return false;
            }
        });
    }

    private static string Build7zExcludeArgs(DirectorySyncConfig config)
    {
        return string.Join(" ", ParseExcludePatterns(config.ExcludePatterns).Select(pattern => $"-xr!\"{pattern}\""));
    }

    private static void ClearTargetDirectory(string targetDir)
    {
        foreach (var file in Directory.EnumerateFiles(targetDir))
            File.Delete(file);
        foreach (var dir in Directory.EnumerateDirectories(targetDir))
            Directory.Delete(dir, recursive: true);
    }

    private static long GetDirectorySize(string directory)
    {
        if (!Directory.Exists(directory))
            return 0;
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Sum(file =>
            {
                try { return new FileInfo(file).Length; }
                catch { return 0; }
            });
    }

    private static void ValidateConfig(DirectorySyncConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.SourceDir))
            throw new InvalidOperationException("源目录不能为空。");
        if (string.IsNullOrWhiteSpace(config.TargetDir))
            throw new InvalidOperationException("目标目录不能为空。");
        if (!Directory.Exists(config.SourceDir))
            throw new InvalidOperationException($"源目录不存在：{config.SourceDir}");

        var source = Path.GetFullPath(config.SourceDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.GetFullPath(config.TargetDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("源目录和目标目录不能相同。");
        if (BackupPathGuard.IsRootDirectory(target))
            throw new InvalidOperationException("目标目录不能是磁盘根目录。");
        if (BackupPathGuard.IsPathUnderDirectory(target, source))
            throw new InvalidOperationException("目标目录不能位于源目录内部。");
        if (BackupPathGuard.IsPathUnderDirectory(source, target))
            throw new InvalidOperationException("源目录不能位于目标目录内部。");
    }

    private static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToUpperInvariant();
    }
}
