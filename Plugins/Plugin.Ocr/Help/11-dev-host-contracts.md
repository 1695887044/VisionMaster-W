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
| `Text` | `OutputPort<string>` | 拒识字符显示为 `?` |
| `Count` | `OutputPort<int>` | 切分并识别到的字符个数 |
| `Confidence` | `OutputPort<double>` | 逐字符均值 |
| `Detail` | `OutputPort<string>` | 序号加字符加置信度，`?` 表示拒识 |

没有数组端口——这是它和 YOLO 最大的接口差别：OCR 的"逐字符"信息全部塞在 `Detail` 这一个文本端口里，
下游要结构化数据得自己解析（现场建议接 `Text` 做判定，别解析 `Detail`）。

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
