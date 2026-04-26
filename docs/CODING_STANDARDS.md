# DB Keeper — 编码规范（CODING_STANDARDS）

> 维护说明：后续开发以 `docs/当前审查结果与后续开发计划.md` 为主依据。本文保留编码风格参考；架构边界以后续补齐 Service/Application 层为准。

**最后更新**：2026-04-14

---

## 1. 架构模式：MVVM

项目严格遵循 **Model-View-ViewModel** 分层：

```
View（XAML）       →  只负责 UI 呈现，不包含业务逻辑
    ↕ 数据绑定
ViewModel（C#）    →  UI 逻辑、命令处理、数据转换
    ↕ 调用
Model / Service    →  业务逻辑、数据访问、外部通信
```

### 1.1 分层规则

| 层 | 允许 | 禁止 |
|----|------|------|
| **View** (.xaml / .xaml.cs) | 数据绑定、动画、纯 UI 交互 | 直接访问数据库、业务逻辑 |
| **ViewModel** | 属性绑定、命令、调用 Service | 直接操作 UI 控件、引用 View |
| **Model** | 数据实体定义（POCO） | 包含 UI 相关代码 |
| **Service** | 数据库访问、业务计算、外部调用 | 引用 ViewModel 或 View |

### 1.2 ViewModel 基类

使用 `CommunityToolkit.Mvvm` 简化 MVVM 样板代码：

```csharp
// ✅ 推荐：使用 source generator
public partial class TaskListViewModel : ObservableObject
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    [RelayCommand]
    private async Task LoadTasksAsync()
    {
        // ...
    }
}
```

---

## 2. 项目结构约定

```
src/
├── DBKeeper.App/                  # WPF 启动项目
│   ├── Views/                     # XAML 页面
│   │   ├── DashboardPage.xaml
│   │   ├── TaskListPage.xaml
│   │   └── ...
│   ├── ViewModels/                # ViewModel
│   │   ├── DashboardViewModel.cs
│   │   ├── TaskListViewModel.cs
│   │   └── ...
│   ├── Dialogs/                   # 对话框（ContentDialog）
│   ├── Converters/                # 值转换器
│   ├── Assets/                    # 图标、图片资源
│   └── App.xaml / MainWindow.xaml
│
├── DBKeeper.Core/                 # 核心层（无 UI 依赖）
│   ├── Models/                    # 数据实体
│   │   ├── Connection.cs
│   │   ├── TaskItem.cs
│   │   ├── BackupFile.cs
│   │   └── ExecutionLog.cs
│   ├── Services/                  # 业务服务接口 + 实现
│   │   ├── IConnectionService.cs
│   │   ├── ConnectionService.cs
│   │   └── ...
│   └── Helpers/                   # 工具类（加密、路径处理等）
│
├── DBKeeper.Data/                 # 数据访问层
│   ├── DbInitializer.cs          # SQLite 建库建表
│   ├── Repositories/              # 数据仓储
│   │   ├── IConnectionRepository.cs
│   │   ├── ConnectionRepository.cs
│   │   └── ...
│   └── SqlServerClient.cs        # 目标 SQL Server 操作封装
│
├── DBKeeper.Scheduling/           # 调度引擎
│   └── SchedulerService.cs        # Cronos + Timer 轻量调度
│
└── DBKeeper.Executors/            # 任务执行器
    ├── ITaskExecutor.cs           # 统一接口
    ├── BackupExecutor.cs
    ├── ProcedureExecutor.cs
    ├── SqlExecutor.cs
    └── CleanupExecutor.cs
```

---

## 3. 命名约定

### 3.1 C# 命名

| 类型 | 规则 | 示例 |
|------|------|------|
| 类 / 接口 | PascalCase | `ConnectionService`, `ITaskExecutor` |
| 接口 | I 前缀 | `IConnectionRepository` |
| 公共方法 | PascalCase | `GetAllConnections()` |
| 异步方法 | Async 后缀 | `LoadTasksAsync()` |
| 私有字段 | _ 前缀 + camelCase | `_connectionService` |
| 局部变量 / 参数 | camelCase | `taskName`, `backupDir` |
| 常量 | PascalCase | `MaxRetryCount` |
| 枚举值 | PascalCase | `TaskType.Backup` |

### 3.2 文件命名

