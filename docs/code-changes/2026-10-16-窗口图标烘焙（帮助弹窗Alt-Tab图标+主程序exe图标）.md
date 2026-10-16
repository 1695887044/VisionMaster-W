# 2026-10-16 窗口图标烘焙（帮助弹窗 Alt-Tab 图标 + 主程序 exe 图标）

## 需求与口径

用户看着 Alt-Tab 切换卡片上那枚系统默认图标问：

> 这个帮助 的图像可以单独改下吗

一句"图像"有三个可能的落点（弹窗标题栏里那枚蓝方块徽块 / 窗口左上角图标 / Alt-Tab 与任务栏那枚窗口图标），所以先用提问收窄，两个答案逐字记录：

| 问题 | 用户选择 |
| --- | --- |
| 你说的「帮助的图像」是指哪一处？ | **Alt-Tab / 任务栏的窗口图标** |
| 如果要换窗口图标，范围要多大？ | **顺手把主程序图标也补上** |

于是本批口径定为两件事：① 帮助弹窗的**窗口图标**换成和"帮助"这件事同源的一枚；② 主程序（exe + 其余所有窗口）也补一枚。窗口**内**标题栏那枚 30×30 蓝方块徽块不动——用户没提它，而且它是 `HelpView.xaml` 里的界面元素，不是操作系统读的那份图标。

## 改动前的事实盘点（为什么这是"从零补"而不是"换一张图"）

改动前整个仓库**没有任何图标资产**，也没有任何一处声明。台账（命令可复算，见末节）：

| 查什么 | 命令口径 | 结果 |
| --- | --- | --- |
| 有没有 `.ico` 文件 | 全仓 `find . -name "*.ico"`（排除 `bin\obj`、`VM3.0-main`、`DLL\HslCommunication`） | **0 个** |
| csproj 有没有声明 `ApplicationIcon` | grep 全部 `.csproj` | **0 处** |
| 有没有窗口挂过 `Icon` | grep 全部第一方 `.xaml` 的 `Property="Icon"` / `Icon="` | **0 处**（只有 vendored `VM3.0-main` 的 `{h:Logo}` 那批，属第三方源码，不算本仓事实） |
| 有没有 `Window.Icon` 赋值 | grep 第一方 `.cs` | **0 处** |

后果就是截图里看到的形状：主窗口、所有弹窗、帮助弹窗，Alt-Tab 上全是同一枚 Windows 默认"窗口"图标——不是"图标不好看"，是**这个应用从来没交过图标**。

## 设计：两枚图标同源

一枚"主程序"、一枚"帮助"，共用同一块底，只有前景不同——这样 Alt-Tab 里两个卡片并排时看得出是同一个软件的两类窗口。

| | 底（共用） | 前景 |
| --- | --- | --- |
| 主程序 `visionmaster` | 24 单位圆角方块，r=5.4，竖向线性渐变 sky-400 `#38BDF8` → sky-600 `#0284C7` | 白色圆角取景框（x4.6 y6.2 w14.8 h11.6 r2.4，描边 1.9，`PenLineJoin=Round`）+ 中心白色镜头点（r=2.7，圆心 12,12） |
| 帮助 `help` | 同上 | **同一个 Font Awesome 6 Pro Solid U+F02D 书本字形**，转成几何后居中 |

三条设计依据，都不是随手挑的：

1. **主色取 `DialogAccentBrush`（`UI\Controls\Themes\Colors.xaml:103` = `#0EA5E9`）**。渐变两端 sky-400 / sky-600 把它夹在中间，所以这块底色与弹窗里的强调色是同一家族。
2. **帮助图标用 U+F02D 书本**，因为帮助弹窗页眉那枚徽块画的就是它（`VisionMaster\Views\DialogViews\HelpView.xaml:313` 的 `Text="&#xf02d;"`）。任务栏那一枚和窗口内那一枚**同源同形**，用户才认得出"这就是同一个帮助"。
3. **主程序用取景框 + 镜头点**（机器视觉的字面意思），16px 下只剩两条笔画：一个框 + 一个点，不画第三个元素——多一条线，16px 就开始糊。

## 烘焙：矢量源 → 逐尺寸原生渲染

脚本 `tools\bake_icons.ps1`（**纯 ASCII**，守 AGENTS.md R1；注释里也不许出现中文）。

