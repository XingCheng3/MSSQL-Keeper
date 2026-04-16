using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBKeeper.Core.Helpers;
using DBKeeper.Core.Models;
using DBKeeper.Data;
using DBKeeper.Data.Repositories;
using Serilog;

namespace DBKeeper.App.ViewModels;

public partial class ConnectionsViewModel : ObservableObject
{
    private readonly IConnectionRepository _repo;
    private readonly ITaskRepository _taskRepo;

    public ObservableCollection<ConnectionCardItem> Connections { get; } = [];

    [ObservableProperty] private bool _isLoading;

    public ConnectionsViewModel(IConnectionRepository repo, ITaskRepository taskRepo)
    {
        _repo = repo;
        _taskRepo = taskRepo;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _repo.GetAllAsync();
            Connections.Clear();
            foreach (var c in list)
                Connections.Add(new ConnectionCardItem(c));
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task TestConnectionAsync(ConnectionCardItem item)
    {
        item.IsTesting = true;
        try
        {
            var result = await SqlServerClient.TestConnectionAsync(item.Model);
            item.StatusText = result.Success ? "连接成功" : $"失败: {result.ErrorMessage}";
            item.IsOnline = result.Success;
        }
        catch (Exception ex)
        {
            item.StatusText = $"异常: {ex.Message}";
            item.IsOnline = false;
        }
        finally { item.IsTesting = false; }
    }

    [RelayCommand]
    private async Task DeleteConnectionAsync(ConnectionCardItem item)
    {
        var taskCount = await _taskRepo.CountByConnectionIdAsync(item.Model.Id);
        if (taskCount > 0)
        {
            // 由 View 层弹出确认对话框（通过事件通知）
            item.PendingDeleteTaskCount = taskCount;
            return;
        }
        await ConfirmDeleteAsync(item);
    }

    public async Task ConfirmDeleteAsync(ConnectionCardItem item)
    {
        await _repo.DeleteAsync(item.Model.Id);
        Connections.Remove(item);
        Log.Information("删除连接: {Name}", item.Model.Name);
    }

    [RelayCommand]
    private async Task SetDefaultAsync(ConnectionCardItem item)
    {
        await _repo.SetDefaultAsync(item.Model.Id);
        // 刷新列表中的默认标记
        foreach (var c in Connections) c.IsDefault = false;
        item.IsDefault = true;
    }

    /// <summary>保存新建或编辑的连接</summary>
    public async Task SaveConnectionAsync(Connection conn, bool isNew)
    {
        // 加密密码（仅当密码为明文时，已加密的带有 DPAPI: 前缀）
        if (!string.IsNullOrEmpty(conn.Password) && !conn.Password.StartsWith("DPAPI:", StringComparison.Ordinal))
            conn.Password = DpapiHelper.Encrypt(conn.Password);

        if (isNew)
        {
            conn.Id = await _repo.InsertAsync(conn);
            Connections.Add(new ConnectionCardItem(conn));
            Log.Information("新建连接: {Name}", conn.Name);
        }
        else
        {
            await _repo.UpdateAsync(conn);
            // 更新现有对象的属性，保留引用以维持选中状态
            var existing = Connections.FirstOrDefault(c => c.Model.Id == conn.Id);
            if (existing != null)
            {
                existing.UpdateFrom(conn);
            }
            Log.Information("更新连接: {Name}", conn.Name);
        }
    }
}

/// <summary>
/// 连接卡片的展示模型
/// </summary>
public partial class ConnectionCardItem : ObservableObject
{
    public Connection Model { get; }

    public ConnectionCardItem(Connection model)
    {
        Model = model;
        _isDefault = model.IsDefault;
    }

    /// <summary>从另一个 Connection 更新当前 Model 属性，保持对象引用不变</summary>
    public void UpdateFrom(Connection source)
    {
        Model.Name = source.Name;
        Model.Host = source.Host;
        Model.Username = source.Username;
        Model.Password = source.Password;
        Model.DefaultDb = source.DefaultDb;
        Model.TimeoutSec = source.TimeoutSec;
        Model.TrustServerCertificate = source.TrustServerCertificate;
        Model.IsDefault = source.IsDefault;
        Model.Remark = source.Remark;
        Model.UpdatedAt = source.UpdatedAt;
        IsDefault = source.IsDefault;
        OnPropertyChanged(nameof(Model));
    }

    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private bool _isDefault;
    /// <summary>待确认删除时关联的任务数</summary>
    [ObservableProperty] private int _pendingDeleteTaskCount;
}
