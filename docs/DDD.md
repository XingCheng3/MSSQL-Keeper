# DB Keeper — 数据库设计文档（DDD）

> 维护说明：后续开发以 `docs/当前审查结果与后续开发计划.md` 为主依据。本文保留原始表结构参考；外键策略、删除策略和 JSON 字段命名需要按主文档重新修订。

**版本**：v1.0
**最后更新**：2026-04-14
**存储引擎**：SQLite（WAL 模式）
**文件路径**：`{程序目录}/data/dbkeeper.db`

---

## 1. ER 关系图

```
connections 1 ──── * tasks 1 ──── * backup_files
                        │
                        └──── * execution_logs

settings（独立键值对表）
```

- 一个连接可绑定多个任务
- 一个任务可产生多个备份文件记录
- 一个任务可产生多条执行日志

---

## 2. 表结构

### 2.1 connections（连接信息）

```sql
CREATE TABLE connections (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT    NOT NULL,           -- 连接名称，如「MES 主库」
    host        TEXT    NOT NULL,           -- 服务器地址，如 192.168.1.10,1433
    username    TEXT    NOT NULL,           -- SQL Server 登录用户名
    password    TEXT    NOT NULL,           -- DPAPI 加密后的密码（Base64）
    default_db  TEXT,                       -- 默认数据库，空则用 master
    timeout_sec INTEGER DEFAULT 30,        -- 连接超时秒数
    is_default  INTEGER DEFAULT 0,         -- 1 = 默认连接（全局唯一）
    remark      TEXT,                      -- 备注
    created_at  TEXT    NOT NULL,           -- ISO 8601
    updated_at  TEXT    NOT NULL            -- ISO 8601
);
```

> 注：认证方式固定为 SQL Server 认证，无 `auth_type` 字段。

---

### 2.2 tasks（定时任务）

```sql
CREATE TABLE tasks (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    name            TEXT    NOT NULL,       -- 任务名称
    task_type       TEXT    NOT NULL,       -- BACKUP / PROCEDURE / CUSTOM_SQL / BACKUP_CLEANUP
    connection_id   INTEGER NOT NULL,       -- 外键 → connections.id
    is_enabled      INTEGER DEFAULT 1,      -- 0=禁用 1=启用
    schedule_type   TEXT    NOT NULL,       -- DAILY / WEEKLY / MONTHLY / INTERVAL / CRON
    schedule_config TEXT    NOT NULL,       -- JSON，调度参数
    task_config     TEXT    NOT NULL,       -- JSON，任务参数
    last_run_at     TEXT,                   -- 上次执行时间
    last_run_status TEXT,                   -- SUCCESS / FAILED / WARNING
    next_run_at     TEXT,                   -- 下次执行时间
    created_at      TEXT    NOT NULL,
    updated_at      TEXT    NOT NULL,
    FOREIGN KEY (connection_id) REFERENCES connections(id)
);
```

---

### 2.3 backup_files（备份文件记录）

```sql
CREATE TABLE backup_files (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    task_id         INTEGER NOT NULL,       -- 外键 → tasks.id
    database_name   TEXT    NOT NULL,       -- 数据库名
    file_name       TEXT    NOT NULL,       -- 文件名
    file_path       TEXT    NOT NULL,       -- 完整本地路径
    file_size_bytes INTEGER,               -- 文件大小（字节）
    backup_type     TEXT,                   -- FULL / DIFF / LOG
    created_at      TEXT    NOT NULL,       -- 备份完成时间
    expires_at      TEXT,                   -- 计划过期时间，NULL=永久
    is_pinned       INTEGER DEFAULT 0,      -- 1=手动保留，跳过清理
    is_verified     INTEGER DEFAULT 0,      -- 1=已 RESTORE VERIFYONLY
    status          TEXT    DEFAULT 'NORMAL', -- NORMAL / SIZE_ANOMALY / EXPIRED / DELETED
    deleted_at      TEXT,                   -- 实际删除时间
    FOREIGN KEY (task_id) REFERENCES tasks(id)
);
```

---

### 2.4 execution_logs（执行日志）

```sql
CREATE TABLE execution_logs (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    task_id         INTEGER NOT NULL,       -- 外键 → tasks.id
    task_name       TEXT    NOT NULL,       -- 快照任务名（删除后仍可显示）
    task_type       TEXT    NOT NULL,       -- BACKUP / PROCEDURE / CUSTOM_SQL / BACKUP_CLEANUP
    trigger_type    TEXT    NOT NULL,       -- SCHEDULED / MANUAL
    started_at      TEXT    NOT NULL,       -- 开始时间
    finished_at     TEXT,                   -- 结束时间
    duration_ms     INTEGER,               -- 耗时毫秒
    status          TEXT    NOT NULL,       -- RUNNING / SUCCESS / FAILED / WARNING
    summary         TEXT,                   -- 简要摘要
    error_detail    TEXT,                   -- 完整错误信息
    FOREIGN KEY (task_id) REFERENCES tasks(id)
);
```

---

### 2.5 settings（全局设置）

