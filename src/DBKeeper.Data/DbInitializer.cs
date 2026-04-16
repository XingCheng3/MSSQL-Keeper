using Microsoft.Data.Sqlite;
using Serilog;

namespace DBKeeper.Data;

/// <summary>
/// SQLite 数据库初始化：建库、建表、建索引、预置设置
/// </summary>
public class DbInitializer
{
    private readonly string _dbPath;

    public DbInitializer(string? dbPath = null)
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        _dbPath = dbPath ?? Path.Combine(dataDir, "dbkeeper.db");
    }

    public string ConnectionString => $"Data Source={_dbPath};Foreign Keys=False";

    /// <summary>
    /// 首次运行时初始化数据库
    /// </summary>
    public void Initialize()
    {
        // Dapper：启用下划线列名 → PascalCase 属性自动映射
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();

        // WAL 模式提升并发读写性能
        Execute(conn, "PRAGMA journal_mode=WAL;");
        Execute(conn, "PRAGMA busy_timeout = 5000;"); // 并发写等待 5 秒

        CreateTables(conn);
        Migrate(conn);
        CreateIndexes(conn);
        SeedSettings(conn);

        Log.Information("SQLite 数据库初始化完成: {DbPath}", _dbPath);
    }

    private void CreateTables(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS connections (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                name        TEXT    NOT NULL,
                host        TEXT    NOT NULL,
                username    TEXT    NOT NULL,
                password    TEXT    NOT NULL,
                default_db  TEXT,
                timeout_sec INTEGER DEFAULT 30,
                trust_server_certificate INTEGER DEFAULT 1,
                is_default  INTEGER DEFAULT 0,
                remark      TEXT,
                created_at  TEXT    NOT NULL,
                updated_at  TEXT    NOT NULL
            );
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS tasks (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                name            TEXT    NOT NULL,
                task_type       TEXT    NOT NULL,
                connection_id   INTEGER NOT NULL,
                is_enabled      INTEGER DEFAULT 1,
                schedule_type   TEXT    NOT NULL,
                schedule_config TEXT    NOT NULL,
                task_config     TEXT    NOT NULL,
                last_run_at     TEXT,
                last_run_status TEXT,
                next_run_at     TEXT,
                created_at      TEXT    NOT NULL,
                updated_at      TEXT    NOT NULL,
                FOREIGN KEY (connection_id) REFERENCES connections(id)
            );
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS backup_files (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                task_id         INTEGER NOT NULL,
                database_name   TEXT    NOT NULL,
                file_name       TEXT    NOT NULL,
                file_path       TEXT    NOT NULL,
                file_size_bytes INTEGER,
                backup_type     TEXT,
                created_at      TEXT    NOT NULL,
                expires_at      TEXT,
                is_pinned       INTEGER DEFAULT 0,
                is_verified     INTEGER DEFAULT 0,
                status          TEXT    DEFAULT 'NORMAL',
                deleted_at      TEXT,
                FOREIGN KEY (task_id) REFERENCES tasks(id)
            );
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS execution_logs (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                task_id         INTEGER NOT NULL,
                task_name       TEXT    NOT NULL,
                task_type       TEXT    NOT NULL,
                trigger_type    TEXT    NOT NULL,
                started_at      TEXT    NOT NULL,
                finished_at     TEXT,
                duration_ms     INTEGER,
                status          TEXT    NOT NULL,
                summary         TEXT,
                error_detail    TEXT,
                FOREIGN KEY (task_id) REFERENCES tasks(id)
            );
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS settings (
                key         TEXT    PRIMARY KEY,
                value       TEXT    NOT NULL,
                updated_at  TEXT    NOT NULL
            );
            """);
    }

    private void Migrate(SqliteConnection conn)
    {
        // 兼容已有数据库：添加 trust_server_certificate 列
        try
        {
            Execute(conn, "ALTER TABLE connections ADD COLUMN trust_server_certificate INTEGER DEFAULT 1;");
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // 列已存在，忽略
        }
    }

    private void CreateIndexes(SqliteConnection conn)
    {
        Execute(conn, "CREATE INDEX IF NOT EXISTS idx_logs_task_id    ON execution_logs(task_id);");
        Execute(conn, "CREATE INDEX IF NOT EXISTS idx_logs_started_at ON execution_logs(started_at);");
        Execute(conn, "CREATE INDEX IF NOT EXISTS idx_logs_status     ON execution_logs(status);");
        Execute(conn, "CREATE INDEX IF NOT EXISTS idx_backup_task_id    ON backup_files(task_id);");
        Execute(conn, "CREATE INDEX IF NOT EXISTS idx_backup_status     ON backup_files(status);");
        Execute(conn, "CREATE INDEX IF NOT EXISTS idx_backup_expires_at ON backup_files(expires_at);");
        Execute(conn, "CREATE INDEX IF NOT EXISTS idx_tasks_type    ON tasks(task_type);");
        Execute(conn, "CREATE INDEX IF NOT EXISTS idx_tasks_enabled ON tasks(is_enabled);");
    }

    private void SeedSettings(SqliteConnection conn)
    {
        var defaults = new Dictionary<string, string>
        {
            ["minimize_to_tray_on_close"] = "true",
            ["auto_start"] = "false",
            ["log_retention_days"] = DefaultSettings.LogRetentionDays.ToString(),
            ["max_concurrent_tasks"] = DefaultSettings.MaxConcurrentTasks.ToString(),
            ["disk_warn_threshold"] = DefaultSettings.DiskWarnThreshold.ToString(),
            ["disk_danger_threshold"] = DefaultSettings.DiskDangerThreshold.ToString(),
            ["disk_check_interval_min"] = DefaultSettings.DiskCheckIntervalMin.ToString(),
            ["heartbeat_interval_sec"] = DefaultSettings.HeartbeatIntervalSec.ToString(),
            ["backup_scan_interval_min"] = DefaultSettings.BackupScanIntervalMin.ToString()
        };

        var now = DateTime.Now.ToString("O");
        foreach (var (key, value) in defaults)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO settings (key, value, updated_at) VALUES (@key, @value, @now)";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

/// <summary>预设设置默认值常量</summary>
public static class DefaultSettings
{
    public const int LogRetentionDays = 90;
    public const int MaxConcurrentTasks = 3;
    public const int DiskWarnThreshold = 20;
    public const int DiskDangerThreshold = 10;
    public const int DiskCheckIntervalMin = 5;
    public const int HeartbeatIntervalSec = 60;
    public const int BackupScanIntervalMin = 30;
}
