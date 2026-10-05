# 2026-10-04 Blob 配置窗口预览区空白修复（内联 Style 顶掉控件模板）

- 日期：2026-10-04
- 项目：VisionMaster（WPF + Prism 9 + Halcon）
- 范围：3 个文件——`Plugins\Plugin.BlobDetect\BlobDetectView.xaml`（修复）；`FlowCanvasChecks\BlobDetectChecks.cs`、`UIThemeSmokeTest\Program.cs`（各新增一条静态回归闸）
- 类型：缺陷修复（配置窗口预览区**静默**空白）+ 回归断言收口
- 口径：**探针** = `%TEMP%` 隔离探针（真实 `BlobDetectView` + 真实窗口 + 复刻 `Engine\PluginTestRunner.cs` 的后台写 `SrcImage`）；**构建** = BlobDetect 工程重编；**冒烟** = `FlowCanvasChecks` / `UIThemeSmokeTest` 运行输出。落盘时对修复落点与断言文本做了静态复核（file:line 见正文）。

---

## 一、现象

- Blob 缺陷检测**配置窗口**点「执行」（试运行）后，右侧预览区「图像视图」页签**永远空白**。
- 同一次试运行：插件信息栏**有结果文字**（「NG：…」）、主界面显示窗口**也能收到标注图**。
- 两者都不经过预览区那个控件：结果文字走 ViewModel 属性、主界面显示走图片发布链路——所以"只有预览区空白"是精确症状。
- 全程**不报错、不留日志**，属"沉默故障"。（来源：隔离探针复现，见「五、验证证据」）

## 二、根因

### 2.1 机制链（五环）

1. `Plugins\Plugin.BlobDetect\BlobDetectView.xaml` 给 `h:ImageReadOnly` 挂了**内联** `<h:ImageReadOnly.Style>`（只设 `Visibility` 的 `DataTrigger`，**没有 `BasedOn`**）。
2. WPF 规则：控件的**显式 Style 会顶掉**资源查找链里的**隐式样式**。而 `ImageReadOnly` 的 `ControlTemplate` 只存在于 `Core.Halcon` 合并字典的隐式样式里——`Services\Core.Halcon\Generic.xaml:5` 合并 `Themes\ImageReadOnly.xaml`；后者 `:6-36` 的无 `x:Key` 隐式 `Style` 定义了 `ControlTemplate`，内含 `PART_Halcon`（`HSmartWindowControlWPF`，`:21`）。
3. → 控件**没有模板**：可视树里不存在 `HSmartWindowControlWPF` / `PART_Halcon`。
4. → `HalconBase.RenderAll()`（`Services\Core.Halcon\Base\HalconBase.cs:1013`）在 `hWindow == null`（`:1015-1016`）时**直接 return**——无异常、无日志。
5. → 预览区永远空白：看起来像"图没送到"，实际是"控件根本没被渲染"。

（第 3~5 环由隔离探针实证；第 1~2 环由源码静态核对。）

**引入时间**：该内联样式是 **2026-10-01 12:28** 加「图像视图/数据输出」页签时引入的。（来源：修复会话全文件内容级 diff 核对）

### 2.2 为什么与 `[StepConfig]` partial 属性迁移无关

同日进行的 `[StepConfig]` partial property 迁移**只动了属性区**——全文件内容级 diff 证明 `BlobDetectView.xaml` 未被迁移触碰，且该内联样式早于迁移（10-01 就在）。两件事在时间、文件上均无交集。（来源：修复会话 diff 核对）

### 2.3 附带更正：不要泛化成"所有控件都会丢模板"

"任何控件挂内联 Style 都会丢模板"**已被最小实验证伪**：stock 控件（`ComboBox` / `ScrollViewer`）即使挂无 `BasedOn` 的内联样式，其模板来自 OS 主题样式、**不会丢**。中招的只限"ControlTemplate 放在合并字典隐式样式里"的控件——本仓库即 `h:`（`Core.Halcon`）系控件。据此复核：`CSharpScript` / `ImageScript` 视图里的 `ComboBox` 内联样式**无需修改**。（来源：最小实验 + 视图复核）

## 三、修法

- 把两个页签的可见性触发器**移到外层包装 Grid 上**：`h:ImageReadOnly` 与 `ScrollViewer` 各包一层，控件自身不再挂内联样式（`BlobDetectView.xaml:715-746`）。
- XAML 内留注释说明机制（`BlobDetectView.xaml:716-719`）。
- 可见性行为不变（`SelectedPreviewTab` 0/1 切换）——修复只改"触发器挂在哪一层"。

## 四、回归断言（防复发）

| # | 位置 | 内容 |
| --- | --- | --- |
| 1 | `FlowCanvasChecks\BlobDetectChecks.cs` —— `RunViewAndThemeContract()`（`:87`） | 新增静态闸：正则 `<h:[A-Za-z0-9_]+\.Style>` 扫本插件视图，必须 0 命中（`:120-125`）；断言文案「【收编】视图不给 h: 控件挂内联 Style（顶掉隐式样式=控件没模板，预览会永远空白）」 |
| 2 | `UIThemeSmokeTest\Program.cs` —— 仓库级 XAML 扫描 | 递归扫全仓 `*.xaml`（跳过 `\obj\` / `\bin\` / 参考工程）新增同款闸（`:242-276`）；断言文案「XAML 不给 h: 控件挂内联 Style（顶掉隐式样式=控件没模板，界面会永远空白）」（`:272`） |

两条闸均为**静态扫描**（读源文件比对，不实例化视图），沿用 2026-09-25 的决定与理由（见 `BlobDetectChecks.cs:72-86` 注释）。

## 五、验证证据

| 项 | 结果 | 口径 |
| --- | --- | --- |
| 修复前隔离探针 | `ImageReadOnly.TemplateIsNull=True`、`hWindow=False` —— 复现"控件没模板"链 | 探针 |
| 修复后隔离探针 | `TemplateIsNull=False`；`HSmartWindowControlWPF` 在可视树中且 `Loaded`；`hWindow=True`；`VM.PreviewImage` 与控件 DP 同引用；dump 出的 HALCON 窗口 PNG（276 KB）内容为**带标注的检测图**（判定 NG / 缺陷数 / 编号红框） | 探针 |
| 页签切换 | 「数据输出」页签正常（缺陷清单 8 行 + 直方图） | 探针 |
| BlobDetect 工程重编 | 0 error；`Modules\Plugin.BlobDetect.dll` 已刷新 | 构建 |
| `FlowCanvasChecks` 全量 | 通过 1003 / 失败 19；**[Blob] 段全绿（含新闸）**；19 项失败均为既有环境项（虚拟相机 `VIRTUAL-001` 未配置、测试 ONNX 模型缺失、相机注册表 / Dialog 主题键计数、运动卡候选等），与本修复无关；其中 Yolo「类别过滤」一项系 `ResolveTestModel()==null` 时回落不存在模型路径（`FlowCanvasChecks\YoloChecks.cs:183-190`） | 冒烟 |
| `UIThemeSmokeTest --controls` | 新闸 **PASS**（扫过 129 个 xaml，0 命中）；其余 4 项开关几何失败为既有 | 冒烟 |

## 六、未决

- 无。
