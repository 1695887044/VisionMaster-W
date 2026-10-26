# 用户管理（user-management）设计基线 v3.1

> 设计媒介，**不是交付物**：不进构建、不打包、不部署、不作为第二客户端维护（AGENTS.md §30.1 / R126）。
> 状态：**设计稿 v3.1 已获用户会话确认（2026-10-25，确认 5 项关键口径）**。B5 可按本基线复刻；完整 19 项 UI 清单在 B5 逐页验收时按 R132 逐项勾选。
> v3 变更（B3）：权限模型为 **用户组 + 命名权限集合（≤32）**，并在此基础上去掉 Stop 特例——**`Stop` 是普通权限 Key**，默认三个模板都勾选，自定义组可取消（取消时红字警告 + 二次确认）；`JogStop` 跟随 `Stop`；唯一保留的系统安全动作是**点动中权限被收回/登出时自动补发 `JogStop`**。同时**不兼容旧方案**（旧 5 字段 `Role` 账号文件 fail-closed），权限删除守卫改为**查组 + 查按钮**。
> v3.1 变更（B3 补充态）：**主结构不变**，补 5 个补充态（新建权限行内表单 / 32/32 满额 / 模板·管理员全选只读 / 运行态鉴权失败 / 运行态取消回执），证据 14 → **19 张**。
> 唯一口径：`docs/方案设计/2026-10-25-B3权限模型定稿（Stop可配置+无旧兼容）.md`（与本文冲突时以该文件为准）。

| 项 | 值 |
|---|---|
| 版本 | v3.1（已确认，冻结）· **v4 已确认并复刻（§十）** |
| 日期 | 2026-10-25 |
| 确认人 | 用户（2026-10-25 会话确认 v3.1；同日确认 v4：「好的 按照这个复刻」） |
| 确认日期 | 2026-10-25（v3.1 五项关键口径；v4 按草案原样确认，含密码眼睛 / 时间格式 / 结果五桶 / 按钮语义四项） |
| 覆盖范围 | `VisionMaster\Views\DialogViews\UserManagementView.xaml`（拟建）：1100×820 / 最小 980×700，复用 `HardwareManagerView` 大窗壳；三页签「账号与用户组 / 在线会话 / 审计记录」；8 种命名状态；用户组编辑弹窗（Stop 普通复选 + 取消警告）；权限目录管理弹窗（含行内新建表单与 32 满额）；运行态未授权登录态（含鉴权失败与取消回执）；三处入口 |
| 入口文件 | [`design.html`](design.html)（v3.1，单文件自包含）+ [`design-v4.html`](design-v4.html)（v4 草案，同一形态；`?shot=1` 隐藏评审条） |
| 证据目录 | [`evidence/`](evidence/)（v3.1 共 **19 张 1100×820**）+ [`evidence/v4/`](evidence/v4/)（v4 草案 **7 张**） |
| 落地代码 | **v4 已复刻（2026-10-25）**：四个视图 + 四个 VM；账号页签不再复用旧 760px 弹窗；证据 `evidence/v4-wpf/` |
| 取代 | v1（角色下拉）、v2（11 个固定权限位）、v3-B2 稿（Stop 不查权限）；旧稿 `docs/UI设计基线/剩余视图设计稿-2026-10-22/parts/19-scada-user-manager.html`（保留不删） |

---

## 一、怎么打开与评审

直接双击 [`design.html`](design.html)。页面顶部是**评审控制条**（不属于产品界面）：

- **状态**：`Normal` + UM5 的 8 个状态名，点一下立即切换；
- **页签**：账号与用户组 / 在线会话 / 审计记录；
- **视图**：`组编辑`、`权限目录`、`运行态登录`、`用户管理页`；
- **补充态 v3.1**：`新建权限`、`32/32 满额`、`模板·管理员`、`运行态拒绝`、`运行态取消`（v3.1 新增的 5 个态，点一下直接切）；
- 标题栏右侧的「状态：…」是**设计标注**，不是 WPF 页面控件；复刻时不要把它做进产品。

单张截图（隐藏评审条，窗口正好 1100×820）：

```text
design.html?state=NoSolution&shot=1
design.html?state=Normal&tab=sessions&shot=1
design.html?state=Normal&tab=audit&shot=1
design.html?groups=1&shot=1
design.html?groups=1&permissions=1&shot=1
design.html?screen=runtime&shot=1
# v3.1 补充态（不带 mode 时行为与 v3 的 14 张完全一致）
design.html?groups=1&permissions=1&mode=new&shot=1
design.html?groups=1&permissions=1&mode=full&shot=1
design.html?groups=1&group=tpl-admin&shot=1
design.html?screen=runtime&mode=denied&shot=1
design.html?screen=runtime&mode=cancelled&shot=1
```

`state` 取值：`Normal` / `NoSolution` / `UnsavedSolution` / `NotAdministrator` / `LoadingOrFailed` / `EmptyAccounts` / `WebUnavailable` / `WriteFailed` / `FactoryFallback`。
`group` 取值：`worker`（普工）/ `line-op`（产线操作员，默认）/ `vendor`（厂家）/ `tpl-operator` / `tpl-engineer` / `tpl-admin`。
`mode`（与 `permissions=1` 连用）：`new` 行内新建表单 / `full` 32 满额；`mode`（与 `screen=runtime` 连用）：`denied` 鉴权失败 / `cancelled` 取消回执。

---

## 二、B3 权限模型（v3 核心）

### 2.1 命名权限目录（15 条内置 + 最多 32 条）

| 权限 Key | 显示名 | 类型 |
|---|---|---|
| `ViewRuntime` | 看运行画面 | 内置 |
| `ControlRun` | 运行控制 | 内置 |
| `Start` | 启动 | 内置 |
| `Initialize` | 初始化 | 内置 |
| `Stop` | 停止 | 内置 |
| `ReportQuery` | 报表查询 | 内置 |
| `AxisJog` | 轴点动 | 内置 |
| `AxisSpeedConfig` | 定位速度配置 | 内置 |
| `EditStructure` | 改结构 | 内置 |
| `Compile` | 编译 | 内置 |
| `SaveSolution` | 保存方案 | 内置 |
| `HardwareRuntime` | 硬件运行态 | 内置 |
| `HardwareConfig` | 硬件配置 | 内置 |
| `SystemConfig` | 系统配置 | 内置 |
| `UserAdministration` | 用户管理 | 内置 |

