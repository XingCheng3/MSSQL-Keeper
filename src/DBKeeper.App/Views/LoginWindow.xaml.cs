using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace DBKeeper.App.Views;

public partial class LoginWindow : Window
{
    private int _remainingAttempts = 3;

    public LoginWindow()
    {
        InitializeComponent();
        // 窗口加载后自动聚焦密码框
        Loaded += (_, _) => passwordBox.Focus();
    }

    private void BtnLogin_Click(object sender, RoutedEventArgs e) => ValidatePassword();

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ValidatePassword();
    }

    private void ValidatePassword()
    {
        var config = App.Services.GetRequiredService<IConfiguration>();
        var correctPassword = config["AppPassword"];
        if (string.IsNullOrEmpty(correctPassword))
        {
            correctPassword = config["AppSettings:StartupPassword"];
            if (!string.IsNullOrEmpty(correctPassword))
                Log.Warning("检测到旧配置键 AppSettings:StartupPassword，请迁移到 AppPassword");
        }
        var input = passwordBox.Password;

        if (string.IsNullOrEmpty(correctPassword))
        {
            Log.Error("未配置启动密码，请在 appsettings.json 中设置 AppPassword");
            errorText.Text = "系统未配置启动密码，请联系管理员";
            errorText.Visibility = Visibility.Visible;
            return;
        }

        if (input == correctPassword)
        {
            Log.Information("登录验证通过");
            var mainWindow = new MainWindow();
            mainWindow.Show();
            Close();
            return;
        }

        _remainingAttempts--;
        Log.Warning("登录验证失败，剩余 {Remaining} 次", _remainingAttempts);

        if (_remainingAttempts <= 0)
        {
            Log.Warning("登录失败 3 次，程序退出");
            Application.Current.Shutdown();
            return;
        }

        errorText.Text = $"密码错误，还剩 {_remainingAttempts} 次机会";
        errorText.Visibility = Visibility.Visible;
        passwordBox.Password = string.Empty;
        passwordBox.Focus();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void DragSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); }
        catch { /* 忽略拖拽过程中鼠标状态变化导致的异常 */ }
    }
}
