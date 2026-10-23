# 数据库插件（Plugin.Database）· 配置窗口设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-23 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `Plugins\Plugin.Database\DatabasePluginView.xaml` + `.xaml.cs`（插件内容区）；宿主外壳 `PluginConfigShellView` 960 × 680 / 最小 640 × 560 仅作既定容器，不在本基线内改动 |
| 设计文件 | `design.html`（自包含、无外部依赖） |
| 证据 | `evidence/design-v1-plugin-writerow.png`（数据库写入）、`evidence/design-v1-plugin-query.png`（数据库读取）、`evidence/design-v1-plugin-execute.png`（数据库执行）、`evidence/design-v1-plugin-states.png`（8 态：无库/无语句/只读/缺必填/类型不匹配/测试失败/空结果/危险语句） |
| 复刻状态 | **未复刻**；本文件是设计基线 |

## 一、定位

数据库页（`docs/UI设计基线/database/`）负责“配置数据库 + 封装命名语句”；本设计是**流程步骤的配置窗口**：只选数据库 profile + 命名语句 + 填参数，不写 SQL、不碰连接串。

- 插件工程：`Plugins\Plugin.Database`（新工程，登记 `VisionMaster.sln`）。
- 插件边界：只引用 `Core.Interfaces`；数据库能力通过 `IExecutionContext.Database`（`IDatabaseAccess`）取得。
- 宿主外壳：`PluginConfigShellView` 960 × 680；本视图只做内容区，不改外壳。

## 二、三个步骤

| 步骤 | Kind | 输入 | 输出 | 用途 |
| --- | --- | --- | --- | --- |
| 数据库查询 | Query | Profile + 语句 + 参数 | `Scalar` / `RowsJson` / `RowCount` / `Columns` / `ElapsedMs` | 读配方、工单、计数 |
| 数据库执行 | Execute | Profile + 语句 + 参数 | `AffectedRows` / `ElapsedMs` | 单条 UPDATE/DELETE/DDL（v1 不做 DDL） |
| 数据库写行 | WriteRow | Profile + 语句 + 参数 | `Accepted` / `AffectedRows` / `ElapsedMs` | 每件结果写一行 |

### 变体差异

| 变体 | 额外校验 | 测试语义 |
| --- | --- | --- |
| Query | 只读 SQL；允许只读 PRAGMA；`RowCount=0` 不算失败 | 只读执行，结果预览最多 100 行（受 MaxRows 限制） |
| Execute | UPDATE/DELETE 必须带 WHERE；只读 profile 禁止 | 事务执行后回滚，banner 明示“未写入数据” |
| WriteRow | 单行 INSERT/UPSERT；必填参数校验 | 事务执行后回滚，banner 明示“未写入数据” |

> 用户 2026-10-23 确认：**不是独立插件**；三个步骤放在同一个 `Plugin.Database` 类库，共用一个配置视图。

三个步骤共用同一套视图布局，仅“输出”区、校验规则和测试语义不同。`design.html` 分别给出三个变体的完整画面：
① 数据库写入（WriteRow）、② 数据库读取（Query）、③ 数据库执行（Execute）。

## 三、设计方向

1. **左选右填**：左栏选数据库与语句；右栏只出现该语句定义的参数行。
2. **SQL 只读预览**：插件里不提供 SQL 编辑器；SQL 在硬件管理 → 数据库页维护。
3. **参数由语句定义生成**：名称/类型/必填/默认值来自 `NamedStatement`；每行可常量、可链接上游（`LinkableValueEditor`）。
4. **输出固定**：按步骤类型固定输出端口，不做自由 SQL 列。
5. **只读 profile 禁止写步骤**：`OpenMode=ReadOnly` 时 Execute/WriteRow 直接禁用并说明原因。
6. **测试在插件内**：提供「测试语句」按钮，用当前参数执行；默认回滚（与数据库页一致）。
7. **不重复配置**：连接串、凭据、SQL 都不在插件窗口出现；只显示 profile 名/语句名/SQL 预览。

## 四、布局结构