- **上限 32 条**（含内置 15 条，剩余 17 个自定义槽位）；到 32 条禁止新建；
- 判定：`currentGroup.Permissions.Contains(requiredKey)`，不是比大小、也不是位掩码；**没有 Stop 特例**；
- **没有 `SafetyStop`**；`Always`（登录/帮助/浏览）是系统级直通（不查权限），不进目录；
- `Stop` 是普通权限 Key，与其它 Key 完全一样；`JogStop` 跟随 `Stop`（用户按不到 `JogStop`，它只由系统在点动收手时补发）；
- **唯一保留的系统安全动作**：点动中权限被收回/登出时，系统自动补发 `JogStop`（不是用户按钮，不经过 `Can`）。

### 2.2 用户组：模板 + 自定义

- **内置模板（只读）**：`模板·操作员` / `模板·工程师` / `模板·管理员`，固定权限集，不可改名/改权限/删除；**三个模板都含 `Stop`**（安全默认，仍可在自定义组里取消）；
- **自定义组**：普工 / 产线操作员 / 厂家，可新建、改名、改权限、删除；
- 组存 `Permissions: string[]`，不是 `Mask`；
- 删除守卫：有用户引用不可删；最后一个带 `UserAdministration` 的组不可删/不可取消该权限；
- 空组（无任何权限）允许但提示“该组登录后什么也做不了”；
- **取消 `Stop`**：允许保存，但组编辑弹窗必须显示红字警告“取消「停止（Stop）」后该组将无法停止设备；若含「轴点动（AxisJog）」，点动中只能靠系统自动 JogStop”，并在保存时**二次确认**。

示例（与 `design.html` 组编辑一致）：

| 组 | 权限 |
|---|---|
| 普工 | `ViewRuntime` + `Start` + `Stop` |
| 产线操作员 | `ViewRuntime` + `ControlRun` + `Start` + `Initialize` + `Stop` + `ReportQuery` + `AxisJog`（7 项） |
| 厂家 | 全部 15 条（含 `AxisSpeedConfig` / `HardwareConfig` / `SystemConfig` / `UserAdministration`） |
| 模板·操作员 | 产线操作员同款 7 项 |
| 模板·工程师 | 操作员 7 项 + `EditStructure` + `Compile` + `SaveSolution` + `HardwareRuntime` + `AxisSpeedConfig` |
| 模板·管理员 | 全部 15 条 |

### 2.3 账号与文件（不兼容旧方案）

- `Users/ScadaUsers.json` 改为裸数组**五字段**：`Name / Salt / Hash / Iterations / GroupId`，**不再有 `Role`**；
- 旧 5 字段 `Role` 文件**不自动迁移、不提供导入**：直接 fail-closed，提示“旧账号文件格式不受支持，请重建账号”，也不写回 `Role`；
- `Users/ScadaGroups.json` 存组：`{ Id, Name, Permissions: string[], IsBuiltIn }`；
- `Users/ScadaPermissions.json` 存权限目录：`{ Key, Name, IsBuiltIn }`，最多 32 条；
- 三文件缺失：权限目录/组自动种内置模板（不写盘，首次保存再落）；用户文件缺失走 `BookSource=Factory`（新方案兜底，不是旧兼容）；
- 写盘顺序：权限目录 → 组 → 用户；任一失败回滚三者，并同时 catch `IOException` + `UnauthorizedAccessException`（R180）。

### 2.4 权限目录管理（用户管理页内）

- 入口：账号页签 →「管理用户组…」→ 组编辑弹窗 →「管理权限目录…」；
- 列表：名称 / Key / 使用组数 / 类型（内置/自定义）；计数“已定义 N/32”（当前 16/32 = 15 内置 + 1 自定义示例）；
- **新建（v3.1 补充态）**：点「＋ 新建权限」在列表上方展开**行内表单**——`显示名` + `Key（自动生成，创建后不可改）` 预览 + `取消` / `创建`，不另开二级弹窗；Key 由显示名生成，创建后不可改（见 `design-v3-15-new-permission.png`）；
- **32 满额（v3.1 补充态）**：「＋ 新建权限」禁用、计数变 `32/32`（警告色）、底部固定提示“已达上限 32 条，删除或合并后再建。”（见 `design-v3-16-catalog-full.png`）；
- 改名：只改显示名，Key 不变；
- **删除守卫 = 查组 + 查按钮**：删除前扫描所有组的 `Permissions` 与全方案页面/图元的 `RequiredPermission`，**任一引用即拒绝**，并报“N 个组 / M 个按钮引用”；内置不可删；删除后 Key 不复用；
- 组编辑弹窗的复选框**动态来自权限目录**，不再是固定 11 项。

### 2.5 运行态未授权登录

- 运行态 SCADA 画面/图元未授权点击 → 弹登录框（见 `design-v3-14-runtime-login.png`）；
- 登录成功后**重新判定**，有权限则继续原操作，无权限则拒绝并给出“需要 X 权限”；
- **鉴权失败（v3.1 补充态）**：输入错误用户名/密码 → 登录框内红字“用户名或密码错误（剩余尝试 4 次）”+ 明写**动作未下发**（轴点动未执行），可修正后重试或取消（见 `design-v3-18-runtime-denied.png`）；
- **取消（v3.1 补充态）**：点「取消」→ 登录框关闭、动作不执行，画面底部给出**「已取消」回执**（“已取消：未登录，轴点动未执行（动作未下发）”），回执用警告色，不是成功色（见 `design-v3-19-runtime-cancelled.png`）；
- 示例：普工点“轴点动” → 需要 `AxisJog` → 弹登录 → 产线操作员登录后继续；
- **`Stop` 也按权限**：无 `Stop` 权限时点“停止”同样弹登录；默认模板含 `Stop`，避免默认锁死；
- 点动中权限被收回/登出 → 系统自动补发 `JogStop`（不弹登录、不受组权限限制）；
- 工程菜单/管理页仍灰显 + 回执（不弹登录）。

---

## 三、形态与入口（规格 §3.9 + 附录 D.9）

### 3.1 窗口壳

| 项 | 决定 |
|---|---|
| 尺寸 | 1100×820；最小 980×700（与 `HardwareManagerView.xaml:193-203` 同壳） |
| 窗口样式 | `WindowStyle=None` + 自绘标题栏 + `ResizeMode=CanResize` + `SizeToContent=Manual` |
| 标题栏 | 52px；左侧图标 + 「用户管理」+ 副标题；右侧关闭按钮 |
| 页签行 | 44px；三页签；选中项 2px 主色底边 |
| 内容区 | 672px；账号页签左清单 300px + 16px 间距 + 右表单 |
| 底栏 | 52px；左侧提示，右侧「关闭」 |
| 不做 | 不做停靠文档页（避免卷进 `Layout.xml` 恢复/版本升级，R43/R56） |

