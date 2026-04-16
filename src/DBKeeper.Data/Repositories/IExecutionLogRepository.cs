using DBKeeper.Core.Models;

namespace DBKeeper.Data.Repositories;

public interface IExecutionLogRepository
{
    Task<int> InsertAsync(ExecutionLog log);
    Task UpdateFinishAsync(int id, string status, int durationMs, string? summary, string? errorDetail);
    Task<(List<ExecutionLog> Items, int Total)> GetPagedAsync(int page, int pageSize, string? taskName = null, string? status = null, string? startFrom = null, string? startTo = null);
    Task<List<ExecutionLog>> GetRecentAsync(int count);
    Task CleanupAsync(int retentionDays);
    Task ClearAllAsync();
    Task<int> CountTodayAsync();
    Task<int> CountRecent24hFailedAsync();
}
