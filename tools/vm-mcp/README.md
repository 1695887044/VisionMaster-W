# vm-mcp — 上机测试用的 MCP 服务

把「AI 驱动 VisionMaster 做桌面端测试」这件事从**每轮手写 PowerShell** 变成**调用固定工具**。
每个工具都固化了 `docs/工程纪律/分册-AI操作与exe测试踩坑.md` 里一条踩过的坑，
所以它不是"又一个 UIA 封装"，而是那份台账的可执行版本。

- 语言/运行时：.NET 9（`net9.0-windows`），`UseWPF` 只用来拿 `System.Windows.Automation`、WIC 解码与 PNG 编码
- 依赖：**零 NuGet 包**（MCP 协议手写 JSON-RPC over stdio，截图走 GDI，936 解码走 Win32）
- 位置：`tools\vm-mcp\`，不在 `VisionMaster.sln` 里，不参与产品构建

## 1. 工具清单（23 个）

### 进程与构建

| 工具 | 干什么 | 固化的坑 |
|---|---|---|
| `vm_status` | 预检：VM 进程 / 断言宿主 / 真构建 / Roslyn 常驻服务 / 端口 / Layout 时间 / 最新日志 | A-01 A-02 A-04 F-01 |
| `vm_windows` | 列出进程的**全部**顶层窗口（标题/类/矩形/是否无标题）；"弹窗卡住"先看这里 | C-36 |
| `vm_start` | 起 VM 并等**外壳窗口**（有标题 + ≥640×480）；已有实例直接拒绝；超时列出全部窗口 | A-02 C-36 |
| `vm_stop` | 优雅关闭（先 `CloseMainWindow`，`force` 才杀），等到真的退出 | A-01 A-03 |
| `vm_backup_layout` | 跑真 exe 前备份 `Layout.xml` / `DefaultLayout.xml` | A-03 R176 |
| `vm_build` | `-m:1 -nodeReuse:false`，判据=退出码 0 **且** 0 错误 **且** 无失败横幅 | R179 B-01 B-02 |
| `vm_run_host` | 跑断言宿主：原始字节落盘 → UTF-8 严格解码 → 失败回落 936 → 数 `[PASS]/[FAIL]`；零输出判红；顺带报陈旧 exe | C-01 C-02 C-03 C-25 R65 R183 |
| `vm_gates` | 跑 `tools\run_gates.ps1` 并解析 GATE SUMMARY | R182 |

### 界面驱动

| 工具 | 干什么 | 固化的坑 |
|---|---|---|
| `vm_ui_tree` | UIA 树，**取面积最大的有标题窗口**，`scope=process` 覆盖弹窗/对话框；私有区字形转义成 `\uXXXX` | C-20 C-32 C-36 |
| `vm_ui_act` | 按 UIA 模式动作；可**按 ref 或按名字/id/类型**定位（按名字时自动挑支持该模式的那个元素）；缺模式时报出**它支持哪些** | C-21 C-22 C-38 |
| `vm_ui_click` | 按元素中心点击；**先查前台窗口归属** | C-07 C-26 |
| `vm_ui_type` | 文本输入（ValuePattern 优先，回落真实按键）+ 快捷键；**读回实际值** | R35 C-34 |
| `vm_ui_drag` | 真鼠标按下/移动/抬起（画布连线、图元摆放、滑块） | R171 |
| `vm_ui_scroll` | 滚轮 | — |
| `vm_ui_wait` | 等元素出现/消失，替代固定 sleep | — |
| `vm_assert` | 断言元素 exists/absent/count/text/enabled，或断言日志正则；**失败即 `isError`** | R65 C-33 |
| `vm_shot` | 截图到 `artifacts\render\<yyyyMMdd>\`，返回窗口矩形/图像尺寸/DPI；支持裁剪与整数放大 | C-07 C-30 |
| `vm_log_tail` | 宿主运行中读日志（`FileShare.ReadWrite`），带 pattern 时返回**最后** N 条匹配 | C-09 |

### 图像与夹具体检

| 工具 | 干什么 | 固化的坑 |
|---|---|---|
| `image_info` | 尺寸/格式/位深/每通道 min-max-mean-std/锐度 + 截顶与 16 位低满量程告警 | G-07 G-08 C-29 |
| `image_histogram` | 直方图与**占用区间**（判断夹具是否只用了 0..255） | G-07 G-08 |
| `image_diff` | 两张同尺寸图的差异像素数/最大差/包围盒，可出红标差异图 | R122 验收 |
| `image_crop` | 裁剪 + 整数放大，保留原位深 | C-07 |
| `dataset_scan` | 目录体检：逐文件一行 + 尺寸/格式一致性 + 均值离群 + **锐度峰谷（识别对焦扫描集）** | C-29 |

## 2. 构建与自测

```powershell
dotnet build tools\vm-mcp\VmMcp.csproj -m:1 -nodeReuse:false
tools\vm-mcp\bin\Debug\net9.0-windows\vm-mcp.exe --selftest      # 环境自检 + vm_status
node tools\vm-mcp\smoke.mjs       # MCP 协议（16 项）
node tools\vm-mcp\uismoke.mjs     # GUI 全链路（29 项，自带靶子窗口）
node tools\vm-mcp\imagesmoke.mjs  # 图像工具（9 项，用仓内真图）
```

`uismoke.mjs` 用 `--selftest-window` 起一个自带窗口（按钮 / 文本框 / 拖拽面板 / 滚动区 / 菜单），
覆盖：树遍历 + 模式发现 → `InvokePattern` → 按名字定位并选中"支持该模式的那个元素" → UIA 等待 →
窗口截图 → 真实点击 → 文本输入与读回 → 快捷键 → 拖拽（校验落点位移）→ 滚轮（校验滚动偏移）→
断言成功与**断言失败必须报错** → 布局容器不进 UIA 树的正反两条 → 弹出菜单（`scope=process`）→ 屏幕截图。
不依赖 VisionMaster，也不碰用户开着的程序。

手动戳单个工具（同一会话里 ref 才有效）：

```powershell
node tools\vm-mcp\drive.mjs vm_status
node tools\vm-mcp\drive.mjs vm_ui_tree "{\"maxRows\":40}"
node tools\vm-mcp\drive.mjs --seq tools\vm-mcp\examples\look.seq.json
```

## 3. 注册

ZCode 客户端（`%USERPROFILE%\.zcode\cli\config.json` → `mcp.servers`）：

```json
"vm-tools": {
  "type": "stdio",
  "command": "D:\\C#\\VM\\tools\\vm-mcp\\bin\\Debug\\net9.0-windows\\vm-mcp.exe",
  "args": ["--root", "D:\\C#\\VM"],
  "enabled": true,
  "timeoutMs": 300000
}
```

重跑 `node tools\oneoff\register-vm-mcp.mjs` 可幂等写入（自带备份 + JSON 校验）。
**改完配置要重开会话**，MCP 服务是会话启动时连的。

## 4. 已知边界（都是实测结论，不是推测）

1. **`mode=window` 拍不到弹出菜单 / 下拉 / Tooltip / 原生对话框** —— 它们是独立顶层窗口。
   要连弹窗一起看用 `mode=screen`，找元素用 `vm_ui_tree scope=process`。
2. **`mode=screen` 受前台影响**：目标被别的窗口盖住时，截到的是盖住它的那个。
3. **原生 Win32 对话框按钮是 `Pane`，没有 `InvokePattern`**（C-21）：
   用 `vm_ui_act action=close` 关窗当"取消"，或交回人工/computer-use。
4. **合成点击/拖拽/滚轮需要目标在前台**（C-26）：工具会先查前台 pid，不匹配就报错；
   只给坐标且不给 `pid` 时无法比较，此时不做前台检查（报告里会带前台 pid）。
5. **`ValuePattern.SetValue` 不产生任何输入事件**：凡是用 `PreviewTextInput` / "用户改过"判意图的控件（R35），
   必须用 `mode=keys`，否则界面看起来"没反应"。工具描述里也写了这一条。
6. **元素 ref 只在同一个服务进程内有效**：跨调用要写进 `--seq` 文件。
7. **UIA 需要窗口有矩形**：最小化时 UIA 报空矩形，`MainWindow` 会自动 `SW_RESTORE` 一次再试。
8. **没有 automation peer 的元素根本不在树里**（`Border`/`Canvas` 等）：自测靶子的拖拽面板用的是 `GroupBox`
   就是因为这个；写断言时别指望能拿到裸 `Border`（C-33）。
9. **图标字形按钮的 Name 是私有区码点**（U+E000–U+F8FF）：树里打印成 `\uF067`，按码点或旁边文字节点定位。
10. **`vm_build` 在 VisionMaster 运行时会拒绝**（A-01 会锁 bin）；确认只编不落宿主目录的工程才加
    `allowRunningHost=true`。
11. **`dataset_scan` 的锐度是拉普拉斯方差**：用于识别"对焦/曝光扫描集"，判据是峰值 / 中位数 > 3 倍，
    不是严格的单调性（真实传感器噪声会让单调判据失效）。
12. **启动时先出现一个无标题启动画面**（实测 520×360，t≈1.2s 出现、t≈2.8s 自己消失）。
    `vm_start` 因此以"有标题 + ≥640×480"判外壳窗口；"第一个窗口"或"面积最大的窗口"都会指错。
13. **带未保存改动退出后，下次启动会弹「上次运行的现场」草稿恢复框并阻塞界面**。
    这是产品行为（退出链自动转存草稿），不是缺陷；自动化流程里要先处理它：
    `vm_ui_act {"nameContains":"稍后再说","controlType":"Button","action":"invoke"}`。
14. **按名字过滤 UIA 树时，Button 旁边的 `Text` 会先命中**：`nameContains` 命中的第一个元素往往没有模式。
    别直接拿"第一个 ref"，用 `vm_ui_act` 的名字定位（它会挑支持该模式的元素），或看树里的 `ControlType`。

## 5. 排障

- 服务起不来 / 工具不出现：先 `vm-mcp.exe --selftest`，再 `--list-tools`；
  客户端日志里搜 `[vm-mcp]`（诊断只写 stderr，stdout 全是 JSON-RPC）。
- 报 `process N is not running`：目标进程已退出，`vm_status` 重新取 pid。
- 报 `no top-level window`：窗口在托盘/仍在启动；`vm_status` 看进程存活，必要时人工恢复窗口。
- 截图全黑：会话锁屏时用 `mode=window`；`mode=screen` 在锁屏下必然是黑的。
- 断言报 `verdict: FAIL`：证据在同一条回复里（匹配到的元素行或日志行），不要重试同一条件，
  先看是查询写错还是产品真变了。