### 3.2 三处入口

| 入口 | 位置 | 说明 |
|---|---|---|
| ① 系统菜单 | `系统 → 用户管理` | 新增；权限类菜单项自动灰显 |
| ② 状态栏右键 | 当前用户文字右键菜单 | 两项：`用户登录` / `用户管理`（后者仅管理员可见）；左键保持打开登录弹窗 |
| ③ 登录弹窗 | 「用户管理」按钮 | 保留，改为打开新页；无方案时禁用 |

### 3.3 三页签

| 页签 | 内容 |
|---|---|
| 账号与用户组 | 左：搜索 + 账号清单 + 新建；右：账号名（只读）、用户组下拉 +「管理用户组…」、新密码/确认、保存用户组/重置密码/删除账号；底部只读安全策略摘要 + 「在系统设置中修改」 |
| 在线会话 | 8 列：会话 / 用户 / 用户组 / 来源 IP / 建立时间 / 最近活动 / 只读 / 操作；工具栏「刷新」「全部踢下线」；Web 不可用空态 |
| 审计记录 | 筛选行（日期范围 + 操作者 + 结果 + 显示未归属）；7 列：时间 / 操作者 / 对象 / 事件 / 动作 / 结果 / 说明；底部元信息「显示 X 条 / 共 Y 行 / 已跳过 N 行（格式异常）」 |

**没有「登录策略」页签**：密码最短长度、空闲超时、审计保留天数归机器级 `AppConfig.json`，已有「系统设置 → 安全与留档」入口；本页只显示只读摘要 + 跳转，避免两个事实源（R141）。

### 3.4 用户组编辑弹窗

- 左：组列表（自定义组 + 模板组）+「新建组」；
- 右：组名 + 权限目录复选框（动态，16 项）+「管理权限目录…」；
- **`Stop` 是普通复选**，不再有 `SafetyStop` 只读项；产线操作员 / 普工 / 厂家 / 三个模板均勾选 `Stop`；
- 权限复选框下方固定一行**红字**：“取消「停止（Stop）」后该组将无法停止设备；若含「轴点动（AxisJog）」，点动中只能靠系统自动 JogStop”；勾掉「停止」时该行加框强调，保存时二次确认；
- 模板组只读（名称/权限不可改，**保存与删除按钮禁用**）；**模板·管理员固定全选 15 条内置权限**（`配方下发` 是自定义示例，只给厂家，所以是 15/16 勾选而不是 16/16）；
- **切组即换勾选（v3.1 补充态）**：点左侧组名立刻按该组的权限集重绘复选框——`模板·管理员` 15 项全勾且只读禁用，切回 `产线操作员` 恢复 7 项（见 `design-v3-17-template-admin.png`）；
- 删除组：有用户引用时禁用并提示“先迁移 N 个用户”；最后一个管理组不可删；
- 空组允许但提示“该组登录后什么也做不了”。

### 3.5 权限目录管理弹窗

- 列表：名称 / Key / 使用组数 / 类型 / 操作；
- 顶部计数“已定义 N/32”（当前 16/32）；「＋ 新建权限」；
- **行内新建表单**：显示名 + 自动生成 Key 预览（只读、虚线框）+ 取消/创建，占列表上方一行，不另开二级弹窗；
- **删除守卫查组 + 查按钮**：扫描所有组 `Permissions` 与全方案页面/图元 `RequiredPermission`，任一引用即拒绝并报“N 个组 / M 个按钮引用”；
- 内置不可删；改名只改显示名，Key 不变；
- **到 32 条禁止新建**：按钮禁用 + 计数 32/32 转警告色 + 底部提示“已达上限 32 条，删除或合并后再建。”；删除后的 Key 不复用。

---

## 四、8 种状态（UM5 按名字逐条断言）

| # | 状态名 | 表现 | 证据 |
|---|---|---|---|
| 1 | `NoSolution` | 整页空态「尚未打开方案」；所有操作禁用 | `design-v3-04-nosolution.png` |
| 2 | `UnsavedSolution` | 顶部横幅「方案尚未保存，账号修改将在保存后可用」；列表与表单只读；`CanEditAccounts=false` | `design-v3-05-unsaved.png` |
| 3 | `NotAdministrator` | 页面不可打开；若打开后组权限被降，立即提示并关闭 | `design-v3-09-notadmin.png` |
| 4 | `LoadingOrFailed` | 骨架/进度条；失败横幅显示 `LastLoadError`（含路径与异常类型）+「重新加载」 | `design-v3-10-loadingfailed.png` |
| 5 | `EmptyAccounts` | 左清单空态「账号清单为空」；右表单空态「没有可编辑的账号」；不白屏 | `design-v3-11-emptyaccounts.png` |
| 6 | `WebUnavailable` | 在线会话页签空态「未安装或未启用 SCADA Web 客户端」；账号与审计不受影响 | `design-v3-07-webunavailable.png` |
| 7 | `WriteFailed` | 错误横幅 + 操作回滚；不显示「保存成功」 | `design-v3-06-writefailed.png` |
| 8 | `FactoryFallback` | 账号页签顶部一次性提示「本方案没有账号文件，当前使用出厂管理员…」；改密后提示消失；最后一个管理员不可删除 | `design-v3-08-factory.png` |

三个正常页签证据：`design-v3-01-accounts.png` / `-02-sessions.png` / `-03-audit.png`。
组编辑证据：`design-v3-12-group-editor.png`（产线操作员 7 项含 `Stop`，红字取消警告在列）。权限目录证据：`design-v3-13-permission-catalog.png`（16/32，无 `SafetyStop` 行）。运行态登录证据：`design-v3-14-runtime-login.png`（普工点轴点动 → 需要 `AxisJog`）。

### 4.1 补充态 v3.1（5 个，主结构不变）

