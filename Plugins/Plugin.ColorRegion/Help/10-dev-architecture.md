# 区域颜色检查 · 架构与数据流

> 分类：图像处理 ｜ 来源：插件 Plugin.ColorRegion ｜ 读者：开发

工程：`Plugins\Plugin.ColorRegion`（工具箱条目「区域颜色检查」，类 `RegionColorPlugin`，分类「图像处理」，
标了 `[ParallelSafe]`——纯视觉计算、无共享可变状态，二期真并行可以直接并）。

## 文件与职责

| 文件 | 职责 |
| --- | --- |
| `RegionColorPlugin.cs` | 插件类：配置项（含 13 项阈值副本）、端口、`RunAlgorithm`、试算入口、配置视图与画布联动、结果投射 |
| `RegionColorAnalyzer.cs` | 内核侧：`Analyze`（逐点分类 → 投票 → 主色与占比）与 `Judge`（期望词表 + `MinShare` 判定），两个入口共用 |
| `RegionColorView.xaml` / `.xaml.cs` | 自带配置视图：`IPluginCustomViewProvider` 交出去的 DataContext 视图，含画布、试算按钮、`ColorThresholdEditor` |
| `Core.Halcon.Color`（共享服务） | `ColorSampler`（ROI → 点集、批量取像素）、`ColorThresholds`（判据对象）、`ColorMarks`（颜色词表与投射标记口径）、阈值默认值 |

**分层口径**：插件只负责"收集配置 + 组织流程 + 说话"，**判色的定义全在 `Core.Halcon.Color`**。
所以"玫红"这个词、分类层次（先无彩后有彩）在内核里，插件改不了也不该改；
插件能改的只有**这 13 个阈值数值**（各算子各带一份副本）。

## 状态存在哪几份，谁是真源

真源只有一份：**插件实例上的 `[StepConfig]` 属性**。三条回路都往它写、从它读：

1. **落盘回路**：`[StepConfig]` 快照 → `.vms`；回填走同一份快照（`ApplyConfigValues` 由基类完成）。
2. **画布回路**：用户在图上右键画 ROI → 视图把形状与参数写回 `RoiShape` + `RoiParams`；
   反向（配置 → 画布）由视图在装载时还原轮廓。`RoiShape` 用**字符串**而不是枚举：
   枚举值经步骤的 `InputValues` 往返会抛 `InvalidCastException`（这是选型的硬约束，不是风格偏好）。
3. **上游图回路**：`Image` 端口的值变化 → 视图订阅（判据是 `e.PropertyName != nameof(IInputPort.Value)` 直接 return），
   把上游图**拷一份**显示到画布。拷的原因写在断言里：上游句柄释放后画布必须仍可用，
   直接引用上游句柄会变成空句柄画布。打开配置时**优先用上游图，不去读可能失效的示意图路径**。

`_lastColor` / `_lastShare`（上一次试算的主色与占比）是**会话内暂存**，不落盘：
只服务「把主色填成期望颜色」这个示教按钮。没有试算结果时那个按钮只给提示、不填值。

## RunAlgorithm 逐步

```
Image.GetTypedValue()
  ↓ 为空或未初始化 → Fail("上游没有图像：…")，return（早退①）
TryAnalyze(image, out result, out error)
  ↓ ① RoiParams 长度不是 5 → error "尚未框选采样区：…"，return（早退②）
  ↓ ② CountChannels < 3   → error "区域颜色检查要彩色图，当前图像只有 N 个通道"（早退③）
  ↓ ③ ColorSampler.TryBuildRoiPoints(image, RoiShape, RoiParams, SamplePoints, rows, cols, out error)
       —— ROI → 区域 → 与图像求交（部分越界裁掉不报错，完全没交集报错）→ 均匀抽稀成点集
  ↓ ④ ColorSampler.TryReadPixels(image, rows, cols, out pixelChannels, out error) —— 一次批量取回 R/G/B
  ↓ ⑤ NormalizeTo8BitIfHighBitDepth(pixelChannels, out note)
       —— 按实测最大通道值等比压到 0~255；max 小于等于 255 时原样返回 false（不缩放）
       —— 压过的 note 会追加进 result.Warnings（"为什么 R,G,B 变小了"必须可查）
  ↓ ⑥ RegionColorAnalyzer.Analyze(pixelChannels, BuildThresholds(), out analyzed, out error)
       —— 逐点分类成颜色词 → 投票 → 主色 / 占比 / 明细 / 中位色；判定**不在这里**
Fail(error) 并 return（若上面任何一步 false）
RegionColorAnalyzer.Judge(result, ParseExpected(), MinShare, out error)
  ↓ false → Fail(error)，return（早退⑦：期望为空 / 期望词表外）
foreach (warning in result.Warnings) context.Logger.Warn(warning)   —— 告警不中断
写端口：Color / Share / Rgb（"R, G, B" 带空格）/ Table（TableText()）/ Result / Verdict
context.Logger.Info 三行（主色+中位色+采样点数 / 明细 / 判定全文）
ProjectResult(image, result)                                        —— 投图，异常被吞
```