```sql
CREATE TABLE settings (
    key         TEXT    PRIMARY KEY,        -- 设置键名
    value       TEXT    NOT NULL,           -- 设置值
    updated_at  TEXT    NOT NULL
);
```

**预置键值**：

| key | 默认 value | 说明 |
|-----|-----------|------|
| `minimize_to_tray_on_close` | `true` | 关闭窗口最小化到托盘 |
| `auto_start` | `false` | 开机自启 |
| `log_retention_days` | `90` | 日志保留天数 |
| `max_concurrent_tasks` | `3` | 最大并发任务数 |
| `disk_warn_threshold` | `20` | 磁盘警告阈值（%） |
| `disk_danger_threshold` | `10` | 磁盘危险阈值（%） |
| `disk_check_interval_min` | `5` | 磁盘检测间隔（分钟） |
| `heartbeat_interval_sec` | `60` | 连接心跳间隔（秒） |
| `backup_scan_interval_min` | `30` | 备份文件扫描同步间隔（分钟） |

---

## 3. JSON 字段结构

### 3.1 schedule_config（调度参数）

**DAILY**：
```json
{ "time": "02:00" }
```

**WEEKLY**：
```json
{ "day_of_week": 1, "time": "06:00" }
```
> `day_of_week`：0=周日, 1=周一, ..., 6=周六

**MONTHLY**：
```json
{ "day_of_month": 1, "time": "03:00" }
```

**INTERVAL**：
```json
{ "interval_minutes": 240 }
```

**CRON**：
```json
{ "cron_expression": "0 0 2 * * ?" }
```

### 3.2 task_config（任务参数）

**BACKUP**：
```json
{
  "database_name": "MES_DB",
  "backup_type": "FULL",
  "backup_dir": "D:\\Backup\\MES",
  "file_name_template": "{DB}_{DATE}.bak",
  "retention_days": 30,
  "min_keep_count": 3,
  "use_compression": true,
  "verify_after_backup": false
}
```

**PROCEDURE**：
```json
{
  "database_name": "MES_DB",
  "procedure_name": "dbo.DailyReport",
  "parameters": [
    { "name": "@Date", "value": "2026-04-13" }
  ],
  "timeout_sec": 300
}
```

**CUSTOM_SQL**：
```json
{
  "database_name": "MES_DB",
  "sql_content": "DBCC CHECKDB('MES_DB') WITH NO_INFOMSGS;",
  "timeout_sec": 600
}
```

**BACKUP_CLEANUP**：
```json
{
  "target_dir": "D:\\Backup\\MES",
  "retention_days": 30,
  "min_keep_count": 3
}
```

---

## 4. 索引

```sql
-- 执行日志：按任务和时间查询
CREATE INDEX idx_logs_task_id    ON execution_logs(task_id);
CREATE INDEX idx_logs_started_at ON execution_logs(started_at);
CREATE INDEX idx_logs_status     ON execution_logs(status);

-- 备份文件：按任务和状态查询
CREATE INDEX idx_backup_task_id    ON backup_files(task_id);
CREATE INDEX idx_backup_status     ON backup_files(status);
CREATE INDEX idx_backup_expires_at ON backup_files(expires_at);

-- 任务：按类型和启用状态
CREATE INDEX idx_tasks_type    ON tasks(task_type);
CREATE INDEX idx_tasks_enabled ON tasks(is_enabled);
```

---

## 5. 数据保留策略

| 数据 | 保留规则 | 清理时机 |
|------|----------|----------|
| 执行日志 | 默认 90 天（可配置） | 程序启动时 |
| 已删除备份记录 | `DELETED` 状态保留 30 天 | 程序启动时 |
| 连接 / 任务 / 设置 | 永久 | 用户手动删除 |

---

## 6. 初始化脚本

程序首次运行时自动执行以下操作：

1. 创建 `data/` 目录（如不存在）
2. 创建 `dbkeeper.db` 文件
3. 启用 WAL 模式：`PRAGMA journal_mode=WAL;`
4. 执行建表 SQL（§2.1 ~ §2.5）
5. 执行建索引 SQL（§4）
6. 插入预置设置（§2.5 预置键值）

---

## 7. 备份文件同步机制

程序按 `backup_scan_interval_min`（默认 30 分钟）定时扫描所有备份目录：

1. 读取 `backup_files` 表中状态为 NORMAL / SIZE_ANOMALY 的记录
2. 检查对应文件路径是否存在于磁盘
3. 不存在 → 更新 `status = 'DELETED'`，`deleted_at = 当前时间`
4. 同步结果汇总写入 `execution_logs`（`trigger_type = 'SYSTEM'`）

> 扫描在后台线程异步执行，不阻塞 UI。

## 8. 导入/导出数据格式

详见 [PRD.md §4.7](PRD.md) 导入/导出 JSON 格式定义。导入导出操作直接操作 SQLite 数据库，不涉及独立调度持久化表（导入后重启程序时自动重建调度）。

---

> **关联文档**：
> - [当前审查结果与后续开发计划](当前审查结果与后续开发计划.md)
> - [产品需求文档（PRD）](PRD.md)
> - [UI/UX 设计文档](UI_SPEC.md)

*文档维护：随需求变化持续更新。*