| # | 补充态 | 触发（URL） | 表现 | 证据 |
|---|---|---|---|---|
| 1 | `新建权限` | `?groups=1&permissions=1&mode=new` | 权限目录列表上方展开**行内表单**：显示名（预填“报警确认”）+ 自动生成 Key 预览 `AlarmAck`（只读、虚线框）+ `取消` / `创建`；「＋ 新建权限」按钮转激活色；提示“Key 由显示名生成、创建后不可改；上限 32 条，当前 16/32” | `design-v3-15-new-permission.png` |
| 2 | `32/32 满额` | `?groups=1&permissions=1&mode=full` | 计数 `32/32` 转警告色；「＋ 新建权限」禁用；列表滚到底露出 16 条自定义示例行；底部警告色提示“已达上限 32 条，删除或合并后再建。” | `design-v3-16-catalog-full.png` |
| 3 | `模板·管理员全选` | `?groups=1&group=tpl-admin` | 左侧 `模板·管理员` 选中；15 条内置权限**全勾**、复选框禁用、组名只读；**保存与删除按钮禁用**，删除提示“内置模板不可改名 / 改权限 / 删除”；切回 `产线操作员` 恢复 7 项（`?groups=1` 即该态） | `design-v3-17-template-admin.png` |
| 4 | `运行态拒绝` | `?screen=runtime&mode=denied` | 登录框填入错误用户名 `operator9` / 错误密码；框内红字“用户名或密码错误（剩余尝试 4 次）。动作未下发：轴点动未执行，可修正后重试或取消。”；**动作不下发**，可重试 | `design-v3-18-runtime-denied.png` |
| 5 | `运行态取消` | `?screen=runtime&mode=cancelled` | 登录框关闭；画面底部**警告色回执**“已取消：未登录，轴点动未执行（动作未下发）。”；动作未执行 | `design-v3-19-runtime-cancelled.png` |

> 5 个补充态都是**主结构上的叠加**：三页签、8 状态、组编辑与权限目录的骨架不变；不带 `mode` / `group` 参数时，渲染与 v3 的 14 张证据**逐字节一致**（已用 SHA-256 核过）。

---

## 五、令牌映射（WPF 侧只许引用这些键）

> 原型里的颜色只是渲染值；WPF 复刻必须用 `Colors.xaml` / `Dialog*` 资源，**0 处 `#RRGGBB`**（R140）。下表的 rgb 值仅用于对照，不要抄进 XAML。
> 运行态深色底是 HMI 运行态的示意色，不属于用户管理页令牌；登录弹窗仍用 Dialog* 令牌。

| 令牌（WPF 键） | 原型变量 | 值（对照用） | 用途 |
|---|---|---|---|
| `DialogAccentBrush` | `--dialog-accent` | `rgb(14,165,233)` | 主按钮、选中页签底边、焦点框 |
| `DialogAccentDarkBrush` | `--dialog-accent-dark` | `rgb(2,132,199)` | 主按钮悬停 |
| `DialogAccentLightBrush` | `--dialog-accent-light` | `rgb(224,242,254)` | 选中行底、页签计数底 |
| `DialogAccentSoftBrush` | `--dialog-accent-soft` | `rgb(240,249,255)` | 选中行底（浅） |
| `DialogAccentBorderBrush` | `--dialog-accent-border` | `rgb(186,230,253)` | 选中行/提示条边框 |
| `DialogAccentTextBrush` | `--dialog-accent-text` | `rgb(3,105,161)` | 主色文字、链接 |
| `DialogBackgroundBrush` | `--dialog-bg` | `rgb(238,242,247)` | 页面底色 |
| `DialogSurfaceBrush` | `--dialog-surface` | `rgb(255,255,255)` | 窗口/卡片表面 |
| `DialogScrimBrush` | `--dialog-scrim` | `rgba(0,0,0,0.6)` | 模态遮罩 |
| `DialogCardBorderBrush` | `--dialog-card-border` | `rgb(227,233,242)` | 卡片边框 |
| `DialogInputBackgroundBrush` | `--dialog-input-bg` | `rgb(248,250,252)` | 输入框底 |
| `DialogBorderBrush` | `--dialog-border` | `rgb(226,232,240)` | 常规边框 |
| `DialogBorderLightBrush` | `--dialog-border-light` | `rgb(238,242,247)` | 分隔线 |
| `DialogBorderStrongBrush` | `--dialog-border-strong` | `rgb(203,213,225)` | 虚线标注边框 |
| `DialogTextPrimaryBrush` | `--dialog-text-primary` | `rgb(15,23,42)` | 主文字 |
| `DialogTextSecondaryBrush` | `--dialog-text-secondary` | `rgb(71,85,105)` | 次文字 |
| `DialogTextTertiaryBrush` | `--dialog-text-tertiary` | `rgb(100,116,139)` | 提示文字 |
| `DialogTextMutedBrush` | `--dialog-text-muted` | `rgb(148,163,184)` | 弱化文字 |
| `DialogSuccessBrush` | `--dialog-success` | `rgb(34,197,94)` | 成功语义 |
| `DialogDangerBrush` | `--dialog-danger` | `rgb(239,68,68)` | 危险语义 |
| `DialogWarningBrush` | `--dialog-warning` | `rgb(245,158,11)` | 警告语义 |
| `DialogWarningTextBrush` | `--dialog-warning-text` | `rgb(180,83,9)` | 警告文字 |
| `DialogDangerSubtleBrush` | `--dialog-danger-subtle` | `rgb(254,242,242)` | 错误横幅底 |
| `DialogDangerTextBrush` | `--dialog-danger-text` | `rgb(185,28,28)` | 错误文字 |
| `DialogDangerBorderBrush` | `--dialog-danger-border` | `rgb(254,202,202)` | 错误横幅边框 |
| `DialogWarningSubtleBrush` | `--dialog-warning-subtle` | `rgb(255,244,229)` | 警告横幅底 |
| `DialogWarningBorderBrush` | `--dialog-warning-border` | `rgb(253,230,138)` | 警告横幅边框 |
| `DialogSuccessSubtleBrush` | `--dialog-success-subtle` | `rgb(240,253,244)` | 成功 chip 底 |
| `DialogSuccessTextBrush` | `--dialog-success-text` | `rgb(21,128,61)` | 成功 chip 字 |
| `DialogSuccessBorderBrush` | `--dialog-success-border` | `rgb(187,247,208)` | 成功 chip 边框 |
| `DialogRowHoverBrush` | `--dialog-row-hover` | `rgb(241,245,249)` | 行悬停 |
| `DialogRowAlternateBrush` | `--dialog-row-alt` | `rgb(248,250,252)` | 标题栏/底栏/表头底 |

### 5.1 可复用结构（R141）