**为什么用脚本烘焙而不是一张手画的 256px**：ICO 不是一张大图，是**多档尺寸表**，操作系统按场景挑档（Alt-Tab 与任务栏用最小档 16/24，桌面大图用 48/256）。若只放 256 一档、或把 256 缩到 16，缩出来的取景框描边在 16px 就散成灰块。所以这里的做法是**矢量描述只写一遍，然后对每一档尺寸单独渲染一次**（`RenderTargetBitmap(px, px, 96, 96, Pbgra32)` + `ScaleTransform(px/24)`）——16px 那档是**画**出来的，不是**缩**出来的。

设计网格 `$Unit = 24.0`，档位 `$Sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256`。

产物实测（字节数可复算）：

| 文件 | 大小 | 条目 | 头部 | 各档 PNG 条目字节 |
| --- | --- | --- | --- | --- |
| `VisionMaster\Resources\Icons\visionmaster.ico` | 19,758 B | 9 | 150 B | 16:605 20:710 24:858 32:1100 40:1325 48:1570 64:2046 128:3911 256:7483 |
| `VisionMaster\Resources\Icons\help.ico` | 16,040 B | 9 | 150 B | 16:509 20:635 24:764 32:953 40:1139 48:1311 64:1684 128:3085 256:5810 |

容器：`ICONDIR`（reserved=0 / type=1 / count=9）+ 每档 16 字节 `ICONDIRENTRY`（宽高分别是 16 字节，**256 在字节里写 0**，脚本 `if ($w -ge 256) { $w = 0 }`），条目内容**全部是 PNG 压缩**（Vista 以后的 shell 认这个；`planes=1`、`bitdepth=32`）。两份 ico 里 9/9 条目都以 `\x89PNG` 开头，无 BMP 条目。

另外两条"只有跑过才知道"的实现细节，已写进脚本注释：

- **字形取几何不能用 `GlyphTypeface.CharacterGlyphIndices`**（PowerShell 里取到 null 下标越界）。正解是 `FormattedText.BuildGeometry()`，并且要**居中墨迹盒而不是排版盒**：先 `BuildGeometry((0,0))` 拿 `.Bounds` 探一次，再用偏移把 `Bounds` 的中心搬到 (12,12) 重画一次——直接用排版盒会让书本在方块里偏下。
- **`.otf` 的绝对 URI 不是一个 `FontFamily` 键**，WPF 要的是 `"<文件 uri>#<家族名>"`，所以拼 `#Font Awesome 6 Pro Solid`。本仓根目录含 `#`（`D:\C#`），`Uri.AbsoluteUri` 已经把它百分号编码成 `%23`，**正好**不会与字体键的分隔符打架——这一点是运气不是设计，改脚本时别把它改成手拼。
- `RenderTargetBitmap` / `DrawingVisual` 要 **STA**，所以运行命令必须带 `-STA`。

