namespace DBKeeper.Core.Models;

/// <summary>目录同步任务配置 JSON 结构</summary>
public class DirectorySyncConfig
{
    public string SourceDir { get; set; } = string.Empty;
    public string TargetDir { get; set; } = string.Empty;
    public string SyncMode { get; set; } = "DIFF"; // DIFF / FULL / ARCHIVE
    public string ArchiveFormat { get; set; } = "ZIP"; // ZIP / 7Z
    public string CompressionLevel { get; set; } = "BALANCED"; // FAST / BALANCED / SMALLEST
    public string? FileNameTemplate { get; set; }
    public int RetentionDays { get; set; } = 30;
    public int MinKeepCount { get; set; } = 3;
    public bool IncludeSubdirectories { get; set; } = true;
    public bool OverwriteChangedFiles { get; set; } = true;
    public string? ExcludePatterns { get; set; }
    public int TimeoutSec { get; set; } = 1800;
}