| 结构 | 键 | 用在哪 |
|---|---|---|
| 卡片 | `DialogColumnCard` / `DialogColumnHeader` / `DialogColumnTitle` | 左清单卡、右表单卡、表格卡 |
| 空态 | `DialogEmptyStateIcon` / `DialogEmptyStateTitle` / `DialogEmptyStateHint` | 无方案 / 空账号 / Web 不可用 |
| 按钮 | `DialogPrimaryButton` / `DialogSecondaryButton` / `DialogDangerButton` / `DialogGhostButton` / `DialogIconButton` | 保存用户组 / 重置 / 删除 / 关闭 / 刷新 |
| 输入 | `DialogInput` / `DialogSearchInput` / `DialogCombo` | 搜索、用户组下拉、筛选 |
| 勾选 | `DialogCheckBox` | 权限复选框、「显示未归属」 |
| 标签 | `DialogTag` / `DialogTagText` | 用户组 chip、权限类型 chip |
| 表格 | `GridListViewStyle` + `GridViewRowPresenter` + 就地合并 `GridView.xaml` | 账号清单、在线会话、审计、权限目录 |
| 焦点/提示 | `DialogFocusVisual` / `DialogToolTipStyle` | 所有可聚焦控件 / 所有 ToolTip |

---

## 六、已知偏差（R130 预判，逐项写「接受 / 替代方案」）

| # | 差异点 | 设计稿 vs WPF | 处置 |
|---|---|---|---|
| 1 | 字体度量 | Web 用 Segoe UI / 微软雅黑；WPF 用系统字体，字宽略有差异 | **接受**；以同一字号/字重为准，不按像素抠字宽 |
| 2 | DPI 缩放 | Web 截图按 100%；WPF 在 125%/150% 下整体放大 | **接受**；WPF 用设备无关单位，布局按 1100×820 逻辑像素 |
| 3 | 滚动条占位 | Web 表格滚动条浮在内容上；WPF 占 17px | **替代方案**：列宽按「可用宽度 − 17」预算；账号清单列宽预算见旧页注释（280−2−17−8−12） |
| 4 | GridView 无星号列宽 | Web 用百分比列宽；WPF `GridViewColumn` 只有固定宽 | **替代方案**：按 1100 宽算固定列宽；窄档 980 时允许横向滚动或压缩说明列 |
| 5 | 焦点态 | Web 用 `:focus` 外圈；WPF 用 `DialogFocusVisual` | **接受**；键盘焦点必须可见（R135） |
| 6 | 多层阴影 | Web 单层大阴影；WPF `DropShadowEffect` 单层 | **接受**；只保留一层，不叠多层 |
| 7 | 动效曲线 | Web 原型不做动效；WPF 过渡 150–200ms | **接受**；页签切换/横幅出现用 150–200ms |
| 8 | 文本截断 | Web 用 `text-overflow:ellipsis`；WPF `TextTrimming=CharacterEllipsis` + ToolTip | **接受**；所有活值列必须带 ToolTip |
| 9 | 页签高度 | Web 44px；WPF `TabItem` 模板按 R133 用 `MinHeight≥36` + `Border.Padding` | **替代方案**：目标 44px，但不许用「Grid Auto 行 + 子元素 Margin」表达留白 |
| 10 | 状态标注 | Web 标题栏有「状态：…」虚线 chip；WPF 产品页没有 | **仅设计标注**；复刻时删除，UM5 用 VM 标志断言 |
| 11 | 组编辑弹窗尺寸 | Web 780px 宽、权限区两列 16 项 + 一行红字警告；WPF 模态窗按 820×620 或 780×600 | **替代方案**：以内容不折行为准；权限复选框两列，窄档允许单列滚动；红字警告固定在权限区下方，不随滚动丢失 |
| 12 | 权限目录弹窗 / 取消 Stop 二次确认 | Web 820px 宽、表格 430px 高；取消 `Stop` 只有内联红字，没有二次确认弹窗 | **替代方案**：表格可滚动，“已定义 N/32”固定在标题栏；WPF 二次确认走 `MessageBox`，文案与红字同源（“该组将无法停止设备”），按钮「仍然保存」/「取消」 |
| 13 | 运行态登录态 | Web 深色底是 HMI 运行态示意；WPF 运行画面用 `Hmi*` 令牌 | **接受**；登录弹窗仍用 `Dialog*` 令牌，深色底不抄进用户管理页 |
| 14 | 复选框密度 | Web 两列 8px 间距；WPF `DialogCheckBox` 有默认高度 | **接受**；按 `DialogCheckBox` 实际高度排，允许弹窗纵向滚动 |
| 15 | 新建权限的行内表单 | Web 用 flex 一行排开（显示名自适应宽、Key 预览固定宽、按钮右贴）；WPF 用 `Grid` 三列 + 固定 `MinWidth` | **替代方案**：`Grid` 列宽 `*` / `Auto` / `Auto`，窄档 980 时允许换行；Key 预览用等宽字体 + 虚线 `Border` |
| 16 | Key 自动生成 | Web 只做静态预览（`报警确认 → AlarmAck`）；WPF 中文显示名无法音译 | **替代方案**：生成规则写死为“ASCII 词取 PascalCase；纯中文取 `CustomPermNN`”，预览与落盘同源；生成后不可改，界面必须显示最终 Key 供确认 |
| 17 | 32/32 满额可视证据 | Web 把列表滚到底露出自定义行；WPF `ListView` 无等价 CSS | **替代方案**：`ScrollIntoView(最后一条)`；计数与“已达上限”提示固定在标题栏/底栏，不随滚动消失 |
| 18 | 只读模板的勾选可见性 | Web 用 `opacity:.82` 让 15 个勾仍看得清；WPF 禁用态默认更灰 | **替代方案**：复选框 `IsEnabled=false` 但容器 `Opacity` 不低于 0.8；不要用 `IsHitTestVisible=false` 冒充只读（键盘仍可改） |
| 19 | 运行态回执配色 | Web 用 `DialogWarning*` 令牌的底部条；WPF 用 InfoBar/Toast | **替代方案**：取消回执用警告色、成功继续用成功色，两者不许同色（否则用户分不清“做了”和“没做”） |

---

## 七、证据清单

