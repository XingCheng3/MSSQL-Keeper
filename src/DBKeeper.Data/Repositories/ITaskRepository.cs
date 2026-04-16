using DBKeeper.Core.Models;

namespace DBKeeper.Data.Repositories;

public interface ITaskRepository
{
    Task<List<TaskItem>> GetAllAsync();
    Task<List<TaskItem>> GetEnabledAsync();
    Task<TaskItem?> GetByIdAsync(int id);
    Task<int> InsertAsync(TaskItem task);
    Task UpdateAsync(TaskItem task);
    Task DeleteAsync(int id);
    Task UpdateLastRunAsync(int id, string status, string? nextRunAt);
    Task<int> CountByConnectionIdAsync(int connectionId);
}
