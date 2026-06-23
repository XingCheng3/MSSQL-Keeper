using DBKeeper.Core.Models;
using DBKeeper.Executors;
using DBKeeper.Tests.TestSupport;

namespace DBKeeper.Tests;

public class CleanupExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldSkipDeleteWhenRetentionDaysIsZero()
    {
        using var workspace = new TestWorkspace();
        var backupDirectory = Path.Combine(workspace.RootPath, "backup");
        Directory.CreateDirectory(backupDirectory);
        var filePath = Path.Combine(backupDirectory, "sample.bak");
        await File.WriteAllTextAsync(filePath, "data");

        var executor = new CleanupExecutor();
        var result = await executor.ExecuteAsync(
            new TaskItem
            {
                TaskConfig = $$"""{"TargetDir":"{{backupDirectory.Replace("\\", "\\\\")}}","RetentionDays":0,"MinKeepCount":0}"""
            },
            new Connection());

        Assert.True(result.Success);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldDeleteTrackedTrnFileAndMarkDeleted()
    {
        using var workspace = new TestWorkspace();
        var backupDirectory = Path.Combine(workspace.RootPath, "backup");
        Directory.CreateDirectory(backupDirectory);
        var filePath = Path.Combine(backupDirectory, "sample.trn");
        await File.WriteAllTextAsync(filePath, "log");
        File.SetCreationTime(filePath, DateTime.Now.AddDays(-2));

        var backupFileId = await workspace.BackupFileRepository.InsertAsync(new BackupFile
        {
            DatabaseName = "MES_DB",
            FileName = "sample.trn",
            FilePath = filePath,
            FileSizeBytes = 3,
            BackupType = "LOG",
            CreatedAt = DateTime.Now.AddDays(-2).ToString("O"),
            Status = "NORMAL"
        });

        var executor = new CleanupExecutor(workspace.BackupFileRepository);
        var result = await executor.ExecuteAsync(
            new TaskItem
            {
                TaskConfig = $$"""{"TargetDir":"{{backupDirectory.Replace("\\", "\\\\")}}","RetentionDays":1,"MinKeepCount":0}"""
            },
            new Connection());

        Assert.True(result.Success);
        Assert.False(File.Exists(filePath));
        var files = await workspace.BackupFileRepository.GetAllAsync();
        var deletedFile = files.Single(file => file.Id == backupFileId);
        Assert.Equal("DELETED", deletedFile.Status);
        Assert.False(string.IsNullOrWhiteSpace(deletedFile.DeletedAt));
    }
}
