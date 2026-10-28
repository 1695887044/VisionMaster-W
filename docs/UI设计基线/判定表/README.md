# 判定表（Plugin.JudgeTable）· UI 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | **v1（草案）** |
| 出稿日期 | 2026-10-27 |
| 确认人 | **待用户确认**（R132：验收由用户裁定） |
| 覆盖范围 | `Plugin.JudgeTable` 的规则表配置弹窗：全宽表格 + 可展开行 + 每行结果生成**运行时变量/动态输出端口**（bool，可关）；表级固定端口 `判定`/`失败数`。不含判定算法、流程引擎、变量系统实现 |
| 设计文件 | `design.html`（自包含，无外部依赖，浏览器直接打开） |
| 设计画布 | 1280 × 853（与 `app-shell-chrome` v2 同尺寸） |
| 令牌来源 | `docs/UI设计基线/app-shell-chrome/` v2：`surface / surface-2 / surface-3 / border / text / text-2 / text-3 / primary / primary-strong / primary-soft / primary-light / success / danger / warning / radius / 40·48·28`；**无新增颜色** |
| 状态 | 设计稿 v1 已出，**待用户确认**；未复刻进 WPF |

## 定位

按 `AGENTS.md` §30.1 四步流程，本文件是**第 ① 步（Web 设计基线）**的产物。

- Web 只作设计媒介，**不进构建、不部署、不作为第二客户端维护**（R126）。
- 确认后才进第 ③ 步 WPF 复刻；第 ④ 步以「同一屏、同一状态」截图对照验收（R129 / R132）。
- 本稿未改任何 WPF 代码。

## 交互说明（可点的部分）

1. **行展开**：点行或左侧 `▸` 展开；展开行显示数据源、模式、期望/规格、Actual、判定算式、结果输出。首条失败行默认展开。
2. **结果输出（每行可选）**：展开行右侧「结果输出」开关 + 只读的运行时变量名 + 类型徽标。
   - 关闭 = 不生成；折叠行显示「未生成」。
   - 打开 = 每轮判定后把 `IsOk` 写入执行期运行时变量 `表名.规则名.结果`（bool）；折叠行显示 `→ 表名.规则名.结果`。
   - 停用行不生成、不写入。
   - **不在这里选全局变量**：要对外由下游「变量赋值」Scope=Global 从运行时变量映射；判定表不直接写全局。
3. **展开全部 / 收起全部**：工具条两个按钮；不改变每行的生成开关。
4. **表级结果**：固定端口 `表名.判定`（bool）、`表名.失败数`（int）；摘要右侧显示端口名。
5. **结果计数**：摘要「结果输出 N 条」随开关实时更新；停用行不计入。

## 实现前提（现有接口，不新造）

- 程序**已有**动态生成接口：`IDynamicOutputProvider` + `AddDynamicOutput`，端口快照落 `StepModel.OutputPortDefinitions`；范式 `Plugin.CreateRoi`（ROI 的 `Crop_*` / `Domain_*` / `OffsetRow_*` 就是这么生成的）。
- 判定表**复用这条链**：每条规则生成一个 `bool` 动态输出端口，并回写快照；`RebuildDynamicOutputs()` 在规则增删/改名时重建。
- 运行期同时写 `context.LocalVariables[运行时变量名] = IsOk`，得到**运行时变量**；**不写全局变量、不新增变量声明接口**。
- 全局映射只走现有「变量赋值」：Scope=Runtime 从端口/运行时变量取值 → 局部变量；Scope=Global 再映射到全局。判定表自身不碰全局变量。
- 数组：需要批量结果时用 `bool[]` 端口/运行时变量，下游用 `[i]`（`ArrayIndexProxyPort`）或 `IArrayElementWritable.TryWriteElement` 取/写元素。

## 结果输出的语义（设计口径）

- 端口：`表实例名.规则名.结果`（bool），按规则顺序生成；停用/关闭生成的行不产生端口。
- 运行时变量：与端口同名，执行期写入 `context.LocalVariables`；每轮覆盖（`For` 循环里是最后一圈的值，要看历史请用数组/记录）。
- 写入时机：本行判定完成后写一次；失败/异常轮不写（保留上一次值）。
- 不阻塞：写入失败只记日志/状态栏，不改变本行判定与流程执行（与 R100「系统变量 setter 静默忽略」同口径）。
- 表级：`表名.判定` / `表名.失败数` 为固定端口；是否再出 `表名.结果集`（bool[]）见 Q7。
- 动态下标（如 `For.Index`）本稿不做：运行时变量名/端口名是常量；数组元素在**下游连线**时用 `[i]`，是否支持变量下标见 Q6。

