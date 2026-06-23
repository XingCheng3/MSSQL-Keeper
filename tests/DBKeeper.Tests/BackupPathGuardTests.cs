using DBKeeper.Core.Helpers;

namespace DBKeeper.Tests;

public class BackupPathGuardTests
{
    [Fact]
    public void IsAllowedBackupFile_ShouldValidateDirectoryAndExtension()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dbkeeper-guard");
        var allowedDirectories = new[] { directory };
        var backupFile = Path.Combine(directory, "a.bak");
        var logFile = Path.Combine(directory, "b.trn");
        var invalidExtension = Path.Combine(directory, "c.txt");
        var externalFile = Path.Combine(Path.GetTempPath(), "external", "d.bak");

        Assert.True(BackupPathGuard.IsAllowedBackupFile(backupFile, allowedDirectories));
        Assert.True(BackupPathGuard.IsAllowedBackupFile(logFile, allowedDirectories));
        Assert.False(BackupPathGuard.IsAllowedBackupFile(invalidExtension, allowedDirectories));
        Assert.False(BackupPathGuard.IsAllowedBackupFile(externalFile, allowedDirectories));
    }
}
