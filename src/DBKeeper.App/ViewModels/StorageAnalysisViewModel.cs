using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBKeeper.Core.Models;
using DBKeeper.Data;
using DBKeeper.Data.Repositories;
using Serilog;
#pragma warning disable CS0618 // 与数据层保持一致：暂继续使用 System.Data.SqlClient

namespace DBKeeper.App.ViewModels;

public partial class StorageAnalysisViewModel : ObservableObject
{
    private readonly IConnectionRepository _connectionRepo;
    private CancellationTokenSource? _analysisCts;
    private int _databaseLoadVersion;
    private List<TableSpaceUsage> _allTables = [];
    private List<IndexSpaceUsage> _allIndexes = [];

    public ObservableCollection<Connection> Connections { get; } = [];
    public ObservableCollection<string> Databases { get; } = [];
    public ObservableCollection<StorageFileUsage> Files { get; } = [];
    public ObservableCollection<TableSpaceUsage> Tables { get; } = [];
    public ObservableCollection<IndexSpaceUsage> Indexes { get; } = [];

    [ObservableProperty] private Connection? _selectedConnection;
    [ObservableProperty] private string? _selectedDatabase;
    [ObservableProperty] private StorageOverview? _overview;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _sortMode = "TOTAL";
    [ObservableProperty] private bool _isLoadingDatabases;
    [ObservableProperty] private bool _isAnalyzing;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusText;

    public StorageAnalysisViewModel(IConnectionRepository connectionRepo)
    {
        _connectionRepo = connectionRepo;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        var selectedConnectionId = SelectedConnection?.Id;
        Connections.Clear();
        foreach (var conn in await _connectionRepo.GetAllAsync())
            Connections.Add(conn);

        SelectedConnection = selectedConnectionId.HasValue
            ? Connections.FirstOrDefault(c => c.Id == selectedConnectionId.Value) ?? Connections.FirstOrDefault()
            : Connections.FirstOrDefault();
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (SelectedConnection == null)
        {
            ErrorMessage = "请选择连接";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            ErrorMessage = "请选择数据库";
            return;
        }

        _analysisCts?.Cancel();
        _analysisCts?.Dispose();
        _analysisCts = new CancellationTokenSource();

        ErrorMessage = null;
        StatusText = "正在分析空间占用...";
        IsAnalyzing = true;

        try
        {
            var result = await SqlServerClient.AnalyzeStorageAsync(
                SelectedConnection,
                SelectedDatabase,
                _analysisCts.Token);

            Overview = result.Overview;
            ReplaceCollection(Files, result.Files);
            _allTables = result.Tables;
            _allIndexes = result.Indexes;
            ApplyFilter();
            StatusText = $"分析完成：{DateTime.Now:HH:mm:ss}，表展示Top {result.Tables.Count}，索引展示Top {result.Indexes.Count}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "分析已取消";
        }
        catch (System.Data.SqlClient.SqlException ex)
        {
            Log.Warning(ex, "空间分析 SQL 执行失败");
            ErrorMessage = ex.Number is 229 or 230
                ? "当前账号无权读取空间明细，请检查数据库权限"
                : $"空间分析失败: {ex.Message}";
            StatusText = null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "空间分析失败");
            ErrorMessage = $"空间分析失败: {ex.Message}";
            StatusText = null;
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    [RelayCommand]
    private void CancelAnalyze()
    {
        _analysisCts?.Cancel();
    }

    partial void OnSelectedConnectionChanged(Connection? value)
    {
        _ = LoadDatabasesAsync(value);
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSortModeChanged(string value)
    {
        ApplyFilter();
    }

    private async Task LoadDatabasesAsync(Connection? connection)
    {
        var loadVersion = Interlocked.Increment(ref _databaseLoadVersion);

        Databases.Clear();
        SelectedDatabase = null;
        ErrorMessage = null;
        if (connection == null) return;

        IsLoadingDatabases = true;
        try
        {
            var databases = await SqlServerClient.GetDatabaseListAsync(connection);
            if (loadVersion != _databaseLoadVersion) return;

            foreach (var db in databases)
                Databases.Add(db);
            SelectedDatabase = Databases.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载数据库列表失败");
            ErrorMessage = $"加载数据库列表失败: {ex.Message}";
        }
        finally
        {
            IsLoadingDatabases = false;
        }
    }

    private void ApplyFilter()
    {
        var keyword = SearchText.Trim();
        var tables = _allTables.AsEnumerable();
        var indexes = _allIndexes.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            tables = tables.Where(t =>
                t.SchemaName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                t.TableName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            indexes = indexes.Where(i =>
                i.SchemaName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                i.TableName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                i.IndexName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        tables = SortMode switch
        {
            "DATA" => tables.OrderByDescending(t => t.DataMb),
            "INDEX" => tables.OrderByDescending(t => t.IndexMb),
            _ => tables.OrderByDescending(t => t.TotalMb)
        };

        indexes = indexes.OrderByDescending(i => i.SizeMb);

        ReplaceCollection(Tables, tables.ToList());
        ReplaceCollection(Indexes, indexes.ToList());
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }
}
