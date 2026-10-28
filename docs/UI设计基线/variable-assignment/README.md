# 变量赋值 · 配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | **v3**（名字行策略：全局只能选 / 运行时可选可填 / 值只能连） |
| 设计日期 | 2026-10-27 |
| 确认日期 | 2026-10-27（用户确认「好的 按照你这个来改」） |
| 确认人 | 用户 |
| 覆盖范围 | `Plugins\Plugin.Utility\VariableAssignmentView.xaml` + `.xaml.cs`（插件内容区）；宿主外壳 `PluginConfigShellView.xaml` 只作为 960×680 参考，不改 |
| 宿主尺寸 | 默认 960×680；最小 640×560；插件内容区约 920×520 / 600×400 |
| 设计文件 | **[design-v3.html](design-v3.html)**（v3，自包含、可点击、内联 SVG）；v2 存档 [design-v2.html](design-v2.html)；v1 存档 [design.html](design.html) |
| 复刻状态 | **已复刻（2026-10-27）**：WPF 视图、插件行模型、动态端口、断言与手册已落地；真机 2 行往返 / 作用域切换 / 全局失效 / 640 窄档已验证。证据 `evidence/after-render-v3-*.png` + `artifacts/render/20261027/variable-assignment-real/` |

### 版本历史

| 版本 | 日期 | 主题 | 状态 |
| --- | --- | --- | --- |
| v1 | 2026-10-20 | 单条赋值的 Web 化重构（作用域卡片 / 名字候选 / 值行包裹） | 已复刻（2026-10-20），设计待用户确认；原文保留在下方存档 |
| v2 | 2026-10-27 | 一个节点写多个变量：行列表 + 每行独立值端口 + 旧配置迁移 | 已出 v2；v3 在其上只改名字行策略；未复刻 |
| v3 | 2026-10-27 | 名字行策略：全局只能选（失效保留）、运行时可选可填（四档提示）、值行变量引用只能连 | 已确认并复刻（2026-10-27）；真机验证通过 |

## v3：名字行策略（2026-10-27）

### 目标与范围

v3 在 v2 的行列表基础上，**只改「变量名」这一行的策略与提示**；行列表、值行控件、旧配置迁移、640 窄档、批量操作全部沿用 v2。

- **全局变量名**：只能从变量管理里选（可搜索）；当前值不在快照里 → 只读「失效」项 + 红色提示；不清空、不自动改目标。
- **运行时变量名**：候选来自上游「变量定义」，允许手填；未命中按四档提示；手填给「手填」徽标。
- **值行**：常量可填；上游端口 / 全局变量 / 运行时变量一律走链接，不提供手输变量名。

> **2026-10-27 用户确认**：配置窗口与变量管理不会同时出现（模态）；全局失效保留原名、由运行期检查点名；运行时变量必须由人把控，加提示。

### 查看方式

- 设计稿：[design-v3.html](design-v3.html)。
- 状态深链：`?state=default|empty|single|policy|error|legacy|limit`；宽度 `?w=960|640`；截图模式 `?chrome=window`；全局选择器 `?state=policy&pop=global&row=1`（`row` 从 1 起）。
- 存档：[design-v2.html](design-v2.html)、[design.html](design.html)。
- 证据：[default](evidence/after-render-v3-default-960.png)、[policy](evidence/after-render-v3-policy-960.png)、[narrow](evidence/after-render-v3-narrow-640.png)。全局选择器是交互浮层，headless 截图不包含 fixed 浮层，需在浏览器里点开；v2 的 empty/error/legacy/limit 证据仍适用（v3 未改这些状态的结构）。

### 策略表

| 行 | 全局作用域 | 运行时作用域 |
| --- | --- | --- |
| **变量名** | **只能选**（搜索；失效项只读保留；不自动替换 / 清空） | **候选 + 允许手填**；手填给「手填」徽标；未命中按四档提示 |
| **值** | 常量手输；上游端口 / 全局变量 / 运行时变量**只能连** | 同左 |

### 提示分级（运行时四档 + 全局失效）

| 情况 | 级别 | 文案 |
| --- | --- | --- |
| 命中上游变量定义 | 信息（蓝） | 写入运行时变量「X」（Type）· 说明 |
| 未命中 + 未勾自动创建 | 警告（琥珀） | 运行期会报「本地变量 X 不存在」；请在上游「变量定义」声明，或勾选「自动创建」 |
| 未命中 + 已勾自动创建 | 信息（蓝） | 运行期会新建「X」（类型由值决定）；名字由你负责，拼错会新建另一个变量 |
| 跨作用域同名 | 警告（琥珀） | 「X」是全局变量；要写它请把作用域改成「全局变量」 |
| 全局失效 | 错误（红） | 变量管理里没有「X」：运行期会明确报「找不到全局变量」，不会静默新建 |

### 状态清单

| 状态 | 看什么 | 关键判据 |
| --- | --- | --- |
| 默认 3 行 | 全局行是「只能选」按钮（名字 + 类型），运行时行是输入 + 候选箭头 | 全局行没有文本输入；运行时行按需出现「手填」徽标 |
| 策略演示 | 第 1 行全局失效（红）、第 2 行运行时手填未勾自动创建（琥珀）、第 3 行运行时手填已勾（蓝）、第 4 行全局有效链接 | 红 / 琥珀 / 蓝三档同时可见；顶部「有 1 行配置有问题」 |
| 全局选择器 | 搜索框 + 当前失效项 + 全局候选 | 失效项只读；只能从候选里选；没有「手填变量名」入口（搜索框只过滤候选） |
| 640 窄档 | 全局选择按钮在名字列，类型 chip + 箭头；行模板仍两行式 | 名字不截断；上移下移隐藏 |

