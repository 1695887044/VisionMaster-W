# 视觉样例对拍 — 运行说明

> 方案文件：`解决方案\视觉样例对拍\视觉样例对拍.vms`
> 最后更新：2026-10-28
> 生成器：`tools\vms-generator\Program.SamplePair.cs`（`--pair` 模式，可重复执行）

---

## 1. 这个方案是什么

把 **HALCON 官方例程** 的算法逻辑移植进 VisionMaster 的插件流程，做成一条完整的
**「图像采集 → 处理 → 结果判定 → 输出」** 生产检测链，并配一套 **5 页组态界面**。

七个工位，每个工位一条独立流程（可单独运行，也可被主流程串起来跑）：

| 工位流程 | 算法插件 | HALCON 例程出处 | 样例图 |
|---|---|---|---|
| 模板匹配_NCC散焦图 | 模板匹配（NCC） | `Matching/Correlation-Based/find_ncc_model_defocused.hdev` | `Image\smd\smd_on_chip_01..11.png` |
| 形状匹配_散焦PCB | 模板匹配（形状） | `Applications/Position-Recognition-2D/matching_defocused_pcb.hdev` | `Image\pcb_focus\*.png`（121 张） |
| Blob缺陷检测_标签字符 | Blob 缺陷检测 | `Applications/OCR/find_text_bottle_label.hdev` | `Image\label\label_01..10.png` |
| 码读取_Code128 | 码读取 | `Identification/Bar-Code/code128.hdev` | `Image\barcode_code128\*.png`（3 张） |
| 二维码读取_QR | 码读取 | `Applications/Data-Codes/qrcode_simple.hdev` | `Image\qrcode\qr_workpiece_01..09.png` |
| 形变匹配_局部非刚性 | 可形变模板匹配 | `Matching/Deformable`（局部非刚性） | `Image\checkerboard_400.png` |
| 形变匹配_平面透视 | 可形变模板匹配 | `Matching/Deformable/create_planar_uncalib_deformable_model_xld.hdev` | `Image\brake_disk_bike_01.png` |

> `checkerboard_400.png` 是合成棋盘格（HALCON 未随包提供该图），用于局部非刚性形变的经典验证场景。

**每轮节拍延时**：五条文件夹循环流程的循环体末尾各有一个「延时」步骤（200ms），
让每张图的标注结果都停留一下——否则连续跑图时视觉图像窗口只看到最后一张。

单轮额外耗时：NCC 11×200 + PCB 121×200 + 标签 10×200 + Code128 3×200 + QR 9×200 ≈ **+30.8 秒**
（PCB 占 24.2 秒）。要改延时：改对应流程里「节拍延时_XXX」步骤的 `Delay` 值。

---

## 2. 流程结构

### 2.0 两种运行模式：整线（生产）与整件（追溯）

2026-10-28 起流程管理有了「随整线启动」位（`InvokeType = 128`），七条工位流程的调用方式改成了
**`160 = 子程序(32) | 随整线启动(128)`**：

| 模式 | 怎么启动 | 行为 | 谁负责汇总 |
|---|---|---|---|
| **整线**（生产） | 宿主工具栏「启动整线 / 整线循环」；组态「自动运行」页「启动整线」 | 七个工位**各起各的会话并行跑**，一条失败/卡死只收尾它自己，其余照常循环 | 无：每个工位各自判定、各自上报 |
| **整件**（追溯/演示） | 组态「自动运行」页「执行一次整件」；流程栏选中 Main 运行 | `Main` 串行调七工位 → 统计 → 结果上报 → 写账本 | `Main` 的统计脚本 |

> 为什么工位不再挂在 Main 之下：把工位做成父流程的子调用（串行/并行组/派发汇合都一样）都会让
> 父流程必须"管"子流程——一有故障整线就停。整线要的是"平级"，所以工位保留 `子程序` 位（
> Main 仍能 `调用流程` 拉起它们）的同时勾上 `随整线启动`，两条路互不干扰。
> `Main` 自己的调用方式是「手动」（InvokeType=0），**不会**被「启动整线」带上——
> 否则七工位会被跑两遍（一次整线、一次 Main 的串行调用）。

