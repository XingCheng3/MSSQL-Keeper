namespace DBKeeper.Core.Helpers;

/// <summary>
/// 备份文件路径校验，避免误删备份目录外的文件。
/// </summary>
public static class BackupPathGuard
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bak",
        ".diff",
        ".trn"
    };

    public static bool IsAllowedBackupFile(string filePath, IEnumerable<string> allowedDirectories)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        if (!IsAllowedExtension(filePath))
            return false;

        var directories = allowedDirectories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (directories.Count == 0)
            return false;

        try
        {
            var fullFilePath = Path.GetFullPath(filePath);
            return directories.Any(directory => IsPathUnderDirectory(fullFilePath, directory));
        }
        catch
        {
            return false;
        }
    }

    public static bool IsAllowedExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return AllowedExtensions.Contains(extension);
    }

    public static bool IsPathUnderDirectory(string filePath, string directory)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(directory))
            return false;

        try
        {
            var fullFilePath = Path.GetFullPath(filePath);
            var fullDirectory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return fullFilePath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
