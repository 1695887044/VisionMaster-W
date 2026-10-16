# ROI · 开发：内部结构与数据流

> 分类：常用工具 ｜ 来源：插件 Plugin.CreateRoi ｜ 读者：开发

本页讲**这个插件是怎么组织的、状态放在哪、一份框怎么从画布走到端口**。
改这个插件之前先读这页；要照它写一个新的"画布编辑类"插件，再读「宿主契约与移植清单」和「不变量与陷阱清单」。

## 一、一个类兼任两个身份

```
CreateRoiPlugin  :  VisionPluginBase        ← 运行期算子（RunAlgorithm / 端口 / 配置生命周期）
                    IPluginCustomViewProvider ← 设计期：交出配置视图
                    IDynamicOutputProvider    ← 编译前重建动态端口
                    IPluginConfigView         ← 视图的 Initialize / OnConfirm / OnCancel（基类已给默认实现）
                    ObservableObject          ← 它同时就是这个配置界面的 ViewModel
```

`CreateRoiView.xaml.cs` 里**只剩一行 code-behind**（`Loaded → (DataContext as CreateRoiPlugin)?.OnViewLoaded()`）。
这是刻意的：VM 就是插件本身，视图是纯绑定层，所以不需要额外的 ViewModel 类、也不需要 DI 容器。
代价是"插件类"这个对象活得和配置会话一样长，因此**所有非托管资源都得在它 `Dispose()` 里收尾**（见第六节）。

## 二、三份状态，各自只有一个真相源

| 状态 | 类型 | 角色 | 谁写它 | 谁读它 |
| --- | --- | --- | --- | --- |
| `RoiList` | `ObservableCollection<RoiItem>` | **存盘态 / 唯一真相**（`[StepConfig]`，整表进 `InputValues`） | 画布同步层、`Initialize` 播种 | `RunAlgorithm`、`RebuildDynamicOutputs`、列表 UI |
| `CanvasRois` | `ObservableCollection<DrawingObjectInfo>` | **显示态**（HALCON 句柄对象、活引用） | 控件右键新建/删除/清空、`Initialize` 播种 | `ImageEdit` 控件（`DrawObjectList`） |
| `SmearDraw` / `SmearErase` | `HRegion`（活对象） | 涂擦的**运行态** | 画笔（`ImageEdit.SmearDraw/SmearErase` 双向绑定回写） | `BuildMergedRegion`、掩膜预览 |
| `SmearDrawData` / `SmearEraseData` | `string`（`[StepConfig]`） | 涂擦的**存盘快照**（游程文本） | `SyncSmearData()` | `Initialize` → `RestoreSmear()` |

**为什么要分开 `RoiList` 和 `CanvasRois`**：`RoiItem` 是"一串数字 + 名字 + 形状"，天然可 JSON 序列化；
`DrawingObjectInfo` 是 HALCON 的 `HDrawingObject` 包装，进了 JSON 就是废字节。
所以**存盘只存配方，显示只持有活对象**，两者靠第三节的双向同步保持一致。

`RoiList` 的 setter 里做两件事：`OnPropertyChanged()` + **`RebuildDynamicOutputs()`**——
存盘态一变，端口表和快照必须同一时刻跟上，否则绑定界面看到的端口和实际输出的端口不一致。

## 三、双向同步的三条回路与各自的"刹车"

画布和列表互相联动，最容易写出的 bug 是**回环**（A 改 B、B 又改回 A，无限或丢事件）。三条回路各有各的刹车：

```
回路 ①  画布新建/删除/清空  →  CanvasRois.CollectionChanged  →  OnCanvasRoisChanged  →  RoiList 增删
         刹车：_seedingCanvas 标志。Initialize 播种画布时会触发 Add，但那不是用户动作，
               回写会把刚读出来的 RoiList 再叠一遍（框翻倍）。播种全程 _seedingCanvas=true，finally 复位。

回路 ②  拖句柄  →  info.HTuples（INPC）  →  OnCanvasRoiTuplesChanged  →  roi.Params 写回
         刹车：被删除的 ROI 已 PropertyChanged 退订，删除后的迟到回写自然落空（FirstOrDefault 找不到）。

回路 ③  数值框编辑  →  roi.ParamEdited  →  OnSelectedRoiParamEdited  →  info.HTuples 写画布
         刹车：RoiItem 只在"用户编辑"时抛 ParamEdited，不从"值变了"倒推用户动作。

选中联动（列表 ↔ 画布）
   SelectedRoi.set → CanvasActiveRoi（不同引用才写）
   CanvasActiveRoi.set → SelectedRoi（不同引用才写）
   两处都用 ReferenceEquals 守卫 + SetProperty 先判同值，否则两边互相回写；
   并且 SelectedRoi 的 setter 必须"先 SetProperty 判断，再退订旧的 ParamEdited"——
   无条件先退订会在同值短路路径上把订阅摘走且不补回，参数微调就此断链（历史 bug）。
```