### 2.1 七个工位流程（Role=0，InvokeType=160 子程序+随整线启动）

前五条是**文件夹循环**流程，结构是：

```
For「遍历样例图」(DefaultLoopCount = 样例图张数)
└── 循环体
    ├── 图像采集（Mode=Folder，FolderPath + FileIndex 接 For.Index）
    ├── 算法步骤（匹配 / Blob / 读码 / 形变匹配）
    ├── 结果显示（标注图 → 宿主视觉图像 1~7 号窗口）
    ├── 判定表「结果判定_XXX」   ← 追加
    ├── 变量赋值「写工位判定_XXX」 → 全局变量 XXX_Result   ← 追加
    ├── 变量赋值「写工位数值_XXX」 → 全局变量 XXX_Score/Count   ← 追加
    ├── 变量赋值「写工位说明_XXX」 → 全局变量 XXX_Reason   ← 追加
    ├── 图像赋值「写工位图像_XXX」 → 全局变量 XXX_Image    ← 追加
    ├── 结果上报「结果上报_XXX」  ← 追加（仅 NG 上报，URL 留空）
    └── 延时「节拍延时_XXX」200ms  ← 追加（永远在循环体最后一步）
```

两条形变匹配流程是单图流程，结构相同（无 For），追加的 5 步在顶层。

**判定规则**（阈值直接取算法步骤自己的配置，保证判定口径与算法配置同源）：

- 匹配 / 形变匹配：`分数 ≥ MinScore` 且 `匹配数 ≥ 1`
- Blob：`缺陷数 ≤ MaxDefectCount` 且 `最大面积 ≤ MaxSingleArea`
- 码读取：`码个数 ≥ 1`

**结果上报**（`Plugin.ResultUpload`，2026-10-28 新增算子）：

- 字段表：`station`（内置=步骤名）/ `result`（变量 `XXX_Result`）/ `score`（变量 `XXX_Score`）/
  `reason`（变量 `XXX_Reason`）/ `time`（内置时间戳）；`PayloadKind=FieldTable`，报文即字段表本身。
- 上报时机 **仅 NG**：`JudgeFieldName=result`、`NgValue=False`（`result` 是 bool，插件走 `TryAsBool` 分支）。
- `AsyncMode=true`（异步队列不占节拍）、`BlockOnFailure=false`（宁漏报不停线）、`SpoolEnabled=true`（断网续传）。
- **URL 留空**：命中上报时机的件只留一条 WARN「未配置目标 URL，跳过本次上报」，不打红流程。
  要真正上报：属性面板里填 `Url`（MES 接口或钉钉/企微 webhook），需要鉴权再加 `Headers`。
- **不接 `TriggerPort`**：接它要挂全局变量，而 `FlowCanvasChecks` 的样例流程复跑不做变量恢复，
  全局变量连线会被判「找不到全局变量」→ 整条流程编译失败。要改成"触发端口"时机时，
  得同时在 FlowCanvasChecks 里补变量恢复。

### 2.2 Main（整件串行，Role=2，InvokeType=0 手动）

```
调用工位_Ncc → 调用工位_Pcb → 调用工位_Label → 调用工位_Code128
→ 调用工位_Qr → 调用工位_DefLocal → 调用工位_DefPlanar
→ C#脚本「统计与良率」     （累计计数 + 良率 + 写 CSV 账本）
→ 结果上报「结果上报_整件」 （每次上报：总数/良品/不良/良率/时间）
→ CSV记录（已停用，保留配置）
```

C# 统计脚本做三件事：
1. 读七个 `XXX_Result` 全局变量，全部为 true 才判本件 OK；
2. 累加 `TotalCount` / `OkCount` / `NgCount`，算 `YieldRate`，写 `CurrentResult`；
3. **写生产账本** `History\Main.CSV记录\yyyy-MM\yyyy-MM-dd.csv`（11 列，与组态报表/历史表格列名逐字对齐）。

