namespace DBKeeper.Core.Models;

public class SqlConfig
{
    public string DatabaseName { get; set; } = string.Empty;
    public string SqlContent { get; set; } = string.Empty;
    public int TimeoutSec { get; set; } = 600;
}
