# 图像脚本 · 开发章节：架构与关键路径

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageScript ｜ 读者：开发

源码：`Plugins\Plugin.ImageScript\ImageScriptPlugin.cs`（插件类兼视图模型）、`EProcedure.cs`（过程 XML 编组）、
`HDevDisplayBackend.cs`（显示后端与老算子垫片）、`ScriptVarDef.cs`（变量行与类型枚举）、
`ScriptIntelliSense.cs` / `ScriptTemplates.cs` / `OperatorDoc.cs` / `ScriptFormatter.cs`（编辑器周边）。

## 一 一个类兼两个身份

`ImageScriptPlugin : VisionPluginBase, IPluginCustomViewProvider, IDynamicOutputProvider`。
它既是流程节点（`RunAlgorithm` 被执行），又是配置界面的 DataContext（`GetConfigView` 里 `new ImageScriptView{DataContext=this}`）。
带来的直接后果：**属性改动会同时触发"界面重绘"和"端口重建"**，所以端口重建只在明确需要时做（见第四节），
否则每次敲一个字符都重建一遍端口，连线会被打断。

## 二 三份状态，各有各的真源

| 状态 | 存放处 | 真源 | 丢了的后果 |
| --- | --- | --- | --- |
| 过程清单与过程体 | `StepData.InputValues` 的 `Procedures` 键（EProcedure 序列化成 XML 字符串） | 落盘 | 脚本内容没了 |
| 变量表（输入、输出） | `InputVars`、`OutputVars` 两个 `[StepConfig]` 集合 | 落盘 | 端口全没，连线全断 |
| 编译产物 | 静态 `CallCache`（按脚本 SHA256 指纹） | 内存 | 无，重编译即可 |

`SelectedProcedure` 也是落盘键。插件构造时 `HookVars(InputVars); HookVars(OutputVars); PreWarmEngine();` —— 每建一个节点实例就预热一次引擎。

## 三 引擎与缓存都是静态的

`static readonly HDevEngine Engine = CreateEngine()`，一个进程一个引擎。建的时候做四件事：
把 `Path.GetTempPath()` 设成过程搜索路径；若 `ScriptAssets\procedures` 存在则 `AddProcedurePath` 进去；
打开 `execute_procedures_jit_compiled`；`SetHDevOperators(new HDevDisplayBackend())` 注册显示后端。
最后一行是 R36 的**永久豁免点**：全仓只有这里和 `AnnotationBaker` 允许碰窗口，详见 13 页。

`CallCache` 是 `static Dictionary` 加 `CacheLock`，`Count > 64` 时整体清空（不做 LRU）。
指纹取脚本内容的 SHA256，脚本一个字没改就复用已编译的 `HDevProgram`。
`PreWarmEngine()` 用 `Interlocked` 保证只提交一次 `Task.Run`，跑一个只含 `return ()` 的临时过程，
把原生引擎的冷启动成本挪到节点创建时，而不是用户第一次点运行时。

## 四 配置会话时间线（界面打开到运行）

1. `Initialize(stepData)` → `EnsureDefaultScriptIfNeverSaved`：**只在** `InputValues` 里没有 `Procedures` 键**且**内存过程表也为空时灌默认脚本。落过盘的工程永不被覆盖，这是"恢复默认脚本"只能显式点的原因。
2. `EnsureSelectedProcedure()` → 让下拉指向一个真实存在的过程。
3. `ApplyConfigValues(values)`：`base` 先把 `[StepConfig]` 值灌进来，**然后** `RebuildDynamicInputs(); RebuildDynamicOutputs();`。顺序是硬要求：编译期端口必须先存在，宿主才可能按名字把线接回去。
4. 变量表编辑 → `OnVarDefPropertyChanged` 只对 `Type` 一列响应（改类型要重建端口 CLR 类型，改名/备注不重建）。
5. `SyncInterfaceFromTables()`：清空并按表格重填 `IconicInputList`、`IconicOutputList`、`CtrlInputList`、`CtrlOutputList`，名字统一 `Trim()`。变量表是接口唯一真源。
6. `RebuildDynamicOutputs()` 里 `if (StepData != null)` 才写 `StepData.OutputPortDefinitions`（宿主在配置期要靠它列出可连的输出）。

