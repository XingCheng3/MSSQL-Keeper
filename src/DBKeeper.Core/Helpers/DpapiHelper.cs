using System.Security.Cryptography;
using System.Text;

namespace DBKeeper.Core.Helpers;

/// <summary>
/// 使用 Windows DPAPI 进行密码加密/解密，绑定当前用户
/// </summary>
public static class DpapiHelper
{
    private const string Prefix = "DPAPI:";

    public static string Encrypt(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(encrypted);
    }

    public static string Decrypt(string encrypted)
    {
        // 兼容旧数据：无前缀的纯 Base64 字符串
        var data = encrypted.StartsWith(Prefix, StringComparison.Ordinal)
            ? encrypted[Prefix.Length..]
            : encrypted;
        var bytes = Convert.FromBase64String(data);
        var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }
}