> 整线模式下**没有**整件汇总：七个工位各判各的、各报各的，`TotalCount`/`YieldRate` 只有跑过
> 「执行一次整件」才会更新。这是刻意的——整线里没有"一件"这个概念，七条会话的节拍互不相干。

### 2.3 Home / End

- **Home**：置 `SystemReady=true` → 预热跑一次 NCC 工位 → 置 `Home_Done=true`
- **End**：清 `SystemReady`

---

## 3. 组态界面（8 页 · 触摸屏式布局）

### 3.1 版面结构

```
┌────────────────────────────────────────────────────┐
│ 顶栏（每页各一份）                       40px      │
│ ●就绪 视觉样例对拍 │ 副题 │ ●本件 admin │ 时钟      │
├────────────────────────────────────────────────────┤
│                                                    │
│        本页内容层（卡片化）            928px        │
│                                                    │
├────────────────────────────────────────────────────┤
│ 底栏（每页各一份）                       112px     │
│ ⌂主页 ⟳自动运行 ✋手动控制 ⚙参数 ▤监控 ▮统计 ⚠报警 ☰日志 │
└────────────────────────────────────────────────────┘
```

> **为什么公共层是"每页各一份"而不是基座页**：
> `ScadaDocument.BasePages` 在加载期由 `EnsureIdentity` 按**引用**清理
> （`_pages.Contains(_basePages[i])`），而 .vms 序列化没有 `PreserveReferencesHandling`
> → 基座页在 `Pages` / `BasePages` 里反序列化成两个实例 → 加载后基座被当"幽灵条目"删掉（实测 `BasePages=0`）。
> `Hmi.SubPage` 也不行：内容能渲染，但占位层（"未找到画面"）与内容层同时可见（产品边角缺陷）。
> 所以公共层由生成器的 `PairChrome()` 在每页各生成一份——图元、事件、绑定全走"本页"通道，零依赖边角机制。

### 3.2 八个页面

| 页面 | 定位 | 核心内容 |
|---|---|---|
| **主页** | 宫格入口 | 主操作卡（自动运行，2px 主色边框 + 实心 CTA）+ 6 张入口卡 + 实时 KPI + 运行状态（带图例）+ 使用说明 |
| **自动运行** | 生产主界面 | 运行控制三键（**启动整线 / 停止整线 / 执行一次整件**）+ 七工位设备卡（**标注图缩略图** + 判定灯 + 视图窗口/指标 + 数值 + 单跑按钮）+ 良率趋势 + 实时报警 |
| **手动控制** | 单工位调试 | 七工位"单跑本工位"按钮 + 系统就绪置/清 + 调试提示 |
| **参数管理** | 参数与阈值 | 循环张数/像素当量（步进/复位）+ 判定阈值只读参考表 |
| **状态监控** | 趋势与仪表 | 良率趋势（双曲线）+ 良率仪表 + 生产计数板 + 实时报警 + 指标说明 |
| **结果统计** | 图表与报表 | 良品/不良柱图 + 判定占比环形图 + 良率仪表 + 生产报表 |
| **报警记录** | 报警中心 | 报警横幅（12 行）+ 全部确认 + 四个关键计数 + 报警规则说明 |
| **运行日志** | 账本 | 生产账本历史表格（范围筛选/分页）+ 关键计数 + 账本说明 |

> 「启动整线 / 停止整线」两个按钮背后**不是一个新动作类型**，而是一条钩子里挂七个
> 「启动流程 / 停止流程」动作（组态没有"启动整线"这个 `ScadaActionType`）。
> 语义与宿主工具栏「启动整线」一致：逐条 StartFlow，互不等待；某条失败只影响它自己。

### 3.3 工位图像显示链路

「自动运行」页每张工位卡左侧是**运行态标注图**（`Hmi.ImageView`）。链路是：

```
算法 → 结果显示（烧标注框+判定文字）→ AnnotatedImage 端口
     → 「图像赋值」步骤（Plugin.ImageAssign）
     → 全局图像变量 XXX_Image（HImage 类型）
     → 组态 Hmi.ImageView 绑该变量 → 画面刷图
```