```text
960 × 680（宿主外壳）
├─ 标题栏 72（宿主）：图标 + 步骤名 + 副标题 + 关闭
├─ 内容区 552（本视图）
│  ├─ 左栏 320
│  │  ├─ 「数据库与语句」标题
│  │  ├─ Profile 卡 88：名称 + SQLite chip + 文件 + 状态
│  │  ├─ 语句卡 88：名称 + Kind chip + 参数数 + 启用状态
│  │  ├─ SQL 预览 170（只读、等宽、折叠）
│  │  └─ 提示：SQL 在硬件管理 → 数据库页维护
│  └─ 右栏 600
│     ├─ 「参数（4）」标题 + 必填统计
│     ├─ 参数行 4 × 48：名称/类型/必填 + 常量框 + 🔗
│     ├─ 「输出（3）」标题
│     ├─ 输出行 3 × 36：名称 + 类型徽章 + 说明
│     └─ 测试结果 banner 44 + 「测试语句」按钮
└─ 页脚 56（宿主）：状态胶囊 + 耗时 + 执行 / 取消 / 确认
```

### 尺寸刻度

| 项 | 值 |
| --- | --- |
| 窗口 | 960 × 680（Min 640 × 560） |
| 左栏 | 320 |
| 内容内边距 | 20 |
| Profile 卡 / 语句卡 | 88 高 / 10 圆角 |
| SQL 预览 | 170 高 / 等宽 11 |
| 参数行 | 48 高（标签 150 + 输入 260 + 链接 32） |
| 输出行 | 36 高 |
| 输入框 | 34 高 / 8 圆角 |
| 按钮 | 32 高（主/次）/ 28（行内） |
| 字号 | 步骤名 14 / 区块 12 / 参数 11 / 徽章 9.5 |
| 动效 | 150ms（hover/focus） |

## 五、状态覆盖

| 状态 | 触发 | 设计表现 |
| --- | --- | --- |
| 默认 | 打开步骤配置 | 选中上次的 profile/语句；参数按语句定义生成；输出固定 |
| 无数据库 | 宿主没有 profile | 左栏空态「还没有数据库」+ 「去硬件管理 → 数据库添加」；参数/输出区收起；确认禁用 |
| 无语句 | profile 下没有语句 | 空态「还没有命名语句」+ 「去数据库页新建语句」；确认禁用 |
| 只读 profile + 写步骤 | OpenMode=ReadOnly 且 Kind=Execute/WriteRow | 黄色 banner「只读数据库不允许写操作」；确认禁用 |
| 语句类型不匹配 | 选到 Query 却打开 WriteRow 步骤 | 红色 banner「该步骤需要 WriteRow 语句」；确认禁用 |
| 缺必填参数 | 必填参数为空且未链接 | 行内红框 + 「必填」；确认禁用 |
| 参数已链接 | 点 🔗 选上游 | 显示地址条（如 `视觉检测_0.Result`）；常量框只读 |
| 测试中 | 点「测试语句」 | 按钮禁用 + 脉冲点；状态「测试中」 |
| 测试成功 | 执行成功 | 绿色 banner「测试通过 · 影响 N 行 · Xms · 已回滚」 |
| 测试失败 | SQL/约束/超时 | 红色 banner + SQLite 错误码 + 下一步建议 |
| 查询结果为空 | Query 返回 0 行 | `RowCount=0` / `Scalar=null` / `RowsJson=[]`；**不视为失败**，下游可用 If 判空 |
| 危险语句 | UPDATE/DELETE 未带 WHERE | v1 直接拒绝保存/测试，提示补 WHERE；确需整表操作走后续版本的显式确认 |
| 参数变化 | 改 profile/语句 | 旧参数值清空并提示「语句已切换，参数已重置」 |

## 六、参数与输出契约

### 参数行

- 来源：`NamedStatement.Parameters`（名称/类型/必填/默认值/说明）。
- 每行是一个动态 `InputPort<object>`（或按类型的强类型端口）；配置窗口用 `LinkableValueEditor` 承载常量/链接。
- 参数名就是 SQL 里的 `@name`；不允许在插件里新增/删除参数。
- 必填且无默认值 → 未填时确认禁用；类型按 `DbType` 校验。