**试算入口**与运行期入口共用 `TryAnalyze`，但**不调 `Judge` 的强制分支**：
试算时 `Judge` 的结果只用于渲染摘要（未配期望时 `judgeLine` 显示"尚未配置期望颜色（点下面那个按钮即可把主色填进去）"），
**运行期必须判定**——配置问题不静默通过。这条不对称是刻意的（顺序死锁：试算强制填期望，示教按钮就永远用不上）。

## 判定口径的实现位置

`RegionColorAnalyzer.Judge` 认三种期望状态：空 → 报错；`*` → 恒合格且 `Verdict` 走"只报颜色不判定"模板；
其余按字面比对词表（`ParseExpected()` 用 `,` / `，` / `、` 三种分隔符切、Trim、去空）。
**比对不做模糊、不做近义**——词表外的词直接报错并列出 11 个可用词，这是把"永远匹配不上"的静默故障
改成配置期报错的关键一步。

## 结果投射（`ProjectResult`）

投的是三类标记，颜色约定与「颜色序列检查」完全一致（内核 `ColorMarks`）：**合格绿 / 不合格红**。

- 采样区轮廓按**折线**画（圆/椭圆按 32 段折线），不画成方框——否则看不出采的是哪一块；
- 中心右侧贴 `主色 占比%`；
- 结论行优先贴采样区上方，贴不下改贴下方，再越界就夹回画面内。

**投图是锦上添花**：整段包异常并吞掉，推图失败绝不把一次成功的检测判成失败（端口与判定照写）。
`DisplayViewIndex=0` 完全不推。矩形一轮是 4 条线 + 主色标签 + 结论行 = 6 条标注，这个数字被断言钉着。

## 内存所有权

- 上游 `HImage` **不接管**：只读（取通道数、批量取像素），不 Dispose、不改写；
- 画布上那张是**自己拷的一份**，视图负责释放；
- `TryBuildRoiPoints` / `TryReadPixels` 产出的区域与点集由内核侧负责回收，插件不持有跨轮的对象；
- 投射用的临时几何（折线点集）在 `ProjectResult` 内用完即弃，不进任何字段。

## 并发与线程

`[ParallelSafe]`：整条路径无共享可变状态（`_lastColor` 只在配置视图的试算里读写，运行期不碰）。
`BuildThresholds()` 每轮新建一个 `ColorThresholds`，把界面上 13 个值收成一个对象交给内核——
**内核只认这个对象**，不认插件的属性名，这是"阈值副本可各自覆盖"的实现方式。

## 断言宿主怎么进场的

`FlowCanvasChecks\ColorRegionCheck.cs` 装载插件程序集（含 WPF 视图与共用阈值编辑器）后，
用合成图（多条不同色线条的"5 芯图"）逐形状、逐判定分支跑真实 `RunAlgorithm`，断言端口值与 `Verdict` 文案；
`FlowCanvasChecks\ColorSyntheticChecks.cs` 再把"图像采集 → 颜色序列检查 → 区域颜色检查 × 2"
拼成一条完整流程验协作（两个区域步骤实例并存，验的就是"阈值各带一份副本"这件事真的成立）。
