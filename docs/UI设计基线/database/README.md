# 数据库页（SQLite 优先）· 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-23 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | 硬件管理新增「数据库」页：数据库 profile + 命名语句 + 测试；不含流程插件视图（`Plugin.Database` 另有基线） |
| 窗口尺寸 | 硬件管理 1100 × 820（左栏 210 + 内容 890）；本页内容区最小 860 × 716 |
| 设计文件 | `design.html`（自包含、内联样式、无外部依赖） |
| 证据 | `evidence/design-v1-connection.png`（连接页）、`evidence/design-v1-statements.png`（命名语句页）、`evidence/design-v1-states.png`（空态/只读/测试失败/删除确认/查询预览） |
| 复刻状态 | **未复刻**；本文件是设计基线，不是 WPF 实现 |

## 一、定位

按 `AGENTS.md` §30（R111-R132）与 `docs/UI设计基线/插件自定义视图Web化改造SOP.md` 的同一套流程：

1. **Web 设计基线**（本目录）：布局、间距、配色令牌、字号字重、五态、交互细节落纸；
2. **WPF 复刻**：以“接近设计稿”为唯一目标，复用 `Dialog*` 令牌与共享控件；
3. **通用入库**：跨页复用的结构下沉到 `UI\Controls\Themes\Dialog\`，页面不重复手写。

本目录只做设计媒介：不进入构建、不部署、不作为第二客户端维护。确认后只读；需要调整时升 v2，并同步 WPF 复刻与令牌映射。

## 二、结论：数据库页负责“配置 + 封装”，插件只调用

用户的问题——“是不是应该数据库页也可以配置和添加数据库，以及封装数据库指令，插件层只能调用这些封装好的指令”——**是，第一版就按这个边界做。**

| 层 | 职责 | 不做什么 |
| --- | --- | --- |
| 数据库页（本设计） | 添加/编辑/删除数据库 profile；管理命名语句（SQL + 参数定义 + 测试）；只读/权限/审计 | 不执行流程、不持有运行期连接 |
| 宿主 `Core.Database` | 连接池、事务、参数化、超时、错误分类、凭据、健康、审计 | 不决定业务 SQL |
| `Plugin.Database`（流程步骤） | 只选 `ProfileId` + `StatementId` + 填参数；输出结果 | **不写 SQL、不碰连接串** |
| `.vms` | 只存 ProfileId / StatementId / 参数值 | 不存 SQL、不存密码/连接串 |

身份用 **GUID**：`ProfileId` / `StatementId`。显示名可改，改名不断链；删除前检查当前方案引用。

## 三、信息架构

```text
硬件管理
├─ 概览
├─ 相机
├─ 通讯
├─ 运动控制
└─ 数据库                      ← 本页
   ├─ 数据库列表（左，280）
   │  └─ 生产追溯 · SQLite · 3 条语句 · 已连接
   └─ 详情（右）
      ├─ 连接
      │  ├─ 文件路径 + 浏览/新建
      │  ├─ 打开方式（读写/只读）
      │  ├─ 命令超时 / 最大返回行数
      │  └─ 测试连接（SELECT sqlite_version()）
      └─ 命名语句（3）
         ├─ 语句列表（左，220）
         │  ├─ WriteResult · 写一行 · 4 参数
         │  ├─ ReadRecipe · 查询一行 · 2 参数
         │  └─ QueryWorkOrder · 查询 · 1 参数
         └─ 语句编辑器（右）
            ├─ 名称 / 类型（查询 / 执行 / 写一行）
            ├─ SQL 编辑器（参数占位 @name）
            ├─ 参数表（名称/类型/必填/默认值/说明）
            ├─ 选项（超时 / 最大行数 / 最大影响行数）
            └─ 测试语句 + 保存