### 输出端口

| 步骤 | 输出 |
| --- | --- |
| Query | `Scalar(object)`、`RowsJson(string)`、`RowCount(int)`、`Columns(string)`、`ElapsedMs(double)` |
| Execute | `AffectedRows(int)`、`ElapsedMs(double)` |
| WriteRow | `Accepted(bool)`、`AffectedRows(int)`、`ElapsedMs(double)` |

输出行只读；类型徽章与数据库页一致（文本/整数/数值/布尔）。

## 七、契约保留（不能改）

| # | 契约 | 为什么 |
| --- | --- | --- |
| C1 | 插件只引用 `Core.Interfaces`；数据库能力走 `IExecutionContext.Database` | 插件不引用宿主/Provider |
| C2 | 插件只存 `ProfileId` + `StatementId` + 参数值 | SQL/连接串不进 `.vms` |
| C3 | SQL 不在插件窗口编辑 | 统一在数据库页维护、测试、审计 |
| C4 | 参数行由语句定义生成 | 不在插件里自由加参数，避免 SQL 与参数漂移 |
| C5 | 输出端口按步骤类型固定 | 不让用户把任意 SQL 列映射成端口 |
| C6 | 只读 profile 禁止写步骤 | 与数据库页/宿主服务同一判据 |
| C7 | 视觉走 `Dialog*` 令牌 | R140/R117 |

## 八、令牌映射

与 `docs/UI设计基线/database/README.md` §九 相同：`DialogSurfaceBrush` / `DialogRowAlternateBrush` / `DialogBorderBrush` / `DialogTextPrimaryBrush` / `DialogAccent*` / `DialogSuccess*` / `DialogDanger*` / `DialogWarning*`。不新增颜色语义。

## 九、WPF 复刻计划（待开工）

| 设计项 | WPF 落点 |
| --- | --- |
| 插件类 | `Plugins\Plugin.Database\DatabaseQueryPlugin` / `DatabaseExecutePlugin` / `DatabaseWriteRowPlugin`（同一程序集） |
| 视图 | `DatabasePluginView.xaml` / `.xaml.cs`，实现 `IPluginCustomViewProvider` |
| 左栏 | Profile/Statement 两个 `ComboBox` + 只读 SQL `TextBox`（等宽） |
| 右栏 | 参数 `ItemsControl`（行模板含 `LinkableValueEditor`）+ 输出 `ItemsControl` |
| 动态端口 | 参数按语句定义重建 `InputPort<object>`；输出按步骤类型固定 |
| 测试 | 调用 `IExecutionContext.Database.TryExecute/TryQuery`；默认回滚，结果 banner |
| 宿主注入 | `FlowEngineService` / `PluginConfigShellViewModel` / `PluginTestRunner` / `ExecutionContext` 四处 |
| 断言 | `UIThemeSmokeTest --database-plugin`（静态令牌 + 渲染）；`DatabaseChecks`（参数/输出/只读/类型不匹配） |
| 帮助 | `Plugins\Plugin.Database\Help\{类名}\`，R41 档位，R62 `--no-incremental` |

## 十、待拍板

| # | 问题 | 推荐 |
| --- | --- | --- |
| 1 | 参数行用 `InputPort<object>` 还是按类型强类型端口 | 先用 `InputPort<object>` + `DbType` 校验；需要编译期类型时再细化 |
| 2 | 测试按钮是否允许取消回滚 | 与数据库页一致：默认回滚，可取消但二次确认 |
| 3 | 语句类型不匹配时是否允许保存 | 禁止保存；提示去数据库页改 Kind |
| 4 | 无 profile 时是否允许手填连接串 | 禁止；必须先在数据库页添加 profile |
| 5 | 输出 `RowsJson` 是否截断 | 受数据库页 `MaxRows` 限制；超出标记 `Truncated` |
| 6 | 查询返回 0 行是否算失败 | **已定：不算失败**；`RowCount=0` / `Scalar=null`，下游 If 判空 |
| 7 | UPDATE/DELETE 无 WHERE | **已定：v1 直接拒绝**保存/测试；整表操作走后续版本的显式确认 |
