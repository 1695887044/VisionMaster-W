# 胶路检测 · 端口与参数

> 分类：缺陷检测 ｜ 来源：插件 Plugin.BeadInspect

端口名一经发布就是契约（`FlowCanvasChecks` 里有两条把输入 5 口 / 输出 16 口的名字与类型逐个锁死），
所以这一页的**首列标识符就是源码里的名字**，可直接用于接线与脚本引用。

## 一、输入端口（5 个）

| 端口 | 类型 | 必填 | 接不上 / 不接会怎样 |
| --- | --- | --- | --- |
| `SrcImage` | `HImage` | 是 | 空图或未初始化 → 执行失败 `输入图像为空或未初始化`，这是本算子唯一的"无图"出口 |
| `RecipeName` | `string` | 否 | 不接或为空 → 用「设为默认」的那条配方；仍找不到 → 用配方库第一条；库空 → 失败并提示去配置界面新建并学习 |
| `AlignedImage` | `HImage` | 否 | 仅「匹配位姿」对齐模式使用（C# 属性名是 `AlignedImageIn`，因为输出侧同名口存在）。该模式没接 → 失败并点名让你接匹配插件或换模式 |
| `RoiRegion` | `HRegion` | 否 | 不接 = 整图检测；接了只是**缩小处理域提速**，输出坐标不因它改变 |
| `Transform` | `CalibrationTransform` | 否 | 只在「输出单位 = 毫米」时用到；不接则回退参数 `PixelSizeMm`，两者都没有 → 失败 |

## 二、输出端口（16 个）

| 端口 | 类型 | 内容 |
| --- | --- | --- |
| `IsOk` | `bool` | 判定结果（true = 合格）。过滤碎段后错误段数为 0 才算合格 |
| `NgReason` | `string` | NG 原因串（如 `缺胶 2 段、太细 1 段`）；OK 时为空串 |
| `AnnotatedImage` | `HImage` | 标注图：对齐图 + 参考路径（橙）+ 左右轮廓（绿）+ 错误段（红，线宽 2）+ 角上判定文字 |
| `ErrorCount` | `int` | 错误段总数（已按 `MinErrorLength` 过滤） |
| `ErrorTypes` | `HTuple` | 各错误段类型字符串数组（HALCON 四类字面量，见检测口径页） |
| `ErrorLengths` | `HTuple` | 各错误段长度（像素），与 `ErrorTypes` 逐项对齐 |
| `ErrorRows` | `HTuple` | 各错误段中心 Row，与 `ErrorTypes` 逐项对齐 |
| `ErrorCols` | `HTuple` | 各错误段中心 Col，与 `ErrorTypes` 逐项对齐 |
| `TotalErrorLength` | `double` | 错误段总长度，单位随 `UnitOutput`（像素或毫米） |
| `NoBeadCount` | `int` | 「缺胶」段数 |
| `TooThinCount` | `int` | 「太细」段数 |
| `TooThickCount` | `int` | 「太粗」段数 |
| `MispositionCount` | `int` | 「位置偏移」段数 |
| `ErrorSegments` | `HXLD` | 保留下来的错误段轮廓集合（可直接给下游或存图） |
| `BeadContours` | `HXLD` | 检出的左右胶路轮廓（正常是 2 个对象；只检出一侧时给 1 个） |
| `AlignedImage` | `HImage` | 对齐后的图，供下游复用；`OutputAlignedImage` 关闭时不写（该口保持空） |

**四个数组端口永远逐项对齐**：`ErrorTypes[i]` / `ErrorLengths[i]` / `ErrorRows[i]` / `ErrorCols[i]` 描述的是同一段，
`ErrorCount` 就是它们共同的长度。

## 三、参数（配置界面「模块参数」里可见的 20 项）

> 「可见」= 配置界面里有入口。除下表 20 项外，配置界面的「自动提取输入」折叠区还有一个
> **`ExtractTargetPoints`（抽稀目标点数）**：它是配置会话属性、**不是** `[StepConfig]`，
> 不落盘、不进 `.vms` 的 `InputValues`（见第六节）。