```

## 四、布局结构

```text
1100 × 820（硬件管理外壳）
├─ 标题栏 52（宿主已有）
├─ 主体 716
│  ├─ 左栏 210（宿主已有：硬件管理导航）
│  └─ 内容 890
│     ├─ 页头 64：标题「数据库」+ 副标题 + 「添加数据库」主按钮 + 「刷新」
│     ├─ 双栏 600
│     │  ├─ 数据库列表 280（1px 右边线）
│     │  └─ 详情 610
│     │     ├─ 数据库卡 96：名称 / SQLite chip / 文件 / 状态 / 测试连接 / 删除
│     │     ├─ 页签 40：连接 | 命名语句（3）
│     │     └─ 内容 464（可滚动）
│     └─ 页脚 52：权限提示 / 最近修改 / 保存状态
└─ 底部 52（宿主已有）
```

### 尺寸刻度

| 项 | 值 |
| --- | --- |
| 窗口 | 1100 × 820（Min 980 × 700） |
| 左栏 | 210 |
| 内容左右内边距 | 24 |
| 页头 | 64 |
| 数据库列表 | 280 宽；行高 64 |
| 数据库卡 | 96 高 / 12 圆角 |
| 页签 | 40 高 |
| 表单行 | 44 高（标签 120 + 控件） |
| 输入框 / 下拉 | 34 高 / 8 圆角 |
| SQL 编辑器 | 180 高 / 等宽字体 12 |
| 参数表行 | 38 高 |
| 按钮 | 32 高（主/次）/ 28（行内） |
| 字号 | 页标题 18 / 区块 13 / 正文 12 / 标签 10.5 / 徽章 9.5 |
| 圆角 | 6 / 8 / 10 / 12 |
| 动效 | 150ms（hover/focus）/ 200ms（页签切换） |

## 五、状态覆盖

| 状态 | 触发 | 设计表现 |
| --- | --- | --- |
| 默认 | 打开数据库页 | 选中第一个 profile；连接页签；测试连接结果为空或上次结果 |
| 已连接 | 测试连接成功 | 绿色状态胶囊「已连接 · SQLite 3.45.1」+ 耗时 |
| 连接失败 | 文件不存在/被占用/只读冲突 | 红色状态胶囊 + SQLite 错误码 + 下一步建议（新建/换路径/关占用） |
| 空态 | 没有任何数据库 | 图标 + 「还没有数据库」+ 三步提示 + 「添加 SQLite 数据库」 |
| 无语句 | profile 已建但无语句 | 「还没有命名语句」+ 「新建语句」+ 说明“插件只能调用这里封装好的语句” |
| 语句编辑 | 选中/新建语句 | 名称、类型、SQL、参数表、选项；底部「测试语句 / 保存」 |
| 测试成功 | 查询/执行/写一行测试通过 | 绿色 banner「测试通过 · 影响 1 行 · 12ms · 已回滚」+ 结果预览（查询最多 100 行） |
| 测试失败 | SQL 语法/约束/权限/超时 | 红色 banner + SQLite 错误码 + 出错行/参数提示；不写入数据 |
| 只读 | 流程运行中 / 无配置权限 | 顶部黄色 banner「当前只读：流程运行中 / 无配置权限」；编辑控件禁用；测试连接仍可用 |
| 未保存 | 改了 profile/语句 | 页脚「未保存」+ 保存/取消；切换页签或关闭时确认 |
| 删除确认 | 点删除 | 模态：删除 profile 会同时删除 N 条语句；当前方案有 M 个步骤引用 → 禁止删除或要求先解除 |
| 未来 Provider | 选择 SQL Server/MySQL | v1 只显示 SQLite；其它 Provider 置灰并标注「后续版本」 |

## 六、交互与校验

### 数据库 profile

- **添加**：只支持 SQLite（v1）；允许「新建数据库文件」或「打开已有文件」（用户 2026-10-23 确认允许新建）。
- **文件路径**：只允许 `.db` / `.sqlite` / `.sqlite3`；路径不存在时提示「将在保存时创建」，不静默创建。
- **打开方式**：读写 / 只读。只读 profile 下只能保存/测试 `Query` 语句；`Execute`/`WriteRow` 禁用。只读 profile 允许 `SELECT`/`WITH` 与只读 PRAGMA（`table_info`/`table_list`/`index_list`/`foreign_key_list`）；禁止 `ATTACH`/`DETACH` 与写 PRAGMA。
- **测试连接**：打开文件 → `SELECT sqlite_version()`；成功显示版本/路径/耗时；失败显示 SQLite 错误码。
- **删除**：检查当前方案引用；有引用时列出步骤名并禁止删除；无引用时二次确认。
- **改名**：允许改显示名（用户 2026-10-23 确认）；`ProfileId` 不变；`.vms` 引用不断链。

### 命名语句

- **名称**：profile 内唯一；建议英文/下划线（`WriteResult`）；只做展示，身份是 `StatementId`。
- **类型**：查询（Query）/ 执行（Execute）/ 写一行（WriteRow）。
- **SQL**：单条语句（v1 不允许多语句）；参数一律 `@name`；保存前校验：
  - 占位符必须与参数表一一对应；
  - 必填参数必须有值或默认值；
  - 只读 profile 只允许 `SELECT`/`WITH` 与只读 PRAGMA（`table_info`/`table_list`/`index_list`/`foreign_key_list`）；
  - 禁止 `ATTACH`/`DETACH`/`PRAGMA` 写操作（v1 白名单）。
- **参数表**：名称、类型（Text/Integer/Real/Boolean/DateTime/Blob）、必填、默认值、说明。插件步骤按这张表生成参数输入行。
- **测试语句**：`Query` 只读执行并预览前 100 行；`Execute`/`WriteRow` 默认在事务中执行后 **ROLLBACK**，banner 明示「已回滚，未写入数据」。测试区提供「测试后回滚」勾选项（默认勾选）；取消勾选时必须二次确认，并明确「会真实写入数据库」（用户 2026-10-23 选择“可以选择”）。
- **保存**：校验通过才落盘；失败定位到具体参数/行。
- **删除**：检查当前方案引用；有引用时列出步骤名并禁止删除。

### 权限与审计

- 页面编辑受 `IHardwareEditGateAware` 控制：流程运行中或无管理员权限 → 只读 banner。
- 页脚显示「最近修改：admin · 2026-10-23 10:12」；SQL 变更记日志（语句名 + SQL 哈希，不记参数值）。
- 不显示/不落盘密码（SQLite v1 无密码）；未来 SQL Server/MySQL 的密码走 DPAPI，`.vms` 只存 CredentialId。

## 七、数据模型（设计口径）

```text
DatabaseProfile
  ProfileId (GUID)        显示名 Name
  Provider (Sqlite)       FilePath
  OpenMode (ReadWrite/ReadOnly)
  TimeoutSeconds          MaxRows
  CreatedAt / ModifiedAt / ModifiedBy