### 与 v2 的差异

| 项 | v2 | v3 |
| --- | --- | --- |
| 全局名字 | 可编辑 ComboBox（可手输） | 只能选按钮 + 搜索候选；失效项只读 |
| 运行时名字 | 可编辑 + 候选 | 不变；新增「手填」徽标与四档提示 |
| 全局缺失级别 | 琥珀警告 | 红色错误（硬） |
| 全局同名只在运行时 | 琥珀警告 | 红色错误 |
| 运行时未命中 + 已勾自动创建 | 无行内提示 | 蓝色信息（右栏提示 + 手填徽标） |
| 值行 | 常量 + 链接 | 不变；文案明确「变量引用只能连」 |
| 顶部错误横幅 | 琥珀「未填完」 | 红色「配置有问题」 |
| 行内徽标 | 原配置 | 新增「失效」「手填」 |

### 契约保留（新增）

C1-C5 沿用 v2；v3 新增：

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C6 | 全局名字只能选 | 名字来自 `PluginConfigContext.Variables` 的 Global 项；不接受自由文本；失效项只读保留，不自动替换 |
| C7 | 值行不手输变量名 | 常量用文本框；变量引用必须用 `LinkableValueEditor` 的链接，避免把名字当字面量 |

### 设计决策

- **为什么全局只能选**：变量管理是权威注册表；配置窗口是模态，变量管理不会同时打开，快照在窗口生命周期内稳定（`VisionMaster\Services\StepParameterDialog.cs:112`、`PluginConfigShellViewModel.cs:189-193`）。
- **为什么失效项保留**：清空会让运行期错误变成「变量名不能为空」，丢失原名字；自动选第一个会静默改目标。保留原名，运行期错误能点名（`Core\Binding\GlobalVariableWriter.cs:59-63`）。
- **为什么运行时手填**：脚本 `SetVar` / `CreateIfNotExists` 动态创建的名字不在快照里（`Plugins\Plugin.CSharpScript\ScriptContext.cs:121-137`、`VisionMaster\Helpers\PluginVariableOptions.cs:109-129`）；只给下拉会漏掉合法变量。
- **为什么值行只能连**：手输变量名是字面量，不是引用；要取值必须走 `RuntimeVariable` / `GlobalVariable` 连线（`Engine\FlowCompiler.cs:964-988`）。
- **为什么不做活变量服务**：模态窗口内快照稳定；活服务（查询 / 刷新 / 订阅）是另一个宿主契约，见延后清单。
- **可选后端增强**：编译期校验常量全局名（`FlowCompiler` 读 `Rows` + `VariableRegistry`），让错误提前到编译期；本 UI 稿不依赖它。

### 令牌映射

v3 不新增颜色令牌，复用 v2 映射。新增语义用法：

| 语义 | Web 变量 | WPF 资源键 |
| --- | --- | --- |
| 全局失效按钮 / 徽标 | `--va-danger-soft` / `--va-danger-border` / `--va-danger-text` | `DialogDangerSubtleBrush` / `DialogDangerBorderBrush` / `DialogDangerTextBrush` |
| 手填徽标 | `--va-surface-3` / `--va-border` / `--va-text-3` | `DialogRowHoverBrush` / `DialogBorderBrush` / `DialogTextMutedBrush` |
| 全局选择按钮 | 同输入框 | `DialogSurfaceBrush` + `DialogBorderBrush`（或 `DialogInput`） |
| 搜索框 | 同输入框 | `DialogInput` |

### 复刻状态与验收

- **已复刻（2026-10-27）**：WPF 视图、插件行模型（`AssignmentRow` / `AssignmentRowVm`）、每行动态端口
  `Value_<RowId>`、旧配置迁移、`OnConfirm` 回写、640 窄档、断言与六页手册已落地。
- 复刻实现映射：

| 设计项 | WPF 实现 |
| --- | --- |
| 行列表 + 右侧详情 | `ListBox` + 宽/窄两套行模板；`SelectedRow` 驱动右栏；错误横幅「定位」跳第一条错误 |
| 全局名字只能选 | `ToggleButton` + `Popup` + `ItemsControl FilteredCandidates` + `SelectGlobalCandidateCommand`；失效项 `IsEnabled=False` |
| 运行时名字可选可填 | 可编辑 `ComboBox`（`Text=Row.VariableName`、`IsTextSearchEnabled=False`）+ 手填徽标 + 四档提示 |
| 值行只能连 | 每行 `cv:LinkableValueEditor Port=Port StepData=StepData`；名字行无链接图标 |
| 行操作 | 添加 / 复制（清空目标名）/ 上移 / 下移 / 删除（有值链接先确认） |
| 640 窄档 | `SizeChanged` 写 `IsNarrow = ActualWidth < 700`，行模板两行式 |
| 作用域切换 | `AssignmentRow.Scope` setter 忽略空值；`RefreshHint` 通知 `IsGlobal`/`IsRuntime`/`ScopeText`/`CanCreate`/`ShowLock` |