| 文件 | 内容 |
|---|---|
| `evidence/design-v3-01-accounts.png` | 正常 · 账号与用户组（用户组下拉 + 管理用户组） |
| `evidence/design-v3-02-sessions.png` | 正常 · 在线会话（8 列，用户组列） |
| `evidence/design-v3-03-audit.png` | 正常 · 审计记录（7 列 + 跳过行数） |
| `evidence/design-v3-04-nosolution.png` | `NoSolution` |
| `evidence/design-v3-05-unsaved.png` | `UnsavedSolution` |
| `evidence/design-v3-06-writefailed.png` | `WriteFailed` |
| `evidence/design-v3-07-webunavailable.png` | `WebUnavailable` |
| `evidence/design-v3-08-factory.png` | `FactoryFallback` |
| `evidence/design-v3-09-notadmin.png` | `NotAdministrator`（补充） |
| `evidence/design-v3-10-loadingfailed.png` | `LoadingOrFailed`（补充） |
| `evidence/design-v3-11-emptyaccounts.png` | `EmptyAccounts`（补充） |
| `evidence/design-v3-12-group-editor.png` | 用户组编辑弹窗（产线操作员 7 项；`Stop` 为普通复选；红字取消警告；模板只读；删除守卫） |
| `evidence/design-v3-13-permission-catalog.png` | 权限目录管理（16/32 = 15 内置 + 1 自定义；无 `SafetyStop` 行；删除守卫查组 + 查按钮） |
| `evidence/design-v3-14-runtime-login.png` | 运行态未授权弹登录（普工点轴点动 → 需要 AxisJog；Stop 也按权限、自动 JogStop 例外） |
| `evidence/design-v3-15-new-permission.png` | **v3.1 补充态**：权限目录行内新建表单（显示名 + 自动 Key 预览 + 取消/创建） |
| `evidence/design-v3-16-catalog-full.png` | **v3.1 补充态**：32/32 满额（新建禁用 + 上限提示 + 自定义行滚到底） |
| `evidence/design-v3-17-template-admin.png` | **v3.1 补充态**：模板·管理员 15 项全勾只读（保存/删除禁用；切回产线操作员恢复 7 项） |
| `evidence/design-v3-18-runtime-denied.png` | **v3.1 补充态**：运行态鉴权失败（错误用户名/密码，动作未下发，可重试） |
| `evidence/design-v3-19-runtime-cancelled.png` | **v3.1 补充态**：运行态取消（登录框关闭、动作未执行、警告色「已取消」回执） |

v1 的 11 张角色下拉截图归档在 `evidence/v1/`；v2 的 13 张固定权限位截图归档在 `evidence/v2/`；均不得用于 WPF 复刻。

截图命令（Edge headless，1100×820，`shot=1` 隐藏评审条）：

```text
msedge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 --window-size=1100,820 --screenshot=<png> "design.html?state=<State>&tab=<tab>&shot=1"
msedge ... "design.html?groups=1&shot=1"
msedge ... "design.html?groups=1&permissions=1&shot=1"
msedge ... "design.html?screen=runtime&shot=1"
msedge ... "design.html?groups=1&permissions=1&mode=new&shot=1"
msedge ... "design.html?groups=1&permissions=1&mode=full&shot=1"
msedge ... "design.html?groups=1&group=tpl-admin&shot=1"
msedge ... "design.html?screen=runtime&mode=denied&shot=1"
msedge ... "design.html?screen=runtime&mode=cancelled&shot=1"
```

---

## 八、旧稿说明

`docs/UI设计基线/剩余视图设计稿-2026-10-22/parts/19-scada-user-manager.html` **保留不删**，仅作历史。它描述的是旧 `ScadaUserManagerView`（760 宽、单页、40 处 hex），已被本基线取代：
新页是 1100×820 三页签大窗，0 hex，账号页签以旧页为功能来源但不复用其视觉。

---

## 九、确认清单（R132：请逐项确认）

> 2026-10-25 用户会话确认以下 **5 项关键口径**：① 账号页签用户组下拉 +「管理用户组…」；② `Stop` 为普通复选，取消时红字警告 + 保存二次确认；③ 15 条内置权限（含 `Stop`）上限 32；④ 运行态未授权弹登录、登录后重判；⑤ 旧 5 字段 `Role` 账号文件 fail-closed、不迁移不导入。其余 UI 细节（尺寸、8 状态表现、令牌映射、19 项已知偏差等）在 B5 逐页验收时按 R132 逐项勾选。

- [ ] 窗口尺寸 1100×820 / 最小 980×700，与 `HardwareManagerView` 同壳
- [ ] 三页签及各自内容（账号与用户组 / 在线会话 / 审计记录）
- [ ] 账号页签：用户组下拉（普工/产线操作员/厂家 + 模板）+「管理用户组…」；在线会话显示用户组
- [ ] 组编辑弹窗：动态权限目录复选框（16 项）；**`Stop` 为普通复选**（产线操作员/普工/厂家/模板均勾）；模板组只读；自定义组可建/改/删；删除守卫
- [ ] **取消 `Stop` 的红字警告 + 保存二次确认**（“该组将无法停止设备；若含 AxisJog，点动中只能靠系统自动 JogStop”）
- [ ] 权限目录弹窗：15 条内置 + 1 自定义示例 = 16/32，上限 32；无 `SafetyStop` 行；删除守卫**查组 + 查按钮**
- [ ] 8 个状态名与表现（尤其 `UnsavedSolution` 只读、`WriteFailed` 回滚、`FactoryFallback` 一次性提示）
- [ ] 运行态未授权弹登录：登录后重判并继续；**`Stop` 也按权限**（默认模板含 Stop），点动中权限丢失由系统自动 `JogStop`
- [ ] 账号文件**不兼容旧方案**：只 `Name/Salt/Hash/Iterations/GroupId`，旧 `Role` 文件 fail-closed，不迁移、不导入
- [ ] 令牌映射表与「0 hex」要求
- [ ] **补充态 1 · 新建权限**：点「＋ 新建权限」出现行内表单（显示名 + 自动生成 Key 预览 + 取消/创建），不另开二级弹窗
- [ ] **补充态 2 · 32/32 满额**：新建按钮禁用 + 计数 32/32 警告色 + 底部“已达上限 32 条，删除或合并后再建。”
- [ ] **补充态 3 · 模板·管理员全选**：15 项全勾、只读、保存/删除禁用；切回产线操作员恢复 7 项
- [ ] **补充态 4 · 运行态拒绝**：错误用户名/密码 → 框内错误提示 + 动作不下发 + 可重试
- [ ] **补充态 5 · 运行态取消**：点取消 → 登录框关闭、动作未执行、给出「已取消」回执
- [ ] 已知偏差 19 项（接受 / 替代方案）
- [ ] v1/v2 已归档、不用于复刻；旧稿保留、不删

> 未确认前不得进入 B5 的 WPF 复刻（G6 / R127）。确认后本文件冻结为 v3.1；改设计升 v4 并同步令牌映射与已复刻页面（R128）。

---

## 十、v4 草案（2026-10-25，待确认）

