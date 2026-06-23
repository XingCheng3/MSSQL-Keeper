using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Dapper;
using DBKeeper.Core.Models;
using DBKeeper.Data;
using DBKeeper.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace DBKeeper.App.Services;

/// <summary>
/// 负责配置导入导出，保证 JSON 契约稳定且导入具备事务性。
/// </summary>
public class ConfigurationTransferService
{
    private static readonly HashSet<string> BooleanSettingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "minimize_to_tray_on_close",
        "auto_start"
    };

    private static readonly HashSet<string> IntegerSettingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "log_retention_days",
        "max_concurrent_tasks",
        "disk_warn_threshold",
        "disk_danger_threshold",
        "disk_check_interval_min",
        "heartbeat_interval_sec",
        "backup_scan_interval_min"
    };

    private readonly DbInitializer _dbInitializer;
    private readonly IConnectionRepository _connectionRepository;
    private readonly ITaskRepository _taskRepository;
    private readonly ISettingsRepository _settingsRepository;

    public ConfigurationTransferService(
        DbInitializer dbInitializer,
        IConnectionRepository connectionRepository,
        ITaskRepository taskRepository,
        ISettingsRepository settingsRepository)
    {
        _dbInitializer = dbInitializer;
        _connectionRepository = connectionRepository;
        _taskRepository = taskRepository;
        _settingsRepository = settingsRepository;
    }

    public async Task<string> ExportAsync()
    {
        var connections = await _connectionRepository.GetAllAsync();
        var tasks = await _taskRepository.GetAllAsync();
        var settings = await _settingsRepository.GetAllAsync();
        var connectionNameMap = connections.ToDictionary(c => c.Id, c => c.Name);

        var dto = new ConfigurationExportDto
        {
            Version = "1.1",
            ExportedAt = DateTime.Now.ToString("O"),
            Connections = connections.Select(connection => new ExportConnectionDto
            {
                Name = connection.Name,
                Host = connection.Host,
                Username = connection.Username,
                Password = string.Empty,
                DefaultDb = connection.DefaultDb,
                TimeoutSec = connection.TimeoutSec,
                TrustServerCertificate = connection.TrustServerCertificate,
                IsDefault = connection.IsDefault,
                Remark = connection.Remark
            }).ToList(),
            Tasks = tasks.Select(task => new ExportTaskDto
            {
                Name = task.Name,
                TaskType = task.TaskType,
                ConnectionName = task.ConnectionId.HasValue && connectionNameMap.TryGetValue(task.ConnectionId.Value, out var connectionName)
                    ? connectionName
                    : string.Empty,
                IsEnabled = task.IsEnabled,
                ScheduleType = task.ScheduleType,
                ScheduleConfig = ParseJsonNode(task.ScheduleConfig),
                TaskConfig = ParseJsonNode(task.TaskConfig)
            }).ToList(),
            Settings = settings.ToDictionary(setting => setting.Key, setting => ConvertSettingValueForExport(setting.Key, setting.Value))
        };

        return JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    public async Task<ConfigurationImportResult> ImportAsync(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!TryGetPropertyLoose(root, out _, "version"))
            throw new InvalidOperationException("无效的配置文件格式：缺少 version 字段。");

        var result = new ConfigurationImportResult();
        var importedTasks = new List<ImportedTaskInfo>();

        await using var connection = new SqliteConnection(_dbInitializer.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var existingConnections = (await connection.QueryAsync<Connection>(
                "SELECT * FROM connections", transaction: transaction))
                .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

            if (TryGetPropertyLoose(root, out var connectionsElement, "connections"))
            {
                foreach (var connectionElement in connectionsElement.EnumerateArray())
                {
                    var name = ReadRequiredString(connectionElement, "name");
                    var host = ReadRequiredString(connectionElement, "host");
                    var username = ReadRequiredString(connectionElement, "username");
                    var defaultDb = ReadOptionalString(connectionElement, "default_db", "defaultDb");
                    var timeoutSec = ReadInt32(connectionElement, 30, "timeout_sec", "timeoutSec");
                    var trustServerCertificate = ReadBoolean(connectionElement, true, "trust_server_certificate", "trustServerCertificate");
                    var isDefault = ReadBoolean(connectionElement, false, "is_default", "isDefault");
                    var remark = ReadOptionalString(connectionElement, "remark");

                    if (existingConnections.TryGetValue(name, out var existingConnection))
                    {
                        await connection.ExecuteAsync("""
                            UPDATE connections
                            SET host = @Host,
                                username = @Username,
                                default_db = @DefaultDb,
                                timeout_sec = @TimeoutSec,
                                trust_server_certificate = @TrustServerCertificate,
                                is_default = @IsDefault,
                                remark = @Remark,
                                updated_at = @UpdatedAt
                            WHERE id = @Id
                            """, new
                        {
                            Id = existingConnection.Id,
                            Host = host,
                            Username = username,
                            DefaultDb = defaultDb,
                            TimeoutSec = timeoutSec,
                            TrustServerCertificate = trustServerCertificate,
                            IsDefault = isDefault,
                            Remark = remark,
                            UpdatedAt = DateTime.Now.ToString("O")
                        }, transaction);
                        result.UpdatedConnections++;
                    }
                    else
                    {
                        var newConnectionId = await connection.ExecuteScalarAsync<long>("""
                            INSERT INTO connections (name, host, username, password, default_db, timeout_sec, trust_server_certificate, is_default, remark, created_at, updated_at)
                            VALUES (@Name, @Host, @Username, @Password, @DefaultDb, @TimeoutSec, @TrustServerCertificate, @IsDefault, @Remark, @Now, @Now);
                            SELECT last_insert_rowid();
                            """, new
                        {
                            Name = name,
                            Host = host,
                            Username = username,
                            Password = string.Empty,
                            DefaultDb = defaultDb,
                            TimeoutSec = timeoutSec,
                            TrustServerCertificate = trustServerCertificate,
                            IsDefault = isDefault,
                            Remark = remark,
                            Now = DateTime.Now.ToString("O")
                        }, transaction);
                        existingConnection = new Connection
                        {
                            Id = (int)newConnectionId,
                            Name = name,
                            Host = host,
                            Username = username,
                            Password = string.Empty,
                            DefaultDb = defaultDb,
                            TimeoutSec = timeoutSec,
                            TrustServerCertificate = trustServerCertificate,
                            IsDefault = isDefault,
                            Remark = remark
                        };
                        existingConnections[name] = existingConnection;
                        result.AddedConnections++;
                        result.ConnectionsRequiringPassword.Add(name);
                    }
                }
            }

            var existingTasks = (await connection.QueryAsync<TaskItem>(
                "SELECT * FROM tasks", transaction: transaction))
                .ToList();

            if (TryGetPropertyLoose(root, out var tasksElement, "tasks"))
            {
                foreach (var taskElement in tasksElement.EnumerateArray())
                {
                    var taskName = ReadRequiredString(taskElement, "name");
                    var connectionName = ReadOptionalString(taskElement, "connection_name", "connectionName") ?? string.Empty;
                    if (!existingConnections.TryGetValue(connectionName, out var matchedConnection))
                    {
                        result.SkippedTasks.Add($"{taskName}（未找到连接：{connectionName}）");
                        continue;
                    }

                    var taskType = ReadRequiredString(taskElement, "task_type", "taskType");
                    var isEnabled = ReadBoolean(taskElement, true, "is_enabled", "isEnabled");
                    var scheduleType = ReadRequiredString(taskElement, "schedule_type", "scheduleType");
                    var scheduleConfig = NormalizeJsonPayload(taskElement, "schedule_config", "scheduleConfig");
                    var taskConfig = NormalizeJsonPayload(taskElement, "task_config", "taskConfig");

                    var existingTask = existingTasks.FirstOrDefault(task =>
                        string.Equals(task.Name, taskName, StringComparison.OrdinalIgnoreCase)
                        && task.ConnectionId == matchedConnection.Id);
                    if (existingTask != null)
                    {
                        await connection.ExecuteAsync("""
                            UPDATE tasks
                            SET task_type = @TaskType,
                                connection_id = @ConnectionId,
                                is_enabled = @IsEnabled,
                                schedule_type = @ScheduleType,
                                schedule_config = @ScheduleConfig,
                                task_config = @TaskConfig,
                                updated_at = @UpdatedAt
                            WHERE id = @Id
                            """, new
                        {
                            Id = existingTask.Id,
                            TaskType = taskType,
                            ConnectionId = matchedConnection.Id,
                            IsEnabled = isEnabled,
                            ScheduleType = scheduleType,
                            ScheduleConfig = scheduleConfig,
                            TaskConfig = taskConfig,
                            UpdatedAt = DateTime.Now.ToString("O")
                        }, transaction);
                        existingTask.TaskType = taskType;
                        existingTask.ConnectionId = matchedConnection.Id;
                        existingTask.IsEnabled = isEnabled;
                        existingTask.ScheduleType = scheduleType;
                        existingTask.ScheduleConfig = scheduleConfig;
                        existingTask.TaskConfig = taskConfig;
                        result.UpdatedTasks++;
                        importedTasks.Add(new ImportedTaskInfo(existingTask.Id, isEnabled));
                    }
                    else
                    {
                        var newTaskId = await connection.ExecuteScalarAsync<long>("""
                            INSERT INTO tasks (name, task_type, connection_id, is_enabled, schedule_type, schedule_config, task_config, created_at, updated_at)
                            VALUES (@Name, @TaskType, @ConnectionId, @IsEnabled, @ScheduleType, @ScheduleConfig, @TaskConfig, @Now, @Now);
                            SELECT last_insert_rowid();
                            """, new
                        {
                            Name = taskName,
                            TaskType = taskType,
                            ConnectionId = matchedConnection.Id,
                            IsEnabled = isEnabled,
                            ScheduleType = scheduleType,
                            ScheduleConfig = scheduleConfig,
                            TaskConfig = taskConfig,
                            Now = DateTime.Now.ToString("O")
                        }, transaction);
                        result.AddedTasks++;
                        importedTasks.Add(new ImportedTaskInfo((int)newTaskId, isEnabled));
                    }
                }
            }

            if (TryGetPropertyLoose(root, out var settingsElement, "settings"))
            {
                foreach (var setting in settingsElement.EnumerateObject())
                {
                    var value = ConvertJsonValueToSettingString(setting.Name, setting.Value);
                    await connection.ExecuteAsync("""
                        INSERT INTO settings (key, value, updated_at) VALUES (@Key, @Value, @UpdatedAt)
                        ON CONFLICT(key) DO UPDATE SET value = @Value, updated_at = @UpdatedAt
                        """, new
                    {
                        Key = setting.Name,
                        Value = value,
                        UpdatedAt = DateTime.Now.ToString("O")
                    }, transaction);
                    result.UpdatedSettings++;
                }
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        result.ImportedTasks = importedTasks;
        return result;
    }

    private static JsonNode? ParseJsonNode(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonNode.Parse(json);
        }
        catch
        {
            return JsonValue.Create(json);
        }
    }

    private static object? ConvertSettingValueForExport(string key, string value)
    {
        if (BooleanSettingKeys.Contains(key) && bool.TryParse(value, out var boolValue))
            return boolValue;

        if (IntegerSettingKeys.Contains(key) && int.TryParse(value, out var intValue))
            return intValue;

        return value;
    }

    private static string ConvertJsonValueToSettingString(string key, JsonElement value)
    {
        if (BooleanSettingKeys.Contains(key))
        {
            if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                return value.GetBoolean() ? "true" : "false";
            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var boolValue))
                return boolValue ? "true" : "false";
        }

        if (IntegerSettingKeys.Contains(key))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
                return intValue.ToString();
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out intValue))
                return intValue.ToString();
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            _ => value.GetRawText()
        };
    }

    private static string NormalizeJsonPayload(JsonElement element, params string[] names)
    {
        if (!TryGetPropertyLoose(element, out var value, names))
            throw new InvalidOperationException($"缺少字段：{names[0]}");

        return value.ValueKind switch
        {
            JsonValueKind.String => NormalizeJsonString(value.GetString()),
            JsonValueKind.Object or JsonValueKind.Array => NormalizeJsonString(value.GetRawText()),
            _ => throw new InvalidOperationException($"字段 {names[0]} 必须是 JSON 对象、数组或字符串。")
        };
    }

    private static string NormalizeJsonString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "{}";

        using var document = JsonDocument.Parse(value);
        return document.RootElement.GetRawText();
    }

    private static string ReadRequiredString(JsonElement element, params string[] names)
    {
        if (!TryGetPropertyLoose(element, out var value, names))
            throw new InvalidOperationException($"缺少字段：{names[0]}");

        var result = value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();
        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException($"字段 {names[0]} 不能为空。");
        return result;
    }

    private static string? ReadOptionalString(JsonElement element, params string[] names)
    {
        if (!TryGetPropertyLoose(element, out var value, names))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => value.GetRawText()
        };
    }

    private static bool ReadBoolean(JsonElement element, bool defaultValue, params string[] names)
    {
        if (!TryGetPropertyLoose(element, out var value, names))
            return defaultValue;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => defaultValue
        };
    }

    private static int ReadInt32(JsonElement element, int defaultValue, params string[] names)
    {
        if (!TryGetPropertyLoose(element, out var value, names))
            return defaultValue;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numberValue))
            return numberValue;

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var stringValue))
            return stringValue;

        return defaultValue;
    }

    private static bool TryGetPropertyLoose(JsonElement element, out JsonElement value, params string[] candidateNames)
    {
        foreach (var property in element.EnumerateObject())
        {
            foreach (var candidateName in candidateNames)
            {
                if (NormalizePropertyName(property.Name) == NormalizePropertyName(candidateName))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string NormalizePropertyName(string name)
    {
        return name.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
    }

    public sealed class ConfigurationImportResult
    {
        public int AddedConnections { get; set; }
        public int UpdatedConnections { get; set; }
        public int AddedTasks { get; set; }
        public int UpdatedTasks { get; set; }
        public int UpdatedSettings { get; set; }
        public List<string> ConnectionsRequiringPassword { get; } = [];
        public List<string> SkippedTasks { get; } = [];
        public List<ImportedTaskInfo> ImportedTasks { get; set; } = [];
    }

    public sealed record ImportedTaskInfo(int TaskId, bool IsEnabled);

    private sealed class ConfigurationExportDto
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.1";

        [JsonPropertyName("exported_at")]
        public string ExportedAt { get; set; } = string.Empty;

        [JsonPropertyName("connections")]
        public List<ExportConnectionDto> Connections { get; set; } = [];

        [JsonPropertyName("tasks")]
        public List<ExportTaskDto> Tasks { get; set; } = [];

        [JsonPropertyName("settings")]
        public Dictionary<string, object?> Settings { get; set; } = [];
    }

    private sealed class ExportConnectionDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("host")]
        public string Host { get; set; } = string.Empty;

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        [JsonPropertyName("default_db")]
        public string? DefaultDb { get; set; }

        [JsonPropertyName("timeout_sec")]
        public int TimeoutSec { get; set; }

        [JsonPropertyName("trust_server_certificate")]
        public bool TrustServerCertificate { get; set; }

        [JsonPropertyName("is_default")]
        public bool IsDefault { get; set; }

        [JsonPropertyName("remark")]
        public string? Remark { get; set; }
    }

    private sealed class ExportTaskDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("task_type")]
        public string TaskType { get; set; } = string.Empty;

        [JsonPropertyName("connection_name")]
        public string ConnectionName { get; set; } = string.Empty;

        [JsonPropertyName("is_enabled")]
        public bool IsEnabled { get; set; }

        [JsonPropertyName("schedule_type")]
        public string ScheduleType { get; set; } = string.Empty;

        [JsonPropertyName("schedule_config")]
        public JsonNode? ScheduleConfig { get; set; }

        [JsonPropertyName("task_config")]
        public JsonNode? TaskConfig { get; set; }
    }
}