## 令牌映射（复刻时只认这张表）

| 用途 | `design.html` 变量 | WPF 资源 |
| --- | --- | --- |
| 弹窗/表格底 | `--surface` | `DialogSurfaceBrush` |
| 次级面/表头 | `--surface-2` | `ScadaTitleBarBrush` / `DialogSurfaceBrush` |
| 三级面/胶囊 | `--surface-3` | `DialogMutedBrush`（按现有分册取） |
| 分隔线 | `--border` / `--border-soft` | `DialogBorderBrush` |
| 主文字 | `--text` | `DialogTextBrush` |
| 次文字 | `--text-2` | `ScadaSecondaryTextBrush` |
| 弱化文字 | `--text-3` | `ScadaFaintTextBrush` |
| 主色 | `--primary` / `--primary-strong` | `DialogAccentBrush` |
| 选中/绑定底 | `--primary-light` / `--primary-soft` | `DialogAccentSoftBrush` |
| OK | `--success` / `--success-chip` | `DialogSuccessBrush` |
| NG | `--danger` / `--danger-chip` | `DialogDangerBrush` |
| 警告/待确认 | `--warning` / `--warning-chip` | `DialogWarningBrush` |
| 圆角 6 / 8 | `--radius-sm` / `--radius-md` | 现有 `Dialog*CornerRadius` |
| 顶栏 40 / 运行条 48 / 底栏 28 | `--titlebar-h` / `--runbar-h` / `--statusbar-h` | `Shell.xaml` Row0/Row1/Row3 |

## 已知偏差（R130 / R131，逐条列出，待用户裁定）

1. **字体度量**：Web 用 `Microsoft YaHei UI` + `system-ui`；WPF 同族但行高与中文墨迹宽度不同，表格列宽需按 WPF 实测微调。
2. **滚动条宽度**：Web 默认约 8px 悬浮滚动条；WPF 系统滚动条约 17px，表格 `Actual` 列与原因列需预留。
3. **DPI**：40/48/28 在 125% / 150% 缩放下取整，可能出现 1px 误差。
4. **阴影层数**：Web 弹窗用两层阴影；WPF 建议 1 层 `DropShadowEffect`。
5. **动效曲线**：Web 行展开/开关 150ms；WPF 用 `QuadraticEase` 或线性 150ms 近似。
6. **文本截断**：Web 用 `text-overflow: ellipsis`；WPF 需 `TextTrimming=CharacterEllipsis` + `MaxWidth`。
7. **焦点态**：Web 用 `outline`；WPF 弹窗内可聚焦控件挂 `DialogFocusVisual`（R135）。
8. **无障碍**：Web 有 `role="switch"` / `aria-expanded`；WPF 需 `AutomationProperties.Name` 补齐。

## 待用户拍板

| # | 问题 | 建议 |
| --- | --- | --- |
| Q1 | 结果绑定变量选择方式：下拉（本稿）还是变量浏览器（可搜索/分组）？ | 先下拉；变量多时再换变量浏览器 |
| Q2 | 「取反写入」是否保留？ | **保留**（NG 变量语义常用） |
| Q3 | 表级结果是否也允许绑定 / 关闭？ | **允许绑定，默认开**；摘要显示变量名 |
| Q4 | 展开行默认策略：首条失败展开 / 全部收起 / 记住上次？ | **首条失败展开**（现场先看问题） |
| Q5 | 停用行是否保留绑定配置？ | **保留但不写入**（重新启用即恢复） |
| Q6 | 数组下标是否允许来自变量（如 `For.Index`，每轮动态求值）？ | v1 先做**常量下标**；动态下标 v2 再评估（需运行期求值 + 越界策略） |
| Q7 | 表级是否输出 `表名.结果集`（bool[]，与规则顺序对齐）？ | **建议输出**；批量工位/报表可直接按 `[i]` 取 |

## 未复刻声明

本稿只出 Web 设计基线，**未改任何 WPF 代码**。确认后再按 §30.1 复刻到 `Plugin.JudgeTable` 的自定义视图，并补 `Help\` 手册与断言。