- 变量：7 个 `XXX_Image`，`DataTypeString` 用 **HImage 的完整 AssemblyQualifiedName**
  （`HalconDotNet.HImage, halcondotnet, Version=23.5.0.0, ...`）。
  **不能用短名**：`TypeCache.LoadType` 对短名是"遍历已加载程序集"，
  而方案加载时 halcondotnet 可能还没加载 → 变量被静默跳过 → 运行期报"变量不存在"。
- 换算：HImage → WPF 位图由**宿主侧** `RegistryScadaValueSource` 完成（图元库不引用 Halcon）。
- 未跑过时显示"无图像"占位（变量初值 null），不会显示假图。
  **注意**：图像是运行态数据，重启软件后变量清空 → 必须**先跑一轮**（执行一次/启动连续）才会出图。

### 3.4 视觉与资产

**主题：浅色 Web 仪表盘**（近白底 + 白卡 + 细边框 + 深色文字；颜色只留给状态与主操作）

| 令牌 | 值 | 用途 |
|---|---|---|
| 页面底 | `#FFF7F8FA` | 画布背景（配 `bg_main.png` 极淡光晕） |
| 卡片 | `#FFFFFFFF` | 所有卡片/指标卡 |
| 边框 | `#FFE5E7EB` | 1px 卡片边框、分隔线 |
| 主文字 | `#FF111827` | 标题、数值 |
| 次文字 | `#FF6B7280` | 说明、标签 |
| 主色 | `#FF2563EB` | 主操作按钮、强调条 |
| 语义 | 绿 `#FF10B981` / 琥珀 `#FFD97706` / 红 `#FFDC2626` / 紫 `#FF7C3AED` / 青 `#FF0D9488` | 状态点、图表、强调条 |

**版面尺寸规范**（改公共层时遵守）：

| 区域 | 尺寸 | 元素 |
|---|---|---|
| 顶栏 | 0~48px | 圆点 24×24 @y=12 · 方案名 18px SemiBold @y=10 · 竖分隔线 @x=252 · 副题 13px @y=14 · 右侧圆点+本件+admin+时钟 |
| 页标题 | 56~110px | 竖色条 4×30 @(40,62) · 标题 25px Bold @(56,56) · 副题 12px @(56,90) |
| 内容层 | 130~950px | 卡片圆角 12、内边距 16、标题栏 38px |
| 底栏 | 968~1080px | 8 项 × 220px（间距 16）· 图标 32×32 @y+8 · 文字 14px @y+46 · 整块可点 |

- **背景**：`Image\bg_main.png`（1920×1080 近白渐变 + 极淡蓝紫光晕，无网格），每页 `BackgroundImage`
- **图标**：`Image\icon_{home,run,manual,param,monitor,chart,log,alarm,back}.png`（64×64 **深灰** `#374151` 线稿，透明底——浅色底上才可见）
- **触摸尺寸**：按钮高 ≥ 36、导航项 220×96、卡间距 ≥ 16

> **状态指示只用 `PairStatus()`（20×20 圆点 + 13px 文字）**，不要用公共辅助 `Indicator()`/`Lamp()`：
> 那两个是 64×72 的"大灯 + 下方文字"形态，塞进顶栏/卡片会溢出、把数据行挤歪。
> 另外 `Hmi.Indicator` 的模板是"灯体(*行) + 文字(Auto 行)"两行 Grid——文字行即使 `Text=""`
> 也按 `FontSize` 占高，所以小尺寸圆点必须 `FontSize="1"`，否则灯体被压成 0、圆点消失。

> **多行文本用 `PairText(..., vAlign: "Top")`**：默认垂直居中会让 4 行提示在 200px 高的框里
> 沉到中间、上方留一大片空白。所有"说明/提示"类文本一律顶对齐。
>
> **表格用多列独立 Text 按 x 定位，不要用 `PadRight` 拼字符串**：
> 中文字符宽度不一致，拼出来的列必然错位（阈值参考表就是这么改的）。

