# 数据库通讯 · Provider 扩展（MySQL/MariaDB + DPAPI 凭据）

- 日期：2026-10-23
- 批次：数据库通讯 v1.1
- 上游：`docs/code-changes/2026-10-23-数据库通讯落地（Core.Database-Plugin.Database-数据库页）.md`
- 结果：Provider 抽象完成；SQLite 行为零变化；MySQL/MariaDB 可用（含 DPAPI 密码）
- 验证：`DatabaseChecks` **123 / 0**；`HelpProbe` 全绿；UI 冒烟无新增硬编码色；全量构建 0 错误

---

## 一、做了什么

```text
Plugin.Database（三步骤，零改动）
   │ 只认 IDatabaseAccess（ProfileId + StatementId + 参数）
   ▼
DatabaseService
   ├─ SqliteDbExecutor ──► Microsoft.Data.Sqlite（文件）
   └─ MySqlDbExecutor  ──► MySqlConnector（MySQL / MariaDB）
        └─ DbExecutorBase：事务 / 参数绑定 / 截断 / 上限 / 取消 / 错误文案（只有一份）
```

- **Provider 抽象**：`DbExecutorBase` 收口全部执行语义，Provider 只给连接、会话设置、版本查询、清池。
- **MySQL/MariaDB**：`MySqlConnector 2.6.2`（纯托管，无原生依赖）。
- **凭据**：SQL 认证密码用 DPAPI（`LocalMachine` + 固定 entropy）加密后存
  `databases.json` 的 `PasswordCipher`；明文只在保存那一刻存在，`.vms` 与插件都看不到。
- **方言校验**：`StatementValidator` 按 Provider 分派；MySQL 额外禁用
  `LOAD DATA`/`OUTFILE`/`DUMPFILE`/`SET`/`USE`/`CALL`/`PREPARE`/`HANDLER`，查询禁锁定读。
- **错误分类**：MySQL 错误号映射（1062 约束、1045 权限、1146 找不到、1213 死锁、
  2013 暂时性、3024 超时…）。
- **宿主页**：Provider 下拉；SQLite 显示文件行，MySQL 显示服务器/端口/库/用户/密码；
  `PasswordBox` 不进绑定引擎，切换 profile 清空，支持「清除密码」。
- **插件窗口**：profile 摘要改用 `TargetText`（SQLite 文件路径 / MySQL `server:port/db`）。

---

## 二、新增文件

| 文件 | 内容 |
| --- | --- |
| `Services\Core.Database\DbExecutorBase.cs` | 执行底板（连接/事务/参数/截断/上限/取消） |
| `Services\Core.Database\SqliteDbExecutor.cs` | SQLite Provider（原 `SqliteExecutor` 拆分） |
| `Services\Core.Database\MySqlDbExecutor.cs` | MySQL/MariaDB Provider + 连接串构造 |
| `Services\Core.Database\DbCredentialProtector.cs` | DPAPI 加解密（固定 entropy、可读失败原因） |

## 三、改动文件

| 文件 | 改动 |
| --- | --- |
| `Shard\Core.Interfaces\Database\DbModels.cs` | `DbProviderKind.MySql`；`DbProfileInfo` 加 `Server/Port/DatabaseName`、`ProviderText`、`TargetText`；`DbProviderNames` |
| `Services\Core.Database\DatabaseModels.cs` | profile 加 MySQL 字段与 `PasswordCipher`、`TargetText` |
| `Services\Core.Database\DatabaseService.cs` | 双执行器注册；Provider 校验；密码加密与"未改动沿用密文"；清池两个 Provider |
| `Services\Core.Database\StatementValidator.cs` | 按 Provider 分派；MySQL 禁用语句/锁定读；`#` 行注释 |
| `Services\Core.Database\DbErrorClassifier.cs` | MySQL 错误号分类与文案 |
| `Services\Core.Database\Core.Database.csproj` | `net9.0-windows`；`MySqlConnector 2.6.2`；`ProtectedData 9.0.0`；`InternalsVisibleTo DatabaseChecks` |
| `VisionMaster\...\DatabasePageViewModel.cs` | Provider 下拉、MySQL 字段、密码状态机（未改动/改/清空） |
| `VisionMaster\...\DatabasePageView.xaml(.cs)` | Provider 条件字段、`PasswordBox` 事件、切换清空 |
| `Plugins\Plugin.Database\DatabasePluginView.xaml` | profile 摘要绑 `TargetText` |
| `Plugins\Plugin.Database\Help\**` | 三类帮助页补 Provider / MySQL 排障 / DPAPI 说明 |
| `DatabaseChecks\Program.cs` | MySQL 段 35 条（profile/凭据/方言/错误码/env-gated 真库） |

---

## 四、关键口径

| # | 口径 | 理由 |
| --- | --- | --- |
| 1 | MySQL 与 MariaDB 共用 `DbProviderKind.MySql` | 同一协议、同一驱动；拆两个枚举只会让配置多一个无意义选项 |
| 2 | 密码只存 DPAPI 密文，`DbProfileInfo` 不暴露密码字段 | 插件与 `.vms` 永远拿不到凭据 |
| 3 | 保存 MySQL profile **不连库** | 配置阶段服务器可能没开；连不通由「测试连接」暴露，保存不被网络状态绑架 |
| 4 | 只读档：SQLite 连接串强制；MySQL 应用层拦 | MySQL 没有连接级只读；硬保证要靠账号只授 SELECT（帮助页写明） |
| 5 | MySQL 禁 `CALL`/`LOAD DATA`/`OUTFILE`/`SET`/`USE` | 存储过程与文件读写能绕开参数化与事务边界 |
| 6 | 查询禁 `FOR UPDATE` / `LOCK IN SHARE MODE` | 查询步骤不该拿写锁 |
| 7 | 写一行允许 MySQL `REPLACE` | `REPLACE INTO` 就是 MySQL 的 upsert 写法 |
| 8 | 参数统一走 `DbParameter` + 标准 `DbType` | 两个 Provider 共用一套绑定规则，避免行为分叉 |
| 9 | MySQL 会话设 `innodb_lock_wait_timeout` | 服务端默认 50s 会超过命令超时，压到与超时一致 |
| 10 | 事务依赖 InnoDB | MyISAM 不支持事务，回滚不生效（帮助页与开发页都写明） |