**画布改名会连带端口**：`OnCanvasRoisChanged` 里新增项先过 `NextFreeName` 强制唯一名，再进 `RoiList`；
`Initialize` 播种时同样去重，一旦有改名就 `RebuildDynamicOutputs()`，让端口与快照跟着新名字走。

## 四、一次配置会话的时间线

```
双击步骤 → PluginConfigShell 调 GetConfigView(stepData)
  ├─ Initialize(stepData)                       ← 基类：StepData 记下 + ApplyConfigValues 灌值
  │    ├─ [StepConfig] 属性 ← InputValues（DisplayViewIndex / MaskInvert / RoiList / Smear*Data）
  │    ├─ 端口值灌入（已链接的端口跳过）
  │    └─ 本插件追加：_seedingCanvas=true → CanvasRois.Clear() → 去重播种 → finally false → RestoreSmear()
  ├─ new CreateRoiView { DataContext = this }   ← 绑定在这一刻自动建立，底图与句柄自己上屏
  └─ 视图 Loaded → OnViewLoaded()               ← 把 SrcImage 当前值回填成预览底图（试运行过的图）

用户编辑 …（回路 ①②③ + 涂擦 setter → SyncSmearData + ScheduleMaskPreview）

点确定 → OnConfirm(stepData)（基类默认实现）
  ├─ 未链接的输入端口值写回 InputValues；已链接的端口 RemoveInputValue（避免残留脏数据）
  └─ [StepConfig] 属性逐个 SnapshotConfigValue 后写入（RoiList 走 JSON 往返深拷贝，见第五节）
点取消 → OnCancel()（默认空实现：试运行读的是已保存的 InputValues，不落盘，无需还原）

关闭 → Dispose()：停防抖 → 释放预览图/合成图/涂擦区域 → foreach CanvasRois.Dispose() → base.Dispose()
```

## 五、运行期：一次 `RunAlgorithm` 干了什么

```
0. 基类 Execute 预置 Success=true、清 ErrorMessage；AutoDisposeRoundOutputs 在轮首回收上一轮输出端口的 HImage
   （透传防御：与某条上游链接当前值同引用的输出会被跳过，不误杀上游图像）
1. src = SrcImage.ActualValue；null 或未初始化 → Fail("输入图像为空或未初始化") → return（唯一的失败出口）
2. PreviewImage = src（配置界面下次打开能看到本轮的图）
3. _runtimeMaskedImg?.Dispose() = null      ← 发布到主窗口的合成图不走端口，自管回收
4. merged = BuildMergedRegion()             ← (ΣROI ∪ 涂抹) − 擦除；ROI 为空时返回 GenEmptyRegion 的空区域（防爆）
   MaskRegion.Value = merged                ← 所有权移交端口
5. visual = BuildMaskAndResult(src, merged, MaskInvert, out mask, out _)
   MaskImage.Value = mask                   ← region_to_bin(内255/外0，invert 时互换)
   _runtimeMaskedImg = visual               ← MultImage(src, mask, 1/255, 0)；三通道先 Compose3
   if (DisplayViewIndex > 0 && visual 有效) this.PublishPreview(visual, DisplayViewIndex)   ← 窗口号直传
6. foreach roi in RoiList：
     重名防御（HashSet 判重，只输出第一个）→ 端口存在性检查
     SetValuePort(OffsetRow_/OffsetCol_, 0d)          ← 先清零，任一失败分支下游读到 0 而不是残值
     region = BuildRegion(roi)   null → Warn("形状参数无效，跳过裁剪") → continue
     region = region.Difference(_smearErase)          ← 擦除作用于每个 ROI 的有效域
     area_center 的 D <= 0 → Warn("有效区域为空（被擦除殆尽），跳过裁剪") → continue
     smallest_rectangle1 → (r1, c1) = 裁剪原点
     reduce_domain(src, region) → new HImage(...) → SetImagePort("Domain_"+Name, ...)
                                                    返回 true 才把本地引用置 null（所有权已交端口）
     SetValuePort(OffsetRow_/OffsetCol_, r1/c1)
     crop_domain(reduced) → new HImage → port.Set(crop)；writtenCount++
     finally：region / crop / domain 中"没交出去的"就地释放（端口写入前抛异常时不泄漏）
7. RoiCount.Value = writtenCount            ← 成功数，不是配置数
```

