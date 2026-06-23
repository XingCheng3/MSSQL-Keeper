namespace DBKeeper.Core.Models;

/// <summary>组合备份向导参数</summary>
public class BackupPlanOptions
{
    public string PlanName { get; set; } = string.Empty;
    public int ConnectionId { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string BackupDir { get; set; } = string.Empty;
    public int FullBackupDayOfWeek { get; set; }
    public string BackupTime { get; set; } = "02:00";
    public int RetentionDays { get; set; } = 30;
    public int MinKeepCount { get; set; } = 3;
    public bool UseCompression { get; set; } = true;
    public bool VerifyAfterBackup { get; set; }
    public bool CreateCleanupTask { get; set; } = true;
    public string CleanupTime { get; set; } = "03:00";
}
