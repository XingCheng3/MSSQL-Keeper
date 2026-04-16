using DBKeeper.Core.Models;

namespace DBKeeper.Data.Repositories;

public interface ISettingsRepository
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task<List<AppSetting>> GetAllAsync();
}
