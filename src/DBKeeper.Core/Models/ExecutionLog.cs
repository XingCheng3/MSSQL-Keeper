namespace DBKeeper.Core.Models;

public class ExecutionLog
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;   // SCHEDULED / MANUAL / SYSTEM
    public string StartedAt { get; set; } = string.Empty;
    public string? FinishedAt { get; set; }
    public int? DurationMs { get; set; }
    public string Status { get; set; } = string.Empty;        // RUNNING / SUCCESS / FAILED / WARNING
    public string? Summary { get; set; }
    public string? ErrorDetail { get; set; }
}
