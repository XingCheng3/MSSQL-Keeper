namespace DBKeeper.Core.Models;

/// <summary>
/// 数据库存储空间分析结果
/// </summary>
public class StorageAnalysisResult
{
    public StorageOverview Overview { get; set; } = new();
    public List<StorageFileUsage> Files { get; set; } = [];
    public List<TableSpaceUsage> Tables { get; set; } = [];
    public List<IndexSpaceUsage> Indexes { get; set; } = [];
}

/// <summary>
/// 数据库空间总览
/// </summary>
public class StorageOverview
{
    public string DatabaseName { get; set; } = string.Empty;
    public decimal TotalMb { get; set; }
    public decimal DataMb { get; set; }
    public decimal LogMb { get; set; }
    public decimal UsedMb { get; set; }
    public decimal UnusedMb { get; set; }
    public int TableCount { get; set; }
    public int IndexCount { get; set; }
}

/// <summary>
/// 数据库文件空间占用
/// </summary>
public class StorageFileUsage
{
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string? FileGroup { get; set; }
    public string PhysicalName { get; set; } = string.Empty;
    public decimal SizeMb { get; set; }
    public decimal UsedMb { get; set; }
    public decimal FreeMb { get; set; }
    public string Growth { get; set; } = string.Empty;
}

/// <summary>
/// 表空间占用排行
/// </summary>
public class TableSpaceUsage
{
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public decimal TotalMb { get; set; }
    public decimal DataMb { get; set; }
    public decimal IndexMb { get; set; }
    public decimal UnusedMb { get; set; }
    public decimal PercentOfDatabase { get; set; }
}

/// <summary>
/// 索引空间占用排行
/// </summary>
public class IndexSpaceUsage
{
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string IndexName { get; set; } = string.Empty;
    public string IndexType { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public decimal SizeMb { get; set; }
}