- 真机验证（2026-10-27，`VisionMaster.exe` Debug，样例方案临时副本）：
  - 旧配置迁移为 1 行，旧「值」端口保留；
  - 运行时手填 `TestVar` → 琥珀警告；勾「自动创建」→ 蓝色信息；
  - 切全局 → 名字列换「选择全局变量」、右栏徽标换「全局变量」、自动创建变锁、失效项红色提示；
  - 全局选择器搜索 `zzz` → 失效候选被过滤；失效项只读（`IsEnabled=False`）；
  - 加第二行 `SecondVar`、复制（目标名清空）、上移/下移、删除空行（直接删）；
  - 确认后重开 → 2 行名字/作用域/提示全部往返；640×680 两行式布局可用；
  - 截图：`artifacts/render/20261027/variable-assignment-real/config-2rows-960.png`、`config-narrow-640.png`。
- 断言：`UIThemeSmokeTest --plugin-views Plugin.Util`（两行注入 + 全局候选/选中项）、
  `FlowCanvasChecks`（名字行静态扫描/全局只能选/运行时绑定/真实输入上报）、`HelpManualCheck`（M 档）。
- 验收：按 R132 由用户裁定；本页与证据已同步，差异清单见「已知偏差」。
- 复刻后发现的真机缺陷（已修）：作用域 `ComboBox` 初始化把 `SelectedValue` 推成 `null`，
  `AssignmentRow.Scope` 旧 setter 回落 `Runtime`，导致「全局」行打开即变「运行时」；
  且 `IsGlobal`/`IsRuntime` 未通知，留下半旧模板。修法见上表「作用域切换」两行，已真机复测。

### 未纳入 / 延后

- 活变量服务 / 刷新 / 订阅（需要宿主契约）。
- 编译期全局名校验（可选后端增强，见上）。
- 变量浏览器 / 变量管理入口。
- 拖拽排序、配置态类型预览、每行独立输出端口、深色主题。

### 变更规则（v3）

v3 已于 2026-10-27 确认并复刻，本页与 `design-v3.html` 转为只读。需要调整设计时升 v4，并同步更新
`design-v3.html`、本 README、`VariableAssignmentView.xaml` 与相关断言；不得直接改已确认的 v3 文件。

---

## v2（存档，2026-10-27）

> 以下为 v2 原文；v3 只改名字行策略，行列表 / 值行 / 旧配置迁移 / 640 窄档不变。

### 目标与范围

**一个节点写多个变量**：每行一个作用域、一个变量名、一个值；行顺序即执行顺序；某行失败不阻断后续行，整节点 `Success=false`。

本版覆盖：

- 行列表（添加 / 复制 / 上移下移 / 删除 + 撤销）；
- 每行独立「值」端口（`Value_<RowId>`），行内链接互不影响；
- 作用域按行选择（运行时 / 全局混写）；
- 旧版单条配置迁移（旧「值」连线保留，旧「名字」连线解除并提示）；
- 行内错误 / 警告两级反馈；
- 640 窄档两行式行模板；
- 键盘操作与 32 行软上限。

本版不含：变量浏览器 / 打开变量管理、配置态类型预览、每行独立 `Success/ErrorMessage` 输出端口、拖拽排序、深色主题。

### 查看方式

- 设计稿：[design-v2.html](design-v2.html)（自包含，直接双击打开；交互浮层需在浏览器里点开）。
- 状态深链：`?state=default|empty|single|error|legacy|limit`；宽度 `?w=960|640`；截图模式 `?chrome=window`（隐藏页面工具条与说明，只留 960×680 或 640×680 窗口）。
- v1 存档：[design.html](design.html)。
- 证据截图：
  - [after-render-v2-default-960.png](evidence/after-render-v2-default-960.png)
  - [after-render-v2-error-960.png](evidence/after-render-v2-error-960.png)
  - [after-render-v2-legacy-960.png](evidence/after-render-v2-legacy-960.png)
  - [after-render-v2-empty-960.png](evidence/after-render-v2-empty-960.png)
  - [after-render-v2-narrow-640.png](evidence/after-render-v2-narrow-640.png)

### 结构

- **左栏 `2*`（MinWidth 320）**：标题「赋值设置」+ 行数胶囊 + 「添加一行」；表头（作用域 / 变量名 / 值（常量 / 上游）/ 自动创建 / 操作）；行列表（独立滚动）；空态；底部上限与键盘提示。
- **右栏 `1*`（MinWidth 220）**：当前行卡（第 N 行 / 共 M 行、作用域胶囊、目标名、类型 / 候选数 / 值来源、行内提示、「名字不是值」提醒）；作用域规则卡（运行时 / 全局两条说明 + 一句总提示）。
- **行高 48**（窄档 76，两行式）；表头 30；列表只滚列表，外层不套 `ScrollViewer`。
- **行结构**：作用域胶囊 / 名字输入 + 候选箭头 + 警告图标 / 值（常量输入 + 链接按钮，或链接 chip）/ 自动创建（全局 = 锁）/ 上移下移复制删除；错误 / 警告行在下方展开一行说明。
- **选中态**：选中行左侧 3px 主色条 + 白底；错误行左侧红条 + `DialogDangerSubtleBrush` 浅底；警告行左侧琥珀条。

### 状态清单（6 个深链）