> 状态：**已确认并复刻（2026-10-25）**。用户会话确认「按照这个复刻」；v3.1 保留为历史基线，v4 为当前生效版本。
> 入口：[`design-v4.html`](design-v4.html)；设计证据：[`evidence/v4/`](evidence/v4/)（7 张）；WPF 复刻证据：[`evidence/v4-wpf/`](evidence/v4-wpf/)（7 张）。

### 10.1 为什么出 v4

2026-10-25 UI 审查（[`用户管理页UI审查（B5视觉验收缺口）`](../../code-changes/2026-10-25-用户管理页UI审查（B5视觉验收缺口）.md)）的结论：B5 实现与 v3.1 不是细节偏差，而是结构性缺项——账号页直接复用旧 760px 弹窗（双标题 / 双关闭 / 居中窄卡）、审计页丢「事件」列且无表头 / 筛选 / 计数、权限目录缺 3 列且「满额禁用」绑定失效、同屏两套调色板。

v4 不是换风格，而是**把 v3.1 的口径落成一张可复刻的图**，并把实现阶段暴露出的三处设计问题一并改掉：

1. 账号页「重置」语义不清（实现里实际是「放弃修改」）→ 拆成「放弃修改」与「保存用户组」；
2. 审计时间只有 `HH:mm:ss`，跨天查询会歧义 → 改 `MM-dd HH:mm:ss`；
3. 结果列会出现历史值 `Warning` → 定义第五个显示桶「警告」，读取端归一，禁止英文原值上屏。

### 10.2 覆盖范围（7 张）

| # | 文件 | 内容 |
|---|---|---|
| 1 | `design-v4-01-accounts.png` | 账号与用户组（搜索 / 计数 / 「当前」标记 / 确认密码 / 安全策略摘要） |
| 2 | `design-v4-02-sessions.png` | 在线会话（8 列 + 刷新 / 全部踢下线） |
| 3 | `design-v4-03-audit.png` | 审计记录（筛选行 + 7 列 + 结果 chip + 底部计数） |
| 4 | `design-v4-04-catalog.png` | 权限目录（5 列 + 使用组 + 类型 chip + 行内操作） |
| 5 | `design-v4-05-catalog-new.png` | 权限目录 · 行内新建（显示名 + 自动 Key + 取消 / 创建） |
| 6 | `design-v4-06-catalog-full.png` | 权限目录 · 32/32（计数警告色 + 新建禁用 + 底部上限提示 + 滚到底） |
| 7 | `design-v4-07-groups.png` | 用户组编辑（模板只读 / 权限 7/16 / Stop 红字警告 / 删除守卫） |

**不在本次证据内**：v3.1 §4 的 8 个命名状态与运行态登录 / 拒绝 / 取消 5 态。主结构未变（顶部横幅 + 表单禁用 + 空态），定稿后按 v4 版式重出，见 §10.5。

### 10.3 相对 v3.1 的变更（逐项）

| # | v3.1 | v4 | 理由 |
|---|---|---|---|
| 1 | 右卡按钮「重置密码 / 保存用户组」 | 「放弃修改 / 保存用户组」 | 「重置密码」与密码框的「留空即不改」重复；实现里该命令实际是丢弃草稿（`CancelNewCommand`），文案必须说真话 |
| 2 | 密码提示「界面不提供显示密码」 | 两个密码框各带眼睛，提示改为「两次输入必须一致」 | 实现侧 `PasswordBox` 全局样式本来就有眼睛；文案不能与控件互相打脸 |
| 3 | 账号清单右侧状态点（无图例） | 「当前」chip（仅当前登录账号） | 状态点没有图例，用户无法知道绿 / 黄 / 红各代表什么；「当前」是唯一会改变可操作性的状态 |
| 4 | 审计时间 `HH:mm:ss` | `MM-dd HH:mm:ss` | 日期范围可跨天，只有时分秒无法定位 |
| 5 | 审计结果四值 | 五桶：成功 / 失败 / 未执行 / 被拒绝 / 警告 | CSV 历史值会出现 `Warning`；读取端归一到「警告」，禁止英文原值上屏（R84/R85 同类） |
| 6 | 审计说明保留 `[sol=…]` 前缀 | 当前方案行不显示前缀；勾选「显示未归属」后才显示来源方案 | 页头已声明「只显示当前方案」，逐行重复是噪声 |
| 7 | 权限目录计数只在标题栏 | 标题 chip + 工具条「已定义 N 条（内置 x + 自定义 y）」 | 满额时两个数字都要能核对；工具条文案与计数同源 |
| 8 | 权限目录操作列只有「改名 / 删除」 | 内置行「删除」置灰并带 ToolTip，自定义行红色 | 基线要求内置不可删；实现里按钮可点、点了才报错 |
| 9 | 用户组编辑权限行只显示名称 | 名称 + 小号 Key（同行左对齐） | 组编辑与权限目录要对得上号；Key 不能右对齐飘在两列中间 |
| 10 | 无「当前账号」提示 | 清单行「当前」chip + 右卡脚注「当前登录账号与最后一个管理员不可删除」 | 删除守卫在按下前就要可见（R136 同一口径） |

### 10.4 令牌映射

**不新增令牌**。全部沿用 §5 的 `Dialog*` 键；原型 CSS 变量名与 `Colors.xaml` 键逐字对应（`--DialogAccentBrush` → `DialogAccentBrush`），复刻时按名字取，不抄色值。

账号页不再引用任何 `Scada*` 令牌（`#409EFF` 旧蓝、`#ECF5FF` 旧浅蓝），全页只有一套主色 `#0EA5E9`。

### 10.5 复刻前必须补齐

1. **8 个命名状态 + 运行态 5 态**按 v4 版式重出证据（结构同 v3.1 §4，只换外壳）。
2. **宽度预算**：审计 7 列按 1041px（1100 − 2×20 页边距 − 17 滚动条）定固定列宽；`GridView` 无星号列宽（§6 #4）。
3. **ToolTip**：审计「说明」、账号名、组名等活值列必须带 ToolTip（§6 #8）；原型里出现省略号处即超宽。
4. **焦点态**：原型未画键盘焦点；WPF 侧所有可聚焦控件挂 `DialogFocusVisual`（R135）。
5. **模态**：原型用遮罩表达模态；WPF 侧是 Prism 独立模态窗，**不要求实现遮罩**（§6 #6 同类，只保留一层阴影）。

### 10.6 验收清单（R132，逐项勾）

