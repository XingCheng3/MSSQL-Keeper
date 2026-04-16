using DBKeeper.Core.Models;

namespace DBKeeper.Data.Repositories;

public interface IBackupFileRepository
{
    Task<List<BackupFile>> GetAllAsync();
    Task<List<BackupFile>> GetByTaskIdAsync(int taskId);
    Task<int> InsertAsync(BackupFile file);
    Task UpdateStatusAsync(int id, string status, string? deletedAt = null);
    Task SetPinnedAsync(int id, bool pinned);
    Task DeleteAsync(int id);
    Task<List<BackupFile>> GetActiveByDirAsync(string directory);
    Task<List<BackupFile>> GetAllActiveAsync();
}
