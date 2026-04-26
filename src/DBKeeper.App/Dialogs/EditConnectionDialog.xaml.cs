using System.Windows;
using DBKeeper.Core.Models;

namespace DBKeeper.App.Dialogs;

public partial class EditConnectionDialog : Wpf.Ui.Controls.FluentWindow
{
    private readonly Connection? _existing;

    /// <summary>对话框确认后的结果</summary>
    public Connection? Result { get; private set; }

    public EditConnectionDialog(Connection? existing = null)
    {
        InitializeComponent();
        _existing = existing;

        if (existing != null)
        {
            Title = "编辑连接";
            txtName.Text = existing.Name;
            txtHost.Text = existing.Host;
            txtUsername.Text = existing.Username;
            // 密码不回显，留空表示不修改
            txtDefaultDb.Text = existing.DefaultDb ?? string.Empty;
            txtTimeout.Text = existing.TimeoutSec.ToString();
            chkTrustCert.IsChecked = existing.TrustServerCertificate;
            txtRemark.Text = existing.Remark ?? string.Empty;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // 表单验证
        if (string.IsNullOrWhiteSpace(txtName.Text) ||
            string.IsNullOrWhiteSpace(txtHost.Text) ||
            string.IsNullOrWhiteSpace(txtUsername.Text))
        {
            errorText.Text = "连接名称、服务器地址、用户名为必填项";
            errorText.Visibility = Visibility.Visible;
            return;
        }

        // 新建时密码必填
        if (_existing == null && string.IsNullOrEmpty(txtPassword.Password))
        {
            errorText.Text = "新建连接时密码为必填项";
            errorText.Visibility = Visibility.Visible;
            return;
        }

        Result = new Connection
        {
            Id = _existing?.Id ?? 0,
            Name = txtName.Text.Trim(),
            Host = txtHost.Text.Trim(),
            Username = txtUsername.Text.Trim(),
            // 密码为空时保留原密码
            Password = string.IsNullOrEmpty(txtPassword.Password) ? (_existing?.Password ?? string.Empty) : txtPassword.Password,
            DefaultDb = string.IsNullOrWhiteSpace(txtDefaultDb.Text) ? null : txtDefaultDb.Text.Trim(),
            TimeoutSec = int.TryParse(txtTimeout.Text, out var t) ? t : 30,
            TrustServerCertificate = chkTrustCert.IsChecked ?? true,
            Remark = string.IsNullOrWhiteSpace(txtRemark.Text) ? null : txtRemark.Text.Trim(),
            IsDefault = _existing?.IsDefault ?? false,
            CreatedAt = _existing?.CreatedAt ?? DateTime.Now.ToString("O"),
            UpdatedAt = DateTime.Now.ToString("O")
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void DragBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;
        try { DragMove(); }
        catch { /* 忽略拖拽过程中鼠标状态变化导致的异常 */ }
    }
}