> **浅色主题下六条硬约束**（都是深色主题看不出来、浅色才暴露的）：
> ① `Hmi.Panel` 的 `TitleHeight` 默认不是 0 —— 空标题卡必须在 `PairCard` 里显式写 `TitleHeight="0"`，
>   否则卡片顶部留一条 38px 深色带；
> ② `Hmi.KpiBoard` 的格子底色是模板固定的深色（无属性可改）→ 指标展示改用 `PairMetric`（白卡+大数字）；
> ③ `AlarmBanner`/`MultiTrend`/`BarChart`/`PieChart`/`Report`/`DataGrid` 是"深色令牌底 + 用户 Fill 叠加"，
>   浅色主题必须给**不透明白** `#FFFFFFFF`（给透明 `#00000000` 会露出深底）；
> ④ 运行窗口的画面区容器底色**不能用 XAML 绑定**（页面底色是 string、`Grid.Background` 是 Brush，
>   WPF 不做隐式转换 → 静默失败、留白永远是窗口深灰）；已在 code-behind 用 `BrushConverter` 转换；
> ⑤ **画面四周不许留窗口边距**：`ScadaRuntimeWindow` 里画面区 `Margin` 必须是 0（边距区域属于父容器，
>   露的是窗口深灰底）；而且 `ScadaCanvas` 的模板是"外层视口底 + 内层页面矩形"，等比缩放后左右/上下
>   letterbox 露的是**外层**那个 —— code-behind 除了刷 `PageHost`，还要刷 `Canvas.Background` 与
>   `PopupCanvas.Background`，否则浅色页四周仍有一圈深色；
> ⑥ **表格/报表的表头默认是深色令牌**：`Hmi.DataGrid` / `Hmi.Report` 的标题条 / 列头 / 工具栏 / 脚注
>   用的是 `HmiSurfaceRaisedBrush`。2026-10-28 给两者加了可选属性
>   `HeaderBackground` / `HeaderForeground` / `ZebraBackground`（留空 = 深色令牌，零回归），
>   并把 `Fill` / `Stroke` / `Foreground` 真正接进模板（此前 DataGrid 模板写死令牌、这三个属性是死开关）。
>   本方案给的组合：`Fill=#FFFFFFFF`、`Stroke=#FFE5E7EB`、`Foreground=#FF111827`、
>   `HeaderBackground=#FFF9FAFB`、`HeaderForeground=#FF111827`、`ZebraBackground=#FFF3F4F6`。

### 3.5 视觉规范（2026-10-28 前端评审后的第二轮整改）

上一版是"看起来像 Web"，这一版把评审挑出来的**可读性 / 状态语义 / 主次**问题修掉了。
改动全在生成器 `Program.SamplePair.cs` 与两个控件模板里，改完重跑 `--pair` 即可复现。

| 维度 | 规范 | 落地位置 |
|---|---|---|
| 内容栅格 | 左缘 **40**、右缘 **1840**（8 页统一；主页原为 1860，已收齐）；列间距 20、行间距 20/24 | `PairPageHome` 等 |
| 字号地板 | 页副题 15、卡片标题 18、卡片说明/正文/表格 14、分区标题 16 semibold、KPI 数字 40、页标题 25 | `PairTitle`/`PairTile`/`PairMetric` |
| 主操作唯一 | 主页 hero 卡 = 2px 主色边框 + 实心 CTA；日志页「查询」实心；报表页「导出 Excel」实心 | `PairHeroTile`、`DataGridPrimaryButton`、`ReportPrimaryButton` |
| 主色令牌 | `HmiPrimaryBrush #2563EB` / `HmiPrimaryHoverBrush` / `HmiPrimaryTextBrush`（模板里不写十六进制，R140） | `ScadaTokens.xaml` |
| 状态灯 | 两态：满足 = 实心绿、不满足 = 实心浅灰；卡片右上必须有图例「● 满足　○ 未满足」 | `PairStatus` + 主页状态卡 |
| 导航选中态 | 当前页：`#EFF6FF` 底 + 底部 3px 主色条 + 主色标签；其余 `#6B7280`。公共层每页烘一份，选中态是**编译期常量** | `PairChrome` |
| 空值语义 | `YieldRate` 初值 **0**（原来 100 → 首屏"0 次检测 / 良率 100%"自相矛盾）；表盘与进度条设计默认值同样为 0 | `PairBuildVariables`、各表盘 |
| 进度条 | 必须有标签（「当前节拍进度」）+ 目标说明（「目标 ≥ 90%」）；值文字用深色（白字压在空轨道上看不见）；关掉 `ShowLimits` 的两根孤立刻度 | 主页状态卡 |
| 卡片高度 | **按内容给**，不许出现"卡底比内容高一倍"的空洞（调试提示 286→200、指标说明 190→160、报警规则 230→200） | 各页 |
| 单位 | 跟标签同一行右对齐，不甩到卡片右下角 | `PairMetric` |
| 顶栏 | 去掉无标签的"就绪灯"（和「本件」灯长得一样、没有文字，只会让人猜） | `PairChrome` |

