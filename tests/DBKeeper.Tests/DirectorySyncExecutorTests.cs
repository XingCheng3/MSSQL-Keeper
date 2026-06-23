using System.IO.Compression;
using System.Text.Json;
using DBKeeper.Core.Models;
using DBKeeper.Executors;
using DBKeeper.Tests.TestSupport;

namespace DBKeeper.Tests;

public class DirectorySyncExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_Diff_ShouldCopyNewAndNestedFiles()
    {
        using var workspace = new TestWorkspace();
        var sourceDir = Path.Combine(workspace.RootPath, "source");
        var targetDir = Path.Combine(workspace.RootPath, "target");
        Directory.CreateDirectory(Path.Combine(sourceDir, "nested"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "nested", "b.txt"), "beta");

        var result = await ExecuteAsync(sourceDir, targetDir, "DIFF");

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(targetDir, "a.txt")));
        Assert.True(File.Exists(Path.Combine(targetDir, "nested", "b.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_Diff_ShouldNotDeleteExtraTargetFiles()
    {
        using var workspace = new TestWorkspace();
        var sourceDir = Path.Combine(workspace.RootPath, "source");
        var targetDir = Path.Combine(workspace.RootPath, "target");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(targetDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(targetDir, "extra.txt"), "keep");

        var result = await ExecuteAsync(sourceDir, targetDir, "DIFF");

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(targetDir, "a.txt")));
        Assert.True(File.Exists(Path.Combine(targetDir, "extra.txt")));
    }

    [Fact]
    public async Task ExecuteAsync_Full_ShouldClearTargetBeforeCopy()
    {
        using var workspace = new TestWorkspace();
        var sourceDir = Path.Combine(workspace.RootPath, "source");
        var targetDir = Path.Combine(workspace.RootPath, "target");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(targetDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(targetDir, "stale.txt"), "remove");

        var result = await ExecuteAsync(sourceDir, targetDir, "FULL");

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(targetDir, "a.txt")));
        Assert.False(File.Exists(Path.Combine(targetDir, "stale.txt")));
        Assert.Equal("DIR_FULL", result.Metadata?["BackupType"]);
    }

    [Fact]
    public async Task ExecuteAsync_ArchiveZip_ShouldCreateZipWithSourceFiles()
    {
        using var workspace = new TestWorkspace();
        var sourceDir = Path.Combine(workspace.RootPath, "source");
        var targetDir = Path.Combine(workspace.RootPath, "target");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "alpha");

        var result = await ExecuteAsync(sourceDir, targetDir, "ARCHIVE");

        Assert.True(result.Success);
        Assert.Equal("DIR_ZIP", result.Metadata?["BackupType"]);
        var archivePath = Assert.IsType<string>(result.Metadata?["FilePath"]);
        Assert.True(File.Exists(archivePath));
        using var archive = ZipFile.OpenRead(archivePath);
        Assert.Contains(archive.Entries, entry => entry.FullName == "a.txt");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectNestedTargetDirectory()
    {
        using var workspace = new TestWorkspace();
        var sourceDir = Path.Combine(workspace.RootPath, "source");
        var targetDir = Path.Combine(sourceDir, "target");
        Directory.CreateDirectory(sourceDir);

        var result = await ExecuteAsync(sourceDir, targetDir, "DIFF");

        Assert.False(result.Success);
        Assert.Contains("目标目录不能位于源目录内部", result.ErrorDetail);
    }

    private static Task<ExecutionResult> ExecuteAsync(string sourceDir, string targetDir, string mode)
    {
        var config = new DirectorySyncConfig
        {
            SourceDir = sourceDir,
            TargetDir = targetDir,
            SyncMode = mode,
            ArchiveFormat = "ZIP",
            CompressionLevel = "BALANCED",
            FileNameTemplate = "{NAME}_{DATE}_{TIME}.{EXT}",
            RetentionDays = 30,
            MinKeepCount = 3,
            IncludeSubdirectories = true,
            OverwriteChangedFiles = true,
            TimeoutSec = 60
        };

        var task = new TaskItem
        {
            Name = "目录同步测试",
            TaskType = "DIRECTORY_SYNC",
            TaskConfig = JsonSerializer.Serialize(config)
        };

        return new DirectorySyncExecutor().ExecuteAsync(task, null);
    }
}
