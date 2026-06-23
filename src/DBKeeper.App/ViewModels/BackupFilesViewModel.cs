using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBKeeper.App.Services;
using DBKeeper.Core.Helpers;
using DBKeeper.Core.Models;
using DBKeeper.Data.Repositories;
using Serilog;

namespace DBKeeper.App.ViewModels;

public partial class BackupFilesViewModel : ObservableObject
{
    private readonly IBackupFileRepository _repo;
    private readonly ITaskRepository _taskRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly BackupFileSyncService _syncService;

    public ObservableCollection<BackupFile> Files { get; } = [];
    public ObservableCollection<BackupFile> SelectedFiles { get; } = [];

    [ObservableProperty] private string? _filterDatabase;
    [ObservableProperty] private string? _filterStatus;
    [ObservableProperty] private DateTime? _filterDateFrom;
    [ObservableProperty] private DateTime? _filterDateTo;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string? _lastSyncSummary;

    public BackupFilesViewModel(
        IBackupFileRepository repo,
        ITaskRepository taskRepo,
        ISettingsRepository settingsRepo,
        BackupFileSyncService syncService)
    {
        _repo = repo;
        _taskRepo = taskRepo;
        _settingsRepo = settingsRepo;
        _syncService = syncService;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        var list = await _repo.GetAllAsync();
        Files.Clear();
        SelectedFiles.Clear();
        SelectedCount = 0;
        foreach (var f in list)
        {
            // 过滤
            if (!string.IsNullOrEmpty(FilterDatabase) && f.DatabaseName != FilterDatabase) continue;
            if (!string.IsNullOrEmpty(FilterStatus) && f.Status != FilterStatus) continue;
            if (FilterDateFrom.HasValue && DateTime.TryParse(f.CreatedAt, out var created) && created < FilterDateFrom.Value) continue;
            if (FilterDateTo.HasValue && DateTime.TryParse(f.CreatedAt, out var created2) && created2 > FilterDateTo.Value.AddDays(1)) continue;
            Files.Add(f);
        }

        // 大小异常检测
        await DetectSizeAnomaliesAsync();
    }

    [RelayCommand]
    public async Task SyncAndLoadAsync()
    {
        var result = await _syncService.ScanNowAsync();
        LastSyncSummary = result.Summary;
        await LoadAsync();
    }

