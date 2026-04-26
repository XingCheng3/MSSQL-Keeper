using DBKeeper.Core.Helpers;
using DBKeeper.Core.Models;
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
        bool useCompression, int timeoutSec = 600, string backupType = "FULL")
    {
        var sql = backupType.ToUpper() switch
        {
            "DIFF" => $"BACKUP DATABASE [{database}] TO DISK = @path WITH DIFFERENTIAL, INIT",
            "LOG"  => $"BACKUP LOG [{database}] TO DISK = @path WITH INIT",
            _      => $"BACKUP DATABASE [{database}] TO DISK = @path WITH INIT"
        };
        if (useCompression && backupType.ToUpper() != "LOG") sql += ", COMPRESSION";

        await using var sqlConn = new SqlConnection(BuildConnectionString(conn, "master"));
        await sqlConn.OpenAsync();
        await using var cmd = new SqlCommand(sql, sqlConn) { CommandTimeout = timeoutSec };
        cmd.Parameters.AddWithValue("@path", backupPath);

        // BACKUP 通过 InfoMessage 报告进度
        sqlConn.InfoMessage += (_, e) => Log.Debug("SQL Server 备份消息: {Message}", e.Message);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>执行 RESTORE VERIFYONLY 校验备份文件</summary>
    public static async Task ExecuteVerifyAsync(Connection conn, string backupPath, int timeoutSec = 300)
    {
        await using var sqlConn = new SqlConnection(BuildConnectionString(conn, "master"));
        await sqlConn.OpenAsync();
        await using var cmd = new SqlCommand("RESTORE VERIFYONLY FROM DISK = @path", sqlConn)
            { CommandTimeout = timeoutSec };
        cmd.Parameters.AddWithValue("@path", backupPath);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>执行存储过程</summary>
    public static async Task<string?> ExecuteProcedureAsync(Connection conn, string database,
        string procedureName, Dictionary<string, string>? parameters = null, int timeoutSec = 300)
    {
        await using var sqlConn = new SqlConnection(BuildConnectionString(conn, database));
        await sqlConn.OpenAsync();
        await using var cmd = new SqlCommand(procedureName, sqlConn)
        {
            CommandType = System.Data.CommandType.StoredProcedure,
            CommandTimeout = timeoutSec
        };

        if (parameters != null)
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value);

        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        return $"存储过程执行完成，影响 {rowsAffected} 行";
    }

    /// <summary>执行自定义 SQL</summary>
    public static async Task<string?> ExecuteSqlAsync(Connection conn, string database,
        string sql, int timeoutSec = 600)
    {
        await using var sqlConn = new SqlConnection(BuildConnectionString(conn, database));
        await sqlConn.OpenAsync();
        await using var cmd = new SqlCommand(sql, sqlConn) { CommandTimeout = timeoutSec };
        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        return $"SQL 执行完成，影响 {rowsAffected} 行";
    }

    /// <summary>获取数据库大小（MB），用于备份前空间预检</summary>
    public static async Task<long> GetDatabaseSizeMbAsync(Connection conn, string database)
    {
        await using var sqlConn = new SqlConnection(BuildConnectionString(conn, database));
        await sqlConn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT SUM(size) * 8 / 1024 FROM sys.database_files", sqlConn);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull ? 0 : Convert.ToInt64(result);
    }
}
