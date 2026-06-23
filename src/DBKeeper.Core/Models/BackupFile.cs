namespace DBKeeper.Core.Models;

public class BackupFile
{
    public int Id { get; set; }
    public int? TaskId { get; set; }
    public int? ExecutionLogId { get; set; }
    public string SourceType { get; set; } = "DATABASE";
    public string SourceName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long? FileSizeBytes { get; set; }
    public string? BackupType { get; set; }     // FULL / DIFF / LOG
    public string CreatedAt { get; set; } = string.Empty;
    public string? ExpiresAt { get; set; }
    public bool IsPinned { get; set; }
    public bool IsVerified { get; set; }
    public string Status { get; set; } = "NORMAL"; // NORMAL / SIZE_ANOMALY / EXPIRED / DELETED
    public string? DeletedAt { get; set; }

    public string SourceTypeDisplay => SourceType switch
    {
        "DIRECTORY" => "目录",
        "DATABASE" => "数据库",
        _ => SourceType
    };

    public string SourceDisplay => !string.IsNullOrWhiteSpace(SourceName) ? SourceName : DatabaseName;
}