| 状态 | 看什么 | 关键判据 |
| --- | --- | --- |
| 默认 3 行 | 运行时常量 + 全局链接 + 运行时自动创建；选中第 1 行 | 右栏显示当前行类型 / 候选数 / 值来源；行内无错误 |
| 空 | 空态图标 + 「添加第一行」；右栏「未选择行」 | 确认按钮禁用；行数胶囊 0 行 |
| 单行 | 旧配置迁移的最小形态 | 添加 / 复制 / 删除可用；右栏显示单行详情 |
| 行内错误 | 第 2 行全局找不到 = 琥珀警告；第 3 行空名 = 红色错误 | 底部「3 行 · 1 行未填完」+ 红点；错误横幅带「定位」 |
| 旧配置迁移 | 顶部信息横幅 + 行首「原」徽标 + 右栏「原配置」胶囊 | 旧「值」链接保留；横幅可「知道了」关闭 |
| 32 行上限 | 列表滚动；添加按钮禁用 | 底部「已满 32 行」；行数胶囊 32 行 |

### 交互细节

1. **行选中**：点行或聚焦行内输入 → 右侧切到该行；选中行左侧 3px 主色条。点行内按钮也会先选中该行，避免右栏显示错行。
2. **作用域**：点胶囊出两项菜单（运行时 / 全局，带一句用途）；切到全局自动取消「自动创建」并提示；名字留在原处，跨作用域给琥珀警告（不静默清空）。
3. **变量名**：点箭头或输入展开候选；候选按作用域过滤，显示「名字 + 类型 + 说明」；无匹配时显示「仍可手填，运行期会明确报错」；选中落成常量，不建立连线。
4. **值**：常量输入 + 链接图标；点链接图标出上游结果菜单（步骤 · 端口 / 类型 / 地址）；选中后变链接 chip（显示地址 + 解除 ✕）；解除后回到常量输入。
5. **自动创建**：运行时 = 18px 勾选框；全局 = 锁图标 + tooltip「全局变量必须先在变量管理里存在」，不静默失效。
6. **删除**：链接行 / 旧配置行先出内联确认横幅（说明会解除链接）；普通行直接删 + 5 秒「撤销」toast。
7. **添加 / 复制 / 上移下移**：添加在末尾并聚焦名字；复制清空名字并提示选目标（避免两行写同一变量）；上移下移保持选中。
8. **键盘**：`Enter` 在值行 → 下一行（末行 → 新增）；`Enter` 在名字行 → 选中候选 / 跳到值行；`↑↓` 选行；`Delete` 删行；`Ctrl+D` 复制；`Esc` 关浮层 / 取消删除。
9. **行内反馈**：空名 = 红；找不到 / 跨作用域 = 琥珀；全局行自动创建 = 锁；底部状态条按错误 / 警告变色；错误不阻断「确认」（保持运行期明确失败的既有语义）。
10. **上限**：软上限 32 行；到顶禁用添加并显示「已满 32 行」；列表独立滚动。
11. **浮层**：固定定位，列表滚动不裁切；点外部 / `Esc` 关闭；候选面板内部滚动不会误关。

### 640 窄档

- 左栏约 364；行模板两行式：第一行 `作用域(68) + 名字(1fr) + 操作(66)`；第二行 `值(跨两列) + 自动创建(66)`。
- 上移 / 下移在窄档隐藏（宽档可用）；作用域只显示短标签（运行 / 全局），隐藏箭头。
- 右栏 220；规则卡隐藏「一句话」提示；当前行卡头部 chips 允许换行。
- 表头隐藏；底部键盘提示隐藏；「上限 / 行顺序」提示保留。
- 不堆叠两栏（保持左右两栏），避免把行列表压到首屏以下。

### 与 v1 的差异（R130 预判）

| 项 | v1 | v2 |
| --- | --- | --- |
| 主体 | 单表单（作用域卡片 / 名字 / 值 / 自动创建） | 行列表（每行一套）+ 顶部添加按钮 |
| 右栏 | 单个目标变量卡 | 「当前选中行」详情卡（行号 / 作用域 / 类型 / 值来源 / 行内提示） |
| 提示 | 单条 `NameHint` | 每行提示 + 行内错误 / 警告 + 底部状态条 |
| 值 | 单个 `LinkableValueEditor` | 每行一个 `LinkableValueEditor`（`Value_<RowId>` 端口） |
| 作用域 | 全局唯一 | 每行独立 |
| 批量操作 | 无 | 添加 / 复制 / 上移下移 / 删除 + 撤销 |
| 删除 | 无行概念 | 链接行二次确认；普通行可撤销 |
| 上限 | 无 | 32 行软上限 |
| 640 | 左栏整表单滚动 | 两行式行模板，列表独立滚动 |
| 规则卡 | 三条现场提示 | 两条作用域说明 + 一句总提示（腾出行列表高度） |
| 页面媒体查询 | 1024px 以下堆叠 | 600px 以下堆叠；截图模式固定两栏（避免 960 产品窗口被页面媒体查询压成一栏） |

### 契约保留（WPF 复刻时不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | 每行一个 `InputPort<object>`，端口名 `Value_<RowId>`，`IsRequired=false` | `LinkableValueEditor` 按 `Port.Name` 读写链接；`RowId` 稳定，改名不断线 |
| C3 | 变量名是「名字」不是「值」 | 名字行不接通用绑定弹窗；候选来自宿主快照，选中落成常量；只有「值」行可以连线 |
| C4 | 用户编辑只认真实输入事件 | 键入 / 删除 / 剪切 / 粘贴 / 选择 / 拖放才解除旧名字连线；程序灌值不算 |
| C5 | `OnConfirm` 一次回写 | `Rows` JSON 快照 + 各 `Value_<RowId>` 常量一起写入；未连线行才写常量 |
| C6 | 旧键迁移口径 | 旧 `Name/Value/Scope/CreateIfNotExists` 与旧 `LinkedSources["Value"]` 必须迁移或保留兼容端口；旧 `Name` 连线删除并提示 |

