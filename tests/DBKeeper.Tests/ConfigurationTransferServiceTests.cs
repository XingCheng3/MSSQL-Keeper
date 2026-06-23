using System.Text.Json;
using DBKeeper.App.Services;
using DBKeeper.Core.Models;
using DBKeeper.Tests.TestSupport;

namespace DBKeeper.Tests;

public class ConfigurationTransferServiceTests
{
    [Fact]
    public async Task ExportAndImportAsync_ShouldRoundTripCurrentContract()
    {
        using var source = new TestWorkspace();
        await source.SettingsRepository.SetAsync("max_concurrent_tasks", "5");
        await source.SettingsRepository.SetAsync("default_backup_dir", @"D:\Backup");

        var connectionId = await source.ConnectionRepository.InsertAsync(new Connection
        {
            Name = "MES 主库",
            Host = "127.0.0.1,1433",
            Username = "sa",
            Password = "DPAPI:test",
            DefaultDb = "MES_DB",
            TimeoutSec = 30,
            TrustServerCertificate = true,
            IsDefault = true,
            Remark = "测试连接"
        });

        await source.TaskRepository.InsertAsync(new TaskItem
        {
            Name = "每日备份",
            TaskType = "BACKUP",
            ConnectionId = connectionId,
            IsEnabled = true,
            ScheduleType = "DAILY",
            ScheduleConfig = """{"time":"02:00"}""",
            TaskConfig = """{"DatabaseName":"MES_DB","BackupType":"FULL","BackupDir":"D:\\Backup","RetentionDays":30,"MinKeepCount":3,"UseCompression":true,"VerifyAfterBackup":false,"TimeoutSec":600}""",
            CreatedAt = DateTime.Now.ToString("O"),
            UpdatedAt = DateTime.Now.ToString("O")
        });

        var exporter = new ConfigurationTransferService(
            source.DbInitializer,
            source.ConnectionRepository,
            source.TaskRepository,
            source.SettingsRepository);
        var json = await exporter.ExportAsync();

        using var target = new TestWorkspace();
        var importer = new ConfigurationTransferService(
            target.DbInitializer,
            target.ConnectionRepository,
            target.TaskRepository,
            target.SettingsRepository);
        var result = await importer.ImportAsync(json);

        Assert.Equal(1, result.AddedConnections);
        Assert.Equal(1, result.AddedTasks);
        Assert.Empty(result.SkippedTasks);

        var importedConnections = await target.ConnectionRepository.GetAllAsync();
        var importedTasks = await target.TaskRepository.GetAllAsync();
        Assert.Single(importedConnections);
        Assert.Single(importedTasks);
        Assert.Equal(string.Empty, importedConnections[0].Password);
        Assert.Equal("5", await target.SettingsRepository.GetAsync("max_concurrent_tasks"));
        Assert.Equal(@"D:\Backup", await target.SettingsRepository.GetAsync("default_backup_dir"));
        Assert.Equal(
            """{"time":"02:00"}""",
            NormalizeJson(importedTasks[0].ScheduleConfig));
        Assert.Equal(
            """{"DatabaseName":"MES_DB","BackupType":"FULL","BackupDir":"D:\\Backup","RetentionDays":30,"MinKeepCount":3,"UseCompression":true,"VerifyAfterBackup":false,"TimeoutSec":600}""",
            NormalizeJson(importedTasks[0].TaskConfig));
    }

    [Fact]
    public async Task ImportAsync_ShouldAcceptLegacyPropertyNamesAndTypedSettings()
    {
        const string legacyJson = """
            {
              "version": "1.1",
              "connections": [
                {
                  "Name": "LegacyConn",
                  "Host": "127.0.0.1",
                  "Username": "sa",
                  "default-db": "MES_DB",
                  "timeout_sec": 45,
                  "trustServerCertificate": false,
                  "is_default": true
                }
              ],
              "tasks": [
                {
                  "name": "历史备份任务",
                  "taskType": "BACKUP",
                  "connection_name": "LegacyConn",
                  "isEnabled": true,
                  "schedule-config": {
                    "time": "03:30"
                  },
                  "schedule_type": "DAILY",
                  "task_config": "{\"database_name\":\"MES_DB\",\"backup_dir\":\"D:\\\\LegacyBackup\",\"retention_days\":15,\"use_compression\":true,\"verify_after_backup\":false,\"backup_type\":\"FULL\"}"
                }
              ],
              "settings": {
                "max_concurrent_tasks": 7,
                "auto_start": true
              }
            }
            """;

        using var target = new TestWorkspace();
        var importer = new ConfigurationTransferService(
            target.DbInitializer,
            target.ConnectionRepository,
            target.TaskRepository,
            target.SettingsRepository);

        var result = await importer.ImportAsync(legacyJson);

        Assert.Equal(1, result.AddedConnections);
        Assert.Equal(1, result.AddedTasks);
        Assert.Equal("7", await target.SettingsRepository.GetAsync("max_concurrent_tasks"));
        Assert.Equal("true", await target.SettingsRepository.GetAsync("auto_start"));

        var importedTasks = await target.TaskRepository.GetAllAsync();
        Assert.Single(importedTasks);
        Assert.Equal("""{"time":"03:30"}""", NormalizeJson(importedTasks[0].ScheduleConfig));
        Assert.Equal(
            """{"database_name":"MES_DB","backup_dir":"D:\\LegacyBackup","retention_days":15,"use_compression":true,"verify_after_backup":false,"backup_type":"FULL"}""",
            NormalizeJson(importedTasks[0].TaskConfig));
    }

    private static string NormalizeJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }
}