NamedStatement
  StatementId (GUID)      ProfileId
  Name                    Kind (Query/Execute/WriteRow)
  Sql                     TimeoutSeconds
  MaxRows                 MaxAffectedRows
  IsEnabled               CreatedAt / ModifiedAt

StatementParameter
  Name                    DbType
  IsRequired              DefaultValue
  Description
```

插件步骤只存：`ProfileId` + `StatementId` + 参数值（按 `StatementParameter` 生成输入行）。

## 八、契约保留（不能改）

| # | 契约 | 为什么 |
| --- | --- | --- |
| C1 | 数据库页只做配置/封装，不执行流程 | 执行归 `Plugin.Database` + 宿主服务 |
| C2 | 插件层只能选 profile + 语句名 + 参数 | SQL/连接串不进 `.vms`，避免明文与误用 |
| C3 | 身份用 GUID，显示名可改 | 改名不断链；删除前查引用 |
| C4 | 连接/事务/参数化归宿主 | 插件不持有连接，不做字符串拼接 |
| C5 | 编辑受 `IHardwareEditGateAware` 门禁 | 与相机/通讯页同一口径，不在页面里自判角色 |
| C6 | 视觉全部走 `Dialog*` 令牌 | R140/R117，不新增硬编码颜色 |

## 九、令牌映射（Web → WPF）

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--db-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--db-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--db-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--db-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--db-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--db-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--db-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--db-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--db-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--db-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--db-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--db-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--db-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--db-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--db-success` | `#22C55E` | `DialogSuccessBrush` |
| `--db-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--db-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--db-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--db-danger` | `#EF4444` | `DialogDangerBrush` |
| `--db-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--db-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--db-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--db-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--db-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--db-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--db-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |

不新增颜色语义；实施时只引用既有键，不改既有键值。

## 十、WPF 复刻计划（第 ② 步，待开工）

| 设计项 | WPF 落点 |
| --- | --- |
| 左栏「数据库」导航项 | `HardwareManagerViewModel.NavItems` 增加一项；`HardwareManagerView.xaml` 增加 `TabItem` |
| 页面骨架 | 新 `DatabasePageView.xaml` / `.xaml.cs`，实现 `IEmbeddedDialogPage` + `IHardwareEditGateAware` |
| 数据库列表 | `ListBox` + `DataTemplate`（名称/SQLite chip/文件/状态/语句数） |
| 连接页签 | 表单 + 文件选择（`OpenFileDialog`/`SaveFileDialog`）+ 测试连接命令 |
| 命名语句页签 | 左 `ListBox` + 右编辑器；SQL 用 `TextBox`（等宽、`AcceptsReturn`）或 AvalonEdit（若已有依赖） |
| 参数表 | `DataGrid` 或 `ItemsControl`（名称/类型/必填/默认值/说明） |
| 测试结果 | 结果预览 `DataGrid` + 状态 banner（复用 `DialogSuccessSubtleBrush` / `DialogDangerSubtleBrush`） |
| 删除确认 | 复用 `EasyDialog` / `MessageDialog` 基线，不新造弹窗 |
| 断言 | `UIThemeSmokeTest --database-page`：静态令牌扫描 + 页面渲染；`DatabaseChecks`：服务/插件三层 |

## 十一、需要用户拍板

| # | 问题 | 推荐 |
| --- | --- | --- |
| 1 | SQLite 文件路径：允许“新建”还是只允许“打开已有”？ | **已确认：允许新建**；新建时明确提示“保存时创建” |
| 2 | 命名语句是否允许改名？ | **已确认：允许**；身份用 `StatementId`，改名不断链 |
| 3 | 删除有引用的语句/profile | **待确认**：推荐禁止删除并列出引用步骤；先解除再删（见下方说明） |
| 4 | 测试 `Execute`/`WriteRow` 是否回滚？ | **已确认：可选择**；默认回滚，取消勾选需二次确认并提示“会真实写入” |
| 5 | 只读 profile 是否允许 `PRAGMA`/`ATTACH`？ | **已定**：允许只读 PRAGMA 白名单；禁止 `ATTACH`/`DETACH` 与写 PRAGMA |
| 6 | 未来 Provider 的切换位置 | **已定**：profile 卡上的 Provider 下拉；v1 只显示 SQLite，其它置灰 |

## 十二、与 R3 架构评审的对应

- 本设计落实 `docs/方案设计/数据库通讯-架构评审与第一版方案.md` 的 C 方案：数据库页负责 profile + 命名语句；插件只调用。
- 第一版 Provider 固定 SQLite；SQL Server/MySQL 后续版本。
- 凭据：SQLite v1 无密码；未来 SQL 认证走 DPAPI，`.vms` 只存 CredentialId。
- 失败语义、事务、参数化、只读档、审计由宿主服务实现，页面只负责配置与测试。

## 十三、用户确认记录

| 日期 | 问题 | 用户答复 | 设计落点 |
| --- | --- | --- | --- |
| 2026-10-23 | SQLite 是否允许新建文件 | **允许新建** | 文件路径行保留「新建…」；提示“保存时创建” |
| 2026-10-23 | 命名语句是否允许改名 | **允许** | 名称可编辑；身份用 `StatementId` |
| 2026-10-23 | 有流程引用时删除 | **待确认** | 设计稿按“禁止删除 + 列出引用步骤”呈现；推荐此方案 |
| 2026-10-23 | 测试写操作是否回滚 | **可以选择** | 测试区增加「测试后回滚」勾选（默认勾选）；取消需二次确认 |
| 2026-10-23 | 只读 profile 的 PRAGMA/ATTACH | **由实现判断** | 允许只读 PRAGMA 白名单；禁止 `ATTACH`/`DETACH` 与写 PRAGMA |
| 2026-10-23 | Provider 切换位置 | **由实现判断** | profile 卡上的 Provider 下拉；v1 只显示 SQLite，其它置灰 |

### 第 3 问的说明（待用户确认）

“有流程引用时删除”指：某个数据库 profile 或命名语句已经被流程步骤引用时，是否允许直接删除。

- **推荐：禁止删除**。删除前列出引用它的流程/步骤，要求先解除引用；避免流程下次编译突然失败。
- **备选：允许删除**。直接删除，流程下次编译报“找不到语句”，由用户自行修复。

设计稿当前按“禁止删除”呈现（删除确认卡片的「仍然删除」按钮置灰）。用户确认后，此条改为“已确认”。
