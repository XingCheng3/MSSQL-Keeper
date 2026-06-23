using System.Text;

namespace DBKeeper.Core.Helpers;

/// <summary>
/// 按 SSMS 常见 GO 规则拆分 SQL 批次。
/// </summary>
public static class SqlBatchSplitter
{
    public static List<string> SplitBatches(string sql)
    {
        var batches = new List<string>();
        if (string.IsNullOrWhiteSpace(sql))
            return batches;

        var current = new StringBuilder();
        using var reader = new StringReader(sql.Replace("\r\n", "\n"));
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (IsGoLine(line))
            {
                FlushCurrentBatch(batches, current);
                continue;
            }

            current.AppendLine(line);
        }

        FlushCurrentBatch(batches, current);
        return batches;
    }

    private static bool IsGoLine(string line)
    {
        return string.Equals(line.Trim(), "GO", StringComparison.OrdinalIgnoreCase);
    }

    private static void FlushCurrentBatch(List<string> batches, StringBuilder current)
    {
        var batch = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(batch))
            batches.Add(batch);
        current.Clear();
    }
}