### 设计决策

- **ItemsControl + 共享列 Grid，不用 DataGrid / ListView+GridView**：DataGrid 单元格要先进编辑态，内嵌 `LinkableValueEditor` 得双击才能输入；`GridView` 列宽是固定像素、无星号列，640 档无法自适应；若坚持 GridView，R19-R22 三件套一条不能少。
- **作用域按行选择**：同名变量在两个作用域各有一份；强制整组一致会堵死「运行时 + 全局」混写。
- **删除链接行二次确认**：删行会一并 `RemoveLink`；不提示就点确定，用户会以为只是删了一行文字。普通行删除给「撤销」。
- **640 两行式**：窄档左栏约 364，作用域 68 + 名字 + 操作 66 已贴边；第二行放「值 + 自动创建」，避免把输入框压到不可用。
- **32 行上限**：每行一个动态端口，32 行约 100+ 控件；软上限防止误建，到顶禁用添加。
- **错误不阻断确认**：保持现有语义（空名在运行期明确失败）；界面用红色行内提示 + 底部状态提醒，不静默吞掉，也不阻止用户先保存草稿。
- **可编辑名字单元格不要用 `DialogCombo`**：`DialogCombo` 模板没有 `PART_EditableTextBox`（`UI\Controls\Themes\Dialog\Dialog.Input.xaml:105-162`）；用无显式 `Style` 的 `ComboBox` 回落框架默认模板（含 `PART_EditableTextBox`，与 `Dialog.ConditionEditor.xaml:25` 同一做法），或 `TextBox` + 箭头 `Button` + `Popup`。
- **删除按钮用 `DialogIconButton`**：已存在（`UI\Controls\Themes\Fluent\FluentAliases.xaml:55-64`），30×30 方形、`Padding=0`，符合 R68。

### 令牌映射

**v2 不新增颜色令牌**，复用 v1 映射表（见下方存档）。新增语义用法：

| 语义 | Web 变量 | WPF 资源键 |
| --- | --- | --- |
| 错误行底 / 错误横幅 | `--va-danger-soft` | `DialogDangerSubtleBrush` |
| 错误文字 / 图标 | `--va-danger-text` | `DialogDangerTextBrush` |
| 警告横幅 / 警告行 | `--va-warning-soft` / `--va-warning-border` | `DialogWarningSubtleBrush` / `DialogWarningBorderBrush` |
| 警告文字 / 图标 | `--va-warning-text` | `DialogWarningTextBrush` |
| 锁 / 禁用 | `--va-text-3` | `DialogTextMutedBrush` |
| 行选中主色条 | `--va-primary` | `DialogAccentBrush` |
| 链接 chip | `--va-primary-soft` / `--va-primary-border` / `--va-primary-text` | `DialogAccentSoftBrush` / `DialogAccentBorderBrush` / `DialogAccentTextBrush` |
| 图标按钮 30×30 | — | `DialogIconButton`（`Padding=0`，R68） |

Web 稿里的 hover 浅色（`#FBFDFF`）、阴影 rgba、滚动条 `#CBD5E1` 是设计稿表现；WPF 用既有令牌 / 宿主样式，不新增令牌。间距 / 字号 / 圆角仍按 v1 的字面量纪律（仓内暂无间距令牌字典）。

### 复刻状态与验收

- **未复刻**：本轮只出 Web 交互稿，未改 WPF 视图、未构建、未跑断言宿主。
- 证据：5 张 PNG（默认 / 错误 / 迁移 / 空 / 窄档）；交互浮层（作用域菜单、名字候选、上游结果菜单、删除确认、撤销 toast）需在浏览器里点开查看。
- 验收：按 R132 由用户裁定；WPF 复刻后补「同一屏、同一状态」改前改后截图 + 逐项对照表 + 差异清单。
- 复刻前必须同步的断言：`FlowCanvasChecks\VariableAssignmentCheck.cs:351-379`（`LinkableValueEditor` 恰好 1 次 → 按行判据）、`ParallelExecutionChecks` P14（`SetInputValue("Name")` → `Rows`）、`UIThemeSmokeTest\PluginViewSmoke.cs:158-171`（每行 `Port != null` / 自有按钮必须有 `Command`）、`HelpManualCheck` M 档。

### 未纳入 / 延后

- 拖拽排序（本轮用上移 / 下移；gong-wpf-dragdrop 与 R171 单独立项）。
- 配置态类型预览 / 转换器（运行期按声明类型解析）。
- 变量管理入口 / 变量浏览器（需要新的宿主导航契约）。
- 每行独立 `Success/ErrorMessage` 输出端口（本轮只保留节点级输出，避免画布端口爆炸）。
- 深色主题。

### 变更规则（v2）

v2 确认后只读。需要调整设计时升 v3，并同步更新 `design-v2.html`、本 README、`VariableAssignmentView.xaml` 与相关断言；不得直接改已确认的 v2 文件。

---

## v1（存档，2026-10-20）

> 以下为 v1 原文，保留作历史对照；v2 改动以上一节为准。

## 定位

