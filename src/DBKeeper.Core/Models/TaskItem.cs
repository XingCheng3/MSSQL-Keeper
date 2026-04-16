namespace DBKeeper.Core.Models;

public class TaskItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;       // BACKUP / PROCEDURE / CUSTOM_SQL / BACKUP_CLEANUP
    public int ConnectionId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string ScheduleType { get; set; } = string.Empty;   // DAILY / WEEKLY / MONTHLY / INTERVAL / CRON
    public string ScheduleConfig { get; set; } = string.Empty;  // JSON
    public string TaskConfig { get; set; } = string.Empty;      // JSON
    public string? LastRunAt { get; set; }
    public string? LastRunStatus { get; set; }
    public string? NextRunAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}
