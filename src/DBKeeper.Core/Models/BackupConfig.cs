namespace DBKeeper.Core.Models;

/// <summary>备份任务配置 JSON 结构</summary>
public class BackupConfig
{
    public string DatabaseName { get; set; } = string.Empty;
    public string BackupType { get; set; } = "FULL";
    public string BackupDir { get; set; } = string.Empty;
    public string? FileNameTemplate { get; set; }
    public int RetentionDays { get; set; } = 30;
    public int MinKeepCount { get; set; } = 3;
    public bool UseCompression { get; set; } = true;
    public bool VerifyAfterBackup { get; set; }
    public int TimeoutSec { get; set; } = 600;
}
