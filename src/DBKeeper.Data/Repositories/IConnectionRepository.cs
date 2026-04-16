using DBKeeper.Core.Models;

namespace DBKeeper.Data.Repositories;

public interface IConnectionRepository
{
    Task<List<Connection>> GetAllAsync();
    Task<Connection?> GetByIdAsync(int id);
    Task<int> InsertAsync(Connection conn);
    Task UpdateAsync(Connection conn);
    Task DeleteAsync(int id);
    Task SetDefaultAsync(int id);
}