- [ ] 账号页：搜索 / 计数 / 「当前」chip / 确认密码 / 安全策略摘要 / 「放弃修改」与「保存用户组」分离
- [ ] 账号页：`Scada*` 令牌清零，主按钮与页签同色
- [ ] 在线会话：8 列 + 表头 + 刷新 / 全部踢下线 + Web 不可用空态
- [ ] 审计：筛选行（日期范围 / 操作者 / 结果 / 显示未归属）+ 7 列 + 表头 + 结果 chip + 底部计数
- [ ] 审计：时间 `MM-dd HH:mm:ss`；结果五桶，无英文原值
- [ ] 权限目录：5 列（名称 / Key / 使用组 / 类型 / 操作）+ 行内新建 + 32/32 三件套
- [ ] 权限目录：内置行删除置灰；被引用删除给出「N 个组 / M 个按钮」
- [ ] 用户组编辑：模板只读、权限 7/16、Stop 红字警告、删除守卫带原因
- [ ] 8 个命名状态与运行态 5 态按 v4 重出
- [ ] 已知偏差 §6 十九项逐项接受或给替代方案

### 10.7 复刻结果（2026-10-25）

- **代码**：`UserManagementView.xaml`（外壳 / 页签 / 底栏 / 会话 / 审计）、`ScadaUserManagerView.xaml`（账号两卡）、`ScadaPermissionCatalogView.xaml`（五列 + 行内新建 + 32/32）、`ScadaGroupEditorView.xaml`（模板/自定义两段 + 权限 Key + 删除原因）；配套四个 VM。
- **WPF 证据**：[`evidence/v4-wpf/after-0*.png`](evidence/v4-wpf/)（7 张，与 §10.2 的设计稿一一对应）；原始出图 `artifacts/render/20261025/user-management-v4/`。
- **探针**：`tools/probes/_UserManagementUiProbe`（已登记 `probes.json`）。七屏尺寸与像素指纹均通过；另抽查搜索过滤（`op` → 1 条）、两次密码不一致拒绝、内置权限删除 `CanExecute=False`。
- **闸门**：`tools/run_gates.ps1` **14 步全绿**（`UIThemeSmokeTest` 125 PASS / 0 FAIL、`ScadaChecks` 3052/0、`FlowCanvasChecks` 3672/0、`ScadaWebChecks` 190/0）。

**逐屏差异（R132，均已处置）**：

| # | 屏 | 差异 | 处置 |
|---|---|---|---|
| 1 | 账号 | 账号顺序按名字排序（admin / engineer1 / operator1 / viewer），设计稿是 admin / operator1 / engineer1 / viewer | 接受：顺序由 `ScadaUserStore.Users` 决定，不为截图改排序 |
| 2 | 全部 | 选中页签文字用共享 `DialogTabItem` 的主色蓝；设计稿是主文字色 | 接受：改共享样式会影响全部弹窗，单页覆盖不了模板触发器 |
| 3 | 全部 | 窗口四角直角；设计稿 12px 圆角 | 接受：`WindowStyle=None` 无圆角，平台差异（§6 #6 同类） |
| 4 | 全部 | 标题栏品牌块纯主色；设计稿渐变 | 接受：`Colors.xaml` 只有画刷令牌，渐变需新增 Color 令牌（独立批次） |
| 5 | 会话 | 「会话」列显示 8 位 `SessionRef` 前缀（完整值在 ToolTip）；设计稿示例 `web-7f3a` | 接受：DTO 只给 16 hex `SessionRef`，`SessionId` 禁止上屏（SW8-a） |
| 6 | 审计 | 默认日期范围近 7 天；设计稿示例 10-01..10-25 | 接受：默认值属交互口径 |
| 7 | 审计 | 「共 11 行」是探针种子行数；设计稿示例 128 行 | 接受：数据驱动 |
| 8 | 权限目录 | 纯中文显示名的 Key 预览是 `CustomPerm`；设计稿示例 `AlarmAck` | 接受：§6 #16 已定「纯中文取 CustomPerm」，音译不可实现；预览与落盘同一生成函数 |
| 9 | 权限目录 / 组编辑 | 无遮罩（独立模态窗） | 接受：WPF 是 Prism 独立窗，§10.5 已写明不要求遮罩 |
| 10 | 账号 | 策略摘要数字取实际配置；探针按设计稿示例注入 8 位 / 30 分钟 / 90 天，生产默认 4 位 / 10 分钟 / 90 天 | 接受：摘要必须数据驱动，不能写死示例值 |

**未覆盖**：8 个命名状态（`NoSolution` / `UnsavedSolution` / `WriteFailed` / `EmptyAccounts` / `WebUnavailable` / `NotAdministrator` / `LoadingOrFailed` / `FactoryFallback`）与运行态登录 / 拒绝 / 取消 5 态未按 v4 重出证据；模板组只读态未截图；DPI 125%/150%、键盘焦点、Tab 键序未验。

> v4 已于 2026-10-25 确认并复刻；后续改动按 R128 升 v4.1，并同步本节的差异清单。

---

## 十一、版本历史

| 版本 | 日期 | 变更 |
|---|---|---|
| v1 | 2026-10-25 | 三页签 + 8 状态 + 角色下拉；**已作废** |
| v2 | 2026-10-25 | 用户组 + 11 个固定权限位；**已作废** |
| v3 | 2026-10-25 | B2：命名权限目录（≤32）+ 模板组 + 自定义组 + 权限目录管理弹窗；14 张证据。**其中 Stop 特例与 `SafetyStop` 已被 B3 废弃** |
| v3（B3 修订） | 2026-10-25 | B3：`Stop` 改普通权限 + 默认模板含 `Stop` + 取消红字警告与二次确认 + 自动 `JogStop` 例外；不兼容旧账号文件（无 `Role`，fail-closed）；删除守卫查组 + 查按钮；运行态 `Stop` 也弹登录；重出 14 张证据 |
| v3.1（B3 补充态） | 2026-10-25 | **主结构不变**，补 5 个补充态（新建权限行内表单 / 32/32 满额 / 模板·管理员全选只读 / 运行态鉴权失败 / 运行态取消回执）；证据 14 → **19 张**，全部 1100×820；14 张旧证据逐字节不变（SHA-256 已核）；已知偏差 14 → 19 项 |
| v4（已确认并复刻） | 2026-10-25 | 依 UI 审查重出：单窗口壳 / 账号页补搜索·确认密码·策略摘要·「当前」标记 / 审计 7 列 + 筛选 + 五桶结果 / 权限目录 5 列 + 行内新建 + 32/32 / 组编辑权限行带 Key；令牌收敛到 `Dialog*`；设计证据 7 张 + WPF 证据 7 张；`run_gates` 14 步绿 |