> **两处刻意不改**（评审提了，但改它们要动库模板且与库作者意图相反）：
> ① 报警「全部确认」保留 danger 红 —— `AckButton` 的模板把内层 Button 钉在 `AckButtonDangerStyle` 上，
>   `Fill`/`Stroke` 是不被消费的基类外观属性（要改颜色只能改库模板）；而"确认"确实会清掉当前报警态，
>   红与报警卡同族是刻意选择。
> ② 宿主深色 chrome 与浅色页面硬切 —— 那是宿主窗口，不是页面；页面顶部 8px 呼吸带已够。

**重新生成图标/背景**（改了图标设计时）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\oneoff\bake_hmi_assets.ps1
```

脚本读 `tools\oneoff\asset_paths.txt`（UTF-8 清单，第一行 = 目标 Image 目录）。

另外配置了：
- **3 条报警**：系统未就绪（BoolOff）、良率过低（<90%）、累计不良超限（>20）
- **1 条变量事件**：`CurrentResult` 变 true/false 各记一条日志

---

## 4. 怎么运行

### 4.1 打开方案

1. 启动 `VisionMaster\bin\Debug\net9.0-windows\VisionMaster.exe`
2. 文件 → 打开 → `解决方案\视觉样例对拍\视觉样例对拍.vms`
   （若启动时提示"上次运行的现场"，选「稍后再说」即可）

### 4.2 跑起来

**整线（生产模式，推荐）**

- 宿主工具栏点「启动整线」/「整线循环」，或组态「自动运行」页点「启动整线」。
- 七个工位**同时**各自开一个会话跑，谁先跑完谁先进入下一轮，互不等待。
- 停：工具栏停止，或「停止整线」（一条钩子发七个 StopFlow）。

**整件（追溯模式）**

- 组态「自动运行」页点「执行一次整件」，或流程栏选中 `Main` 运行。
- 七工位串行 → 统计 → 结果上报 → 写账本。单轮约 35~55 秒（PCB 121 张占 24 秒）。

**单工位调试**

- 「手动控制」页点某个工位的「单跑本工位」（RunFlowOnce 该工位流程）。

> 整线跑起来后，主页/自动运行的判定灯、工位图、数值会各工位独立刷新；
> 但 `累计检测 / 良率 / 账本` 只在跑「执行一次整件」时才更新（整线里没有"一件"这个概念）。
>
> 结果上报默认**不发**：`Url` 留空 + 仅 NG 时机，NG 件只留一条
> `结果上报：未配置目标 URL，跳过本次上报` 的 WARN。要真发就在该步骤属性面板里填 `Url`。

### 4.3 看结果

- **实时标注图**：主窗口「视觉图像」面板的 1~7 号窗口（与七工位一一对应）
- **组态主页**：KPI、七工位判定灯与数值、良率曲线
- **结果统计页**：柱图 / 环形图 / 仪表盘 / 生产报表
- **运行日志页**：历史表格（点「查询」）
- **CSV 账本**：`解决方案\视觉样例对拍\History\Main.CSV记录\<yyyy-MM>\<yyyy-MM-dd>.csv`

---

## 5. 已知边界（务必知道）

### 5.1 采集路径是绝对路径

五条文件夹流程的 `FolderPath` / `FilePath` 写的是**绝对路径**（`D:\C#\VM\解决方案\视觉样例对拍\Image\...`）。

