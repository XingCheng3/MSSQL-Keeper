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
        ".trn",
        ".zip",
        ".7z"
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

    public static bool IsAllowedBackupPath(string path, IEnumerable<string> allowedDirectories)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (File.Exists(path))
            return IsAllowedBackupFile(path, allowedDirectories);

        if (Directory.Exists(path))
            return IsAllowedBackupDirectory(path, allowedDirectories);

        var extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension)
            ? IsAllowedBackupFile(path, allowedDirectories)
            : IsAllowedBackupDirectory(path, allowedDirectories);
    }

    public static bool IsAllowedBackupDirectory(string directoryPath, IEnumerable<string> allowedDirectories)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return false;

        var directories = allowedDirectories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (directories.Count == 0)
            return false;

        try
        {
            var fullDirectoryPath = Path.GetFullPath(directoryPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (IsRootDirectory(fullDirectoryPath))
                return false;

            return directories.Any(directory =>
            {
                var fullAllowedDirectory = Path.GetFullPath(directory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(fullDirectoryPath, fullAllowedDirectory, StringComparison.OrdinalIgnoreCase)
                    || IsPathUnderDirectory(fullDirectoryPath, fullAllowedDirectory);
            });
        }
        catch
        {
            return false;
        }
    }

    public static bool IsRootDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return true;

        try
        {
            var fullPath = Path.GetFullPath(directoryPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetPathRoot(fullPath)?
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
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
