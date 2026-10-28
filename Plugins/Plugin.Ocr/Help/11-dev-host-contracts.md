# OCR 文本识别 · 开发章节：宿主契约与配置模型

> 分类：图像处理 ｜ 来源：插件 Plugin.Ocr ｜ 读者：开发

## 一 类声明与一个特例

```
[Display(Name = "OCR 文本识别", GroupName = "图像处理",
         Description = "在识别区里切分字符并用内置分类器识别（0-9 / A-Z，带拒识）", ShortName = "\uf031")]
[ParallelSafe] // 二期真并行：纯视觉计算
public class OcrPlugin : VisionPluginBase, IPluginCustomViewProvider
```

**本类不是 `partial`，全部 `[StepConfig]` 属性手写。** 它是五个条目里唯一不依赖 `Core.PluginGenerators` 的：
加新配置项要自己写背垫字段与 `OnPropertyChanged()`，忘了就表现为"改了不生效、保存后回退"。
（对照：图像脚本、C# 脚本、YOLO、深度学习分类的多数配置项走源生成 `partial` 属性。）

## 二 配置项全表（默认值逐条照源码）

| 键 | 类型 | 默认值 | 归属 |
| --- | --- | --- | --- |
| `RoiParams` | `double[]` | `Array.Empty<double>()` | 识别区，落盘 |
| `TextIsDark` | `bool` | true | 极性（暗字亮底） |
| `MinCharHeight` | `int` | 8 | 切分筛选 |
| `MaxCharHeight` | `int` | 300 | 切分筛选 |
| `MinCharWidth` | `int` | 4 | 切分筛选 |
| `MaxCharWidth` | `int` | 300 | 切分筛选 |
| `UseFixedThreshold` | `bool` | false | 阈值方式 |
| `FixedThreshold` | `int` | 128 | 固定阈值 |
| `MinArea` | `int` | 20 | 面积筛选 |
| `MaxArea` | `int` | 5000 | 面积筛选 |
| `ClosingRadius` | `double` | 0 | 形态学 |
| `ErosionRadius` | `double` | 0 | 形态学 |
| `DilationRadius` | `double` | 0 | 形态学 |
| `ModelPath` | `string` | `string.Empty` | 空即用内置 |
| `ColumnFirst` | `bool` | false | 竖排才开 |

界面把后四类归到"高级参数（多数场景不用动）"分组里，但落盘键一律平铺进 `.vms`，没有第二份存储。

## 三 端口契约

| 端口 | CLR 类型 | 口径 |
| --- | --- | --- |
| `Image` | `InputPort<HImage>` | `IsRequired = false`（先摆节点再接线），空判定在 `RunAlgorithm` |
| `Text` | `OutputPort<string>` | `Classes` 直接拼接；**拒识位是 U+001A（SUB），不是 `?`** |
| `Count` | `OutputPort<int>` | 切分并识别到的字符个数（含拒识），= `Classes.Length` |
| `Confidence` | `OutputPort<double>` | 逐字符均值 |
| `Detail` | `OutputPort<string>` | 序号加字符加置信度，`?` 表示拒识（`Display()` 负责把 U+001A 换成 `?`） |
| `Classes` | `OutputPort<string[]>` | 逐字符原始类别名，拒识保留 U+001A |
| `Confidences` | `OutputPort<HTuple>` | 逐字符置信度 0~1，与 `Classes` 同下标 |
| `RejectedCount` | `OutputPort<int>` | `Classes` 里 U+001A 的个数 |
| `CharBoxes` | `OutputPort<HTuple>` | 逐字符外接框，扁平 stride 4：`[row1,col1,width,height, …]`（像素含端点） |

`Classes` / `Confidences` / `CharBoxes` 三者按下标逐项对齐、顺序 = 阅读顺序（`segment.Chars` 经
`sort_region` 排过序），长度分别是 `Count` / `Count` / `Count×4`。
`CharBoxes` 由 `TryBuildCharBoxes` 逐对象 `SelectObj(i+1)` + `SmallestRectangle1` 算出——
**逐对象取而不是一次算整元组**，是为了把"第 i 个框 ↔ 第 i 个类别"写死在代码里，不靠 HALCON 的输出顺序契约。

口径提醒：`HTuple` 不是 `IDisposable`，基类 `AutoDisposeRoundOutputs` 管不到它，所以 `RunAlgorithm` 开轮
必须显式把八个输出端口清零（`Text` 空串 / `Count` 0 / `Confidence` 0 / `Detail` 空串 / `Classes` 空数组 /
`Confidences`、`CharBoxes` 空元组 / `RejectedCount` 0），否则失败轮下游会读到上一帧的值。

## 四 白拿的基类行为与界面绑定

`Success` 预置、`Fail`、`PublishPreview`、`Logger`、`ApplyConfigValues`、`OnConfirm` 与 `SnapshotConfigValue` 沿用基类；
没有动态端口，所以不实现 `IDynamicOutputProvider`。
配置界面走 `IPluginCustomViewProvider.GetConfigView(IStepConfigData)`，界面里的画布控件读写 `CanvasRois`、
`CanvasActiveRoi`、`DisplayImage`，按钮文本包括"清空识别区""按列优先排序（竖排文本）""暗字亮底（不勾 = 亮字暗底）"
"用固定阈值（不勾 = 按直方图自动找分界）""试算一下""浏览"。
`Hint` 与 `ResultSummary` 是界面态属性，不落盘。

## 五 生命周期与打包

`Dispose`：`_engine.Dispose()`（释放分类器句柄）→ 释放 `DisplayImage` → `base.Dispose()`。
`OcrSession` 之类的进程级缓存这里**没有**，实例级 `OcrEngine` 就够了：模型小、纯 CPU，
（同一套写法在 YOLO 是错的，理由见 YOLO 那册 10 页第五节）。
手册四页用户册加三页开发章节（M 档，开发合计 ≥150 行），扁平在 `Help\` 下、由已有的
`.csproj` 里那条把 `Help` 目录全部 Markdown 收进 DLL 的 `EmbeddedResource` 通配项 打进 `Plugin.Ocr.dll`；`Help\dev\` 这类子目录会被 HM12b 判红。
内置模型的投放目录是 `Assets\ocr`，缺目录不报错（会退回 HALCON 自己的搜索路径）。
