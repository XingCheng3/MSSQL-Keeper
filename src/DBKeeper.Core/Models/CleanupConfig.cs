namespace DBKeeper.Core.Models;

public class CleanupConfig
{
    public string TargetDir { get; set; } = string.Empty;
    public int RetentionDays { get; set; } = 30;
    public int MinKeepCount { get; set; } = 3;
}
