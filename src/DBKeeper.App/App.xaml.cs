using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using DBKeeper.Data;
using DBKeeper.Data.Repositories;
using DBKeeper.Scheduling;
using DBKeeper.App.Services;

namespace DBKeeper.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private SchedulerService? _scheduler;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 配置
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        // Serilog：从 appsettings.json 读取配置（单文件发布需显式指定程序集）
        var readerOptions = new Serilog.Settings.Configuration.ConfigurationReaderOptions(
            typeof(Serilog.Sinks.File.FileSink).Assembly);
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(config, readerOptions)
            .CreateLogger();

        Log.Information("DB Keeper 启动");

        // SQLite 初始化
        var dbInit = new DbInitializer();
        dbInit.Initialize();
        var connStr = dbInit.ConnectionString;

        // DI 容器
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(dbInit);

        // 仓储
        services.AddSingleton<IConnectionRepository>(new ConnectionRepository(connStr));
        services.AddSingleton<ITaskRepository>(new TaskRepository(connStr));
        services.AddSingleton<IBackupFileRepository>(new BackupFileRepository(connStr));
        services.AddSingleton<IExecutionLogRepository>(new ExecutionLogRepository(connStr));
        services.AddSingleton<ISettingsRepository>(new SettingsRepository(connStr));

        // 调度引擎
        services.AddSingleton<SchedulerService>();

        // 后台服务
        services.AddSingleton<ConnectionHeartbeatService>();
        services.AddSingleton<BackupFileSyncService>();

        // ViewModel
        services.AddSingleton<ViewModels.ConnectionsViewModel>();
        services.AddSingleton<ViewModels.TaskListViewModel>();
        services.AddSingleton<ViewModels.ExecutionLogsViewModel>();
        services.AddSingleton<ViewModels.DashboardViewModel>();
        services.AddSingleton<ViewModels.BackupFilesViewModel>();
        services.AddSingleton<ViewModels.StorageAnalysisViewModel>();

        Services = services.BuildServiceProvider();

        // 启动调度引擎
        _scheduler = Services.GetRequiredService<SchedulerService>();
        await _scheduler.InitializeAsync();
        _ = _scheduler.StartAsync()
            .ContinueWith(t => { if (t.Exception != null) Log.Error(t.Exception, "调度引擎启动失败"); });

        // 启动心跳检测
        var heartbeat = Services.GetRequiredService<ConnectionHeartbeatService>();
        var settingsRepo = Services.GetRequiredService<ISettingsRepository>();
        var heartbeatIntervalStr = await settingsRepo.GetAsync("heartbeat_interval_sec") ?? "60";
        var heartbeatInterval = int.TryParse(heartbeatIntervalStr, out var hbSec) ? hbSec : 60;
        heartbeat.Start(heartbeatInterval);

        // 启动备份文件同步扫描
        var backupSync = Services.GetRequiredService<BackupFileSyncService>();
        var scanIntervalStr = await settingsRepo.GetAsync("backup_scan_interval_min") ?? "30";
        var scanInterval = int.TryParse(scanIntervalStr, out var scanMin) ? scanMin : 30;
        backupSync.Start(scanInterval);

        // 启动时清理过期日志
        _ = CleanupOnStartAsync()
            .ContinueWith(t => { if (t.Exception != null) Log.Error(t.Exception, "启动清理失败"); });

        // 先启动登录窗口
        var loginWindow = new Views.LoginWindow();
        loginWindow.Show();
    }

    /// <summary>启动时清理过期日志和已删除的备份记录</summary>
    private async Task CleanupOnStartAsync()
    {
        try
        {
            var settingsRepo = Services.GetRequiredService<ISettingsRepository>();
            var retentionStr = await settingsRepo.GetAsync("log_retention_days") ?? "90";
            var retentionDays = int.TryParse(retentionStr, out var d) ? d : 90;

            var logRepo = Services.GetRequiredService<IExecutionLogRepository>();
            await logRepo.CleanupAsync(retentionDays);
            Log.Information("已清理超过 {Days} 天的日志", retentionDays);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "启动清理失败");
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_scheduler != null) await _scheduler.StopAsync();
        Log.Information("DB Keeper 退出");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