预览图落在 `_icon_preview\`（18 张，`{name}-{size}.png`），256px 与 16px 两档已肉眼复核：主程序=天蓝方块上的白色取景框+镜头点，帮助=同一方块上的白色书本；16px 下两枚都还能认出是什么。

## 接线：两处，各管各的范围

| # | 位置 | 写法 | 覆盖范围与理由 |
| --- | --- | --- | --- |
| 1 | `VisionMaster\VisionMaster.csproj:15` | `<ApplicationIcon>Resources\Icons\visionmaster.ico</ApplicationIcon>` | 喂 **exe 的 PE 图标目录**，同时是"**没显式设 `Icon` 的窗口**"的默认图标 → 主窗口 `Shell.xaml`、`SplashScreen`、`ScadaRuntimeWindow`、`DebugHitWindow` 和**全部 Prism 弹窗**一次到位 |
| 2 | `VisionMaster\Views\DialogViews\HelpView.xaml:34`（在 `prism:Dialog.WindowStyle` 的 `Style TargetType="Window"` 里） | `<Setter Property="Icon" Value="/VisionMaster;component/Resources/Icons/help.ico" />` | **只**覆盖帮助弹窗。整仓只有它带自己的图标，其余窗口照旧走 `ApplicationIcon` |

盘点确认第一方代码里除这一条外**没有任何 `Icon` setter**（见上表），所以不存在"某窗口原本挂了别的图标、现在被 `ApplicationIcon` 顶掉或顶不掉"的暗冲突。

`help.ico` 走 `<Resource>`（csproj:20）而不是 `<EmbeddedResource>`：`Window.Icon` 的字符串值是 pack URI `/VisionMaster;component/...`，只有编译进 WPF 资源流才能被这个 URI 解出来。**失败模式很坏**：这条 Setter 的字符串是"样式应用到窗口的时刻"才做类型转换的，写错了编译期全绿，现场表现为**帮助弹窗打不开**（`XamlParseException`），与 R30 那族"资源键在全仓存在、但当前作用域解析不到 → 弹窗直接打不开"同形。所以给它配了运行期闸（见下节）。

## 验证台账

| # | 验证 | 命令口径 | 结果 |
| --- | --- | --- | --- |
| 1 | 全解决方案重建 | `dotnet build VisionMaster.sln --no-incremental` | **0 错误** |
| 2 | `ApplicationIcon` 真的进了 exe | 读 `bin\Debug\net9.0-windows\VisionMaster.exe`（176,640 B），逐条比对 `visionmaster.ico` 的 9 个 PNG 条目字节 | **9/9 命中**（PE 图标目录里就是这九档） |
| 3 | `help.ico` 真的进了包 | 同法读 `VisionMaster.dll`（1,495,552 B）：资源名 `resources/icons/help.ico` **1 处**、16px 条目字节命中 | 通过。（`help.ico` 在 **exe 里 0/9**，这是对的——它不是 `ApplicationIcon`，只进托管 dll 的资源流） |
| 4 | 新增运行期闸 | `dotnet run --project HelpProbe`（`HelpProbe\Program.cs` §3c，用与 Setter **逐字相同**的 pack URI 走 `BitmapDecoder` 解一遍） | **PASS 70 / FAIL 0**，明细行：`帮助弹窗的窗口图标在包里且有 16px 那一档（Alt-Tab 用最小档，缩出来会糊）（9 档：16x16 20x20 24x24 32x32 40x40 48x48 64x64 128x128 256x256）` |
| 5 | **变异反证**（R38：这条闸必须能被反证） | 把探针里的 URI 改成 `helpX.ico` 后重建重跑 | 准确变红：`[FAIL] …（1 档：解不开：找不到资源"resources/icons/helpx.ico"。）` → `### HelpProbe: 1 项失败`；还原后回到 70/0 |
| 6 | 静态 gate 没被 csproj / XAML 改动带坏 | `dotnet run --project FlowCanvasChecks --no-build` | **通过 2458 / 失败 0**（与图标批次前的基线同数） |

变异反证那一次顺带量出一条**实测新知**，并把探针注释里我原本写错的一句话改了：

> 报错文案里的资源名已经是**全小写**（`resources/icons/helpx.ico`），而 WPF 的资源名查找**不区分大小写**。

所以这条闸拦得住"资源没打进包 / 程序集名写错 / 文件名拼错 / 少档"，**拦不住大小写**——原注释写"路径大小写错也在这关红"是错的，已按实测改成"文件名少档会红 + 明写这一关拦不住大小写"。这也是 R41 口径的反向应用：注释与手册里的断言只能来自实测，不能来自"通常如此"。

## 事故实录：`--no-build` 让我差点把"没跑到的闸"当成"跑过了"

上一轮我记录的是"probe → PASS 69 / FAIL 0，其中包含图标那条"。这一轮为落最终证据重跑 `dotnet run --project HelpProbe --no-build`，输出仍然是 69/0，但**把 70 条 `[PASS]` 逐行拉出来找，找不到图标那条**——图标断言**根本没被执行**，跑的是**加断言之前的陈旧二进制**（`--no-build` 的语义就是"别编译"）。补一次 `dotnet build HelpProbe` 之后才出现第 70 条、并打出真实的 9 档明细。

三条教训：

1. 这条与红线③"产物陈旧"、R16（改生成器必须重建插件）、R42（增量构建静默漏打包内嵌资源）、R28（索引不重建就静默少返回）是**同一族**，而且这次栽在**验证工具自己**身上——不是产线代码，所以更隐蔽：绿灯是真的，只是**少了一条**。
2. **"全部通过"不能当覆盖证明**。判据要落在**条数 + 明细行文本**上（本批的正解就是那行 `9 档：16x16 …` 被原文打出来）。改过断言宿主源码之后，验证命令里**不许用 `--no-build`**。
3. 探针/断言宿主加一条 `Check()` 之后，**必须同时确认它在输出里出现了**，否则等于没加。

## 现场判读与已知边界