---

## 五、验证

| 项 | 结果 |
| --- | --- |
| 全量构建 `-m:1 -nodeReuse:false` | 0 错误 |
| `DatabaseChecks` | **123 通过 / 0 失败**（原 88 条 SQLite 全保留 + 35 条 MySQL） |
| `HelpProbe` | 全部通过（12 个 `.Help.` 资源 = 磁盘 12 页） |
| `UIThemeSmokeTest` | 无新增硬编码颜色 |
| 页面渲染探针（一次性，已删） | SQLite 与 MySQL 两种 profile 均 890×900 布局成功；`TargetText=10.0.0.5:3306/mes`；密码状态机生效 |
| `Modules\Plugin.Database.dll` | 07:45:28 ≥ 最新源码 07:45:09；内嵌 12 页 |

`DatabaseChecks` 新增覆盖：

- MySQL profile 保存/往返、`TargetText`/`Caption`、明文不落盘、密文落盘；
- `DbProfileInfo` 不暴露密码字段；DPAPI 往返与坏密文可读原因；
- 连接串字段（Server/Port/Database/User ID/Allow User Variables=False）；
- 缺服务器/端口越界/缺库名/缺用户名四类拒绝；
- 方言：SELECT 通过、PRAGMA 拒、FOR UPDATE 拒、UPDATE 无 WHERE 拒、REPLACE 归写行、
  LOAD DATA 拒、CALL 拒、`#` 注释剥离；只读档拒写；
- 错误号：1062/1045/1146/1213/2013/3024/未知；
- env-gated 真库：设置 `VM_MYSQL_TEST`（标准 MySQL 连接串）时跑连接 + `SELECT 1`，
  未设置时打印 `[INFO]` 明确说明"跳过"，不伪装成通过。

---

## 六、外部干扰（非本批）

收尾时 `run_gates` 相关宿主受另一个会话的进行中改动影响：

1. `FlowCanvasChecks`：**3380 / 3**，3 条红全部是 `Plugin.PreProcessing` 的算子注册表与 CALM 端口
   （算子总数期望 25 实际 30、新增「二值化」分组、`MmPerPixel` 变必填）。
2. `audit_artifacts`：`MISSING_DLL Plugin.ImageDiff`（该会话新增的插件工程尚未构建）。

本批自己的产物审计干净（`Plugin.Database` 不旧于源码、12 页资源齐）。
等对方把断言与工程收尾后，`run_gates` 即可回绿。

---

## 七、踩过的坑

1. **`FOR UPDATE` 里含 `UPDATE` 关键字**：先判"写关键字"会把锁定读报成"不允许出现写关键字"。
   锁定读必须在写关键字之前判（已修，断言点名"锁定读"）。
2. **`PasswordBox.Password` 不能绑定**：只能走 `PasswordChanged` 事件；
   切换 profile 必须手动清空，否则上一条的明文留在界面上。
3. **密码"未改动"与"清空"必须分开**：`null` = 没动（沿用密文）、空串 = 清空。
   混成一个值会导致"每次保存都把密码清掉"或"永远改不了密码"。
4. **`PasswordCipher` 不要从页面回传**：页面没有密文（也不该有），
   保存时由服务端按"是否给了新密码"决定沿用还是重写。
5. **MyISAM 没有事务**：回滚不生效，写失败会留下半套数据；文档必须写明用 InnoDB。
6. **MySQL 只读不是连接级开关**：`SET SESSION TRANSACTION READ ONLY` 不影响 autocommit 写，
   应用层只能拦语句类型，硬保证要靠 GRANT。
7. **受限环境**：`%TEMP%` 建目录/建文件与 `artifacts\` 下 SQLite 均可能被拒；
   断言宿主把测试文件放到自己的输出目录。
8. **只写 `GetConfigView` 方法、不声明 `IPluginCustomViewProvider` = 宿主永远走通用面板**
   （现场表现：弹出 `ProfileId` / `StatementId` / `Parameters` 这些内部字段，且都是只读）。
   宿主判定是 `pluginInstance is IPluginCustomViewProvider`，不是"有没有同名方法"。
   已在 `DatabasePluginBase` 声明接口，并在 `DatabaseChecks` 加三条接口断言钉住。
9. **`[Display]` 不写 `ShortName` = 工具箱与流程栏没有图标**：`PluginService` 用
   `att.ShortName` 填 `ToolItemModel.Icon`，步骤创建时再快照进 `.vms`。
   三个类已补 `ShortName`（`\uf002` 查询 / `\uf013` 执行 / `\uf0c7` 写行），
   `DatabaseChecks` 加三条断言。
10. **步骤图标是建步骤时的快照，改插件不会让旧步骤自动有图标**：`StepModel.Icon` 原来是
    `init`，`.vms` 里存的是空串。已改为 `set` 并新增 `StepIconBackfill`（`Core\StepIconBackfill.cs`）：
    方案加载后按 `PluginTypeName` 从插件表补空图标与分类，只补空值、不覆盖、不触发版本递增。
    钩子 `SolutionService.StepIconBackfillHook` 由 `App` 注入；`DatabaseChecks` 用假插件表
    覆盖"补空/不覆盖/未知类型跳过/嵌套容器"四种情形。
