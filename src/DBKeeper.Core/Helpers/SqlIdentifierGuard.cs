using System.Text.RegularExpressions;

namespace DBKeeper.Core.Helpers;

public static partial class SqlIdentifierGuard
{
    public static void EnsureSimpleIdentifier(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{fieldName}不能为空。");

        if (!SimpleIdentifierRegex().IsMatch(value))
            throw new InvalidOperationException($"{fieldName}只能包含字母、数字、下划线，且不能以数字开头。");
    }

    public static string Quote(string identifier)
    {
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex SimpleIdentifierRegex();
}
