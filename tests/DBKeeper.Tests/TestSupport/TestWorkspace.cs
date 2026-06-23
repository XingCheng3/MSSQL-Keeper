using DBKeeper.Data;
using DBKeeper.Data.Repositories;

namespace DBKeeper.Tests.TestSupport;

internal sealed class TestWorkspace : IDisposable
{
    public string RootPath { get; }
    public string DatabasePath { get; }
    public DbInitializer DbInitializer { get; }
    public IConnectionRepository ConnectionRepository { get; }
    public ITaskRepository TaskRepository { get; }
    public IBackupFileRepository BackupFileRepository { get; }
    public IExecutionLogRepository ExecutionLogRepository { get; }
    public ISettingsRepository SettingsRepository { get; }

    public TestWorkspace()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "dbkeeper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
        DatabasePath = Path.Combine(RootPath, "dbkeeper.db");
        DbInitializer = new DbInitializer(DatabasePath);
        DbInitializer.Initialize();

        ConnectionRepository = new ConnectionRepository(DbInitializer.ConnectionString);
        TaskRepository = new TaskRepository(DbInitializer.ConnectionString);
        BackupFileRepository = new BackupFileRepository(DbInitializer.ConnectionString);
        ExecutionLogRepository = new ExecutionLogRepository(DbInitializer.ConnectionString);
        SettingsRepository = new SettingsRepository(DbInitializer.ConnectionString);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
        catch
        {
            // 测试结束后的临时目录清理失败不影响断言结果。
        }
    }
}
