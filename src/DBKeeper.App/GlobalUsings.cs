// 解决 WPF + WinForms 命名空间冲突
// WPF 优先，WinForms 类型需要完整限定名访问
global using Application = System.Windows.Application;
global using ComboBox = System.Windows.Controls.ComboBox;
global using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
global using Timer = System.Threading.Timer;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using MessageBox = System.Windows.MessageBox;
global using MessageBoxButton = System.Windows.MessageBoxButton;
global using MessageBoxResult = System.Windows.MessageBoxResult;