按 `docs/UI设计基线/插件自定义视图Web化改造SOP.md` 的 §5.2：本文件是**Web 设计基线**，Web 只作为设计媒介，不进入构建、不部署。确认后按本基线复刻 WPF 视图；验收以「同一屏、同一状态」的截图对照为准。

## 现状审查（改动理由）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 单张白卡 + `StackPanel`，三行表单（作用域 / 变量名 / 值） | 信息层级平，作用域与目标变量没有「当前写到哪里」的整体感 |
| 2 | 作用域是裸 `ComboBox`（两个 `ComboBoxItem`） | 两个选项的语义差异（本轮有效 / 跨流程存盘）看不出来 |
| 3 | 变量名是裸可编辑 `ComboBox`，提示是一行灰字 | 候选与「写哪个变量、什么类型、找不到会怎样」混在一起，重要提示容易被忽略 |
| 4 | 视图内联 `#FFFFFF / #E5E5E5 / #303133 / #909399 / #606266` | 违反「视图 XAML hex=0」；换肤/统一调整改不动 |
| 5 | 固定 72px 标签列 + 左侧堆叠 | 宽窗口下右侧大片空白，窄窗口下帮助文字换行拥挤 |
| 6 | 「自动创建」是一个孤立 `CheckBox` | 与作用域的关系（仅运行时有效）没有解释 |

## v1 方向

> **一屏完成一次赋值配置**：左侧配置轨负责「写到哪里、写什么名字、写什么值」，右侧目标卡与规则卡负责「现在指向谁、两种作用域差在哪」。只做纯 UI 重构。

1. **作用域卡片化**：两张 56px 卡片（运行时变量 / 全局变量），图标 + 名称 + 一句用途；选中态 = 主色描边 + 主色浅底 + 右侧对勾。
2. **变量名行保持可编辑候选**：仍是可编辑 `ComboBox`；候选来自 `NameCandidates`，选择落成常量，不建立连线。
3. **提示升级为信息条**：`NameHint` 原文进入浅主色信息条，紧贴变量名行；不加第二套状态枚举。
4. **值行只包裹**：`LinkableValueEditor` 保持原样（`Port` / `StepData`），常量与上游链接仍由它完成。
5. **自动创建卡**：勾选框 + 一句「仅运行时变量生效」的说明，仍绑 `CreateIfNotExists.Value`。
6. **右侧目标卡**：显示当前变量名、作用域胶囊、当前作用域候选数；让「写到哪里」始终可见。
7. **右侧规则卡**：运行时 / 全局两种语义 + 三条现场最容易踩的提示；全部是静态说明，不新增功能。
8. **全令牌化**：颜色映射 `Colors.xaml` / `PluginConfigColors.xaml`；视图 XAML 内 0 个十六进制颜色。

## 设计要点（完整清单）

1. 页面节奏：标签 12px / 控件 36px / 帮助 11.5px / 字段间距 14px / 卡片内边距 16px。
2. 作用域卡片：未选中白底 + 浅描边；悬停加深描边；选中主色描边 + `DialogAccentSoftBrush` 浅底 + 右侧 16px 对勾。
3. 变量名输入：36px 高、8px 圆角；右侧 32px 箭头按钮展开候选；候选行 32px，显示「名字 + 类型」。
4. 提示条：浅主色底 + 主色浅描边 + 信息图标；文案完全由 `NameHint` 提供，不在视图里拼业务文案。
5. 值行：输入框 + 36×36 链接按钮；链接后由 `LinkableValueEditor` 自己切换为「链接地址 + 解除」形态（共享控件行为，不在视图里另造）。
6. 自动创建：16px 勾选框 + 12px 文案；下方 11.5px 说明「作用域=全局变量时此选项不生效」。
7. 右栏目标卡：作用域胶囊（运行时=中性灰 / 全局=主色浅底）、20px 变量名、候选数、 一条「名字不是值」的提醒。
8. 右栏规则卡：两行图标 + 标题 + 说明；分隔线后三条提示。
9. 窄档 640：左右栏收窄为 `minmax(0,1fr) 220px`，不堆叠；帮助文字自然换行；左栏可纵向滚动。
10. 五态：hover / pressed / focus / disabled 在本地样式中定义；焦点环走 `DialogFocusVisual`；工具提示走 `DialogToolTipStyle`。
11. 图标按钮 30×30 / 36×36 均 `Padding="0"`（R68），避免 Fluent 隐式 `Padding=11,5,11,6` 切掉字形。
12. 数字等宽：候选数、耗时使用 tabular-nums（WPF 用 `Typography.NumeralAlignment="Tabular"`）。

## 状态清单（design.html 可点击）

| 状态 | 作用域 | 变量名 | 提示语义 |
| --- | --- | --- | --- |
| 默认 | 运行时 | 空 | 还没填名字，名字通常来自上游「变量定义」 |
| 运行时已选 | 运行时 | `OKCount` | 写入运行时变量（Int32），本轮累计良品数 |
| 全局已选 | 全局 | `OKCount` | 写入全局变量（Int32），值按类型解析 |
| 跨作用域 | 运行时 | `LineName` | 同名变量在全局，提示切换作用域 |
| 找不到 | 全局 | `OKCountX` | 变量管理里没有，运行期明确报错，不静默新建 |
| 旧连线 | 运行时 | `OKCount` | 该行原由上游提供，编辑后改为手填 |

> 这 6 个状态都来自插件现有的 `NameHint` 分支，不是设计稿自造的业务状态。