    /// <summary>
    /// 检测大小异常：同一任务的连续备份文件，当前文件比上一份小50%以上则标记 SIZE_ANOMALY
    /// </summary>
    private async Task DetectSizeAnomaliesAsync()
    {
        var updates = new List<BackupFile>();
        var groups = Files
            .Where(f => f.Status is "NORMAL" or "SIZE_ANOMALY")
            .GroupBy(f => f.TaskId);

        foreach (var group in groups)
        {
            var sorted = group
                .OrderBy(f => f.CreatedAt)
                .ToList();

            for (int i = 1; i < sorted.Count; i++)
            {
                var prev = sorted[i - 1];
                var curr = sorted[i];

                if (prev.FileSizeBytes.HasValue && prev.FileSizeBytes.Value > 0
                    && curr.FileSizeBytes.HasValue
                    && curr.FileSizeBytes.Value < prev.FileSizeBytes.Value * 0.5)
                {
                    if (curr.Status != "SIZE_ANOMALY")
                    {
                        curr.Status = "SIZE_ANOMALY";
                        updates.Add(curr);
                        Log.Warning("备份文件大小异常: {FileName} ({Size}) < 上一份 {PrevFileName} ({PrevSize}) 的50%",
                            curr.FileName, curr.FileSizeBytes, prev.FileName, prev.FileSizeBytes);
                    }
                }
            }
        }

        foreach (var file in updates)
        {
            try
            {
                await _repo.UpdateStatusAsync(file.Id, "SIZE_ANOMALY");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "备份文件大小异常状态写回失败: {FileName}", file.FileName);
            }
        }
    }

    [RelayCommand]
    private async Task DeleteFileAsync(BackupFile file)
    {
        var allowedDirectories = await GetAllowedBackupDirectoriesAsync();
        if (!BackupPathGuard.IsAllowedBackupFile(file.FilePath, allowedDirectories))
            throw new InvalidOperationException($"不允许删除非备份目录文件：{file.FilePath}");

        if (System.IO.File.Exists(file.FilePath))
        {
            try
            {
                System.IO.File.Delete(file.FilePath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "删除磁盘文件失败: {Path}", file.FilePath);
                throw new InvalidOperationException($"删除磁盘文件失败：{ex.Message}", ex);
            }
        }

        var deletedAt = DateTime.Now.ToString("O");
        await _repo.UpdateStatusAsync(file.Id, "DELETED", deletedAt);
        file.Status = "DELETED";
        file.DeletedAt = deletedAt;
        SelectedFiles.Remove(file);
        SelectedCount = SelectedFiles.Count;
        await LoadAsync();
        Log.Information("删除备份记录并标记为 DELETED: {FileName}", file.FileName);
    }

    [RelayCommand]
    private async Task BatchDeleteAsync()
    {
        if (SelectedFiles.Count == 0) return;

        var toDelete = SelectedFiles.ToList();
        var allowedDirectories = await GetAllowedBackupDirectoriesAsync();
        var failedDeletes = new List<string>();
        var deletedAt = DateTime.Now.ToString("O");
        foreach (var file in toDelete)
        {
            if (!BackupPathGuard.IsAllowedBackupFile(file.FilePath, allowedDirectories))
            {
                failedDeletes.Add($"{file.FileName}: 文件路径不在允许的备份目录内");
                continue;
            }

            try
            {
                if (System.IO.File.Exists(file.FilePath))
                    System.IO.File.Delete(file.FilePath);
                await _repo.UpdateStatusAsync(file.Id, "DELETED", deletedAt);
                Log.Information("批量删除备份记录并标记为 DELETED: {FileName}", file.FileName);
            }
            catch (Exception ex)
            {
                failedDeletes.Add($"{file.FileName}: {ex.Message}");
                Log.Warning(ex, "批量删除-磁盘文件删除失败: {Path}", file.FilePath);
            }
        }

        SelectedFiles.Clear();
        SelectedCount = 0;
        await LoadAsync();

        if (failedDeletes.Count > 0)
            throw new InvalidOperationException("以下文件删除失败：" + Environment.NewLine + string.Join(Environment.NewLine, failedDeletes));
    }

    public void ToggleSelection(BackupFile file, bool isSelected)
    {
        if (isSelected && !SelectedFiles.Contains(file))
            SelectedFiles.Add(file);
        else if (!isSelected)
            SelectedFiles.Remove(file);
        SelectedCount = SelectedFiles.Count;
    }

    public void SelectAll()
    {
        SelectedFiles.Clear();
        foreach (var f in Files) SelectedFiles.Add(f);
        SelectedCount = SelectedFiles.Count;
    }

    public void DeselectAll()
    {
        SelectedFiles.Clear();
        SelectedCount = 0;
    }

    [RelayCommand]
    private async Task TogglePinAsync(BackupFile file)
    {
        file.IsPinned = !file.IsPinned;
        await _repo.SetPinnedAsync(file.Id, file.IsPinned);
    }

    [RelayCommand]
    private static Task OpenFolderAsync(BackupFile file)
    {
        var dir = System.IO.Path.GetDirectoryName(file.FilePath);
        if (dir != null && System.IO.Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", dir);
        return Task.CompletedTask;
    }

    private async Task<HashSet<string>> GetAllowedBackupDirectoriesAsync()
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultBackupDir = await _settingsRepo.GetAsync("default_backup_dir");
        if (!string.IsNullOrWhiteSpace(defaultBackupDir))
            directories.Add(defaultBackupDir);

        var tasks = await _taskRepo.GetAllAsync();
        foreach (var task in tasks.Where(task => task.TaskType is "BACKUP" or "BACKUP_CLEANUP"))
        {
            if (string.IsNullOrWhiteSpace(task.TaskConfig))
                continue;

            try
            {
                using var document = JsonDocument.Parse(task.TaskConfig);
                if (document.RootElement.TryGetProperty("BackupDir", out var backupDir)
                    && !string.IsNullOrWhiteSpace(backupDir.GetString()))
                {
                    directories.Add(backupDir.GetString()!);
                }
                else if (document.RootElement.TryGetProperty("TargetDir", out var targetDir)
                         && !string.IsNullOrWhiteSpace(targetDir.GetString()))
                {
                    directories.Add(targetDir.GetString()!);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "解析任务目录失败: {TaskName}", task.Name);
            }
        }

        return directories;
    }
}