## 五 RunAlgorithm 的十二步

1. 取脚本并编译（`EnsureCompiled`，失败即第 2 步）。
2. 编译失败 → `Success.Value=false`，`ErrorMessage.Value = compileError ?? "脚本编译失败"`，**不抛异常**，返回。
3. 逐个检查图形输入：未连线或值为空 → `输入变量[X]未链接或图像为空`，返回（脚本一行不跑）。
4. 第一张 `HImage` 输入被选作 `baseImage`（效果图的底图）。
5. `HDevDisplayBackend.Begin(baseImage, callback)` 起一张 buffer 窗口并把底图预画上去。
6. 逐个绑实参：图形走 `SetInputIconicParamObject`，控制值走 `SetInputCtrlParamTuple`（`ToTuple` 按 `, ; 空格 制表符` 切）。
7. `call.Execute()`。
8. 逐个取结果：图形走 `GetOutputIconicParamObject` 系列，控制走 `GetOutputCtrlParamTuple`。
9. 类型不符 → 翻译成"请把类型下拉改成 X"的中文提示并失败。
10. 非图形输出先攒进 `pending`，全部输出取完后再汇成随图信息（`BuildInfoRows` 用 `Remark` 或变量名做标签）。
11. `v.DisplayWindow >= 1` 的图形输出单独 `PublishPreview` 到 1~9 号窗口。
12. `PublishPreview(image, view, title, info, null)` 发布主图；`if (HDevDisplayBackend.HasDrawing)` 再 `Capture()` 出一张效果图并 `PublishPreview(fx, 1)`；最后 `Success=true`。

异常出口三层：`DisplayOpException` → 用它的 `Message`（画图环节的问题）；`HalconException` → `Halcon 脚本异常: {GetErrorMessage()}`；
`Exception` → `脚本执行异常: {Message}`。

## 六 显示后端与老算子垫片

`HDevDisplayBackend` 是 `internal sealed : IHDevOperators`。窗口尺寸下限 `MinSide=16`、上限 `MaxSide=8192`、
取不到尺寸时用 `FallbackW=800` / `FallbackH=600`；窗口是 `open_window(... 'white','buffer','local' ...)`。
`Capture()` 用 `dump_window_image` 回读，随后 `new HImage(ho)` 拷出独立句柄、释放中间对象集，`Release()` 收尾。

`HasDrawing` 是**静态**标记，跨一次运行有效，`Begin` 复位。交给 HDevelop 的窗口句柄必须是"新容器"，
不能把内部 `_win` 实例直接 `out` 出去——引擎会往里写，把我们的实例改坏。

`LegacyDisplayShim` 把 `set_color`、`set_draw`、`set_line_width`、`set_shape`、`set_paint`、`set_lut`、
`set_contour_style`、`set_colored`、`set_part` 自动改写成 `dev_set_*`；
`disp_rectangle1`、`disp_circle`、`disp_line`、`disp_arrow`、`disp_cross`、`disp_polygon`、`set_tposition`、`write_string`
无法垫片，直接报错并在错误里给"改成哪两行"的具体写法。`disp_font` 已从黑名单摘出（当初"引擎不支持设字体"的实测结论不成立）。

## 七 非图形结果的合成路径

`ComposeIconicToImage` 只用纯算子（`PaintRegion`、`PaintXld`、`Compose3`、`SelectObj`、`AreaCenter`、`SmallestRectangle1`、`GetContourXld`），
绿色是 `{0,255,0}`；区域/轮廓小于原图时叠到原图（`margin`），大于原图时按自身范围造画布（`fill`）。
探针图与自建画布、合成结果在 `finally` 里逐个释放，任一环节抛异常就返回 `null`（不失败，只是不显示）。
`GenBlankCanvas` 的边长夹在 64~4096，默认 640×480。