## 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | `Scope.Value`（string，Runtime/Global） | 作用域白名单校验、候选过滤、`NameHint` 都依赖它；不能换成枚举 |
| C3 | `VariableName.Value`（string）+ `NameCandidates` + `NameHint` | 变量名是「名字」不是「值」；候选由宿主快照过滤；提示由插件生成 |
| C4 | `Value`（`InputPort<object>`）+ `LinkableValueEditor` 的 `Port` / `StepData` | 常量、变量链接、解除链接、类型转换都在共享控件内部 |
| C5 | `CreateIfNotExists.Value`（bool） | 运行时变量不存在时是否自动创建；全局作用域不使用 |
| C6 | `NotifyNameEditedByUser()` 的真实输入事件 | 只在键盘/粘贴/剪切/拖拽/选择时上报；程序灌值不触发，避免误解除旧连线 |

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--va-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--va-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--va-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--va-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--va-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--va-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--va-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--va-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--va-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--va-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（`#66708A`，近似） |
| `--va-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--va-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--va-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--va-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--va-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--va-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--va-success` | `#22C55E` | `DialogSuccessBrush` |
| `--va-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--va-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--va-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--va-danger` | `#EF4444` | `DialogDangerBrush` |
| `--va-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--va-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--va-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--va-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--va-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--va-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--va-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| 卡片圆角 | — | `PluginCardCornerRadius`（=8，通用卡片）；作用域卡片 / 提示条 / 自动创建卡为 10（2026-10-20 遗留项 VA2） |
| 图标字体 | — | `DialogIconFont`（FA6 Solid） |
| 焦点环 | — | `DialogFocusVisual` |
| 工具提示 | — | `DialogToolTipStyle` |
| 输入框 36px | — | `DialogInput`（TextBox）；`LinkableValueEditor` 内部固定 36px |
| 间距 / 字号 / 圆角 6/10/12 | — | WPF 字面量（仓内暂无间距令牌字典） |

**缺失令牌**：无颜色令牌缺口。`DialogBorderStrongBrush`、`DialogSuccessBorderBrush`、`DialogDangerTextBrush`、`DialogDangerBorderBrush`、`DialogWarningBorderBrush` 已由 ImageAcquisition V2 追加到 `Colors.xaml`，本视图直接复用。

## 复刻实现映射（第 ③ 步，已完成 2026-10-20）

| 设计项 | WPF 实现 |
| --- | --- |
| 作用域卡片 | 2 个 `RadioButton` + `ScopeCardStyle`；`IsChecked` 经 `ScopeEqualsConverter` 写回 `Scope.Value`（string） |
| 变量名可编辑候选 | `ComboBox x:Name="NameBox"`：`IsEditable=True`、`IsTextSearchEnabled=False`、`ItemsSource=NameCandidates`、`Text=VariableName.Value`；候选行经 `NameCandidateDisplayConverter` 显示「名字 + 声明类型」（同源 `NameCandidateItems`），选择落下的仍是名字常量 |
| 提示条 | `Border` + `TextBlock Text="{Binding NameHint}"`；不新增状态枚举 |
| 值行 | `cv:LinkableValueEditor Label="" Port="{Binding Value}" StepData="{Binding StepData}"` |
| 自动创建 | `CheckBox IsChecked="{Binding CreateIfNotExists.Value, Mode=TwoWay}"` + 说明文字 |
| 右栏目标卡 | `VariableName.Value` + `Scope.Value`（`ScopeLabelConverter`）+ `NameCandidates.Count` |
| 右栏规则卡 | 静态文本 + 图标；不绑定业务数据 |
| 窄档 | `Grid` 两列 `2*` / `1*`，`MinWidth=320` / `220`；左栏 `ScrollViewer` |

## 复刻证据

| 项 | 结果 |
| --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.Utility\Plugin.Util.csproj`：**0 错误**；全量 143 / 最终增量 20 个警告全部来自既有文件与依赖，本次改动的 `.xaml` / `.xaml.cs` 无新增警告 |
| `Modules` 产物 | `Modules\Plugin.Util.dll` 已刷新（2026-10-20 01:40:50，252,416 字节） |
| 视图静态 | 视图 XAML 十六进制颜色 **0 处**；`NameBox` / `Scope.Value` / `NameCandidates` / `NameHint` / `CreateIfNotExists.Value` / `cv:LinkableValueEditor` 均在位 |
| 通用冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.Util`：**11 PASS / 0 FAIL**（2026-10-20 遗留项修复后复跑；早期记录的 8 PASS / 2 FAIL 是当时冒烟脚本的路径/计数假设，现已不复现） |
| 常规 gate | `FlowCanvasChecks` 构建连续两次在 `Plugin.DlClassify.csproj(63,5)` 的 `xcopy OnnxRuntime → VisionMaster\bin` 报 `MSB3073`（退出码 4，环境占用，与本插件无关）；按约定未杀进程，gate 待宿主关闭后由主会话重跑 |
| 初始态渲染 | `evidence/after-render-v1-initial.png`（同时由批量冒烟输出到 `_batch-evidence/Plugin.Util-VariableAssignmentPlugin.png`，920×520，149 个元素） |

**早期冒烟两条限制（已不复现）**：

1. `XAML hex=0`：早期 `PluginViewSmoke` 把程序集名当项目目录名（`Plugins\Plugin.Util`），现按前缀解析到 `Plugins\Plugin.Utility`；本次复跑 `xaml=1 hex=0`。
2. `Button.Command 绑定`：本视图自身没有命令按钮（`LinkableValueEditor` 内部按钮不计入），冒烟按 `ownButtons.Count == 0` 放行；本次复跑 PASS。

> 2026-10-20 遗留项修复后的实际输出：11 条 PASS / 0 条 FAIL（见 `docs/code-changes/2026-10-20-VariableAssignment-Web化.md` §9）。

## 复刻约束（已写入 XAML/code-behind 的设计纪律）

1. 根 `UserControl`、`MinWidth="560"`、`MinHeight="320"`；不写死 900×500。
2. `DataContext = 插件实例`；构造签名、`Initialize(stepData)` 时序不变。
3. `LinkableValueEditor` 只设 `Label=""`，`Port` / `StepData` / `BrowseCommand`（本插件无 Browse）原样。
4. 不引入独立 VM；只新增 `ScopeEqualsConverter`、`ScopeLabelConverter`、`NameDisplayConverter` 三个 UI 转换器。
5. 不修改共享文件：`Colors.xaml`、`PluginConfigColors.xaml`、`LinkableValueEditor`、`PluginConfigShellView.xaml`、其它插件。
6. 视图 XAML 内 0 个十六进制颜色；静态断言由 `--plugin-views Plugin.Util` 的 `XAML hex=0` 检查。
7. 图标按钮：30×30 / 36×36 + `Padding="0"`（R68）；焦点 `DialogFocusVisual`；ToolTip `DialogToolTipStyle`。
8. 不造死按钮：本视图没有新增命令按钮；作用域卡片是 `RadioButton`，值行的链接/解除由共享控件提供。

## 已知偏差（复刻时按 R130 记录）

1. **图标字体**：Web 用内联 SVG（Lucide 风格），WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同，语义一致。
2. **变量名下拉**：Web 稿是自绘候选面板；WPF 沿用可编辑 `ComboBox` 的 Fluent 下拉，候选行显示「名字 + 声明类型」（`NameCandidateItems`），箭头形状与行高略有差异。
3. **值链接形态**：Web 稿用浅蓝 chip 模拟；WPF 由 `LinkableValueEditor` 内部渲染链接地址与解除按钮，外观以共享控件为准。
4. **输入框 placeholder**：Web 稿的「输入或从右侧候选选择」「输入常量…」是 CSS placeholder；WPF 的 `ComboBox` / `LinkableValueEditor` 不提供 watermark，改用下方 `NameHint` 信息条与帮助文字表达，不改共享控件。
5. **右栏候选数**：Web 稿按预设写死；WPF 绑定 `NameCandidates.Count`，无候选时显示 0。
6. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 字宽、行高略有差异，不按 px 硬套。
7. **滚动条**：WPF 左栏滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
8. **阴影/动效**：Web 窗口多层 CSS 阴影；WPF 用宿主现有 `DropShadowEffect`；动效统一 150~200ms。
9. **宿主外壳**：Web 稿包含标题栏与底部按钮条，仅用于展示上下文；插件视图只负责中间内容区，外壳不改。
10. **640×560 最小档**：宿主已统一 MinHeight=560；本稿 640 宽档按两列收窄展示，真机需复核左栏滚动与右栏文字换行。

## 未纳入本版（记录，不改）

- **变量浏览器 / 打开变量管理**：需要新的宿主导航契约。
- **值类型校验器 / 转换预览**：配置态没有目标变量类型元数据可绑定；写入侧仍在运行期按声明类型解析。
- **加载骨架 / 运行日志 / 试运行可视化**：需要新增 VM 状态或宿主契约。
- **深色主题**：本稿只覆盖浅色设计系统。
- **批量赋值 / 多目标**：属于功能扩展，不在本次范围。

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v1 → v2），并同步更新已复刻的 `VariableAssignmentView.xaml` 与本映射表。

## 2026-10-20 遗留项修复（VA1 / VA2 / VA3 / VA4）

- **VA1**：`NameBox.Background` 从 `DialogInputBackgroundBrush` 改为 `DialogSurfaceBrush`，与卡片底色区分，层次更清楚。
- **VA2**：作用域卡片（`ScopeCardStyle` 模板 Border）、提示条（`HintBorderStyle`）、自动创建卡三处圆角 8 → 10；图标 chip 的 8 保持不变。
- **VA3**：新增只读 `NameCandidateItems`（`Name` / `TypeName` / `Note`，与 `NameCandidates` 同源同序），候选行显示「名字 + 声明类型」；`NameCandidates`（字符串集合）仍是 `ItemsSource`/Text 绑定的唯一来源，选择落成名字常量，不改业务契约。若后续要改成对象集合，需同步 `FlowCanvasChecks\VariableAssignmentCheck.cs` 的候选断言。
- **VA4**：`ScopeLabelConverter` 对非空且非 Runtime/Global 的值返回「未知作用域（值）」；插件 `CurrentScope` 不再把未知值静默回退 Runtime（返回空串 → 无候选，`NameHint` 明确写「作用域取值无法识别」，运行期既有白名单校验照常报错）。
- **验证**：`dotnet build Plugins\Plugin.Utility\Plugin.Util.csproj -v m` → 0 错误（20 个既有警告，与改动前基线一致）；`UIThemeSmokeTest --plugin-views Plugin.Util` → 11 条 PASS / 0 条 FAIL。