**原因**：`Plugin.ImageAcquisition` 的文件夹模式直接把端口值交给 `Directory.Exists()`，
**不做方案根锚定**；`ImageAssetId`（能解析相对路径）只对单文件模式生效。
`docs\方案设计\AI生成VMS文件指南.md` §1.1 已把这个列为"已知边界"。

**影响**：整个方案文件夹搬到别的机器/目录后，需要改这 5 条采集步骤的路径（或改用图像资产模式）。
其余资产（图像显示页的 7 张样图、模板）都是方案内相对路径，搬走即用。

### 5.2 账本目录也是绝对路径

C# 统计脚本用全局变量 `SolutionRoot`（初值 = 方案根绝对路径）拼账本路径。
搬走方案时把该变量改成新路径即可。

**为什么不用相对路径**：`RecordingHub` 展开模板后按宿主进程 CWD 解析，
而运行态的报表/历史表格按 `<方案根>\History` 锚定——两边不一致会让账本落到
`bin\History` 而组态页查不到。用绝对路径让两边对齐。

### 5.3 参数页的循环张数是"参考值"

参数配置页的步进/复位按钮改的是全局变量 `Loop_*`，**不会改变流程实际遍历张数**。

**要真正改遍历张数**：流程导航右键「遍历样例图」→ 改「循环次数」（For 步骤的 `DefaultLoopCount`）。

**为什么不接成联动**：`FlowCanvasChecks` 的样例流程断言只做 `SwitchSolution`、
不做 `VariablePersistenceService.Restore`，全局变量链接会被判「找不到全局变量」→ 流程编译失败。
为保证断言宿主常绿，改用手填值。

### 5.4 「CSV记录」插件步骤已停用

该步骤在真机运行链中**未被调度**（实测零日志、零落盘），账本已改由 C# 统计脚本直接写盘。
插件步骤配置完整保留但 `IsDisEnable=true`；若要改回插件口径，右键「启用」即可，
但要先查清它为何不执行。

### 5.5 PCB 工位会有真实 NG

`形状匹配_散焦PCB` 的判定阈值是 `分数 ≥ 0.7`，而散焦图库里有部分图匹配分数低于该值
（日志会显示 `规则「分数下限」NG：不满足 ≥ 0.7`）。**这是检测逻辑在正确工作**，
不是缺陷。要调阈值：改该流程「形状模板匹配」步骤的 `MinScore`。

### 5.6 公共层是"每页各一份"（不是共享页）

顶栏/底栏在 8 页各存一份图元（每页约 30 个）。这是刻意取舍：

- 用 `ScadaDocument.BasePages` 会因加载期按引用清理 + 序列化不保引用而被清空（实测）
- 用 `Hmi.SubPage` 会出现"未找到画面"占位与内容同时可见

代价：**改公共层要改 8 页**（实际做法是改生成器 `PairChrome()` 再重跑 `--pair`）。
好处：图元、事件、绑定全走"本页"通道，零依赖边角机制，导航按钮点击 100% 可靠。

---

## 6. 修改与再生成

### 6.1 改流程/组态

直接用软件界面改，正常保存即可（`--pair` 是增量改造，不会覆盖你手改的内容——
除非你重新跑一次生成器）。

### 6.2 重新生成

```powershell
# 1) 备份当前方案（生成器也会自动备份到 artifacts\logs\<日期>\backup-vms\）
# 2) 重建生成器
dotnet build tools\vms-generator\GenSolution.csproj -m:1 -nodeReuse:false
# 3) 跑 --pair（注意：必须直接跑 DLL，用 dotnet run 会把 -m:1 当程序参数）
dotnet tools\vms-generator\bin\Debug\net9.0\GenSolution.dll --pair
```

**注意**：`--pair` 是**可重入**的（幂等闸按 `结果判定_<工位>` 步骤名判断，已升级的工位跳过追加），
但它的期望输入仍是**基线** .vms。要回到干净基线，从
`artifacts\logs\<日期>\backup-vms\视觉样例对拍-pre-pair-*.vms` 取第一份（时间戳最早的那个）。