**异常口径**：单个 ROI 的异常在循环内被 catch 成 Warn，**不阻断整体**；
只有"无图"走 `Fail`。基类 `Execute` 还会把未捕获异常统一转成 `Fail("{Type}: {Message}")`（`RethrowOnException` 默认 false）。

## 六、内存所有权表（谁 new、谁 dispose）

| 对象 | 创建处 | 释放责任 |
| --- | --- | --- |
| `MaskRegion` / `MaskImage` / 各 `Crop_` / `Domain_` 的值 | `RunAlgorithm` | **基类轮首自动回收**（`AutoDisposeRoundOutputs`），插件不写 `DisposeOldOutputs` |
| 主窗口合成图 `_runtimeMaskedImg` | `BuildMaskAndResult` 每轮 | 本插件：轮首 + `Dispose()`（它不走端口，基类管不到） |
| 掩膜预览 `_maskPreviewImage` | `RefreshMaskPreview` | 本插件：每次重算前先释放、`Dispose()` 兜底 |
| `SmearDraw` / `SmearErase` | 画笔回写的新实例 | 本插件 setter 释放被替换的旧实例；`Dispose()` 收尾（控件只读参与并集，**VM 是唯一所有者**） |
| `CanvasRois` 里的 `DrawingObjectInfo` | 控件/播种 | 正常路径控件 Unloaded 处置；`Dispose()` 里 foreach 兜底（`Dispose` 幂等，覆盖异常路径） |
| `SetImagePort` 返回 false 时的 `domain` | `new HImage(reduced)` | **调用方**就地释放——所以它必须返回 bool："到底有没有接管所有权" |
| `BuildMergedRegion` 中间量 | `union2` / `difference` | 每次合并立刻释放两个输入，只留结果 |

`new HImage(x)` 是**独立句柄**，之后释放 `x` 不影响它——第 5 步里 `reduced` 被 `crop_domain` 用完就 dispose，
而 `Domain_` 端口拿的那份照样有效。

## 七、几个"为什么这样实现"

- **发布合并掩膜合成图，而不是逐 ROI 发小图**：以前逐框发到同一窗口互相覆盖，只能看到最后一个。
  一帧发全景，配置者一眼看清所有框与涂擦修正；要小图走端口。
- **预览带 200ms 防抖**（`DispatcherTimer`）：掩膜重算是**全图像素运算**，跟着拖句柄每帧算就卡。
  防抖只在掩膜预览模式下启用（`ScheduleMaskPreview` 首行 `if (!IsMaskPreview) return;`）。
- **涂擦用游程编码而不是区域对象**：`gen_region_runs` 天然可序列化成 `行,起列,止列;` 三元组文本，
  比逐点轮廓小两个数量级；>4KB 再 GZip+base64 加 `gz:` 前缀（`CompressionLevel.Fastest`，图的是不卡保存）。
  解析失败 `catch` 成 `null` = "无涂擦"，**宽容降级而不是抛**——方案文件不该因为一块涂擦打不开。
- **`ParamEntries` 必须 `[JsonIgnore]`**：它是给 UI 用的派生集合（get-only），
  Newtonsoft 会尝试填充它、而 `RoiParamEntry` 没有无参构造 → 反序列化直接抛。
  存盘只认 `Name` / `ShapeType` / `Params` 三个字段。
- **`Offset` 拆成两个 `double` 端口而不是一个 `HTuple`**：下游（脚本/变量/计算）可以直接当标量连线，
  也保住端口的编译期类型检查，不引入 `object` 多态端口。
