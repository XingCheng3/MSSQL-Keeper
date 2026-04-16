using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBKeeper.Core.Models;
using DBKeeper.Data.Repositories;
using Serilog;

namespace DBKeeper.App.ViewModels;

public partial class BackupFilesViewModel : ObservableObject
{
    private readonly IBackupFileRepository _repo;

    public ObservableCollection<BackupFile> Files { get; } = [];
    public ObservableCollection<BackupFile> SelectedFiles { get; } = [];

    [ObservableProperty] private string? _filterDatabase;
    [ObservableProperty] private string? _filterStatus;
    [ObservableProperty] private DateTime? _filterDateFrom;
    [ObservableProperty] private DateTime? _filterDateTo;
    [ObservableProperty] private int _selectedCount;

    public BackupFilesViewModel(IBackupFileRepository repo)
    {
        _repo = repo;
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
        DetectSizeAnomalies();
    }

    /// <summary>
    /// 检测大小异常：同一任务的连续备份文件，当前文件比上一份小50%以上则标记 SIZE_ANOMALY
    /// </summary>
    private void DetectSizeAnomalies()
    {
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
                        _ = _repo.UpdateStatusAsync(curr.Id, "SIZE_ANOMALY");
                        Log.Warning("备份文件大小异常: {FileName} ({Size}) < 上一份 {PrevFileName} ({PrevSize}) 的50%",
                            curr.FileName, curr.FileSizeBytes, prev.FileName, prev.FileSizeBytes);
                    }
                }
            }
        }
    }

    [RelayCommand]
    private async Task DeleteFileAsync(BackupFile file)
    {
        // 删除磁盘文件（如果存在）
        try { if (System.IO.File.Exists(file.FilePath)) System.IO.File.Delete(file.FilePath); }
        catch (Exception ex) { Log.Warning(ex, "删除磁盘文件失败: {Path}", file.FilePath); }

        // 从数据库物理删除记录
        await _repo.DeleteAsync(file.Id);
        Files.Remove(file);
        SelectedFiles.Remove(file);
        SelectedCount = SelectedFiles.Count;
        Log.Information("删除备份记录: {FileName}", file.FileName);
    }

    [RelayCommand]
    private async Task BatchDeleteAsync()
    {
        if (SelectedFiles.Count == 0) return;

        var toDelete = SelectedFiles.ToList();
        foreach (var file in toDelete)
        {
            try { if (System.IO.File.Exists(file.FilePath)) System.IO.File.Delete(file.FilePath); }
            catch (Exception ex) { Log.Warning(ex, "批量删除-磁盘文件删除失败: {Path}", file.FilePath); }

            await _repo.DeleteAsync(file.Id);
            Files.Remove(file);
            Log.Information("批量删除备份记录: {FileName}", file.FileName);
        }

        SelectedFiles.Clear();
        SelectedCount = 0;
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
}
