using System.Drawing;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using DBKeeper.Data.Repositories;
using DBKeeper.Scheduling;
using Wpf.Ui.Controls;

namespace DBKeeper.App;

public partial class MainWindow : FluentWindow
{
    private TaskbarIcon? _trayIcon;
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();

        // 窗口加载后自动导航到概览页 + 初始化托盘
        Loaded += async (_, _) =>
        {
            RootNavigation.Navigate(typeof(Views.DashboardPage));
            await InitTrayAsync();
        };
    }

    private async Task InitTrayAsync()
    {
        // 读取设置判断是否启用托盘
        var settings = App.Services.GetRequiredService<ISettingsRepository>();
        var trayEnabled = (await settings.GetAsync("minimize_to_tray_on_close")) == "true";

        if (!trayEnabled) return;

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "DB Keeper - 数据库维护工具",
            Icon = LoadAppIcon(),
            ContextMenu = BuildTrayMenu()
        };
        _trayIcon.TrayMouseDoubleClick += (_, _) => ShowFromTray();
    }

    /// <summary>从嵌入资源或默认图标加载</summary>
    private static Icon LoadAppIcon()
    {
        // 尝试使用应用图标文件
        var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (System.IO.File.Exists(icoPath))
            return new Icon(icoPath);

        // 回退：使用系统默认图标
        return SystemIcons.Application;
    }

    private System.Windows.Controls.ContextMenu BuildTrayMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var showItem = new System.Windows.Controls.MenuItem { Header = "显示主窗口" };
        showItem.Click += (_, _) => ShowFromTray();
        menu.Items.Add(showItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var pauseItem = new System.Windows.Controls.MenuItem { Header = "暂停所有任务" };
        var resumeItem = new System.Windows.Controls.MenuItem { Header = "恢复所有任务", IsEnabled = false };

        pauseItem.Click += async (_, _) =>
        {
            var scheduler = App.Services.GetRequiredService<SchedulerService>();
            await scheduler.PauseAllAsync();
            pauseItem.IsEnabled = false;
            resumeItem.IsEnabled = true;
        };

        resumeItem.Click += async (_, _) =>
        {
            var scheduler = App.Services.GetRequiredService<SchedulerService>();
            await scheduler.ResumeAllAsync();
            pauseItem.IsEnabled = true;
            resumeItem.IsEnabled = false;
        };

        menu.Items.Add(pauseItem);
        menu.Items.Add(resumeItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "退出" };
        exitItem.Click += (_, _) =>
        {
            _forceClose = true;
            Close();
        };
        menu.Items.Add(exitItem);

        return menu;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose)
        {
            // 清理托盘图标
            _trayIcon?.Dispose();
            base.OnClosing(e);
            return;
        }

        // 先阻止关闭，避免 async void 中 await 后 e.Cancel 设置无效
        e.Cancel = true;

        // 检查是否启用最小化到托盘
        var settings = App.Services.GetRequiredService<ISettingsRepository>();
        var trayEnabled = (await settings.GetAsync("minimize_to_tray_on_close")) == "true";

        if (trayEnabled)
        {
            Hide();

            // 如果托盘图标还没创建，补创建
            if (_trayIcon == null) await InitTrayAsync();
        }
        else
        {
            // 未启用托盘 → 真正关闭
            _forceClose = true;
            _trayIcon?.Dispose();
            Close(); // 再次触发 OnClosing，走 _forceClose 路径
        }
    }
}