| 类型 | 规则 | 示例 |
|------|------|------|
| View | `{Name}Page.xaml` | `TaskListPage.xaml` |
| ViewModel | `{Name}ViewModel.cs` | `TaskListViewModel.cs` |
| Model | `{Name}.cs` | `Connection.cs` |
| Service | `{Name}Service.cs` | `ConnectionService.cs` |
| Repository | `{Name}Repository.cs` | `ConnectionRepository.cs` |
| 对话框 | `{Name}Dialog.xaml` | `EditConnectionDialog.xaml` |

### 3.3 XAML 命名

| 类型 | 规则 | 示例 |
|------|------|------|
| 控件 x:Name | camelCase | `x:Name="searchTextBox"` |
| 资源 Key | PascalCase | `Key="PrimaryButtonStyle"` |

---

## 4. 代码模式

### 4.1 依赖注入

所有 Service 和 Repository 通过 DI 容器注册，ViewModel 通过构造函数注入：

```csharp
// ✅ 推荐
public class TaskListViewModel
{
    private readonly ITaskService _taskService;

    public TaskListViewModel(ITaskService taskService)
    {
        _taskService = taskService;
    }
}

// ❌ 禁止
public class TaskListViewModel
{
    private readonly TaskService _taskService = new TaskService(); // 直接 new
}
```

### 4.2 数据访问模式（Repository）

数据库操作统一通过 Repository 接口：

```csharp
public interface IConnectionRepository
{
    Task<List<Connection>> GetAllAsync();
    Task<Connection?> GetByIdAsync(int id);
    Task<int> InsertAsync(Connection conn);
    Task UpdateAsync(Connection conn);
    Task DeleteAsync(int id);
}
```

### 4.3 异常处理

```csharp
// ✅ 在 Service 层捕获业务异常，返回结果对象
public class OperationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

// ✅ 在 ViewModel 层处理 UI 反馈
try
{
    var result = await _backupService.ExecuteBackupAsync(config);
    if (!result.Success)
        ShowError(result.ErrorMessage);
}
catch (Exception ex)
{
    _logger.Error(ex, "备份执行异常");
    ShowError("操作失败，请查看日志");
}

// ❌ 禁止：吞掉异常
catch (Exception) { }
```

### 4.4 调度热更新

任务编辑保存后，调度引擎自动同步：
- 调度参数变更 → 删除旧 Job + Trigger → 重新注册
- 任务被禁用 → 暂停对应 Trigger
- 任务被删除 → 删除对应 Job + Trigger
- 正在执行的任务不受影响，下次调度时生效

### 4.5 日志规范

```csharp
// 使用 Serilog 结构化日志
_logger.Information("任务 {TaskName} 开始执行, 类型={TaskType}", task.Name, task.TaskType);
_logger.Error(ex, "备份执行失败, 任务ID={TaskId}", task.Id);

// ❌ 禁止：日志中记录密码
_logger.Information("连接 {Host}, 密码={Password}", host, password);
```

---

## 5. 注释规范

### 5.1 XML 文档注释

所有 public 类和方法必须有 `///` 注释：

```csharp
/// <summary>
/// 执行数据库全量备份
/// </summary>
/// <param name="config">备份配置参数</param>
/// <returns>操作结果，包含生成的文件路径</returns>
public async Task<BackupResult> ExecuteBackupAsync(BackupConfig config)
```

### 5.2 行内注释

- 注释解释 **为什么**，而非 **是什么**
- 复杂业务逻辑必须注释原因
- 临时代码标记 `// TODO:` 并附说明

---

## 6. 其他约定

| 项目 | 约定 |
|------|------|
| 字符串 | 优先使用字符串插值 `$"..."` |
| Null 处理 | 启用 nullable reference types，避免 `!` 强制断言 |
| 时间格式 | 统一使用 ISO 8601：`yyyy-MM-ddTHH:mm:ss` |
| 配置读取 | 通过 `IOptions<T>` 模式 |
| 单元测试 | 测试类命名 `{被测类}Tests.cs`，方法命名 `{方法}_{场景}_{预期}` |

---

> **关联文档**：
> - [当前审查结果与后续开发计划](当前审查结果与后续开发计划.md)
> - [产品需求文档（PRD）](PRD.md)
> - [UI/UX 设计文档](UI_SPEC.md)
> - [数据库设计文档（DDD）](DDD.md)

*文档维护：随项目演进持续更新。*