### 6.3 验证

```powershell
# 流程编译（真机同一条路：加载 → 变量恢复 → 逐流程编译）
dotnet tools\probes\_VmsCompileProbe\bin\Debug\net9.0-windows\VmsCompileProbe.dll `
  --solution "解决方案\视觉样例对拍\视觉样例对拍.vms"

# 组态渲染（每页出 PNG 到 docs\UI设计基线\app-shell\evidence\）
dotnet tools\probes\_ScadaUiProbe\bin\Debug\net9.0-windows\ScadaUiProbe.dll `
  --solution "解决方案\视觉样例对拍\视觉样例对拍.vms"

# 运行窗口整窗出图（验"画面四周黑框 / 列表标题黑框"这类画面区之外的问题）
dotnet tools\probes\_ScadaUiProbe\bin\Debug\net9.0-windows\ScadaUiProbe.dll `
  --runtime-window "解决方案\视觉样例对拍\视觉样例对拍.vms" 运行日志
```

三条都应输出 `RESULT: OK`。最后一条会装**真** `ScadaRuntimeWindow`（需要 `VisionMaster.App` 的全局资源），
出图是 `runtime-window-<画面名>.png`；省略画面名则停在启动页。

---

## 7. 本次改造的验证证据

| 验证项 | 结果 |
|---|---|
| 方案自检（连线完整性） | 84 条连线，**断链 0** |
| 流程编译（VmsCompileProbe） | 10 条流程 **全部 OK**，`RESULT: OK` |
| 组态渲染（ScadaUiProbe） | 8 页全部出图，`RESULT: OK`（主页 75 图元 / 自动运行 85 图元） |
| 运行窗口整窗出图（`--runtime-window`） | 启动页 / 运行日志 / 结果统计三张：**画面区外无深色缝、表格与报表无黑标题条** |
| 真机运行 | 七工位全部执行、判定表生效、变量写入、统计产出、**账本落盘** |
| 节拍延时 | 五条循环流程各含 1 个 200ms 延时步骤；单轮耗时从约 5 秒增至 35~55 秒 |
| 组态交互 | 底栏 8 项导航全部可点（日志 `公共_点击_XXX · 按下 → 切换画面`）、宫格大卡可点、数据泵绑定 6~11 条全命中 |
| `run_gates` 方案相关项 | `vms_verify` OK、`audit_root` OK、`audit_artifacts` OK、`audit_docs` OK |

真机运行日志摘录：

```
[Main.统计与良率] 账本根目录=D:\C#\VM\解决方案\视觉样例对拍
[Main.统计与良率] 账本文件=D:\C#\VM\解决方案\视觉样例对拍\History\Main.CSV记录\2026-10\2026-10-28.csv
[Main.统计与良率] 统计完成：总数=6，OK=6，NG=0，良率=100.0%
[组态事件] admin · 单次主流程 · 按下 → 执行一次流程 Main
[运行] 数据泵建表：命中 23 条绑定 / 未命中 0 条 / 订阅变量 21 个
```

账本内容：

```
时间,序号,综合结果,NCC,PCB,标签,条码,二维码,局部形变,平面形变,良率
2026-10-28 09:16:13.520,6,True,True,True,True,True,True,True,True,100.0
```

---

## 8. 相关文件

| 内容 | 路径 |
|---|---|
| 方案 | `解决方案\视觉样例对拍\视觉样例对拍.vms` |
| 生成器（本方案） | `tools\vms-generator\Program.SamplePair.cs` |
| 生成器入口 | `tools\vms-generator\Program.cs`（`--pair` 分支） |
| 方案格式指南 | `docs\方案设计\AI生成VMS文件指南.md` |
| 流程编译探针 | `tools\probes\_VmsCompileProbe\` |
| 组态渲染探针 | `tools\probes\_ScadaUiProbe\` |
| 组态出图 | `docs\UI设计基线\app-shell\evidence\视觉样例对拍-*.png` |
| 基线备份 | `artifacts\logs\<日期>\backup-vms\视觉样例对拍-pre-pair-*.vms` |
