namespace DBKeeper.Core.Models;

public class Connection
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    /// <summary>DPAPI 加密后的密码（Base64）</summary>
    public string Password { get; set; } = string.Empty;
    public string? DefaultDb { get; set; }
    public int TimeoutSec { get; set; } = 30;
    /// <summary>是否信任服务器证书，默认 true（兼容内网环境）</summary>
    public bool TrustServerCertificate { get; set; } = true;
    public bool IsDefault { get; set; }
    public string? Remark { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}
