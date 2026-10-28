# 插件算子对齐 HALCON（预处理 / 图像差分 / 几何计算 / OCR）

- 日期：2026-10-27
- 触发：用户「全部修复 对齐Halcon」——把并行交叉验证确认的算子口径差异全部落地
- 范围：`Plugins\Plugin.PreProcessing`、`Plugins\Plugin.ImageDiff`、`Plugins\Plugin.GeometryRelation`、`Plugins\Plugin.Ocr`、`Shard\Core.Interfaces\Geometry\GeometryParam.cs`、`FlowCanvasChecks`
- 结论：修复已构建并验证。`tools\run_gates.ps1` **14/14 全绿**（`FlowCanvasChecks` 3962/0、`ScadaChecks` 3138/0、`CommChecks` 336/0、`RecipeChecks` 138/0、`ScadaWebChecks` 193/0、`DataPipelineChecks` 97/0、`DatabaseChecks` 134/0；`UIThemeSmokeTest`/`HelpProbe` exit 0；三项 audit + docs_index + sync_constraints 全 OK）。

## 一、当前状态

- 四个插件的差异修复全部落盘、构建通过（`-m:1 -nodeReuse:false`，0 错误）。
- 收尾入口 `tools\run_gates.ps1` 已跑：**14 steps green，exit 0**。
- `Modules\` 内无契约程序集，`audit_artifacts` 核对 40 个插件工程全部「DLL 新于源码 + Help 页数一致」。
- 真机复跑：4/5 条流程在 GUI 里跑通（NCC 11、PCB 121、Code128 3、QR 9，均 0 错误）；Blob 用生产运行时复跑覆盖（10/10）。新增 `--sample-flows` 模式，37 条断言全绿。

## 二、已做

### 1. Plugin.PreProcessing（算子口径对齐 HALCON 23.05）

| 修复 | 判据 / 依据 |
| --- | --- |
| Sobel 滤波类型枚举重写 | HALCON `sobel_amp` 只有 `sum_abs / sum_sqrt / thin_sum_abs / thin_max_abs / x / y`，没有 `sum`。枚举改为 `SumAbs=0 / SumSqrt=1 / ThinSumAbs=2 / ThinMaxAbs=3 / X=4 / Y=5`，`PreprocessHService` 六个分支逐一映射 |
| 中值滤波删除「菱形」 | `median_image` 只接受 `square / circle`，菱形会抛 #1301。枚举删除 `Diamond`（历史存盘值 2 由 `Normalize` 收敛到 `Circle`，旧方案仍能打开）；半径夹 1..50 |
| 高斯模板尺寸夹 3..11 | `gauss_image` 只接受 3/5/7/9/11；`Normalize` 先抬到奇数再夹到 3..11 |
| `var_threshold` 不再按 0~255 夹 | uint2/real 的局部差值可超 255；`AbsThreshold` 取消上下限，掩膜宽高夹 3..999 |
| 矩形模板不再按图像尺寸静默缩小 | `mean_image` / 形态学模板过大时 HALCON 抛 #3033/#1302，应由插件的 `FailedOperators` / `FailOnOperatorError` 如实上抛；静默夹取会改变用户填的参数却不说 |
| 亮度/对比度/光照参数夹取 | 倍率 0..100、加数 −255..255；`illuminate` 的 factor 允许 [0,1] 闭区间 |
| 灰度归一化输出 byte | 原实现输出 real，下游按 byte 处理会类型不符；改为 `convert_image_type` 回 byte，尺寸不变 |
| `ToGray` 支持 4 通道 | 4 通道取 1..3 通道 `rgb3_to_gray`（忽略 alpha） |
| Box 参数取整 | 中心/宽高改 `MidpointRounding.AwayFromZero`，避免银行家舍入造成 1px 偏移 |
| 新增 `Clamp(int,int,int)` 重载 | 整型参数不再走 double 重载（CS0266） |

### 2. Plugin.ImageDiff（有符号差与上屏）

| 修复 | 判据 / 依据 |
| --- | --- |
| Signed 不再 2× | 旧实现 `sub_image(pos, neg)` 会把正差算成 2×、负差抵消；改为直接取 `pos`（Brighter）/`neg`（Darker）/带符号差（Both）再加零点 |
| `Signed + Darker` 带负号 | Help 的算式是 `pos − neg + 零点`；Darker 必须落在零点下方（变暗 10 → 118），否则画面看起来像变亮。断言 A5d 钉住 |
| Signed 的分割/面积判定用绝对差 | 带零点偏置的显示图不能直接阈值（背景 128 会被整片选中）；判定单独算一张 `Absolute/Both` 的差图 |
| `ToGray` 支持 4 通道 | 与预处理同口径 |
| 空区域计数 | `select_shape` 无条件执行（`minArea` 夹到 ≥1）；否则空区域经 `connection` 后 `CountObj` 返回 1，`DiffCount` 假报 1 |
| Overlay 差异着色 | 蓝通道 `max_image(chR, mask)`：底图保留、差异区变蓝，不再整幅替换 |
| `MaskRegion` 文案 | 明确「只算框内（差异区与它求交）」，不是「排除框内」 |

### 3. Plugin.GeometryRelation（残差与退化保护）

| 修复 | 判据 / 依据 |
| --- | --- |
| 残差点集类型判据 | 用 `GetObjClass()` 前缀判 `xld`，不再 `source is HXLD`（`HObject` 永远不会是 `HXLD`）；多对象 XLD/区域走 `CountObj + SelectObj` 逐对象取点，修 #1501 |
| 残差不可算只 Warn | 残差是可选诊断输入，读不出来不能把已算好的主结果判失败 |
| `NormalizePhi` 加固 | 非有限值原样返回；负角折返后再判一次（`-1e-15 + 180 == 180`）；`-0.0` 归成正零 |
| 线线求交守卫 1e-9 → 1e-15 | 只有方向完全共线才失败；近平行按 HALCON 返回远交点（失败文案同步为「两线平行（方向共线），没有唯一交点」） |
| 圆-圆相离/内含容差 1e-9 → 1e-12 | 数值级相切不误判成相离/内含 |
| 椭圆残差改最短欧氏距 | 旧「归一化残差 × 短半轴」在长轴方向按扁率低估；改为法向 Newton（24 次）+ 16 点兜底，口径同 HALCON `distance_pc`。断言：轴外点 (5,12) = 4.1177、长轴外溢点 (0,12) = 2 |

### 4. Plugin.Ocr

- 仅修注释：带拒识的分类器对认不出的区域返回 **U+001A（SUB 控制符）**，不是空格；`IsRejected` 的「无字母无数字」判据不变，行为零变更。

### 5. 断言与帮助

- `FlowCanvasChecks\ImageDiffCheck.cs`：新增 A5a~A5d（Signed 零点偏置 / 极性 / 非 2×），反向判据写明「旧实现会给 148 / 128」。
- `FlowCanvasChecks\GeometryChecks.cs`：椭圆残差期望改为精确解（4.1177 / 2），注释说明旧近似 2.81 / 1 是低估。
- `FlowCanvasChecks\PreProcessingChecks.cs`：`SobelFilterType.SumSqrtAbs`（不存在）改为 `SumSqrt`；存盘文案同步。
- Help 同步：
  - PreProcessing `04-算子口径与调参.md`：Sobel 数学口径不再写「Gx+Gy」。
  - ImageDiff `04-criteria-and-tuning.md`：Signed + Darker 落在中灰以下；`12-dev-invariants.md`：新增 A3b，断言表改成「已存在 ImageDiffCheck」。
  - GeometryRelation `03-ports-and-parameters.md`：补 `ResidualPoints` 与三个残差输出端口；`04/05/10/12`：阈值 1e-15、圆容差 1e-12、椭圆精确残差、失败文案。

### 6. 真机复跑与生产运行时复跑（2026-10-27 收尾）

**真机 `VisionMaster.exe`**（启动后自动加载 `视觉样例对拍.vms`，逐条运行）：

| 流程 | 加载次数 | 结果条数 | 错误 |
| --- | --- | --- | --- |
| 模板匹配_NCC散焦图 | 11/11（11 个不同文件） | 11 条「匹配 1 个实例」 | 0 |
| 形状匹配_散焦PCB | 121/121（121 个不同文件） | 121 条「匹配 1 个实例」 | 0 |
| 码读取_Code128 | 3/3（3 个不同文件） | 3 条「读到 1 个码」 | 0 |
| 二维码读取_QR | 9/9（9 个不同文件） | 9 条「读到 1 个码」 | 0 |
| Blob缺陷检测_标签字符 | GUI 未跑（桌面被 ZCode 占用，合成点击送不进应用） | — | — |

**生产运行时复跑**（新增 `FlowCanvasChecks --sample-flows`）：用 `SolutionService` 打开同一份 `.vms`，`FlowCompiler` 编译后 `CompiledFlow.Run` 整跑 5 条流程（For + 文件夹采集 + 真插件 + 结果显示），断言每圈换图/出结果/发布显示帧：

- **37 条断言全通过，0 失败**；Blob 10/10、NCC 11/11、PCB 121/121、Code128 3/3、QR 9/9。
- 每圈都发布了「结果显示」帧到窗口 1（11/121/10/3/9），证明图像窗口收到的是本轮结果。
- 日志：`artifacts\logs\20261027\sample-flows-replay2.log`。

**默认套件复跑（加 `--sample-flows` 之后）**：功能断言全绿，但两次连跑各有一处**计时类**断言抖动——
第一次是断点等待 5s 超时（`第 2 圈同一断点再次断开住`），第二次是托管计时器 300ms 内只跳 7 拍
（期望 ≈30）。同一份代码在 09:00 的 `run_gates.ps1` 里是 3962/0；两次红的条目互不相同，
且都发生在用户 `VisionMaster.exe` 与另一会话构建同时占用 CPU 的时段。判据：**计时类断言在负载下会抖，
不能当功能回归**；功能类（Replay / 预处理 / 差分 / 几何）本轮全绿。

## 三、未做

- **GUI 里 Blob 流程尚未点击运行**（生产运行时复跑已覆盖 10/10；真机 GUI 只差这一条）。
- 未做 `Core.Halcon` Debug+Release 双构建（本轮未改 `Services\Core.Halcon`）。
- OCR 只有注释变更，未新增行为断言（其行为本来已由 `OcrChecks` 覆盖）。
- 残差端口（`ResidualPoints` → `ResidualRms/Max/Count`）只有纯数学层断言，尚无「真 XLD 多对象」的端到端断言。

## 四、下一步

1. 桌面空闲时在 GUI 里点一次「Blob缺陷检测_标签字符」的「运行一次」，补上最后一条真机证据。
2. 若后续要接更多 HALCON 样例，沿用「先写断言宿主、再上真机」的顺序（R175）；整跑用 `FlowCanvasChecks --sample-flows`。
3. 可选补一条残差端到端断言：喂两条 XLD 轮廓 + 拟合直线，核对 `ResidualRms` 与手工点线距一致。

## 五、踩过的坑

1. **`Math.Clamp` 混合重载 → CS0266**：`Clamp(MaskWidth, 3, 999)` 的实参全是 int 却匹配到 `Clamp(double,double,double)`，结果赋回 int 编译失败。加了 `Clamp(int,int,int)` 重载一次解决四处。
2. **给 `RectMaskOperator` 加"按图像尺寸缩小"看着更稳，实际是错的**：`mean 999×999` 因此变成成功，一次打红 7 条失败契约断言。HALCON 的 #3033 应该由 `FailedOperators` 上抛——静默夹取会让 VM 与 HALCON 直接跑的结果不一致。
3. **HALCON `SubImage` 是 5 参**（`mult`/`add` 必填），4 参写法编译报 CS7036，别按文档记忆抄。
4. **`Signed + Darker` 把 `neg` 当正幅度加零点 → 变暗区显示在中灰以上**，与 Help 的 `pos − neg + 零点` 相反。新增的 A5d 断言直接抓到（亮=128 暗=138，期望 118）。
5. **`HelpProbe` 日志按控制台代码页（936）写**，用 UTF-8 回读是乱码；回读要「严格 UTF-8 失败回落 936」（R183 同族）。本轮用 936 回读后确认「全部通过」。
6. **远程桌面/前台被占时，`SetCursorPos`+`mouse_event` 的合成点击送不进目标应用**：UIA 仍报 VisionMaster 是 foreground、元素 `IsOffscreen=False`，但右键菜单根本不弹；`SetForegroundWindow`/TOPMOST 也压不住正在使用的 ZCode/GameViewer。能用的只有 `InvokePattern`（无焦点也能点按钮）或进程内生产运行时复跑；**不要靠坐标点击做无人值守 GUI 测试**。
7. **`CompiledFlow.Run` 整跑完，进程不退出**：结果显示/图集的异步发布留下了非后台线程，`Main` 返回后宿主一直挂着（`cmd /c` 已返回 0，exe 仍锁着自己的 bin）。`--sample-flows` 模式必须 `Environment.Exit(Environment.ExitCode)` 显式退出，否则下次构建报 MSB3027「文件被 FlowCanvasChecks 锁定」。