| # | 边界 | 说明 |
| --- | --- | --- |
| 1 | **Windows 图标缓存** | 同一个路径的 exe 换了图标，资源管理器 / 任务栏可能仍显示旧图（图标按路径缓存）。**真机判读时如果 Alt-Tab 是新图、桌面/任务栏是旧图，先怀疑缓存**（换输出目录或重启 explorer 验证），不要先改代码。 |
| 2 | 真机未确认 | 本批只有构建产物 + 探针级证据（PE 条目、pack URI 解码、预览图肉眼复核）。**Alt-Tab 卡片与任务栏上实际显示成什么样，需要跑一次真机**——尤其 16px 那档在系统 100%/150% 缩放下的观感。 |
| 3 | 帮助弹窗内那枚蓝方块**没动** | 用户选的是"Alt-Tab / 任务栏的窗口图标"，页眉 30×30 徽块（`HelpView.xaml:294-309`）保持原样。两枚现在是同一支书本、同一色家族，视觉上是一对。 |
| 4 | 只给了帮助弹窗单独图标 | 其余弹窗仍继承 `ApplicationIcon`。若以后要让某类弹窗各带图标，就照第 2 处那条 `Setter` 加，并把新 `.ico` 也交给 `tools\bake_icons.ps1` 烘——**不要手塞单尺寸 ico**。 |
| 5 | `_icon_preview\` 是烘焙副产物 | 18 张 PNG 只为肉眼看档，不参与构建。要清就整个目录删掉，脚本会重新生成。 |

## 改动清单

| 文件 | 改动 |
| --- | --- |
| `tools\bake_icons.ps1` | **新增**（183 行，纯 ASCII，STA 运行；矢量源 + 逐档渲染 + ICO 容器写入） |
| `VisionMaster\Resources\Icons\visionmaster.ico` | **新增**（19,758 B / 9 档 PNG 条目） |
| `VisionMaster\Resources\Icons\help.ico` | **新增**（16,040 B / 9 档 PNG 条目） |
| `VisionMaster\VisionMaster.csproj` | 新增 `ApplicationIcon`（:15）与 `<Resource Include="Resources\Icons\help.ico" />`（:20），各带一句来由注释 |
| `VisionMaster\Views\DialogViews\HelpView.xaml` | `Dialog.WindowStyle` 里新增 `Icon` Setter（:34）+ 注释说明"这是 Alt-Tab 图标、不是页眉蓝方块、与徽块同源" |
| `HelpProbe\Program.cs` | §3c 新增窗口图标运行期闸（URI 与 Setter 逐字一致 + 判 `16x16` 那一档在）；变异反证后还原源码，并按实测改掉"大小写"那句错注释 |
| `docs\code-changes\2026-10-16-窗口图标烘焙….md` | 本文件 |

## 复算命令

```powershell
# 烘焙（改几何时只跑这一条，再重建宿主）
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File tools\bake_icons.ps1

# 改动前"仓内零图标资产"的盘点
find . -name "*.ico" -not -path "*/bin/*" -not -path "*/obj/*" -not -path "./VM3.0-main/*"
grep -rn --include=*.csproj "ApplicationIcon" .
grep -rn --include=*.xaml 'Property="Icon"' . | grep -v VM3.0-main

# exe 里 9 档条目对不对 / dll 里 help.ico 在不在（Python 逐条目比字节，见"验证台账"2、3）
# 运行期闸（含图标那条的明细行）
dotnet build HelpProbe/HelpProbe.csproj
dotnet run --project HelpProbe --no-build
# 静态 gate
dotnet run --project FlowCanvasChecks --no-build
```

## 关联纪律

- **R1/R2/R3/R6**：`.ps1` 纯 ASCII、中文常量走码点、不用 `Get-Content` 往返、写回用 `[IO.File]::WriteAllBytes/WriteAllText`。
- **R38**：新增断言必须能反证——本批的变异是 URI 改成 `helpX.ico`，准确变红并给出可判读明细。
- **R41**：注释与文档里的每条事实都要指着实测（本批据此改掉了"大小写会红"这句想当然）。
- **红线③ / R42 / R28 / 本批事故实录**：产物与**验证工具**都会陈旧；`--no-build` 只在"确认源码没动"之后用。

## 未决

1. **真机确认**：Alt-Tab 与任务栏上两枚图标的实际观感（尤其 16px + 高 DPI），以及图标缓存是否造成"看起来没变"。
2. **是否给别的弹窗也各配一枚图标**（工具箱 / 变量管理 / 参数弹窗等）——本批按用户口径只做"帮助 + 主程序"两处。
3. **`_icon_preview\` 要不要纳入版本控制**（当前是未跟踪的烘焙副产物；也可以只留 16/32/256 三档当参考图）。
4. 上一批遗留、与本批无关但仍未裁决的：`Plugin.Camera.Network` / `Plugin.ImageAssign` 是否加进 `VisionMaster.sln`、`[Description]` 与 `[Display]` 枚举显示链是否统一、CodeGraph 重建。
5. **未做任何 git 提交**（按用户纪律，未经确认不提交/不推送）。
