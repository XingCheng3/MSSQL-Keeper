using DBKeeper.Core.Helpers;
using DBKeeper.Core.Models;
using System.Data;
using System.Data.SqlClient;
#pragma warning disable CS0618 // System.Data.SqlClient 标记过时但功能完好，用它省 ~200MB 内存
using Serilog;

namespace DBKeeper.Data;

/// <summary>
/// 封装对目标 SQL Server 实例的操作
/// </summary>
public class SqlServerClient
{
    /// <summary>
    /// 根据 Connection 实体构建连接字符串（密码自动 DPAPI 解密）
    /// </summary>
    public static string BuildConnectionString(Connection conn, string? database = null)
    {
        var password = DpapiHelper.Decrypt(conn.Password);
        var db = database ?? conn.DefaultDb ?? "master";
        var trustCert = conn.TrustServerCertificate ? "True" : "False";
        return $"Server={conn.Host};Database={db};User Id={conn.Username};Password={password};" +
               $"Connect Timeout={conn.TimeoutSec};TrustServerCertificate={trustCert};" +
               $"Pooling=false;";
    }

    /// <summary>测试连接是否可用</summary>
    public static async Task<OperationResult> TestConnectionAsync(Connection conn)
    {
        try
        {
            await using var sqlConn = new SqlConnection(BuildConnectionString(conn));
            await sqlConn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT 1", sqlConn);
            await cmd.ExecuteScalarAsync();
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    /// <summary>获取目标实例的数据库列表</summary>
    public static async Task<List<string>> GetDatabaseListAsync(Connection conn)
    {
        var list = new List<string>();
        await using var sqlConn = new SqlConnection(BuildConnectionString(conn));
        await sqlConn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT name FROM sys.databases WHERE state_desc = 'ONLINE' ORDER BY name", sqlConn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(reader.GetString(0));
        return list;
    }

    /// <summary>执行 BACKUP DATABASE/LOG 命令</summary>
    public static async Task ExecuteBackupAsync(Connection conn, string database, string backupPath,
        bool useCompression, int timeoutSec = 600, string backupType = "FULL", CancellationToken cancellationToken = default)
    {
        var sql = backupType.ToUpper() switch
        {
            "DIFF" => $"BACKUP DATABASE [{database}] TO DISK = @path WITH DIFFERENTIAL, INIT",
            "LOG"  => $"BACKUP LOG [{database}] TO DISK = @path WITH INIT",
            _      => $"BACKUP DATABASE [{database}] TO DISK = @path WITH INIT"
        };
        if (useCompression && backupType.ToUpper() != "LOG") sql += ", COMPRESSION";

        await using var sqlConn = new SqlConnection(BuildConnectionString(conn, "master"));
        await sqlConn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, sqlConn) { CommandTimeout = timeoutSec };
        cmd.Parameters.AddWithValue("@path", backupPath);
        using var cancelRegistration = cancellationToken.Register(() =>
        {
            try { cmd.Cancel(); }
            catch { /* 取消时连接可能已释放 */ }
        });

        // BACKUP 通过 InfoMessage 报告进度
        sqlConn.InfoMessage += (_, e) => Log.Debug("SQL Server 备份消息: {Message}", e.Message);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>执行 RESTORE VERIFYONLY 校验备份文件</summary>
    public static async Task ExecuteVerifyAsync(Connection conn, string backupPath, int timeoutSec = 300, CancellationToken cancellationToken = default)
    {
        await using var sqlConn = new SqlConnection(BuildConnectionString(conn, "master"));
        await sqlConn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand("RESTORE VERIFYONLY FROM DISK = @path", sqlConn)
            { CommandTimeout = timeoutSec };
        cmd.Parameters.AddWithValue("@path", backupPath);
        using var cancelRegistration = cancellationToken.Register(() =>
        {
            try { cmd.Cancel(); }
            catch { /* 取消时连接可能已释放 */ }
        });
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>执行存储过程</summary>
    public static async Task<string?> ExecuteProcedureAsync(Connection conn, string database,
        string procedureName, Dictionary<string, string>? parameters = null, int timeoutSec = 300, CancellationToken cancellationToken = default)
    {
        await using var sqlConn = new SqlConnection(BuildConnectionString(conn, database));
        await sqlConn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(procedureName, sqlConn)
        {
            CommandType = System.Data.CommandType.StoredProcedure,
            CommandTimeout = timeoutSec
        };

        if (parameters != null)
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value);

        using var cancelRegistration = cancellationToken.Register(() =>
        {
            try { cmd.Cancel(); }
            catch { /* 取消时连接可能已释放 */ }
        });
        var rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return $"存储过程执行完成，影响 {rowsAffected} 行";
    }

    /// <summary>执行自定义 SQL</summary>
    public static async Task<string?> ExecuteSqlAsync(Connection conn, string database,
        string sql, int timeoutSec = 600, CancellationToken cancellationToken = default)
    {
        await using var sqlConn = new SqlConnection(BuildConnectionString(conn, database));
        await sqlConn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, sqlConn) { CommandTimeout = timeoutSec };
        using var cancelRegistration = cancellationToken.Register(() =>
        {
            try { cmd.Cancel(); }
            catch { /* 取消时连接可能已释放 */ }
        });
        var rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return $"SQL 执行完成，影响 {rowsAffected} 行";
    }

    /// <summary>获取数据库大小（MB），用于备份前空间预检</summary>
    public static async Task<long> GetDatabaseSizeMbAsync(Connection conn, string database, CancellationToken cancellationToken = default)
    {
        await using var sqlConn = new SqlConnection(BuildConnectionString(conn, database));
        await sqlConn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(
            "SELECT SUM(size) * 8 / 1024 FROM sys.database_files", sqlConn);
        using var cancelRegistration = cancellationToken.Register(() =>
        {
            try { cmd.Cancel(); }
            catch { /* 取消时连接可能已释放 */ }
        });
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is DBNull ? 0 : Convert.ToInt64(result);
    }

    /// <summary>分析指定数据库的文件、表、索引空间占用</summary>
    public static async Task<StorageAnalysisResult> AnalyzeStorageAsync(
        Connection conn,
        string database,
        CancellationToken cancellationToken = default)
    {
        var result = new StorageAnalysisResult();
        await using var sqlConn = new SqlConnection(BuildConnectionString(conn, database));
        await sqlConn.OpenAsync(cancellationToken);

        result.Files = await QueryFileUsageAsync(sqlConn, cancellationToken);
        var (tableCount, indexCount) = await QueryStorageObjectCountsAsync(sqlConn, cancellationToken);
        result.Tables = await QueryTableUsageAsync(sqlConn, cancellationToken);
        result.Indexes = await QueryIndexUsageAsync(sqlConn, cancellationToken);

        var dataMb = result.Files
            .Where(f => string.Equals(f.FileType, "ROWS", StringComparison.OrdinalIgnoreCase))
            .Sum(f => f.SizeMb);
        var logMb = result.Files
            .Where(f => string.Equals(f.FileType, "LOG", StringComparison.OrdinalIgnoreCase))
            .Sum(f => f.SizeMb);
        var totalMb = dataMb + logMb;
        var usedMb = result.Files.Sum(f => f.UsedMb);

        foreach (var table in result.Tables)
        {
            table.PercentOfDatabase = totalMb > 0
                ? Math.Round(table.TotalMb * 100 / totalMb, 2)
                : 0;
        }

        result.Overview = new StorageOverview
        {
            DatabaseName = database,
            TotalMb = totalMb,
            DataMb = dataMb,
            LogMb = logMb,
            UsedMb = usedMb,
            UnusedMb = Math.Max(0, totalMb - usedMb),
            TableCount = tableCount,
            IndexCount = indexCount
        };

        return result;
    }

    private static async Task<(int TableCount, int IndexCount)> QueryStorageObjectCountsAsync(SqlConnection sqlConn, CancellationToken cancellationToken)
    {
        var sql = $$"""
            SELECT
                (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0) AS table_count,
                (SELECT COUNT(*) FROM sys.indexes i
                 INNER JOIN sys.tables t ON i.object_id = t.object_id
                 WHERE t.is_ms_shipped = 0 AND i.type > 0) AS index_count;
            """;

        await using var cmd = CreateCancellableCommand(sqlConn, sql, cancellationToken);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return (0, 0);

        return (
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("table_count"))),
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("index_count")))
        );
    }

    private static async Task<List<StorageFileUsage>> QueryFileUsageAsync(SqlConnection sqlConn, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT
                df.name AS file_name,
                df.type_desc AS file_type,
                fg.name AS file_group,
                df.physical_name,
                CAST(df.size * 8.0 / 1024 AS decimal(18,2)) AS size_mb,
                CAST(ISNULL(FILEPROPERTY(df.name, 'SpaceUsed'), 0) * 8.0 / 1024 AS decimal(18,2)) AS used_mb,
                CAST((df.size - ISNULL(FILEPROPERTY(df.name, 'SpaceUsed'), 0)) * 8.0 / 1024 AS decimal(18,2)) AS free_mb,
                CASE
                    WHEN df.is_percent_growth = 1 THEN CAST(df.growth AS varchar(20)) + '%'
                    WHEN df.growth = 0 THEN N'固定'
                    ELSE CAST(CAST(df.growth * 8.0 / 1024 AS decimal(18,2)) AS varchar(20)) + ' MB'
                END AS growth_desc
            FROM sys.database_files df
            LEFT JOIN sys.filegroups fg ON df.data_space_id = fg.data_space_id
            ORDER BY df.type, df.file_id;
            """;

        var list = new List<StorageFileUsage>();
        await using var cmd = CreateCancellableCommand(sqlConn, sql, cancellationToken);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new StorageFileUsage
            {
                FileName = reader.GetString("file_name"),
                FileType = reader.GetString("file_type"),
                FileGroup = reader.GetNullableString("file_group"),
                PhysicalName = reader.GetString("physical_name"),
                SizeMb = reader.GetDecimal("size_mb"),
                UsedMb = reader.GetDecimal("used_mb"),
                FreeMb = reader.GetDecimal("free_mb"),
                Growth = reader.GetString("growth_desc")
            });
        }

        return list;
    }

    private static async Task<List<TableSpaceUsage>> QueryTableUsageAsync(SqlConnection sqlConn, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH row_counts AS (
                SELECT object_id, SUM(row_count) AS row_count
                FROM sys.dm_db_partition_stats
                WHERE index_id IN (0, 1)
                GROUP BY object_id
            ),
            table_pages AS (
                SELECT
                    object_id,
                    SUM(reserved_page_count) AS total_pages,
                    SUM(used_page_count) AS used_pages,
                    SUM(CASE WHEN index_id IN (0, 1)
                             THEN in_row_data_page_count + lob_used_page_count + row_overflow_used_page_count
                             ELSE 0 END) AS data_pages,
                    SUM(CASE WHEN index_id > 1 THEN used_page_count ELSE 0 END) AS index_pages
                FROM sys.dm_db_partition_stats
                GROUP BY object_id
            )
            SELECT TOP 200
                s.name AS schema_name,
                t.name AS table_name,
                ISNULL(rc.row_count, 0) AS row_count,
                CAST(ISNULL(tp.total_pages, 0) * 8.0 / 1024 AS decimal(18,2)) AS total_mb,
                CAST(ISNULL(tp.data_pages, 0) * 8.0 / 1024 AS decimal(18,2)) AS data_mb,
                CAST(ISNULL(tp.index_pages, 0) * 8.0 / 1024 AS decimal(18,2)) AS index_mb,
                CAST((ISNULL(tp.total_pages, 0) - ISNULL(tp.used_pages, 0)) * 8.0 / 1024 AS decimal(18,2)) AS unused_mb
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            LEFT JOIN row_counts rc ON t.object_id = rc.object_id
            LEFT JOIN table_pages tp ON t.object_id = tp.object_id
            WHERE t.is_ms_shipped = 0
            ORDER BY total_mb DESC, s.name, t.name;
            """;

        var list = new List<TableSpaceUsage>();
        await using var cmd = CreateCancellableCommand(sqlConn, sql, cancellationToken);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new TableSpaceUsage
            {
                SchemaName = reader.GetString("schema_name"),
                TableName = reader.GetString("table_name"),
                RowCount = reader.GetInt64("row_count"),
                TotalMb = reader.GetDecimal("total_mb"),
                DataMb = reader.GetDecimal("data_mb"),
                IndexMb = reader.GetDecimal("index_mb"),
                UnusedMb = reader.GetDecimal("unused_mb")
            });
        }

        return list;
    }

    private static async Task<List<IndexSpaceUsage>> QueryIndexUsageAsync(SqlConnection sqlConn, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH index_rows AS (
                SELECT object_id, index_id, SUM(row_count) AS row_count
                FROM sys.dm_db_partition_stats
                GROUP BY object_id, index_id
            ),
            index_pages AS (
                SELECT
                    object_id,
                    index_id,
                    SUM(used_page_count) AS used_pages
                FROM sys.dm_db_partition_stats
                GROUP BY object_id, index_id
            )
            SELECT TOP 300
                s.name AS schema_name,
                t.name AS table_name,
                ISNULL(i.name, CASE WHEN i.index_id = 0 THEN N'(HEAP)' ELSE N'(未命名索引)' END) AS index_name,
                i.type_desc AS index_type,
                ISNULL(ir.row_count, 0) AS row_count,
                CAST(ISNULL(ip.used_pages, 0) * 8.0 / 1024 AS decimal(18,2)) AS size_mb
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            INNER JOIN sys.indexes i ON t.object_id = i.object_id
            LEFT JOIN index_rows ir ON i.object_id = ir.object_id AND i.index_id = ir.index_id
            LEFT JOIN index_pages ip ON i.object_id = ip.object_id AND i.index_id = ip.index_id
            WHERE t.is_ms_shipped = 0
              AND ISNULL(ip.used_pages, 0) > 0
            ORDER BY size_mb DESC, s.name, t.name, index_name;
            """;

        var list = new List<IndexSpaceUsage>();
        await using var cmd = CreateCancellableCommand(sqlConn, sql, cancellationToken);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new IndexSpaceUsage
            {
                SchemaName = reader.GetString("schema_name"),
                TableName = reader.GetString("table_name"),
                IndexName = reader.GetString("index_name"),
                IndexType = reader.GetString("index_type"),
                RowCount = reader.GetInt64("row_count"),
                SizeMb = reader.GetDecimal("size_mb")
            });
        }

        return list;
    }

    private static SqlCommand CreateCancellableCommand(SqlConnection sqlConn, string sql, CancellationToken cancellationToken)
    {
        var cmd = new SqlCommand(sql, sqlConn) { CommandTimeout = 300 };
        cancellationToken.Register(() =>
        {
            try { cmd.Cancel(); }
            catch { /* 取消时连接可能已释放 */ }
        });
        return cmd;
    }
}

internal static class SqlDataReaderExtensions
{
    public static string GetString(this SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    public static string? GetNullableString(this SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static decimal GetDecimal(this SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0 : reader.GetDecimal(ordinal);
    }

    public static long GetInt64(this SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt64(reader.GetValue(ordinal));
    }
}
