# AI 操作计划（计划任务化）

> **目标**：把「做一步想一步」改成「想一次 → 生成完整计划 → 点一下任务 → 按计划自动执行」。
>
> **适用**：本仓库的 VisionMaster 桌面端（WPF / net9.0-windows）。
>
> **版本**：1.0 · 2026-10-23
>
> **事实来源**：当前磁盘上的 `Shell.xaml`、`ShellViewModel.cs`、各 `DialogViews/*.xaml`、`StepParameterDialog.cs`、`VariableDraft.cs`、`CommunicationConfig.cs`、`ScadaEditorView.xaml` 等源码与 XAML 文案。**不是**按记忆或旧文档写的。
>
> **验证状态**：24 个计划文件已通过 `validate-plans.ps1` 结构校验；**尚未在真机上端到端执行**。原因见 §9 K-01/K-02：本仓库绝大多数控件没有 `AutomationId`，第一次执行必须先按 §10 校准选择器。

---

## 目录

- [0. 一句话结论](#0-一句话结论)
- [快速开始](#快速开始)
- [1. 为什么会慢：决策与执行混在一起](#1-为什么会慢决策与执行混在一起)
- [2. 总体设计：四个角色](#2-总体设计四个角色)
- [3. 功能清单（31 项）](#3-功能清单31-项)
- [4. 可执行步骤序列](#4-可执行步骤序列)
- [5. 文件怎么组织，AI 才能直接执行](#5-文件怎么组织ai-才能直接执行)
- [6. 把零散操作改造成「计划任务」](#6-把零散操作改造成计划任务)
- [7. 「点一下任务」的三种落地方式](#7-点一下任务的三种落地方式)
- [8. 落地路线](#8-落地路线)
- [9. 风险、边界与已知坑](#9-风险边界与已知坑)
- [10. 选择器校准（第一次必做）](#10-选择器校准第一次必做)
- [11. 验收清单](#11-验收清单)

---

## 0. 一句话结论

把「每一步都让 AI 看图、判断、再决定点哪里」改成**三层分离**：

1. **任务书**（`plans/*.json`）：一个用户可见结果 = 一个计划，步骤是固定的点击/输入/等待/断言。
2. **选择器表**（`ui-map.json`）：所有「点哪里」只在这里写一次，计划只引用 ID。
3. **执行器**（UIA 或 computer-use）：只做机械执行与断言，不再做业务判断。

AI 的工作从「N 步 × 每步推理」变成「1 次规划 + N 步机械执行」。按本仓库 24 个计划估算，常见任务（保存方案、新建流程、加算子、跑一次）每步的模型调用从 1 次降到 0 次。

---

## 快速开始

1. **校验计划**：

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File docs\AI操作计划\tools\validate-plans.ps1
   ```

2. **选任务**：打开 `TASKS.md`，找一句话（如「方案保存」「跑一次」「加相机」）和它对应的计划 ID、必填参数。

3. **对 AI 说一句**：

   ```text
   执行 solution-create-and-save，solution_name=Demo_20261023，
   save_path=D:\C#\VM\解决方案\Demo_20261023.vms
   ```

   AI 只读一次 `plans/solution-create-and-save.json`，按 `steps` 机械执行；中途不再问「下一步点哪」。

> 第一次跑之前，先按 §10 校准选择器；否则按 §9 K-01/K-02 的兜底策略走。

---

## 1. 为什么会慢：决策与执行混在一起

| | 现状（走一步想一步） | 计划任务 |
|---|---|---|
| 每一步 | 截图 → 识别控件 → 判断位置 → 决定动作 → 执行 | 查表拿选择器 → 执行 → 断言 |
| 模型调用 | 每步 1 次（甚至多次） | 规划 1 次，执行 0 次 |
| 失败处理 | 重新看图、重新推理，可能越走越偏 | 停在失败步，报「第 n 步期望 X 实际 Y」 |
| 可复用 | 换个任务重新来一遍 | 同一计划换参数反复跑 |
| 可验收 | 「看起来点对了」 | 每步有机器可判定的 `expect` |

关键不是「让 AI 点得更快」，而是**把判断从执行期移到规划期**。执行期只允许做三类事：定位、动作、断言。

---

## 2. 总体设计：四个角色

```text
用户 / 任务入口（命令、任务板、快捷方式）
        │  选择 plan id + 参数
        ▼
plans/*.json  ──引用──▶  ui-map.json
（做什么、顺序、判据）      （点哪里、窗口、选择器）
        │
        ▼
执行器（UIA / computer-use / 混合）
        │  机械执行：click / type / drag / key / wait
        ▼
VisionMaster
        │
        ▼
断言（expect / postconditions）──▶ 日志 + 截图 + 失败步号
```

| 角色 | 文件 | 只允许放什么 | 不允许放什么 |
|---|---|---|---|
| 任务书 | `plans/*.json` | 业务顺序、参数、期望结果、失败策略 | 坐标、窗口标题、控件类名 |
| 选择器表 | `ui-map.json` | 窗口、控件名、AutomationId、兜底坐标 | 业务顺序、参数、判断逻辑 |
| 执行器 | 外部程序 | 原语实现、等待、重试、日志 | 业务规则、「聪明地」改步骤 |
| 断言 | 计划内 | 可观察的状态（窗口、文本、文件、值） | 「看起来对了」「应该没问题」 |

---

## 3. 功能清单（31 项）

> 入口写法：`☰` = 顶栏最左侧汉堡按钮；`→` = 依次点击；`[文本]` = 按 UIA Name 精确匹配；`{}` = 计划参数。
>
> 「计划」列有文件名的，已有可执行 JSON；标「文档」的，本文件给出步骤序列，尚未落 JSON（按 §6 的六步法可随时补）。

| ID | 功能 | 入口（精确） | 关键输入 | 成功判据 | 计划 / 风险 |
|---|---|---|---|---|---|
| A-01 | 新建方案 | ☰ → 文件(F) → 新建方案 | 解决方案名称 | 顶栏中央显示新方案名；toast「已创建」 | `solution-new` / 低 |
| A-02 | 保存方案 | ☰ → 文件(F) → 保存方案 | 完整 `.vms` 路径 | toast「保存成功」+ 文件存在 | `solution-save` / 低 |
| A-03 | 打开方案 | ☰ → 文件(F) → 打开方案 | 完整 `.vms` 路径 | toast「加载成功」 | `solution-open` / 中 |
| A-04 | 方案列表打开 | ☰ → 文件(F) → 方案列表 → 选中行 → 打开方案 | 方案名 | toast「加载成功」 | `solution-list-open` / 中 |
| B-01 | 新建流程 | 流程胶囊右键 → 流程 → 添加 | 锚点流程名 | 出现「新建流程」胶囊 | `flow-create` / 低 |
| B-02 | 重命名流程 | 流程胶囊右键 → 流程 → 重命名 | 旧名、新名 | 胶囊文本变为新名 | `flow-rename` / 低 |
| B-03 | 添加算子 | 视图 → 流程画布 + 工具箱 → 搜索 → 拖到画布 | 算子名、锚点步骤 | 画布出现节点 + 流程树多一行 | `step-add` / 中 |
| B-04 | 配置 ActionStep 参数 | 双击节点 / 右键 → 步骤参数… | 步骤名、字段键值 | 参数窗关闭，节点摘要更新 | `step-configure` / 中 |
| B-05 | 配置条件/等待步骤 | 双击节点 | 步骤名、表达式、轮询/超时 | 「保存并应用」后窗口关闭 | `step-configure-condition` / 中 |
| B-06 | 变量绑定 | 节点右键 → 编辑绑定…（打开变量绑定） | 步骤名、端口名、来源名 | 绑定窗关闭，端口出现链接 | `step-bind-variable` / 中 |
| B-07 | 编辑步骤（复制/删除/断点） | 画布节点 / 右键菜单 / 快捷键 | 步骤名 | 节点增删或断点圆点变化 | 文档 / 中 |
| C-01 | 编译 | 运行条「编译」（仅调试/自由模式可见） | — | 无「错误列表」按钮；有错则出现红色按钮 | 文档 / 低 |
| C-02 | 运行一次 | 运行条「运行主流程」 | — | toast「已启动 N 个流程的单次运行」 | `run-once` / 中 |
| C-03 | 循环运行 | 运行条「循环主流程」 | — | toast「已启动 N 个流程的连续运行」 | `run-loop` / 中 |
| C-04 | 暂停/继续/单步 | 运行条「暂停 / 继续 / 单步」；F5/F10 | — | 状态文字「已暂停 / 启动中」 | 文档 / 中 |
| C-05 | 停止 | 运行条「停止」 | — | toast「已请求停止 N 个」+ 状态「未启动」 | `run-stop` / 低 |
| D-01 | 新增本地变量 | ☰ → 系统(S) → 全局变量 → 本地变量 → ＋ 添加变量 | 名称、类型、初始值 | 变量明细出现该行 | `variable-add-local` / 低 |
| D-02 | 新增网络变量 | 变量管理 → 左栏选连接 → ＋ 添加变量 | 名称、类型、存储区、偏移量、扫描组 | 变量明细出现该行 | 文档 / 中 |
| D-03 | 新增通讯设备 | ☰ → 系统(S) → 硬件管理 → 通讯 → 添加设备 | 连接名、协议、IP、端口号 | 设备表出现该行；关闭时保存 | `comm-add-device` / 中 |
| D-04 | 新增相机 | 硬件管理 → 相机 → 添加相机 | 驱动、序列号、显示名 | 相机列表出现该序列号 | `camera-add` / 中 |
| E-01 | 捕获配方 | ☰ → 系统(S) → 配方管理 → 捕获当前值 | 配方名、编码、备注 | 左侧配方列表出现该配方 | `recipe-capture` / 中 |
| E-02 | 应用配方 | 配方管理 → 选中配方 → 应用 / 应用并保存 | 配方名 | 参数表状态变为已应用 | 文档 / 中 |
| F-01 | 新建组态画面 | ☰ → 视图(V) → 组态画面 → 新建 | 期望页名（默认 `画面_N`） | 页面下拉选中新页 | `scada-page-new` / 低 |
| F-02 | 拖入图元并改属性 | 组态画面 + 图元工具箱 → 拖到画布 → 属性面板 | 图元名、属性值 | 画布出现图元，属性面板有对象头 | `scada-add-element` / 中 |
| F-03 | 运行/退出组态 | 组态画面 → 运行画面 → ■ 停止运行 | 页名（可选） | 运行窗口出现/关闭 | `scada-run` / 中 |
| G-01 | 切换工作模式 | Ctrl+1/2/3/4 或顶栏 调试/运行/组态/自由 | 模式键、标志文本 | 目标模式的标志文本出现 | `mode-switch` / 低 |
| G-02 | 显示面板 | ☰ → 视图(V) → 流程导航/工具箱/检查器/图层/控制台… | 面板名 | 面板可见 | 文档 / 低 |
| G-03 | 保存布局 | ☰ → 工具(T) → 保存布局 | 程序目录 | `Layout.xml` mtime 更新 | `layout-save` / 低 |
| G-04 | 系统设置 | ☰ → 系统(S) → 系统设置 | 分组、字段值 | 弹窗关闭；`AppConfig.json` 更新 | 文档 / 中 |
| H-01 | 帮助搜索 | ☰ → 帮助(H) → 帮助文档；Ctrl+F | 关键词 | 目录命中并打开正文 | `help-search` / 低 |
| H-02 | 命令面板跳转 | Ctrl+K → 输入 → 回车 | 命令/视图/流程名 | 目标模式或面板出现 | 文档 / 低 |

---

## 4. 可执行步骤序列

> **读法**：每一行都是一个原语。`等待` 不是「睡一会儿」，是「等到某个可观察条件成立，超时则失败」。
>
> **所有序列默认前提**：VisionMaster 已启动、主窗口可见、没有流程在跑（运行中会禁用方案类命令）。

### A-01 新建方案

| 步 | 动作 | 目标（精确文案 / 选择器） | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | `app.menu.root` 顶栏最左 ☰ | — | 文件菜单可点 |
| 2 | click | `app.menu.file` 文本「文件(F)」 | — | 展开 4 项 |
| 3 | click | `app.menu.file.new` 文本「新建方案」 | — | 出现窗口「创建新解决方案」 |
| 4 | fill | `dialog.new_solution.name` 行「解决方案名称」 | `{solution_name}` | 输入框值 = 名称 |
| 5 | click | `dialog.new_solution.ok` 文本「确 定」（EasyDialog 中间有空格） | — | 弹窗关闭 |
| 6 | assert | 顶栏中央方案名 | — | 文本 = `{solution_name}` |

**判据/坑**：

- 新建只改内存，**不落盘**；要持久化必须接 `solution-save`。
- 空名、重名会被拦下并给中文提示，计划应在执行前校验参数。
- 运行中 `CanExecuteSolution` 为 false，菜单项置灰；先停止。

### A-02 保存方案

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | ☰ → 文件(F) → 保存方案 | — | 出现「保存解决方案」 |
| 2 | type | `dialog.save_solution.filename`（AutomationId 1001） | 完整路径 `{save_path}` | 文件名框 = 路径 |
| 3 | click | `dialog.save_solution.save`（AutomationId 1） | — | 保存对话框关闭 |
| 4 | click（可选） | `dialog.overwrite.yes` 文本「是(Y)」 | — | 仅在「确认另存为」出现时执行 |
| 5 | assert | toast | — | 含「保存成功」 |
| 6 | assert | 文件系统 | — | `{save_path}` 存在 |

**判据/坑**：

- **保存对话框每次都会弹**，即使方案已有路径；不要指望「点一下就直接保存」。
- 菜单里的 `InputGestureText="S"` 只是显示文本，**没有 Ctrl+S 绑定**；必须走菜单。
- 保存会同时写变量快照、通讯配置、布局快照；另存为还会把旧目录的 `Image`/`Alarms` 复制到新目录。
- 父目录必须已存在；Windows 保存对话框不会替你建目录。参数里给完整路径前先确认目录存在。
- 运行中保存被禁用；先停止。

### A-03 打开方案

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | ☰ → 文件(F) → 打开方案 | — | 出现「打开解决方案」 |
| 2 | type | `dialog.open_solution.filename` | 完整路径 `{solution_path}` | 文件名框 = 路径 |
| 3 | click | `dialog.open_solution.open` | — | 打开对话框关闭 |
| 4 | assert | toast | — | 含「加载成功」 |
| 5 | assert | 顶栏中央 | — | 方案名 = 文件名对应方案 |

**判据/坑**：

- 打开会替换当前方案，并清空上一份方案的运行会话；运行中禁止。
- 手动打开会按方案里保存的布局恢复；自动加载启动方案时**不恢复布局**。
- 文件不存在/解析失败会给 Error 提示，不会静默。

### A-04 从方案列表打开

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | ☰ → 文件(F) → 方案列表 | — | 出现「方案列表」 |
| 2 | click | `dialog.solution_list.row`，按方案名匹配 | `{solution_name}` | 该行选中 |
| 3 | click | `dialog.solution_list.open` 文本「打开方案」 | — | 列表关闭 |
| 4 | assert | toast | — | 含「加载成功」 |

**判据/坑**：

- 列表持久化在程序目录 `AppConfig.json`，与 `.vms` 文件是两回事。
- 「设为默认启动」只改 `StartupSolutionPath`，不会立刻加载。
- 「浏览添加…」只把文件加入清单，不加载。

### B-01 新建流程

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | ☰ → 视图(V) → 流程导航 | — | 左侧「流程导航」面板可见 |
| 2 | right_click | `flow.capsule`，按锚点流程名匹配 | `{anchor_flow}` | 出现右键菜单 |
| 3 | click | `flow.context.flow` 文本「流程」 | — | 展开子菜单 |
| 4 | click | `flow.context.add` 文本「添加」 | — | 出现新胶囊「新建流程」 |

**判据/坑**：

- 流程胶囊列表**没有常驻「+」按钮**，新建只能走右键菜单。
- 新流程默认名是「新建流程」，重名时加序号；要指定名字接 `flow-rename`。
- 流程胶囊是双列 `UniformGrid`，名称过长会截断；断言用 `name_contains`，不要用全等。

### B-02 重命名流程

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | right_click | `flow.capsule` 旧名 | `{old_name}` | 右键菜单出现 |
| 2 | click | 流程 → 重命名 | — | 出现「流程重命名」 |
| 3 | type | `dialog.flow_rename.input` | `{new_name}` | 输入框 = 新名 |
| 4 | click | `dialog.flow_rename.ok`「确 定」 | — | 弹窗关闭 |
| 5 | assert | `flow.capsule` | — | 文本含 `{new_name}` |

### B-03 添加算子

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | ☰ → 视图(V) → 流程画布 | — | 中央「流程画布」文档可见 |
| 2 | click | ☰ → 视图(V) → 工具箱 | — | 右侧「工具箱」面板可见 |
| 3 | click | `toolbox.search` 占位「搜索节点...」 | — | 搜索框聚焦 |
| 4 | type | 同上 | `{tool_name}` | 列表只剩命中项 |
| 5 | drag | 从 `toolbox.item`（按名称）到 `canvas.after_step`（锚点 `{anchor_step}` 下方 40px） | — | 画布出现节点标题 |
| 6 | assert | `canvas.step_title` + `process.step_row` | — | 两处都能找到 `{tool_name}` |

**判据/坑**：

- **只能拖拽**，工具箱条目没有双击添加。
- 拖放必须走 gong 的拖放链路（`dd:DragDrop.IsDropTarget` + `DropHandler`）；用裸鼠标事件模拟时，拖影 Popup 可能残留（R171）。
- 落点决定插入位置；拖进分支容器要落在「将算子拖入此分支内部」区域。
- 运行中画布编辑被 `EditGate` 锁住；先停止。
- 拖完可以用画布工具条「整理」重排，避免坐标散乱。

### B-04 配置 ActionStep 参数

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | double_click | `canvas.step_title`，按步骤名匹配 | `{step_name}` | 出现标题 = 步骤名的参数窗 |
| 2 | fill | `dialog.plugin_config.form`，按字段显示名 | `{fields}` | 每个字段值写入 |
| 3 | click | `dialog.plugin_config.confirm`「确认」 | — | 参数窗关闭 |
| 4 | assert | 画布节点 | — | 节点摘要/参数标记更新 |

**判据/坑**：

- 参数窗标题 = 步骤名（`PluginConfigShellViewModel.Title`）。
- 「执行」只试运行、**不保存**；「确认」才写回。
- 字段键必须用属性面板上的显示名（`AutomationProperties.Name`）；插件自定义视图的控件名可能不同，先 dump 一次。
- 条件 / For / 等待步骤走 B-05，不是这个窗。

### B-05 配置条件 / 等待步骤

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | double_click | 节点 `{step_name}` | — | 出现「等待条件配置」/「条件逻辑配置中心」/「For 循环配置」/「分支匹配配置」 |
| 2 | click | `dialog.condition.expression` 表达式区 | — | 光标在表达式区 |
| 3 | type | 同上 | `{expression}` | 表达式文本写入 |
| 4 | fill | 轮询间隔 (ms)、最大超时 (ms)、超时处理 | 参数 | 字段值写入 |
| 5 | click | `dialog.condition.save`「保存并应用」 | — | 窗口关闭 |

**判据/坑**：

- 表达式编辑区没有 `AutomationName`，是当前选择器表里**置信度最低**的一格；第一次必须用 `dump-vm-uia.ps1` 校准。
- 轮询间隔/最大超时也是「TextBlock 标签 + TextBox」的自定义布局，没有 `AutomationName`。`step-configure-condition.json` 目前只固化了「表达式 + 保存并应用」；这两个字段要么先补 AutomationId，要么按实际 Tab 顺序补步骤，不要假装 `fill` 能直接填。
- 等待模式：最大超时 `0` = 一直等；超时处理可选终止流程/继续执行。
- 报警变量只列「用户可写的全局 bool 变量」。

### B-06 变量绑定

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | right_click | 节点 `{step_name}` | — | 出现右键菜单 |
| 2 | click | `canvas.context.bind`「编辑绑定…（打开变量绑定）」 | — | 出现「变量绑定」 |
| 3 | click | `dialog.variable_binding.port` 端口行 | `{port_name}` | 端口选中 |
| 4 | click | `dialog.variable_binding.source_tab` | `{source_kind}`（全部/变量/上游算子） | 来源列表切换 |
| 5 | type | `dialog.variable_binding.source_search` | `{source_name}` | 候选列表出现命中 |
| 6 | click | `dialog.variable_binding.candidate` | `{source_name}` | 候选选中 |
| 7 | click | `dialog.variable_binding.confirm`「确认绑定」 | — | 弹窗关闭 |

**判据/坑**：

- 数组 → 标量会弹「选择元素下标」，需要再填索引并确认。
- 名字类端口（变量名、步骤名、卡地址、端口名）**不应挂绑定**（R33）；这些端口用「手填 + 候选下拉」。
- 绑定窗的左栏端口顺序由插件声明决定；按显示名匹配，不要按行号。

### B-07 编辑步骤（复制 / 删除 / 断点 / 禁用）

| 动作 | 操作 | 判据 |
|---|---|---|
| 复制粘贴 | 选中节点 → `Ctrl+C` → 点画布空白 → `Ctrl+V` | 出现副本，位置在鼠标处 |
| 删除 | 选中节点 → `Delete`（画布）或右键 → 删除 | 节点消失，流程树行消失 |
| 断点 | 选中节点 → `F9` 或右键 → 切换断点 | 节点出现断点圆点 |
| 禁用/启用 | 右键 → 菜单文案随状态变化（禁用/启用） | 节点变灰/恢复 |
| 撤销/重做 | `Ctrl+Z` / `Ctrl+Y` 或工具条按钮 | 画布回退/前进 |

**判据/坑**：

- 画布快捷键挂在 `FlowCanvasView` 的 `UserControl.InputBindings`，焦点在画布子树内才生效。
- 删除分支卡片（`StepCollection`）不走「按选中项执行」的命令链（R25）；不要用同一套键去删分支。
- 运行中编辑被锁；先停止。

### C-01 编译

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | key | `Ctrl+1` 切调试模式 | — | 运行条出现 |
| 2 | click | `runbar.compile`「编译」 | — | 成功时无红色「错误列表」；失败时出现 |
| 3 | assert | 错误列表按钮 | — | 不存在 = 编译通过 |

**判据/坑**：

- 运行条在**组态**和**运行**模式下隐藏；先切调试或自由。
- 编译错误不会弹模态框，只在画布工具条出现红色「错误列表」；双击条目定位到步骤。
- 编译失败时运行按钮仍可能可点，但引擎会拒绝；不要以「按钮可点」判成功。

### C-02 运行一次

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | key | `Ctrl+1` | — | `runbar.run_once` 可点 |
| 2 | click | `runbar.run_once`「运行主流程」 | — | toast 含「已启动」 |
| 3 | assert | toast | — | 「已启动 N 个流程的单次运行」 |

**判据/坑**：

- 只跑 `Role=Main` 的流程；工位/子流程由主流程调用。
- 有断点时自动按调试运行（断点/单步生效，并行组按顺序执行）。
- 流程可能在断言前就跑完；不要把「停止按钮仍可点」当唯一判据。

### C-03 循环运行

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | key | `Ctrl+1` | — | `runbar.run_loop` 可点 |
| 2 | click | `runbar.run_loop`「循环主流程」 | — | toast 含「连续运行」 |
| 3 | assert | 状态文字 | — | 「循环运行中」 |

### C-04 暂停 / 继续 / 单步

| 动作 | 操作 | 判据 |
|---|---|---|
| 暂停 | 运行条「暂停」 | 状态「已暂停」；`runbar.resume` 可点 |
| 继续 | `F5` 或运行条「继续」 | 回到「启动中」/「循环运行中」 |
| 单步 | `F10` 或运行条「单步」 | 仅当**恰好一个**会话暂停时可点 |
| 断点 | `F9` | 当前选中步骤断点切换 |

**判据/坑**：

- `F10` 被 `Shell.OnShellPreviewKeyDown` 在隧道阶段截获，并且会挡掉 WPF 的「菜单激活键」行为；只认无修饰键的 F10。
- 多会话暂停时「单步」刻意置灰（步谁说不清）。

### C-05 停止

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | key | `Ctrl+1` | — | `runbar.stop` 可点 |
| 2 | click | `runbar.stop`「停止」 | — | toast「已请求停止 N 个运行中的流程」 |
| 3 | assert | 状态文字 | — | 「未启动」 |

**判据/坑**：

- 停止是 **StopAll**：界面整体运行、单流程运行、HTTP/定时触发一起停。
- 0 条在跑时按钮可能因「有流程在跑」而可点，但点下去只给 Info；不要把它当失败。

### D-01 新增本地变量

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | ☰ → 系统(S) → 全局变量 | — | 出现「变量管理」 |
| 2 | click | `dialog.variable_manager.local_source`「本地变量」 | — | 明细切到本地池 |
| 3 | click | `dialog.variable_manager.add`「＋ 添加变量」 | — | 出现「添加变量」 |
| 4 | fill | 变量名称 / 数据类型 / 初始值（选填）/ 变量描述（选填） | 参数 | 字段值写入 |
| 5 | click | `dialog.add_variable.ok`「确 定」 | — | 弹窗关闭 |
| 6 | assert | 变量明细行 | — | 出现 `{variable_name}` |
| 7 | click | `dialog.variable_manager.close`「关闭」 | — | 变量管理关闭 |

**判据/坑**：

- 数据类型是**显示名**：`整数 (int)`、`小数 (double)`、`单精度 (float)`、`文本 (string)`、`布尔 (bool)`、`图像 (HImage)`、各数组类型。
- 选「系统变量」来源时不能新建（宿主维护，只读）；「添加变量」会自动切回本地。
- 变量名不能与现有变量重名（忽略大小写）。

### D-02 新增网络变量

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | 变量管理 → 左栏选中连接 `{connection_name}` | — | 明细切到该连接 |
| 2 | click | 「＋ 添加变量」 | — | 出现「添加变量」 |
| 3 | fill | 变量名称、数据类型 | 参数 | 值写入 |
| 4 | fill | 所属连接、存储区、偏移量、扫描组 | 参数 | 值写入 |
| 5 | fill | 地址参数（Modbus 站号 / S7 DB块编号 + 位偏移） | 参数 | 值写入 |
| 6 | click | 「确 定」（EasyDialog） | — | 弹窗关闭 |
| 7 | assert | 明细行 | — | 出现变量名 |

**判据/坑**：

- Modbus `Address` 是**原始协议地址**，不是 `4xxxx` 编号；`40001 写法` 是每变量开关，默认关闭（R137/R152）。
- 数组元素个数必须落盘；`Length` 在 `DataType` **之后**写（R149）。
- 切换协议/连接会整表重建，先填切换字段，再填被切换出来的字段（与 `comm-add-device` 同一坑）。

### D-03 新增通讯设备

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | ☰ → 系统(S) → 硬件管理 | — | 出现「硬件管理」 |
| 2 | click | `dialog.hardware.tab_comm`「通讯」 | — | 通讯页可见 |
| 3 | click | `dialog.hardware.add_device`「添加设备」 | — | 出现「创建新通信」 |
| 4 | fill | 连接名称、协议类型 | 参数 | 值写入 |
| 5 | wait | 等属性网格重建完成（协议 `RequireRefresh`） | — | `底层链路参数` 分组出现 |
| 6 | click | `底层链路参数` 分组 | — | IP/端口字段可见 |
| 7 | fill | IP 地址、端口号 | 参数 | 值写入 |
| 8 | click | 「确 定」（EasyDialog） | — | 弹窗关闭 |
| 9 | assert | 设备表行 | — | 出现 `{connection_name}` |
| 10 | click | 「关闭」 | — | 硬件管理关闭；通讯配置脏时先保存 |

**判据/坑**：

- 协议切换会**整表重建**（`RequireRefresh`），一次填完所有字段会被重建吞掉；必须分两次填。
- 关闭硬件管理时如果通讯配置脏，会先 `SaveConfigAsync()`，失败不关窗；可能耗时数秒。
- 未实现的协议（OPC UA 等）会被 `Validate` 拒绝，不会静默连错。
- 没有打开方案时硬件管理直接提示「请先打开一个解决方案」。

### D-04 新增相机

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | 硬件管理 → 相机 | — | 相机页可见 |
| 2 | click | 「添加相机」 | — | 内联表单出现 |
| 3 | click | 驱动类型下拉（页内第一个 ComboBox） | — | 下拉展开 |
| 4 | select | 同上 | `{driver}` | 选中驱动 |
| 5 | key | `Tab` | — | 焦点到序列号 |
| 6 | type_focused | 序列号 | `{serial}` | 值写入 |
| 7 | key | `Tab` | — | 焦点到显示名 |
| 8 | type_focused | 显示名（留空则用序列号） | `{display_name}` | 值写入 |
| 9 | click | 「添加」 | — | 相机列表出现该序列号 |
| 10 | click | 「关闭」 | — | 硬件管理关闭 |

**判据/坑**：

- 序列号非空且唯一，重复会让「A 相机的图跑到 B 流程」，界面会提前拦。
- 驱动下拉取决于已加载的相机驱动插件；计划里不要把驱动名写死成不存在的项。
- 内联表单的三个输入框**没有 `AutomationProperties.Name`**，所以走 `Tab` + `type_focused`；补 AutomationId 后可改回 `fill`。

### E-01 捕获配方

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | ☰ → 系统(S) → 配方管理 | — | 出现「配方管理」 |
| 2 | click | 左栏第一个输入框（配方名称） | — | 焦点在名称框 |
| 3 | type_focused | 配方名称 | `{recipe_name}` | 值写入 |
| 4 | key | `Tab` | — | 焦点到产品编码 |
| 5 | type_focused | 产品编码（可空） | `{recipe_code}` | 值写入 |
| 6 | key | `Tab` | — | 焦点到备注 |
| 7 | type_focused | 备注（可空） | `{notes}` | 值写入 |
| 8 | click | 「捕获当前值」 | — | 左侧配方列表出现该配方 |
| 9 | click | 「关闭」 | — | 弹窗关闭 |

**判据/坑**：

- 捕获的是**当前已确认的步骤参数**；未确认的草稿不会被捕获。
- 配方名在方案内唯一；重名会被拦。
- 左栏三个输入框没有 `AutomationName`，走 `Tab` + `type_focused`；补 AutomationId 后可改回 `fill`。

### E-02 应用配方

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | ☰ → 系统(S) → 配方管理 | — | 出现「配方管理」 |
| 2 | click | 左侧配方行 `{recipe_name}` | — | 右侧参数表加载 |
| 3 | click | 「应用」或「应用并保存」 | — | 出现「确认应用」 |
| 4 | click | 「确认应用」 | — | 参数表状态变为已应用 |
| 5 | click | 「关闭」 | — | 弹窗关闭 |

**判据/坑**：

- 「应用」只写当前参数；「应用并保存」同时把配方值落盘。
- 「清除生效」只清除生效标记，不回滚参数。
- 应用配方会改步骤参数，属于方案内容变更；运行中不要做。

### F-01 新建组态画面

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | ☰ → 视图(V) → 组态画面 | — | 组态画布可见 |
| 2 | click | `dialog.scada.new`「新建」 | — | 页面下拉选中新页 |
| 3 | assert | `dialog.scada.page_selector` | — | 值为 `画面_N` |

**判据/坑**：

- 默认页名是 `画面_1`、`画面_2`…（`ScadaDocument.NextPageName`）。
- 右侧 ▾ 还有「新建弹窗页」「新建基座」；基座是跨页公共层，不能再引用基座。

### F-02 拖入图元并改属性

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | ☰ → 视图(V) → 组态画面 | — | 画布可见 |
| 2 | click | ☰ → 视图(V) → 图元工具箱 | — | 图元工具箱可见 |
| 3 | drag | 从 `dialog.scada.toolbox.item`（按名称）到 `dialog.scada.canvas` | `{element_name}` | 画布出现图元 |
| 4 | click | 新图元 | — | 右侧「属性」出现对象头 |
| 5 | type | 属性面板 X / Y / 宽度 / 高度 | 参数 | 值写入 |
| 6 | click | 画布空白 | — | 失焦提交（`UpdateSourceTrigger=LostFocus`） |

**判据/坑**：

- 图元拖放同样必须走 gong 的拖放链路。
- 属性面板是自定义模板，行没有 `AutomationProperties.Name`；**当前置信度低**，第一次必须校准，或改用「点击行 + 键盘输入」。
- 改完必须让控件失焦（点空白/按 Tab），否则绑定不写回。

### F-03 运行 / 退出组态画面

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | 组态画面 → 「运行画面」 | — | 出现「组态运行 - {页名}」窗口 |
| 2 | assert | 运行窗口 | — | 标题含页名；`■ 停止运行` 存在 |
| 3 | click | `runtime.scada.stop`「■ 停止运行」 | — | 运行窗口关闭 |

**判据/坑**：

- 运行的是**内存里这份**方案，不存盘。
- 运行窗口**没有关闭按钮，Esc 也不退出**；退出只有「■ 停止运行」一条路。
- 默认只读；写操作/运动命令受服务端逐命令鉴权与权限闸门限制（R132A）。

### G-01 切换工作模式

| 模式 | 快捷键 | 标志文本 | 备注 |
|---|---|---|---|
| 调试 | `Ctrl+1` | 「编译」 | 运行条可见，适合编译/运行/单步 |
| 运行 | `Ctrl+2` | 「停止运行」 | **不是「开始运行」**；运行条隐藏 |
| 组态 | `Ctrl+3` | 「基座」 | 运行条隐藏，只留组态面板 |
| 自由 | `Ctrl+4` | 「编译」 | 全部面板与文档 |

**判据/坑**：

- `Ctrl+2` 是切到运行驾驶舱视图，不会启动流程；启动流程用运行条的「运行主流程」/「循环主流程」。
- 组态/运行模式下运行条 `Collapsed`，UIA 里可能直接不存在；不要用「按钮找不到」判失败。

### G-02 显示面板

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | ☰ → 视图(V) | — | 展开面板清单 |
| 2 | click | 目标面板：视觉图像 / 流程导航 / 工具箱 / 控制台 / 数据栏 / 模块输出 / 流程画布 / 组态画面 / 图元工具箱 / 检查器 / 图层 / 设备状态 | 面板名 | 面板可见 |

**判据/坑**：

- **视图菜单只 Show，不 Hide**（R47 已知边界）；隐藏面板要按面板标题栏的隐藏按钮，菜单不提供「隐藏」。
- 面板可见性还会被「工作模式」收敛；切模式后菜单显示结果可能被覆盖。

### G-03 保存布局

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | ☰ → 工具(T) → 保存布局 | — | 无提示 |
| 2 | assert | `{app_dir}\Layout.xml` | — | 文件存在且 mtime ≥ 本次开始时间 |

**判据/坑**：

- 保存布局**没有成功提示**；只能靠文件 mtime 证明。
- 「加载布局」读 `Layout.xml`；「恢复默认布局」会弹确认框（默认按钮是取消），并删除用户布局文件。
- 改过 `Shell.xaml` 的停靠语义要递增 `LayoutHelper.CurrentLayoutSchemaVersion`（R43）。

### G-04 系统设置

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | ☰ → 系统(S) → 系统设置 | — | 出现「系统设置」 |
| 2 | click | 属性网格分组：运行窗口 / 安全与留档 / 画布 / 日志 / 服务与启动 / 并行执行 | 分组名 | 分组展开 |
| 3 | fill | 目标字段 | 参数 | 值写入 |
| 4 | click | 「确定」 | — | 弹窗关闭；`AppConfig.json` 更新 |

**判据/坑**：

- 写入的是程序目录 `AppConfig.json`；装在 Program Files 下可能因权限失败，弹窗内会给红字。
- 部分设置（运行窗口形态/落屏）下一轮生效；不要以「当前窗口没变」判失败。

### H-01 帮助搜索

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | click | ☰ → 帮助(H) → 帮助文档 | — | 出现帮助窗口 |
| 2 | key | `Ctrl+F` | — | 目录搜索框聚焦 |
| 3 | type | `dialog.help.search` 占位「搜索目录（只搜标题）」 | `{query}` | 目录出现命中 |
| 4 | click | 命中节点 | — | 正文标题 = 命中页 |
| 5 | key | `Escape` | — | 帮助窗口关闭 |

**判据/坑**：

- 搜索**只搜标题**，不搜正文。
- 帮助窗口的标题置信度低（`HelpViewModel` 未显式提供 `Title`）；按占位文字/正文锚定，不按窗口标题。
- `Ctrl+P` 打印当前页。

### H-02 命令面板跳转

| 步 | 动作 | 目标 | 输入 | 期望 / 判据 |
|---|---|---|---|---|
| 1 | key | `Ctrl+K` | — | 命令面板出现，焦点在输入框 |
| 2 | type | 面板输入框 | 「全局变量」/「硬件管理」/ 流程名 / 模式名 | 结果列表过滤 |
| 3 | key | `Enter` | — | 执行选中项 |
| 4 | assert | 目标界面 | — | 对应面板/弹窗/流程出现 |

**判据/坑**：

- 面板只收：四种模式、视觉图像/流程画布/组态画面/运行驾驶舱、流程导航/工具箱/检查器/控制台、全局变量、硬件管理、当前方案的流程名。
- **不含**新建/打开/保存方案；这些必须走文件菜单。

---

## 5. 文件怎么组织，AI 才能直接执行

### 5.1 目录结构

```text
docs/AI操作计划/
├─ README.md                 ← 本文件：功能清单 + 步骤库 + 方法
├─ TASKS.md                  ← 任务清单：说法 → 计划 ID → 参数
├─ plan.schema.json          ← 计划文件 JSON Schema（人读 + 工具校验）
├─ ui-map.json               ← 选择器注册表：窗口 + 控件 + 兜底坐标
├─ plans/
│  ├─ solution-new.json
│  ├─ solution-save.json
│  ├─ solution-open.json
│  ├─ solution-list-open.json
│  ├─ solution-create-and-save.json
│  ├─ flow-create.json
│  ├─ flow-rename.json
│  ├─ step-add.json
│  ├─ step-configure.json
│  ├─ step-configure-condition.json
│  ├─ step-bind-variable.json
│  ├─ run-once.json
│  ├─ run-loop.json
│  ├─ run-stop.json
│  ├─ variable-add-local.json
│  ├─ comm-add-device.json
│  ├─ camera-add.json
│  ├─ recipe-capture.json
│  ├─ scada-page-new.json
│  ├─ scada-add-element.json
│  ├─ scada-run.json
│  ├─ layout-save.json
│  ├─ mode-switch.json
│  └─ help-search.json
└─ tools/
   └─ validate-plans.ps1     ← 结构校验（ASCII，PS 5.1 可跑）
```

### 5.2 三层分离（最重要的一条）

| 层 | 文件 | 变更频率 | 谁维护 |
|---|---|---|---|
| 业务层 | `plans/*.json` | 任务变化时 | 业务/实施 |
| 选择器层 | `ui-map.json` | UI 改版时 | 开发/UI |
| 执行层 | 执行器 | 很少 | 平台 |

**规则**：

- 计划里**只许出现 ui-map 的 ID**，不许出现坐标、窗口标题、类名。
- ui-map 里**只许出现定位信息**，不许出现「先点 A 再点 B」。
- UI 改版只改 ui-map；任务改版只改 plans。两边互不污染。

### 5.3 一个计划文件的字段

| 字段 | 必填 | 作用 | 例子 |
|---|---|---|---|
| `schema` | 是 | 版本，执行器据此选解析器 | `vm-plan/1` |
| `id` | 是 | 任务 ID，必须等于文件名 | `solution-save` |
| `title` | 是 | 人读标题 | 保存当前方案到指定路径 |
| `version` | 是 | 计划版本（语义化） | `1.0.0` |
| `risk` | 否 | low/medium/high；high 需二次确认 | `low` |
| `params` | 否 | 参数声明（类型/必填/示例） | `save_path` |
| `preconditions` | 否 | 不满足就不执行 | 主窗口存在、菜单可点 |
| `steps` | 是 | 有序步骤 | 见下 |
| `postconditions` | 否 | 全局验收 | 文件存在、窗口关闭 |
| `on_fail` | 是 | `stop_and_report` / `retry_step` / `continue` | `stop_and_report` |

每个 step：

| 字段 | 作用 |
|---|---|
| `n` | 步号，必须 1..N 连续 |
| `do` | 原语：`menu/click/double_click/right_click/type/fill/key/select/check/uncheck/drag/wait/assert/screenshot/call/note` |
| `target` | ui-map 元素 ID |
| `target_name` | 动态名称（如 `{step_name}`），执行器用它做 `name_contains` |
| `path` | `menu` 原语的点击路径（元素 ID 数组） |
| `text` / `keys` / `fields` | 输入 |
| `from` / `to` | `drag` 的起点/终点 |
| `plan` / `args` | `call` 的子计划与参数 |
| `expect` | 本步的成功判据（窗口/元素/文本/值） |
| `skip_if` | 满足条件则跳过本步（用于可选的「覆盖确认」） |
| `timeout_ms` | 等待上限 |
| `on_fail` | `abort/retry/skip` |
| `retry` | 重试次数（只用于瞬时失败） |

### 5.4 原语表

| 原语 | 参数 | 语义 | 备注 |
|---|---|---|---|
| `menu` | `path` | 依次点击菜单项 | 每次点击后等下一项可见（最多 3s），再点；不要用固定 sleep |
| `click` | `target`, `target_name?` | 左键单击控件中心 | 优先 UIA `Invoke`，失败回落鼠标 |
| `double_click` | 同上 | 双击 | 画布节点开参数 |
| `right_click` | 同上 | 右键 | 打开上下文菜单 |
| `type` | `target`, `text`, `clear` | 聚焦并输入 | `clear=true` 先全选删除 |
| `type_focused` | `text` | 向当前焦点输入 | 控件没有 AutomationName 时用；先用 `click`/`Tab` 把焦点放好 |
| `fill` | `target`, `fields` | 按字段显示名批量填值 | 每项按 `AutomationProperties.Name` 找 Edit/ComboBox/CheckBox |
| `key` | `keys` | 发送键盘和弦 | `Ctrl+1`、`F5`、`F10`、`Escape` |
| `select` | `target`, `text` | 下拉/列表选文本 | ComboBox 优先 `SelectionItem` |
| `check` / `uncheck` | `target` | 勾选/取消 | CheckBox/ToggleButton |
| `drag` | `from`, `to`, `from_name?`, `to_name?` | 拖放 | 必须模拟真实鼠标按下/移动/释放，不能用 `Invoke` |
| `wait` | `for`, `target`, `text`, `timeout_ms` | 等条件成立 | `for`: window/element/text/file |
| `assert` | `assert`, 参数 | 断言 | 见 §5.6 |
| `screenshot` | `name` | 截图落盘 | 失败时自动截一张 |
| `call` | `plan`, `args` | 调用子计划 | 子计划先校验参数再执行 |
| `note` | `text` | 人读注释 | 执行器忽略 |

### 5.5 选择器优先级

执行器解析 `target` 时按以下顺序，**前一条命中就不看后面**：

1. `automation_id`（最稳；本仓库目前只有公共文件对话框和少数按钮有）
2. `name` + `control_type` + `scope`（窗口/面板祖先）
3. `name_contains` + `control_type` + `scope`（动态名称、拼接名称）
4. `class_name` + `index`（如汉堡菜单：`Menu` 的第一个 `MenuItem`）
5. `fallback_coord`（窗口相对坐标，**只作兜底**；必须记录 DPI 与窗口尺寸）

**EasyDialog 的按钮文案是「确 定」/「取 消」**（中间有空格），不是「确定/取消」。`EasyDialog` 用 `StackPanel`（图标 + `TextBlock`）拼按钮，UIA Name 会带上这个空格；选择器统一写 `name_contains`，不要写全等。受影响的弹窗：创建新解决方案、添加变量、创建新通信、流程重命名。

**禁止**：用截图模板匹配、OCR 文本、图标字形作为主选择器。图标按钮的 UIA Name 是 FontAwesome 私有区字符，换图标字体就全废。

### 5.6 等待与断言

| 断言 | 语义 | 例子 |
|---|---|---|
| `process_running` | 进程存在 | `VisionMaster` |
| `window` / `window_closed` | 窗口出现/关闭 | `dialog.save_solution` |
| `element_exists` / `element_absent` | 元素在/不在 UIA 树 | 运行条在组态模式下不存在 |
| `element_enabled` | 可交互 | 菜单项未置灰 |
| `element_value` | 值等于 | 文件名框 = 路径 |
| `text_exists` / `text_absent` | 任意可见文本包含/不包含 | 「保存成功」「未启动」 |
| `toast_contains` | 右下角非模态提示包含 | 「已启动」 |
| `file_exists` | 文件存在 | `.vms` |
| `file_mtime_after` | mtime ≥ 基准时间 | 保存布局 |
| `param_equals` | 参数等于 | 执行前校验 |

**规则**：

- 每个动作步尽量有 `expect`；没有可观察结果的步（`key`、`note`、`screenshot`）可以没有。
- 默认超时：UI 元素 3s、弹窗 8s、文件保存 20s、启动 30s。
- 等待用「条件轮询」，不要固定 `sleep`；固定 sleep 是慢和脆的根源。
- 断言必须能**反证**：写「跳过：环境不支持」之前，先回答「谁证明是环境而不是代码有 bug」（R38）。

### 5.7 失败策略

`on_fail` 三种：

| 值 | 行为 | 适用 |
|---|---|---|
| `stop_and_report` | 停在失败步，截图 + dump UIA 子树 + 报步号/期望/实际 | 默认；所有写操作 |
| `retry_step` | 只重试当前步，最多 `retry` 次 | 瞬时失败（窗口动画、列表懒加载） |
| `continue` | 记录警告继续 | 只用于「非关键断言」，如运行可能瞬间结束 |

**禁止**：对删除、保存、覆盖、运动、写变量这类动作自动重试；失败就停，交给人。

### 5.8 参数与密钥

- 计划参数用 `${snake_case}`；执行器在执行前统一替换。
- 内置变量：`${run_started_at}`（本次开始时间，ISO 8601）、`${run_id}`、`${step_index}`。
- **计划文件里不许出现密码/令牌**；登录类任务把账号密码作为运行时参数传入，执行日志脱敏。
- 路径参数必须是完整路径；不要依赖文件对话框「上次打开的目录」。

### 5.9 版本与校准

- `plan.version`：任务语义变化才升；只改选择器不升计划版本。
- `ui-map.calibrated_at`：每次校准写日期；UI 改版后必须重新校准。
- 校准命令见 §10。
- 计划文件与 `ui-map.json` 一起提交；只改一边会导致校验失败或执行错位。

### 5.10 反模式（不要做）

| 反模式 | 后果 | 正确做法 |
|---|---|---|
| 计划里写坐标 | 换分辨率/DPI 全废 | 写 ui-map ID，坐标只放 ui-map 兜底 |
| 用图标字形当 Name | 换图标字体全废 | 用 tooltip/相邻文本/scope + index |
| 假设 `Ctrl+S` 能保存 | 实际没有绑定 | 走文件菜单 |
| 保存后不检查文件 | 对话框取消也算「点了」 | `file_exists` + toast |
| 一步里塞多个业务动作 | 失败无法定位 | 一个用户可见结果一个 plan，子步骤用 `call` |
| 「适当地配置一下」 | 执行器无法判定 | 字段名 + 值 + 断言 |
| 失败后让 AI 即兴改步骤 | 不可复现 | 停下、报告、改计划、重跑 |

---

## 6. 把零散操作改造成「计划任务」

### 6.1 六步法

```text
① 录一遍  →  ② 切片  →  ③ 归一化  →  ④ 参数化  →  ⑤ 固化选择器  →  ⑥ 验收
```

| 步 | 做什么 | 产出 |
|---|---|---|
| ① 录一遍 | 人工或 AI 用 computer-use 做一次，同时开 UIA dump | 原始动作清单 + UIA 树 |
| ② 切片 | 按「用户可见结果」切，不按「点了几个按钮」切 | 任务边界 |
| ③ 归一化 | 每个动作映射到原语 + target + expect | 步骤表 |
| ④ 参数化 | 把名称/路径/值抽成 `${param}` | params 声明 |
| ⑤ 固化选择器 | 把 target 写进 ui-map，标 confidence | ui-map 条目 |
| ⑥ 验收 | 校验器 + 干跑 + 失败注入 + 真机跑 | 可发布计划 |

### 6.2 示例：用户说的「方案保存」

**原始需求**：AI 只需说「方案保存」，就完成「先新建、再保存」，中途不再思考。

**归一化**：

```text
新建方案(Demo_20261023)
  → 文件 → 新建方案 → 填名称 → 确定
保存方案(D:\C#\VM\解决方案\Demo_20261023.vms)
  → 文件 → 保存方案 → 填完整路径 → 保存 → 断言 toast + 文件存在
```

**落成计划**（`plans/solution-create-and-save.json`，节选）：

```json
{
  "schema": "vm-plan/1",
  "id": "solution-create-and-save",
  "title": "新建方案并保存到指定路径（组合任务）",
  "params": {
    "solution_name": { "type": "string", "required": true },
    "save_path": { "type": "string", "required": true }
  },
  "steps": [
    { "n": 1, "do": "call", "plan": "solution-new",
      "args": { "solution_name": "${solution_name}" } },
    { "n": 2, "do": "call", "plan": "solution-save",
      "args": { "save_path": "${save_path}" } },
    { "n": 3, "do": "assert", "assert": "file_exists", "path": "${save_path}" }
  ],
  "on_fail": "stop_and_report"
}
```

**执行**：

1. 用户/任务板给出 `solution_name=Demo_20261023`、`save_path=D:\C#\VM\解决方案\Demo_20261023.vms`。
2. 执行器读 `solution-create-and-save.json`，展开两个子计划。
3. 机械执行 10 个原语，每步断言。
4. 失败就停在具体步号；成功就报「文件已生成」。

**关键**：执行期没有任何「接下来该点哪」的推理；所有选择在计划里已经定死。

### 6.3 组合任务

组合用 `call`，不要在父计划里复制子计划的步骤：

- 子计划只关心自己的参数与结果；
- 父计划只关心「先 A 后 B」和全局验收；
- 子计划改了，父计划自动跟着变。

本仓库已有组合示例：`solution-create-and-save` 调用 `solution-new` + `solution-save`。

### 6.4 「一次思考」的提示词模板

给 AI 的规划提示词（不是执行提示词）：

```text
你是 VisionMaster 的计划编译器。只输出一个 vm-plan/1 JSON，不要解释。

输入：
- 任务目标：<一句话>
- 参数：<name/type/example>
- 可用选择器：docs/AI操作计划/ui-map.json
- 计划格式：docs/AI操作计划/plan.schema.json
- 参考计划：docs/AI操作计划/plans/solution-save.json

硬约束：
1. 只能使用 ui-map 里已有的 target；缺选择器就停止规划，单独报告 "missing_selector": "<描述>"，不要输出计划、不要猜坐标。
2. 每个有可观察结果的动作步必须有 expect；expect 必须是可观察的窗口/元素/文本/文件/值（key/note/screenshot 可以没有）。
3. 一个步骤只做一件事；不要写「配置好参数」这种无法判定的步骤。
4. 不允许把密码/令牌写进计划。
5. 高风险动作（删除、覆盖、运动、写变量）标 risk=high，并在步骤里加确认。
6. 输出前自检：n 连续、id=文件名、所有 ${var} 都在 params 里声明。
```

执行提示词（另一条，不要混用）：

```text
读取 plan 文件，按顺序执行 steps。
每一步：解析 target → 执行 do → 等待 expect → 记录结果。
expect 不成立立即停止，截图并报告：步号、期望、实际、当前窗口标题。
不要修改计划，不要即兴补步骤。
```

---

## 7. 「点一下任务」的三种落地方式

| 方式 | 做法 | 开发量 | 速度 | 稳定性 | 适用 |
|---|---|---|---|---|---|
| L0 任务清单 + 命令 | `TASKS.md` 列计划；用户说「执行 solution-save，路径=…」，AI 读计划并用 computer-use 执行 | 0 | 中（仍有 agent 开销） | 中 | 现在就能用；低频任务 |
| L1 任务板 | 一个小 WPF/WinForms 窗口，列出 plans，双击弹参数，点「运行」 | 1~2 天 | 快 | 中高 | 现场操作员；需要图形入口 |
| L2 UIA 执行器 | 独立 PowerShell/C# 程序，读 JSON 直接驱动 UIA，不经过 LLM | 2~3 天 | 最快 | 高（选择器稳的话） | 高频重复任务；无人值守 |

**L2 执行器的最小骨架**（伪代码，P2 实现）：

```text
plan = load(plan_id); args = merge(defaults, cli_args)
check(plan.preconditions)
for step in plan.steps:
    if step.skip_if and holds(step.skip_if): continue
    target = resolve(step.target, step.target_name)   # ui-map 选择器
    execute(step.do, target, step.args)               # 原语
    if not wait_until(step.expect, step.timeout_ms):
        screenshot("fail-step-" + step.n)
        dump_uia_subtree(target)
        exit(step.n)                                  # 不继续、不猜
check(plan.postconditions)
exit(0)
```

**推荐路线**：

1. **先 L0**：用本文件的 24 个计划 + 现有 `tools/vm-uia.ps1` 的思路，把「保存/打开/新建流程/加算子/跑一次」跑通。
2. **再 L2**：给高频 5 个任务写执行器；同时给 top 20 控件补 `AutomationId`。
3. **最后 L1**：任务板只是 L2 的壳，不要先做壳。

**「点一下」的最小实现**：一个 `.cmd`/快捷方式，调用

```bat
powershell -NoProfile -ExecutionPolicy Bypass -File tools\run-plan.ps1 -Plan solution-save -Arg save_path="D:\...\Demo.vms"
```

`run-plan.ps1` 就是 L2 执行器（**尚未实现**，P2 交付）；没有它之前，L0 的「点一下」是「在对话里说一句任务 ID + 参数」。

---

## 8. 落地路线

| 阶段 | 内容 | 产出 | 验收 |
|---|---|---|---|
| P0 校准（0.5 天） | 启动 VisionMaster，跑 `dump-vm-uia.ps1 -Depth 8`，把 top 30 控件的 Name/Class/坐标写回 ui-map | `ui-map.calibrated_at` 有日期 | 汉堡菜单、保存对话框、流程胶囊、画布节点、属性面板都能被唯一定位 |
| P1 计划库（本次已交付） | 24 个计划 + schema + 校验器 | `plans/*.json` | `validate-plans.ps1` 输出 OK |
| P2 执行器（2~3 天） | `run-plan.ps1`：原语、等待、断言、失败报告、截图 | 可命令行执行计划 | 5 个高频任务真机跑通，0 次 LLM 调用 |
| P3 任务板（1~2 天） | 列表 + 参数表单 + 运行/停止 + 日志 | 可双击运行的桌面入口 | 操作员不看文档也能跑「保存方案」 |
| P4 治理（持续） | 补 AutomationId、计划评审、失败复盘、版本升级 | 计划覆盖率与成功率指标 | 新增 UI 必须同步 ui-map；新增计划必须过校验器 |

**指标**（建议从 P2 开始记）：

- 单任务模型调用次数：目标 1 次规划 + 0 次执行。
- 单任务耗时：目标 ≤ 手工操作的 1.5 倍（含等待）。
- 首次成功率、失败步分布、选择器失配次数。
- 计划覆盖率：高频操作里有多少已有计划。

---

## 9. 风险、边界与已知坑

| 编号 | 风险/边界 | 影响 | 处置 |
|---|---|---|---|
| K-01 | 绝大多数控件**没有 `AutomationId`**；相机/配方内联表单的输入框连 `AutomationProperties.Name` 都没有 | 只能按 Name/Class/Index 定位，图标按钮 Name 是字形 | P0 给 top 30 控件补 `AutomationProperties.AutomationId`；在此之前相机/配方用 `Tab` + `type_focused` 过渡 |
| K-02 | 本文件的选择器**未在真机校准** | 第一次执行可能找不到控件 | 先跑 §10 的 dump；按 confidence 从高到低用 |
| K-03 | DPI/分辨率变化 | 兜底坐标失效 | 坐标存窗口相对值；记录 DPI；换屏重校准 |
| K-04 | 保存对话框**每次都弹** | 「点保存」不会静默完成 | 计划必须填完整路径 + 断言文件 |
| K-05 | `InputGestureText="S"` 不是快捷键 | 按 Ctrl+S 没反应 | 走文件菜单，不要发明快捷键 |
| K-06 | 运行中方案命令被禁用 | 新建/打开/保存/切换方案失败 | 计划前置断言 + 先 `run-stop` |
| K-07 | 硬件管理要求先打开方案 | 无方案时直接提示，不弹窗 | 前置 `text_absent "未加载方案"` |
| K-08 | 新建方案只在内存 | 以为已保存，实际没有 | 组合计划必须接 `solution-save` |
| K-09 | 组态运行窗口没有关闭按钮/Esc | 退不出去 | 只走「■ 停止运行」 |
| K-10 | 「截屏」菜单项没有 Command | 点了没反应 | **不要**把它写进计划；需要截图用执行器自己的 `screenshot` |
| K-11 | 帮助窗口标题不确定 | 按标题找窗口会失败 | 按占位文字/正文锚定 |
| K-12 | Nodify 画布/ScadaCanvas 是自定义控件 | UIA 子树可能不暴露节点 | 优先点节点标题文本；不行再用画布相对坐标 |
| K-13 | gong 拖放不能用 `Invoke` | 拖影残留/放置不触发 | 执行器必须模拟真实鼠标按下-移动-释放；放置端必须是 gong 的 DropHandler |
| K-14 | 文件对话框按钮文案本地化 | `name="保存"` 在英文系统不匹配 | 用 `AutomationId`（1/2/1001/1148）+ `name_contains` 双保险 |
| K-15 | 高风险动作（删除/覆盖/运动/写变量） | 误操作不可逆 | `risk=high` + 执行前二次确认 + 不自动重试 |
| K-16 | 密码/令牌进计划 | 泄露 | 运行时参数传入，日志脱敏 |
| K-17 | 计划与 UI 脱节 | 静默点错 | ui-map 版本化；UI 改版必须重校准；校验器只查结构，真机断言才查行为 |

---

## 10. 选择器校准（第一次必做）

前置：VisionMaster 已启动，主窗口可见。UIA 查询由应用进程内的自动化提供者回答，**不依赖屏幕渲染**，锁屏/远程桌面下也能用。

```powershell
# 1) 看整棵 UIA 树（窗口 + 控件 + 矩形）
powershell -NoProfile -ExecutionPolicy Bypass -File tools\dump-vm-uia.ps1 -Depth 8

# 2) 只看文本（找 toast / 状态文字 / 方案名）
powershell -NoProfile -ExecutionPolicy Bypass -File tools\vm-uia.ps1 -Action texts

# 3) 列按钮（确认精确 Name）
powershell -NoProfile -ExecutionPolicy Bypass -File tools\vm-uia.ps1 -Action list

# 4) 按 Name 调一个按钮（验证选择器）
powershell -NoProfile -ExecutionPolicy Bypass -File tools\vm-uia.ps1 -Action invoke -Name "运行画面"
```

校准步骤：

1. 打开目标界面（主窗口、保存对话框、变量管理、硬件管理等）。
2. dump 一次，把目标控件的 `ControlType | Name | ClassName | Rect` 抄进 `ui-map.json`。
3. 把 `confidence` 从 `low/medium` 改成 `high`；把 `fallback_coord` 改成窗口相对坐标。
4. 填 `calibrated_at` 日期。
5. 跑 `validate-plans.ps1`；再挑 3 个计划干跑。

**校准记录建议**：`artifacts\logs\<yyyyMMdd>\uia-dump-<界面>.txt`，不要落仓库根目录（R166）。

---

## 11. 验收清单

- [ ] `validate-plans.ps1` 输出 `OK`，0 errors。
- [ ] `ui-map.json` 的 `calibrated_at` 非空，top 30 控件 confidence = high。
- [ ] 在干净启动的 VisionMaster 上跑通 3 个计划：`solution-new`、`solution-save`、`solution-open`。
- [ ] 跑通 1 个组合计划：`solution-create-and-save`。
- [ ] 跑通 1 个流程类计划：`flow-create` + `step-add`。
- [ ] 跑通 1 个运行类计划：`run-once` + `run-stop`。
- [ ] 失败注入：把 `save_path` 改成非法路径，计划必须停在保存步并给出截图/步号，而不是继续。
- [ ] 执行期模型调用次数 = 0（用执行器日志证明）。
- [ ] 计划文件里没有密码/令牌/硬编码用户目录。
- [ ] `risk=high` 的计划有二次确认，且没有被自动重试。
- [ ] UI 改版后重新校准过，旧计划仍能跑或明确报「选择器失配」。

---

## 附录 A：计划索引

| 计划 | 参数 | 风险 | 依赖 |
|---|---|---|---|
| `solution-new` | `solution_name` | low | 主窗口 |
| `solution-save` | `save_path` | low | 已加载方案 |
| `solution-open` | `solution_path` | medium | 无流程在跑 |
| `solution-list-open` | `solution_name` | medium | 清单里有该方案 |
| `solution-create-and-save` | `solution_name`, `save_path` | low | 组合 |
| `flow-create` | `anchor_flow` | low | 有流程胶囊 |
| `flow-rename` | `old_name`, `new_name` | low | 有该流程 |
| `step-add` | `tool_name`, `anchor_step` | medium | 流程画布 + 工具箱 |
| `step-configure` | `step_name`, `fields` | medium | ActionStep |
| `step-configure-condition` | `step_name`, `expression`, `poll_interval_ms`, `max_timeout_ms` | medium | 条件/等待步骤 |
| `step-bind-variable` | `step_name`, `port_name`, `source_kind`, `source_name` | medium | 有可绑定来源 |
| `run-once` | — | medium | 有 Main 流程 |
| `run-loop` | — | medium | 有 Main 流程 |
| `run-stop` | — | low | 有流程在跑 |
| `variable-add-local` | `variable_name`, `data_type`, `initial_value`, `description` | low | 已加载方案 |
| `comm-add-device` | `connection_name`, `protocol`, `ip`, `port` | medium | 已加载方案 |
| `camera-add` | `driver`, `serial`, `display_name` | medium | 已加载方案 + 驱动 |
| `recipe-capture` | `recipe_name`, `recipe_code`, `notes` | medium | 已确认参数 |
| `scada-page-new` | `expected_page_name` | low | 组态画面 |
| `scada-add-element` | `element_name` | medium | 图元工具箱 |
| `scada-run` | `page_name` | medium | 有启动页 |
| `layout-save` | `app_dir` | low | 主窗口 |
| `mode-switch` | `mode_key`, `expect_text` | low | 主窗口 |
| `help-search` | `query` | low | 帮助目录已构建 |

---

## 附录 B：扩展入口（未单独落计划）

这些入口高频但参数/权限差异大，先列精确入口；需要时按 §6 六步法补计划。

| 功能 | 入口（精确） | 关键输入 | 判据 / 备注 |
|---|---|---|---|
| 用户登录 | ☰ → 系统(S) → 用户登录 | 账号、密码 | 状态栏当前用户变化；**密码只走运行时参数，不落计划** |
| 用户管理 | 用户登录弹窗 → 用户管理 | 用户名、角色、密码 | 用户列表更新；需管理员权限 |
| 报警配置 | 组态画面 → 顶栏「…」→ 报警配置… | 名称、监视变量、种类、阈值、等级 | 报警表出现该行；运行中禁用 |
| 变量事件 | 流程胶囊右键 → 变量事件（方案级） | 变量、规则（更改数值/为真/为假/上限/下限） | 事件行出现；改的是方案内容 |
| 系统健康 | 硬件管理 → 左侧「系统健康」 | — | 健康面板出现；1 秒自动刷新 |
| 回零 | ☰ → 系统(S) → 回零 | — | **只深链到运动板卡手动调试页，不会自动回零**；真正回零需在调试页操作 |
| 画布分屏 | ☰ → 画布(C) → 单画面/双画面/主从/四宫格/… | 布局号 | 画布格数变化；每格对应一个显示窗口号 |
| 恢复默认布局 | ☰ → 工具(T) → 恢复默认布局 → 确认框「确定」 | — | 破坏性：丢弃当前面板位置/大小/显隐；默认按钮是「取消」 |

---

## 附录 C：交付物清单

| 文件 | 作用 |
|---|---|
| `README.md` | 本文件：功能清单、步骤库、组织方式、改造方法、风险 |
| `TASKS.md` | 任务清单：说法 → 计划 ID → 参数，现场直接用 |
| `plan.schema.json` | `vm-plan/1` 的 JSON Schema |
| `ui-map.json` | 18 个窗口 + 134 个控件的选择器表 |
| `plans/*.json` | 24 个可执行计划（含 1 个组合计划） |
| `tools/validate-plans.ps1` | 结构校验器（ASCII / PS 5.1） |

校验命令：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs\AI操作计划\tools\validate-plans.ps1
```

期望输出：

```text
plans: 24  windows: 18  elements: 134
OK: all plans are structurally valid
```

---

## 附录 D：事实来源（维护时先看这些）

| 事实 | 来源 |
|---|---|
| 顶栏菜单/按钮/快捷键 | `VisionMaster/Shell.xaml`、`VisionMaster/Shell.xaml.cs` |
| 新建/打开/保存/方案列表 | `VisionMaster/ShellViewModel.cs`（`ExecuteProjectAction` / `SaveSolutionAsync` / `OpenSolutionAsync`） |
| 运行状态与按钮互锁 | `VisionMaster/ShellViewModel.cs`（`RunStateText` / `CanExecuteExecution`） |
| 命令面板内容 | `VisionMaster/ShellViewModel.cs`（`BuildCommandPalette`） |
| 步骤参数分派 | `VisionMaster/Services/StepParameterDialog.cs` |
| 添加变量字段 | `VisionMaster/ViewModels/DialogViewModels/VariableDraft.cs` |
| 通讯字段 | `Core/CommunicationContracts/CommunicationConfig.cs`、`Config/ModbusTcpConfig.cs` |
| 相机添加表单 | `VisionMaster/Views/DialogViews/CameraSettingsView.xaml` |
| 配方捕获/应用 | `VisionMaster/Views/DialogViews/RecipeManagerView.xaml`、`ViewModels/DialogViewModels/RecipeManagerViewModel.cs` |
| 组态画面/图元 | `VisionMaster/Views/ScadaEditorView.xaml`、`ScadaToolboxView.xaml`、`ScadaPropertyView.xaml` |
| 布局文件 | `VisionMaster/Services/LayoutHelper.cs` |
| 系统设置 | `VisionMaster/ViewModels/DialogViewModels/ScadaSystemSettingsViewModel.cs`、`Models/SystemSettingsDraft.cs` |
| 帮助 | `VisionMaster/Views/DialogViews/HelpView.xaml`、`ViewModels/DialogViewModels/HelpViewModel.cs` |
| 现有 UIA 工具 | `tools/dump-vm-uia.ps1`、`tools/vm-uia.ps1` |