| 参数 | 默认 | 范围 / 夹紧 | 改了影响什么 |
| --- | --- | --- | --- |
| `AlignMode` | `PlanarDeformable`（界面显示「平面可变形（投影）」） | 三档：固定相机 / 匹配位姿 / 平面可变形 | 决定是否需要参考图、是否用内置平面模型对齐 |
| `Polarity` | `Dark`（「暗胶」） | `Dark` / `Light`，只能二选一 | 检亮胶还是暗胶；选反的典型表现是整条判「缺胶」 |
| `TargetWidth` | 15 | 赋值处夹到 **6 ~ 10000** | 目标胶宽（像素）。下限 6 是硬约束：低于它建模型直接报 HALCON #3716 |
| `WidthTolerance` | 8 | — | 胶宽容差（像素）：宽/细出这个幅度判「太粗 / 太细」。自动提取的路径偏差实测上界 8.39px，所以默认不是范例里的 7 |
| `PositionTolerance` | 30 | — | 位置容差（像素）：中心线偏离参考路径超过它判「位置偏移」。上线前应用 3~5 张良品重测 |
| `MinErrorLength` | 5 | — | 错误段长度下限（像素），短于此的碎段不计数也不进 `ErrorSegments`（滤小误报） |
| `MinScore` | 0.4 | 界面 0 ~ 1（赋值处同样夹到 0~1） | 平面匹配最低分数，低于它判对齐失败。**配置界面 ⑤ 卡「最低分数」** |
| `PlanarNumLevels` | 5 | 界面 1 ~ 10 整数（赋值处夹到 1~10） | 平面匹配金字塔层数。**配置界面 ⑤ 卡「金字塔层数」** |
| `FindAngleStartDeg` | -22.35 | 单位是**度** | 平面匹配起始角（只在调算法处换算成弧度） |
| `FindAngleExtentDeg` | 44.69 | 度 | 从起始角起的搜索范围 |
| `FindScaleRMin` / `FindScaleRMax` | 1.0 / 1.0 | 1 = 不允许缩放 | 行向缩放搜索区间；工件远近变化时设成如 0.9 ~ 1.1 |
| `FindScaleCMin` / `FindScaleCMax` | 1.0 / 1.0 | 同上 | 列向缩放搜索区间（各向异性时与行向分开设） |
| `UnitOutput` | `Pixel`（「像素」） | 像素 / 毫米 | 只影响 `TotalErrorLength` 的换算层，模型参数永远以像素计 |
| `PixelSizeMm` | 0（未填） | 界面 0 ~ 1000；> 0 才有效 | 毫米输出的兜底当量；有 `Transform` 时轮不到它。**配置界面 ④ 卡「像素当量(mm/px)」** |
| `DisplayViewIndex` | 1 | 0 ~ 9，0 = 不发布 | 标注图发布到主界面几号视图窗口 |
| `OutputAlignedImage` | true | — | 关闭后 `AlignedImage` 输出侧不再写图（对齐本身照做）。**配置界面 ④ 卡「输出对齐图」勾选框** |
| `RecipeLibraryJson` | `[]` | — | 整个配方库的序列化文本，由配置界面维护，不建议手改 |
| `DefaultRecipeName` | 空 | — | 「设为默认」写入；`RecipeName` 未连接/为空时使用 |

## 四、配方条目里存了什么（`RecipeLibraryJson` 内的每条）

| 字段 | 默认 | 说明 |
| --- | --- | --- |
| `Name` | `配方1` | 配方键，`RecipeName` 端口按它选条目 |
| `RefImagePath` | 空 | 参考图路径：平面模型的模板 + 坐标系基准（换路径后需重新学习） |
| `RefNoBeadImagePath` | 空 | 无胶参考图（自动提取的差分基准）；空 = 提取回退 black-hat |
| `RefPointsJson` | `[]` | 参考路径点列，形如 `[[row,col],…]`；坐标在矫正/对齐后的参考坐标系 |
| `RectifyQuadJson` | 空 | 矫正四点；空 = 跳过矫正。两种写法：纯四点数组，或 `{"src":[…],"dst":[…]}` |
| `TargetWidth` | 15 | 本配方的目标胶宽；填 0 = 改用插件级 `TargetWidth` |
| `WidthTolerance` | 8 | 本配方胶宽容差；0 = 回落插件级 |
| `PositionTolerance` | 30 | 本配方位置容差；0 = 回落插件级 |
| `Polarity` | `dark` | 本配方极性；空白 = 回落插件级 `Polarity` |
| `LearnedSignature` | 空 | 学习指纹（点列 + 有效参数）；空 = 未学习；与当前参数指纹不一致 = 「参数已变·需重学」 |

**回落口径**：配方值 > 0（极性非空白）时以配方为准，否则用插件级值。所以"每款产品单独调"填配方行，
"全部产品共用一套阈值"填 ④ 运行参数即可。

## 五、单位与坐标口径速查

```
所有模型参数与逐段长度：像素（TargetWidth / WidthTolerance / PositionTolerance / MinErrorLength / ErrorLengths）
只有 TotalErrorLength 会按 UnitOutput 换算成毫米
ErrorRows / ErrorCols / ErrorSegments 的坐标：对齐后的参考位姿坐标系（RoiRegion 不会改变它）
点列 RefPointsJson：矫正后参考图坐标系（设了矫正四点时）
```

## 六、不落盘的配置会话属性

| 属性 | 默认 | 范围 | 为什么不在 `.vms` 里 |
| --- | --- | --- | --- |
| `ExtractTargetPoints` | 40 | 10 ~ 500 的整数 | 它是「自动提取中心线」这一次操作的抽稀目标点数，属于**教学会话**参数而非产线判定参数：不标 `[StepConfig]`，`OnConfirm` 不写 `InputValues`，重开配置界面回到 40 |

配置界面把它放在「① 配方库 → 自动提取输入（可选）」折叠区最后一行，并常驻一句
**「仅本次配置会话生效，不落盘」**。想固定成别的值，目前只能每次打开配置界面时手填——
真要落盘属于契约变更（会动 `InputValues` 键数，需另开版本）。
