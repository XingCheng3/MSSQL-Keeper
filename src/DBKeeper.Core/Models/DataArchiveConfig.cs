namespace DBKeeper.Core.Models;

/// <summary>数据归档任务配置 JSON 结构</summary>
public class DataArchiveConfig
{
    public string SourceDatabase { get; set; } = string.Empty;
    public string SourceSchema { get; set; } = "dbo";
    public string SourceTable { get; set; } = string.Empty;
    public string TargetDatabase { get; set; } = string.Empty;
    public string TargetSchema { get; set; } = "dbo";
    public string TargetTable { get; set; } = string.Empty;
    public string DateColumn { get; set; } = string.Empty;
    public string PrimaryKeyColumn { get; set; } = string.Empty;
    public string RetentionType { get; set; } = "MONTH"; // DAY / MONTH
    public int RetentionValue { get; set; } = 3;
    public int BatchSize { get; set; } = 5000;
    public int MaxRowsPerRun { get; set; } = 50000;
    public bool DeleteAfterCopy { get; set; } = true;
    public bool SkipExistingRows { get; set; } = true;
    public int TimeoutSec { get; set; } = 1800;
}

public class DataArchiveResult
{
    public int CandidateRows { get; set; }
    public int InsertedRows { get; set; }
    public int SkippedRows { get; set; }
    public int DeletedRows { get; set; }
    public int BatchCount { get; set; }
    public DateTime Cutoff { get; set; }

    public string ToSummary()
    {
        return $"归档完成：截止 {Cutoff:yyyy-MM-dd HH:mm:ss}，批次 {BatchCount}，候选 {CandidateRows} 行，插入 {InsertedRows} 行，跳过 {SkippedRows} 行，删除 {DeletedRows} 行";
    }
}
