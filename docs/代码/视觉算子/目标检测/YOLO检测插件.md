# YOLO 目标检测插件（Plugin.Yolo）

> **这是本插件的唯一文档。** 原先分散的 YOLO 开发记录与配置窗口上游图显示修复记录已全部并入本文件。
>
> 本文件分两层读法：
> - **零基础 / 操作者** → 读第 0 ~ 3 章 + 3.4~3.6 节（原理直觉、界面说明、逐步操作、参数速查、FAQ、排障）
> - **开发者 / 维护者** → 重点读第 4 章（异常与修复记录）与第 5 章（附录：搭配算子、HALCON 算子速查、实战案例、源码索引、回归断言）

---

## 目录

- [第 0 章 这是什么](#第-0-章-这是什么)
- [第 1 章 设计思想](#第-1-章-设计思想)
- [第 2 章 技术特点](#第-2-章-技术特点)
- [第 3 章 使用方式](#第-3-章-使用方式)
- [第 4 章 异常与修复记录](#第-4-章-异常与修复记录)
- [第 5 章 附录](#第-5-章-附录)

---

# 第 0 章 这是什么

**YOLO 目标检测 = 用训练好的神经网络模型，把图里的目标"框出来 + 说出它是什么 + 有多大把握"。**

> 生活比喻：Blob 是"按亮度找异常"，模板匹配是"按长相找目标"，而 YOLO 是**"按学过的经验找目标"**——你先给它看成千上万张标注图（这是训练，在插件外做），它学会了"什么是划痕、什么是缺陷"，之后你只要把新图喂进去，它就告诉你"这里有 2 个划痕，把握 90%"。

它在流程里的位置（归在 **「图像处理」** 分组）：

```
[图像采集] → [创建 ROI] → [图像预处理] → [YOLO 目标检测] → [C#脚本] / [CSV记录] / [结果上报]
                                        ↓
  Count / ClassIds / ClassNames / Confidences / Boxes / BoxesText / BestCrop
```

**一句话**：传统算子（Blob/模板/卡尺）靠**规则**找目标，YOLO 靠**学过的经验**找目标——适合"说不清规则但人一眼就认得"的场景（缺陷种类多、形态多变、背景复杂）。

## 0.1 三条必须先知道的边界

1. **模型是客户资产，不内置**。与 OCR 内置 `.omc` 不同，YOLO 模型由客户自己训练导出，插件只负责"用"它。路径找不到时**明确报错**，不静默退回任何默认模型。
2. **只做目标检测**。分割 / 姿态 / OBB / 分类模型的输出格式与检测完全不同，**按检测解读会得到错误结果却不报错**，所以 `task` 不是 `detect` 的模型会被明确拒绝。
3. **不做 OK/NG 判定**。只输出"检测到了什么"，判定交给下游的比较/逻辑节点。沿用 `Plugin.ColorCheck` / `Plugin.Ocr` 立的规矩。

> "没检测到目标"同理算**正常**（可能本来就是良品），只写 Info 留痕——判 Failed 会把良率统计搞脏。

---

# 第 1 章 设计思想

## 1.1 核心原理：六步推理链路

```
① 读原图尺寸           get_image_size
② 算 letterbox 参数     scale = min(dstW/srcW, dstH/srcH)
③ 构造 letterbox 图     等比缩放 + 居中贴到 114 灰画布
④ 转 NCHW 张量          3×640×640 float，/255 归一化
⑤ ONNX Runtime 推理     _session.Run(...)
⑥ 后处理               布局嗅探 → 解码 → 坐标反算 → NMS → 过滤 → 截断
```

**顺序很重要**：解码 → **坐标反算** → NMS → 类别过滤 → 截断。NMS 必须在**原图坐标**上做。

## 1.2 为什么必须 letterbox，不能直接拉伸

> 若我们直接把图拉成 640×640，目标的长宽比就被改变了——模型见过的形状和现在喂进去的不一样，**精度会静默下降**：不报错、不崩溃，只是漏检变多、框变歪。

## 1.3 letterbox 的几何与坐标反算（最易错，务必看懂）

### 缩放与补边

```
scale        = min(dstW / srcW, dstH / srcH)     ← 取小者，等比，保证不拉伸
scaledW/H    = round(srcW/H × scale)
PadX / PadY  = (dstW − scaledW) / 2 , (dstH − scaledH) / 2    ← 整数整除，居中
PadGrayValue = 114                                ← YOLO 训练时的默认填充值
```

> ⚠️ 填充值必须是 **114** 而不是 0：黑边会让模型把边界当成强边缘，**贴边目标的框会系统性偏移**。

### 坐标反算（模型输出 → 原图坐标）

```
原图坐标 = (letterbox坐标 − 补边) ÷ 缩放系数
然后夹进 [0, 宽−1] × [0, 高−1]
```

> 夹取是必要的：模型可能给出略微越界的框（尤其贴边目标），不夹会导致下游裁剪报错。

**算例（断言实证）**：
| 输入 | letterbox | 结果 |
|---|---|---|
| 宽图 200×100 → 100×100 | `scale=0.5, scaled=100×50, pad=(0,25)` | letterbox 框 `(40,40,60,60)` → 原图 `(80,30,120,70)` |
| 高图 100×200 → 100×100 | `scale=0.5, scaled=50×100, pad=(25,0)` | — |

## 1.4 为什么用 ONNX Runtime，而不用 HALCON 的 DL 算子

实测结论（很重要，避免后人重复踩坑）：

| 实测项 | 结果 |
|---|---|
| HALCON `read_dl_model` 读 `.hdl` | ✅ 成功 |
| HALCON `apply_dl_model` 推理 | ✅ 成功 |
| HALCON 读**自己导出的 .onnx** | ❌ 失败 `#7801` |
| HALCON 读**真实 YOLO ONNX** | ❌ 失败 `#7801` |
| 错误码身份核对 | `#7801 = H_ERR_DL_READ_ONNX`，**不是** `H_ERR_DL_ONNX_LOADER`（=7804）→ 排除"缺 protobuf 组件"，确认是**图不支持**（能力边界） |
| ONNX Runtime CPU 版体积 | 16.36 MB |

> "缺 protobuf 组件"这个假设**先提出、又被实测推翻**：`libprotobuf.dll` 确实躺在不在搜索路径的 `thirdparty\openvino\` 下，但把它加进 PATH 后依旧失败，且错误码是 7801 而非 7804。**把这条写进记录，是为了避免后人再走一遍这条弯路。**

## 1.5 会话生命周期：进程级共享 + 引用计数（与 OCR 的关键差别）

| | OCR / 码读取 | YOLO |
|---|---|---|
| 会话放哪 | 缓存在**插件实例字段**里 | **进程级共享**（静态字典）+ 引用计数 |
| 为什么 | 模型小、纯 CPU，实例每会话一份没问题 | 插件实例是"每次编译一份"——同模型被 5 个流程引用会加载 5 份：CPU 版几百 MB；将来接 GPU **显存按会话占**，5 份直接吃光显卡 |

> **为什么用引用计数而不是简单单例**：流程会被删除、图纸会被重新编译。会话不能"第一次加载就活到进程结束"——那样反复打开不同图纸会持续堆积。用引用计数：**谁 Acquire 谁 Release，归零才真正释放**。
>
> **为什么还要判"模型路径变了没"**：不能每次调用都 Acquire（会持续加计数、永不释放）；也不能只在初始化 Acquire 一次（用户在配置里换了模型后跑的还是旧模型——**"改了配置不生效"是最难查的一类问题**）。所以：路径没变就复用，变了就先放旧的再拿新的。

---

# 第 2 章 技术特点

## 2.1 元数据

| 特性 | 值 |
|---|---|
| `Name` | YOLO 目标检测 |
| `GroupName` | 图像处理 |
| `Description` | 用 ONNX 格式的 YOLO 检测模型找出目标（类别 + 置信度 + 位置） |
| `ShortName` | 图标（FontAwesome `f03d`） |

## 2.2 端口完整清单（**全部固定端口，无动态端口**）

> 本插件**没有**实现 `IDynamicOutputProvider`，所以**没有"按检出数量动态生成端口"**的机制——结果一律以**数组 + 扁平数组 + 文本**形式从固定端口输出。

| 端口 | 方向 | 类型 | 说明 |
|---|---|---|---|
| `Image` | 入 | HImage | 待检测图像（`IsRequired = false`） |
| `Count` | 出 | int | 检测到的目标数量。**0 = 本帧没检测到（属正常工况）** |
| `ClassIds` | 出 | `int[]` | 各类别的索引 |
| `ClassNames` | 出 | `string[]` | 各类别的名字（模型元数据没带时是 `class_N` 占位） |
| `Confidences` | 出 | `double[]` | 各目标的置信度 0~1（与其它数组同序） |
| `Boxes` | 出 | `double[]` | **扁平数组**，每 4 个一组：`x1,y1,x2,y2`（**原图像素坐标**） |
| `BoxesText` | 出 | string | 所有目标的文本摘要（分号分隔） |
| `BestCrop` | 出 | HImage | **置信度最高目标的裁剪图**（供下游精测） |

另有基类隐式端口 `Success`、`ErrorMessage`。

> **`BoxesText` 为什么在数组之外还要给**：画布上数组端口只能整体连给 `object` 输入口，而记录 / 上传 / 比较这些既有下游节点都是按**标量**写的——没有这个它们接不上。
>
> **`BestCrop` 为什么要**：下游的裁剪算子是**参数式**的（框在属性面板里填），没有端口能接收外来框——也就是说 YOLO 算出的框没法直接驱动下游裁剪。这里把"最佳目标"直接裁好输出，「YOLO 定位 → Halcon 精测」这条接力就不用写脚本了。
>
> ⚠️ **没有标注图输出端口**（不做结果投射）。

## 2.3 配置项（7 个）

| 配置 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `ModelPath` | string | 空 | ONNX 模型文件路径。支持绝对路径，也支持相对于宿主程序目录的路径 |
| `InputSize` | int | `0` | letterbox 目标边长。**0 = 用模型元数据里的静态输入尺寸**（推荐）。只有动态形状模型才需手填（常见 640） |
| `ConfidenceThreshold` | double | `0.25` | 置信度阈值（Ultralytics 默认值） |
| `NmsIoU` | double | `0.45` | NMS 的 IoU 阈值：同类框重叠超过它就只保留置信度高的 |
| `MaxDetections` | int | `100` | 最多保留多少目标（按置信度截断）。0 = 不限 |
| `ClassFilterText` | string | 空 | 只保留这些类别（类别索引，逗号分隔，如 `"0,2"`）。留空 = 不过滤 |
| `CropMargin` | int | `0` | 「最佳目标裁剪图」向外扩张的边距（像素）。0 = 按检测框原样裁剪 |

### ⚠️ 几项"没有 / 与预期不同"

1. **没有"类别名文件"配置项**——类别名来自 ONNX **模型元数据**的 `names` 字段；读不到就用 `class_N` 占位，**不编造**。
2. **没有 GPU / CUDA 配置项**——第一期只带 CPU 版。会话选项只固定了两个线程数（都为 1）：
   > 单线程执行的确定性问题：工业现场更在意"同一张图给同样的结果"，而不是榨干 CPU。
3. **没有"会话管理"配置项**——进程级共享 + 引用计数是硬编码策略。
4. **没有首次推理预热**代码。
5. **没有"设备"开关**——CPU-only 阶段的设备开关是个什么都不做的旋钮。

## 2.4 HALCON 算子清单（本插件实际调用）

| 算子 | 用途 |
|---|---|
| `get_image_size` | 取原图宽高 |
| `count_channels` | 判通道数（1 或 3，其它报错） |
| `compose3` | 单通道补 3 通道 / 灰画布补 3 通道 |
| `zoom_image_size` | 等比缩放到 letterbox 内尺寸（`"bilinear"`） |
| `gen_image_const` | 造 byte 尺寸载体 |
| `gen_image_proto` | 造 **114 灰**画布 |
| `gen_rectangle1` | 居中目标矩形（参数 `Row1,Col1,Row2,Col2`） |
| `change_domain` | **值不变、域变**（把缩放图搬到居中位置） |
| `overpaint_gray` | 覆写到画布 |
| `access_channel` | 取单通道平面 |
| `get_image_pointer1` | 拿底层缓冲指针（转 NCHW 用） |
| `crop_part` | 最佳目标裁剪 |

**未使用**：`rgb1_to_gray`、`disp_obj`、`dump_window_image`、`tile_images_offset`、`read_dl_model` / `apply_dl_model`。

## 2.5 性能与独特实现

| 手段 / 设计 | 做法 | 收益 |
|---|---|---|
| **进程级会话 + 引用计数** | 静态字典 + 锁，归零才释放 | ⭐⭐⭐ 同模型 5 个流程只加载 1 份 |
| **letterbox 补 114 灰** | 不用 `tile_images_offset`（固定填 0） | ⭐⭐⭐ 贴边目标框不偏移 |
| **指针取像素转 NCHW** | `get_image_pointer1` + `Marshal.Copy` | ⭐⭐⭐ 640×640×3 = 122 万像素，逐个 `get_grayval` 会远超推理本身 |
| **布局嗅探 + task 守卫** | 不猜布局；非 detect 明确拒绝 | ⭐⭐⭐ 杜绝"结果全错但不报错" |
| **类别级 NMS** | 按 `ClassId` 分组分别做 | ⭐⭐ "人骑车"不会被互相压制 |
| **坐标反算后夹取** | 夹进 `[0,宽−1]×[0,高−1]` | ⭐⭐ 下游裁剪不报错 |
| **裁剪越界自夹 + 留痕** | 先夹位置再算尺寸 | ⭐⭐ 不会得到"看着正常、实际错位"的图 |
| **ORT 日志压到 ERROR** | 避免内部噪声淹没真实日志 | ⭐⭐ 工业软件里日志是排障唯一线索 |
| **`BoxesText` / `BestCrop`** | 兼顾标量下游与精测接力 | ⭐⭐ 不用写脚本 |

---

# 第 3 章 使用方式

## 3.1 安装与启用

1. **需要 ONNX 模型**（客户自己训练导出）。插件不内置任何模型。
2. **依赖投递**：ONNX Runtime 原生库（CPU 版 16.36 MB）随插件投递到宿主目录。
   > ⚠️ `System.Numerics.Tensors` 必须随插件带——它**不在** .NET 9 共享框架里，漏带 = 运行期 `FileNotFoundException`。
3. **必须 64 位进程**：原生库是 win-x64 的，32 位进程加载会抛难以理解的 `DllNotFoundException`（插件已提前判掉并给出可照做的提示）。
4. 重开软件，工具箱里出现「YOLO 目标检测」（**图像处理** 分组）。

## 3.2 配置界面总览（两列，980×620）

```
┌──────────────────────────┬────────────────────────────┐
│ 左：参数（340px 可滚动）     │ 右：只读图像显示              │
│  ① 图像输入（🔗链接上游）    │  ImageReadOnly（不能框选）    │
│  ② 检测模型（.onnx）        │  ← 本节点不吃 ROI，检测整幅图   │
│     路径 + 「浏览」          │                            │
│     模型摘要（形状/task/类名）│                            │
│  ③ 检测参数                 │                            │
│     置信度 / NMS / 最多目标  │                            │
│  ④ 高级参数（Expander）     │                            │
│     输入尺寸 / 类别过滤 / 边距│                            │
│  ⑤ 试算（按钮 + 结果）       │                            │
└──────────────────────────┴────────────────────────────┘
```

> 界面上刻意显示**两处排障信息**（不是装饰）：
> · **模型摘要**（输入输出形状 / task / 类别名）——模型选错时这里是唯一线索；
> · **试算结果里的 letterbox 参数**——框整体偏移时先看它。

## 3.3 第一次检测（5 步）

**第 1 步｜选模型**：填路径或点「浏览」选 `.onnx`。摘要区立刻显示输入/输出形状、task、类别名。
> ⚠️ 若 `task` 不是 `detect`（比如是 `segment`/`pose`），会**明确拒绝**并给出重新导出的命令。

**第 2 步｜接图**：「图像输入」点 🔗 链接上游（或画布拖线）。点「执行」试运行 → 上游图自动显示到右侧。

**第 3 步｜调参**：置信度阈值 / NMS 重叠阈值 / 最多目标数（高级里还有输入尺寸 / 类别过滤 / 裁剪边距）。

**第 4 步｜试算**：点「试算一下」，结果框显示：
```
检测到 2 个目标（耗时 63 ms）
letterbox: 缩放 0.500　补边 (0,25)
  划伤  0.90  [80,30,120,70]
  缺陷  0.70  [80,30,120,70]
```

**第 5 步｜接下游**：`Count`/`ClassNames` 接比较节点做 OK/NG；`BoxesText` 接记录/上传；`BestCrop` 接 Halcon 精测。

## 3.4 参数调节速查表

| 现象 | 调哪里 |
|---|---|
| 漏检（该检的没检到） | 调低 `ConfidenceThreshold`（默认 0.25） |
| 误检多 | 调高 `ConfidenceThreshold` |
| 同一个目标出一堆框 | 调低 `NmsIoU`（默认 0.45 → 0.3 左右压制更强） |
| 不同类别的目标被互相压制 | 已按类别分别做 NMS，正常不会发生 |
| 框整体偏移 | **先看试算里的 letterbox 缩放与补边**；确认没用黑边填充 |
| 贴边目标框偏 | 确认 letterbox 填充是 114 灰（代码已固定） |
| 动态形状模型报错"无法确定输入尺寸" | 手填 `InputSize`（常见 640）——**留 0 会明确报错，不猜** |
| 只想看某几个类别 | 填 `ClassFilterText`（如 `"0,2"`），非法值会明确报错不静默 |
| 类别名显示 `class_N` | 模型元数据没带 `names`，属正常（**不编造名字**） |
| `BestCrop` 裁出来不对 | 看日志是否有"检测框超出图像范围，已夹取"的留痕 |

## 3.5 FAQ

**Q1** 支持哪些 YOLO 版本？
**YOLOv8 / v11 检测模型**（输出布局 `[1, 4+C, N]`）。**YOLOv5 明确不支持**（输出 `[1, N, 5+C]` 带 objectness，会被拒绝并提示重新导出）。

**Q2** 支持分割 / 姿态模型吗？
**不支持**，且会明确拒绝（`task` = `segment`/`pose`/`obb`/`classify` 都拒绝）。拒绝文案里带中文任务名与导出命令：`yolo export model=你的模型.pt format=onnx`。

**Q3** 为什么"没检测到目标"不算失败？
可能本来就是良品。判 Failed 会把良率统计搞脏。只写 Info 留痕，判定交下游。

**Q4** 能不能用 GPU？
第一期只带 CPU 版，**没有设备开关**（CPU-only 阶段的设备开关是个什么都不做的旋钮）。将来接 GPU = 换 ExecutionProvider + 多带原生库，**不改端口契约**。

**Q5** 为什么没有"画了框的图"输出？
**不做结果投射**。结果通过端口（数组 / 文本 / `BestCrop`）输出，判定与展示交下游。

**Q6** `Boxes` 是什么格式？
**扁平数组**，每 4 个一组 `x1,y1,x2,y2`，**原图像素坐标**。

**Q7** 换模型后要重开吗？
不用。会话按**模型路径**判变——路径变了自动先放旧会话再拿新的。

## 3.6 现场排障

| 报错 / 现象 | 排查 |
|---|---|
| "模型文件不存在" | 路径填错或文件没部署 |
| "无法确定模型输入尺寸" | 动态形状模型，手填 `InputSize`（常见 640） |
| 输出形状相关报错 | 布局不认识（非 v8/v11 检测）。报错会带上模型路径与实际输出形状，可直接贴回来定位 |
| `task` 非 detect 被拒 | 用 `yolo export ... format=onnx` 重新导出检测模型 |
| "图像通道数为 N，只支持 1 或 3 通道" | 上游转成灰度或 RGB |
| 类别索引非法 | `ClassFilterText` 填了非数字（会明确报错，不静默） |
| 32 位进程加载失败 | 必须 64 位进程（插件已提前判掉） |
| `FileNotFoundException`（Tensors） | `System.Numerics.Tensors` 没随插件带 |
| 日志被 ORT 内部消息淹没 | 已压到 ERROR 级别 |
| 一个都没检测到 | 看 Info 日志（含阈值与模型名），先自查这两项 |

---

# 第 4 章 异常与修复记录

> 本章记录**真实发生过的问题**（来自代码注释与开发文档）。每条给：现象 → 原因 → 修法。

## 4.1 缺陷修复总览

| 级别 | 问题 | 一句话 |
|---|---|---|
| P0 | HALCON 读不了真实 YOLO ONNX（`#7801`） | 改用 ONNX Runtime |
| P0 | `overpaint_gray` 通道数不一致（`#3122`） | 画布必须**无条件**补成 3 通道 |
| P1 | 形状校验拦不住分割模型 | `[1,116,8400]` 与检测模型形状无法区分 → 靠 `task` 兜底 |
| P1 | ORT 日志刷屏淹没真实日志 | 压到 ERROR |
| P1 | 元数据 `names` 不是合法 JSON | Python 字典字面量，手动切 |
| P2 | `tile_images_offset` 固定填 0（黑边） | 改用 `change_domain + overpaint_gray` 填 114 灰 |
| P2 | 输入项未显式释放 | `NamedOnnxValue` 内部持有 `OrtValue`（原生资源） |
| P2 | NuGet V2 源 502 | 走 `DLL\` 本地引用 |

## 4.2 P0/P1 级详解

### ① HALCON 读不了真实 YOLO ONNX（`#7801`）

**现象**：`read_dl_model` 读 `.hdl` 成功、读自己导出的 `.onnx` 失败、读真实 YOLO ONNX 也失败。

**原因**：错误码 `#7801 = H_ERR_DL_READ_ONNX`（图不支持），**不是** `#7804 H_ERR_DL_ONNX_LOADER`（缺组件）。

**修法**：改用 **ONNX Runtime**（CPU 版 16.36 MB）。

> ⚠️ 排除弯路："缺 protobuf 组件"的假设已被实测推翻——加 `libprotobuf.dll` 进 PATH 后依旧失败，且错误码是 7801 而非 7804。

### ② `overpaint_gray` 通道数不一致（`#3122`）

**现象**：`HALCON #3122 Number of channels in the input parameters are different`。

**原因（第一次还修错了）**：原以为"原图是 3 通道才需要把画布补成 3 通道"——**错在判断依据**。上面已把单通道输入 `compose3` 成 3 通道，所以**送到 overpaint 的源图恒为 3 通道**，画布就必须**无条件**补成 3 通道。只按"原图通道数"判断时，**单通道输入（工业相机最常见的灰度图）必然踩这个错**。

### ③ 形状校验拦不住分割模型

**现象**：YOLOv8-seg 的输出是 `[1, 116, 8400]`（116 = 4 + 80 类 + 32 个 mask 系数）。在形状上它与检测模型**无法区分**（都是"通道在前、通道数远小于锚点数"）。形状校验会把它当成 112 类的检测模型放行，然后**输出一堆垃圾框而不报错**——正是本插件最想避免的那类静默错误。

**修法**：加 **`task` 元数据守卫**。元数据缺失时**放行**而不是拒绝（老模型或第三方导出可能没写 task，因为"没写"而拒绝会让本来可用的模型用不了）。

### ④ ORT 日志刷屏

**现象**：加载一个 60 MB 的老模型会刷出十几条 `Initializer xxx appears in graph inputs...`——那是**模型导出质量**的提醒，与用法无关，却把界面上的真实日志彻底淹没。

**修法**：`LogSeverityLevel = ORT_LOGGING_LEVEL_ERROR`。工业软件里日志是排障的唯一线索，不能被噪声占据。

### ⑤ 元数据 `names` 不是合法 JSON

**现象**：按 JSON 解析类别名会抛异常，然后表现为"读不到类别名"，让人误以为模型元数据缺失。

**原因**：实测该字段是 **Python 风格的字典字面量**，形如 `{0: 'person', 1: 'bicycle'}`——**键没有引号**，不是合法 JSON。

**修法**：按 `{` `}` 与 `,` 手动切。

## 4.3 代码级踩坑汇编

| # | 现象 | 原因 | 正确做法 |
|---|---|---|---|
| 1 | `#7801` 读不了 ONNX | HALCON 图支持能力边界 | 用 ONNX Runtime |
| 2 | `#3122` 通道数不一致 | 画布按"原图通道数"判断补 3 通道 | 画布**无条件**补 3 通道 |
| 3 | 黑边导致贴边框偏移 | `tile_images_offset` 固定填 0 | `change_domain + overpaint_gray` 填 **114** |
| 4 | 分割模型静默输出垃圾 | 形状无法区分 | `task` 守卫 |
| 5 | 日志被淹没 | ORT 默认 WARNING 刷内部消息 | 压到 ERROR |
| 6 | 类别名读不到 | `names` 是 Python 字面量不是 JSON | 手动切 |
| 7 | 原生资源延迟释放 | `NamedOnnxValue` 静态类型不是 `IDisposable` | `as IDisposable` 后 Dispose |
| 8 | 会话重复加载 / 改配置不生效 | 只 Acquire 一次 或 每次都 Acquire | 判"路径变了没"：变了先放旧的再拿新的 |
| 9 | 32 位进程 `DllNotFoundException` | 原生库是 win-x64 | 提前判 `Environment.Is64BitProcess` |
| 10 | 归一化漏除 255 | 模型照样跑、不报错，只是精度静默劣化 | 必须在代码里写清并验证 |

## 4.4 HALCON API 踩坑

| # | 现象 | 原因 | 正确做法 |
|---|---|---|---|
| 1 | `#7801` | HALCON 不支持该 ONNX 图 | 换 ONNX Runtime |
| 2 | `#3122` | `overpaint_gray` 要求源图与目标图通道数一致 | 两边都保证 3 通道 |
| 3 | 贴边框偏移 | 填充值用 0（黑） | 用 **114** |
| 4 | `crop_part` 越界 | **越界不报错**，返回垃圾数据（只有 Row 为负才抛 `#1301`） | 自己先夹取：先夹位置再算尺寸 |
| 5 | 单通道输入报错 | 模型要 3 通道 | `compose3` 补成 3 通道 |
| 6 | 转 NCHW 极慢 | 逐个 `get_grayval` | `get_image_pointer1` + `Marshal.Copy` |

## 4.5 已知边界与待办

- ⚠️ **解码逻辑尚未用真实 v8/v11 模型验证**（最需要知道的一条）。受控张量只能证明公式自洽，不能证明对 v8 语义的理解正确——等真实模型后必须补验（框是否落在目标上、类别是否对得上、数量是否合理）。
- YOLOv5 布局不支持（明确拒绝并提示重新导出）。
- 分割/姿态依赖 `task` 元数据兜底；**元数据缺失 task 时拦不住**（已知风险）。
- 只带 CPU 版（接 GPU = 换 ExecutionProvider + 多带原生库，不改端口契约）。
- `System.Numerics.Tensors` 必须随插件带（不在 .NET 9 共享框架里）。
- 类别名依赖模型元数据，读不到用 `class_N`，**不编造**。
- 动态形状模型必须手填 `InputSize`（留 0 明确报错，不猜）。
- **不做判定、不做结果投射**（不给"画了框的图"）。
- **原生库"在真实宿主进程里加载"尚未端到端验证**（文件到位已验，VisionMaster 真启动后加载未验）。
- letterbox 与训练预处理一致性无运行时校验（客户若用 `rect` 拉伸导出，精度会**静默劣化**）。
- `BestCrop` 只给置信度最高的一个（多目标裁剪需下游自行处理）。
- NuGet V2 源 502 未处理（走 `DLL\` 本地引用绕开；将来任何新增 NuGet 包都会撞）。
- 项目含 16 MB 二进制。
- 跨插件边界：Yolo 试算依赖已选模型；端口订阅不退订；跨线程 `PropertyChanged` 依赖 WPF 封送。

---

# 第 5 章 附录

## 5.1 附录 A：搭配使用的算子清单

★ = 与 YOLO 直接相关。

| 工程目录 | 中文名 | 分组 | 怎么接 | 一句话用途 |
|---|---|---|---|---|
| Plugin.ImageAcquisition ★ | 图像采集 | 常用工具 | 上游取图 | 取图 |
| Plugin.CreateRoi ★ | ROI | 常用工具 | 裁出区域 → `Image` | 缩小检测范围省算力 |
| Plugin.PreProcessing ★ | 图像预处理 | 图像处理 | 洗图 → `Image` | 干净图识别率更高 |
| Plugin.Matching ★ | 模板匹配 | 定位 | 归一化图 → `Image` | 先定位再检测 |
| **Plugin.Yolo** | **YOLO 目标检测** | **图像处理** | — | 用模型找目标 |
| Plugin.BlobDetect ★ | Blob 缺陷检测 | 缺陷检测 | 并列/对比：规则法 vs 学习法 | 规则明确的用 Blob |
| Plugin.CaliperMeasure ★ | 卡尺测量 | 测量 | 接 `BestCrop` 精测 | 「YOLO 定位 → 卡尺精测」 |
| Plugin.CSharpScript ★ | C#脚本 | 逻辑控制 | 接 `Count` / `ClassNames` | 判 OK/NG、分支 |
| Plugin.DataRecord ★ | CSV记录 | 数据处理 | 接 `BoxesText` | 追溯存档 |
| Plugin.ResultUpload ★ | 结果上报 | 数据处理 | 接 `BoxesText` / `Count` | 上报 MES |

### 典型接线

```
① 基本检测
采集.Image ──▶ YOLO.Image ──▶ BoxesText ──▶ { CSV记录 / 结果上报 }

② YOLO 定位 → 传统算子精测（最有价值的接力）
采集.Image ──▶ YOLO.Image ──▶ YOLO.BestCrop ──▶ 卡尺测量.SrcImage
                          └─▶ Count ──▶ C#脚本（判 OK/NG）

③ 先定位再检测（目标位置不固定）
模板匹配.AlignedImage ──▶ YOLO.Image
```

> **位置原则**：YOLO 是"检测"节点，**判定交下游**。它的独特价值是 `BestCrop`——把检测框直接裁好交给传统算子精测，**不用写脚本**。

## 5.2 附录 B：HALCON 算子速查

| 算子 | 用途 |
|---|---|
| `get_image_size` | 取原图宽高 |
| `count_channels` | 判通道数（1 或 3） |
| `compose3` | 单通道补 3 通道 / 灰画布补 3 通道 |
| `zoom_image_size` | 等比缩放到 letterbox 内尺寸 |
| `gen_image_const` | 造 byte 尺寸载体 |
| `gen_image_proto` | 造 **114 灰**画布 |
| `gen_rectangle1` | 居中目标矩形（`Row1,Col1,Row2,Col2`） |
| `change_domain` | 值不变、域变（搬到居中位置） |
| `overpaint_gray` | 覆写到画布（**两边都要 3 通道**） |
| `access_channel` | 取单通道平面 |
| `get_image_pointer1` | 拿底层缓冲指针（转 NCHW） |
| `crop_part` | 最佳目标裁剪（**越界不报错，自己夹**） |

## 5.3 附录 C：实战案例

### 案例 1｜letterbox：为什么填 114 灰而不是黑

```hdevelop
* ✗ 错：tile_images_offset 的背景填充值是固定的（0 = 黑）
*      黑边会让模型把边界当成强边缘，贴边目标的框会系统性偏移
*
* ✓ 对：先造 114 灰画布，再把缩放图的【定义域】换成目标位置的矩形
*      （change_domain：值不变、域变），最后把值覆写到画布上
gen_image_const (SizeCarrier, 'byte', DstWidth, DstHeight)
gen_image_proto (SizeCarrier, GrayCanvas, 114)          * ← 114
compose3 (GrayCanvas, GrayCanvas, GrayCanvas, CanvasImage)   * 无条件 3 通道
gen_rectangle1 (TargetRect, PadY, PadX,
                PadY + ScaledHeight - 1, PadX + ScaledWidth - 1)
change_domain (Scaled, TargetRect, Positioned)
overpaint_gray (CanvasImage, Positioned)
```

> ⚠️ `overpaint_gray` 要求源图与目标图**通道数一致**，否则抛 `#3122`。因为源图已 `compose3` 成 3 通道，**画布必须无条件补 3 通道**（不能按"原图通道数"判断，否则灰度图必踩）。

### 案例 2｜letterbox 几何与坐标反算（算例）

```
输入 200×100（宽图）→ 目标 100×100：
  scale       = min(100/200, 100/100) = 0.5
  scaledW/H   = 100 × 50
  PadX / PadY = (100−100)/2 , (100−50)/2 = (0, 25)

模型给出 letterbox 框 (40,40,60,60) → 原图：
  x1 = (40 − 0)  / 0.5 = 80
  y1 = (40 − 25) / 0.5 = 30
  x2 = (60 − 0)  / 0.5 = 120
  y2 = (60 − 25) / 0.5 = 70
  → 原图框 (80, 30, 120, 70)

最后夹进 [0, 宽−1] × [0, 高−1]（模型可能给出略微越界的框）
```

### 案例 3｜转 NCHW：用指针，不要逐个取

```hdevelop
* 640×640×3 = 122 万个像素，逐个 get_grayval 的调用开销会远超推理本身
* 用 GetImagePointer1 直接拿底层缓冲，一次 Marshal.Copy 拷完
access_channel (Image3Channel, Plane, c)          * c = 1,2,3
get_image_pointer1 (Plane, Pointer, _, W, H)
* Marshal.Copy(pointer, bytes, 0, pixelCount)
* result[offset + i] = bytes[i] / 255f      ← 归一化到 0~1（YOLO 训练时的做法）
```

> ⚠️ 归一化 `/255` 少除或多除，模型照样能跑、也不报错，只是**精度静默劣化**——必须在代码里写清并验证。

### 案例 4｜布局嗅探：v5 与 v8 维度恰好相反

```
YOLOv5 输出：[1, N, 5+C]（带 objectness）
YOLOv8/v11 ：[1, 4+C, N]（不带）

⚠️ 两者维度顺序恰好相反：把 v8 的张量按 v5 解读，程序不会报任何错，
   只会输出一堆乱框。

判据：哪个维度小 —— 通道数（几十~几百）必然远小于锚点数（几千~几万）
  channels   = min(a, b)
  transposed = (a < b)      * 通道在前 = v8 风格
  classCount = channels - 4
```

### 案例 5｜解码：不要再多做 sigmoid / exp / 锚点换算

```
YOLOv8 的导出图里已包含解码（含 DFL），输出即"输入尺寸下的中心点 + 宽高"像素值：
  cx = data[0*N + i]    cy = data[1*N + i]
  w  = data[2*N + i]    h  = data[3*N + i]
  X1 = cx - w/2   Y1 = cy - h/2   X2 = cx + w/2   Y2 = cy + h/2

⚠️ 不需要再做 sigmoid / exp / 锚点换算 —— 多做一步会得到完全错误的框。

先找最高分类得分再判阈值（省掉无效框的计算）。
类别名缺失用 class_N 占位，【不编造名字】（比如按 COCO 顺序硬套）。
```

### 案例 6｜NMS 必须按类别分别做

```
非极大值抑制按 ClassId 分组分别做（YOLO 的标准做法）：
  一个"人"和一个"自行车"框高度重叠是正常的（人骑着车），不该互相压制；
  只有同类别的重叠框才需要去重。

foreach (group in GroupBy(ClassId))
    按置信度降序，取最高的，删掉 IoU > 阈值的同类框
最终所有保留框再按置信度降序
```

### 案例 7｜裁剪：越界必须自己夹（HALCON 不兜底）

```hdevelop
* ⚠️ 实测 crop_part 越界不报错：请求超出图像范围的宽高，它照样返回一张图，
*    多出来的部分是垃圾数据（只有 Row 为负才抛 #1301）。
*    指望 Halcon 兜底就会得到一张"看着正常、实际错位"的图。
*
* 夹取顺序：先夹位置再算可用尺寸，否则会算出负的宽度
row    := clamp(requestedRow,    0, imageHeight - 1)
col    := clamp(requestedCol,    0, imageWidth  - 1)
height := clamp(requestedHeight, 1, imageHeight - row)
width  := clamp(requestedWidth,  1, imageWidth  - col)
crop_part (Source, Cropped, row, col, width, height)   * 注意 .NET 实参是 (row,col,width,height)
* 若发生过夹取 → 记一条 note 到日志
```

## 5.4 附录 D：源码索引

### D.1 文件清单（`Plugins/Plugin.Yolo/`）

| 文件 | 职责 |
|---|---|
| `YoloPlugin.cs` | **插件壳**（543 行）：元数据、端口、`[StepConfig]`、运行期、会话 Acquire-Release、预览 |
| `YoloSession.cs` | 进程级会话缓存（引用计数）+ `YoloModelInfo`（元数据解析），246 行 |
| `YoloEngine.cs` | 推理引擎（letterbox → NCHW → ORT Run → 解码 → 结果）+ `YoloParams`/`YoloResult`，340 行 |
| `YoloPostProcess.cs` | `Detection`/`LetterboxInfo`/`YoloLayout` + 布局嗅探 / v8 解码 / 类别级 NMS / 坐标反算（**全纯函数**），329 行 |
| `YoloCrop.cs` | 按框裁剪 + 边界夹取 + 夹取留痕，83 行 |
| `YoloView.xaml` | 配置界面（208 行） |
| `YoloView.xaml.cs` | 视图后置（43 行，仅 3 件事） |

> 本插件**没有** `Models/`、`Services/` 目录，数据模型平铺在 `YoloPostProcess.cs` / `YoloEngine.cs` / `YoloSession.cs` 里。

### D.2 关键方法与位置

| 方法 | 位置 | 说明 |
|---|---|---|
| `RunAlgorithm` | :330 | 取图 → `TryDetect` → 写 7 个输出端口 → 日志（含"0 个目标也写日志"） |
| `TryDetect` | :383 | 运行期与试算共用（返回 false = 程序级失败；0 目标返回 true） |
| `EnsureSession` | :422 | **判路径变了没**：没变复用，变了先放旧的再拿新的 |
| `ReleaseSession` | :467 | 引用计数 −1 |
| `YoloSession.TryAcquire` | Session:55 | 进程级缓存（静态字典 + 锁），64 位检查、日志级别 |
| `YoloEngine.TryDetect` | Engine:71 | 六步推理链路 |
| `YoloEngine.TryBuildLetterboxImage` | Engine:212 | letterbox 构造（**注释最密集**） |
| `YoloEngine.ToNchwFloat` | Engine:302 | 指针 + `Marshal.Copy` + `/255` |
| `YoloPostProcess.Nms` | PostProcess:276 | **按类别分别做** |
| `YoloCrop.CropBest` | Crop:43 | 越界自夹 + 留痕 |
| `Dispose` | :535 | `ReleaseSession()` + 释放 DisplayImage |

### D.3 数据模型

| 类 | 位置 | 关键字段 |
|---|---|---|
| `Detection` | PostProcess:8 | `ClassId`/`ClassName`/`Confidence`/`X1,Y1,X2,Y2`（**原图像素**）/ `Width`/`Height` |
| `LetterboxInfo` | PostProcess:49 | `Src/Dst` 宽高、`Scale`、`ScaledW/H`、`PadX/PadY`、`PadGrayValue = 114`；`Compute`(:71)、`MapBackToOriginal`(:95) |
| `YoloLayout` | PostProcess:110 | `Unknown`（不猜，报错）/ `V8Transposed` |
| `YoloParams` | Engine:13 | `ConfidenceThreshold`/`NmsIoU`/`MaxDetections`/`ClassFilter`/`InputSize` |
| `YoloResult` | Engine:38 | `Detections`（已 NMS、已换算回原图、已按置信度降序）/`Letterbox`（排障用）/`ElapsedMs` |
| `YoloModelInfo` | Session:161 | `ModelPath`/`InputName`/`InputDimensions`/`OutputName`/`ClassNames`/`Task` |

## 5.5 附录 E：回归断言清单

**文件**：`FlowCanvasChecks/YoloChecks.cs`（561 行，45 处 `Check`），注册于 `Program.cs:86`。

**组织方式（三段，各自独立可跑）**：

| 段 | 是否需要模型 | 说明 |
|---|---|---|
| ① 后处理逻辑 | ❌ 不需要（受控张量） | **最该测的一层**：解码 / NMS / 坐标换算写错的后果是"结果全错但程序不报错"，只能靠"已知输入 → 已知期望输出"来钉 |
| ② ONNX Runtime 依赖链路 | ✅ 需要任一 ONNX 模型 | 找不到就**跳过**（记为通过但写"跳过"） |
| ③ 真实模型的守卫栏 | ✅ 需要任一 ONNX 模型 | 证明"布局不认识就报错、不猜" |

### A. 后处理（`RunPostProcessChecks`，不需要模型）

- letterbox 宽图 `200×100→100×100`：`scale=0.5, scaled=100×50, pad=(0,25)`
- letterbox 高图 `100×200→100×100`：`scale=0.5, scaled=50×100, pad=(25,0)`
- **坐标换算**：letterbox `(40,40,60,60)` → 原图 `(80,30,120,70)`
- 越界夹取 `(-50,-50,9999,9999)` → 落在 `[0,200)×[0,100)`
- 布局：v8 `[1,84,8400]` 识别为 80 类；**v5 `[1,25200,85]` 明确拒绝**
- **已知歧义**：`[1,116,8400]` 被形状校验误读成 112 类（证明必须靠 `task` 兜底）
- **task 守卫**：`segment` 拒绝；`pose` 拒 / `detect` 放行 / 元数据缺失放行
- 解码：植 3 个目标解 3 个；类别 2=划伤 conf 0.90；cxcywh → `(40,40,60,60)`
- **NMS**：同类压掉 1 个；**异类同框不互压**
- 端到端：最终框 `(80,30,120,70)`；无类别名 → `class_N`；全零张量 → 0 个

### B. 运行链路 + 守卫栏（需要模型，否则跳过）

- ONNX Runtime 原生库可加载、会话可创建
- 输入/输出名字与形状；元数据可读（缺失不算失败）
- **同一模型第二次获取命中缓存（缓存数不变）**
- 真实推理一次（需要图，否则跳过）
- **真实非目标模型（v2 网格布局）被守卫栏明确拒绝**——"失败"才是预期结果，关键看错误信息是否说清了原因
- 模型不存在 → 明确报错

> **"真实推理 vs 桩/降级"是明确区分的**：桩 = 受控张量（植 4 个锚点：同类重叠对 + 异类同框 + 全低分），覆盖全部后处理；真实路径需要 `YOLO_TEST_MODEL` 与 `YOLO_TEST_IMAGE`，找不到就跳过。

### C. 裁剪（`RunCropChecks`）

200×100 黑图 + 行 10..19 / 列 30..49 白块：
- 尺寸 20×10；**裁到白块（MaxGray>200，证明位置对）**；**框外裁出全黑（证明行列没写反）**
- 边距 +10 → 40×30 且 note 为空；**边距推出图外 → 必留痕**；**严重越界 → 夹到整幅 200×100 且留痕**
- 无目标 → null

### D. 插件壳（`RunPluginShellChecks`）

- `[Display]` GroupName == `"图像处理"`
- 输出端口契约：`BestCrop, Boxes, BoxesText, ClassIds, ClassNames, Confidences, Count`
- 未选模型 → `Success=false` 且 Error 含"模型"，**不抛异常**
- 模型路径不存在 → 含"不存在"
- `ClassFilterText="0,abc"` → 失败且含"类别索引"（不静默）
- **会话进入进程级缓存**（CachedSessionCount 上升）
- **插件 Dispose 后引用计数归零、缓存释放**

**历史验证**：`[YOLO]` 四段共 35 条断言全绿（全量 610 通过 / 14 失败，失败来自并行任务）。

**实测端口值**：
```
Count=2 / ClassIds=2,0 / ClassNames=划伤,缺陷 / Confidences=0.900,0.700
Boxes=80,30,120,70,80,30,120,70
BoxesText=划伤:0.90@[80,30,120,70];缺陷:0.70@[80,30,120,70]
```

## 5.6 附录 F：术语表

| 术语 | 含义 |
|---|---|
| YOLO | You Only Look Once，单阶段目标检测网络 |
| ONNX | 开放神经网络交换格式（跨框架模型文件 `.onnx`） |
| ONNX Runtime (ORT) | 微软的 ONNX 推理引擎 |
| letterbox | 等比缩放 + 居中补边（保持长宽比）的信箱式预处理 |
| 补边值 114 | YOLO 训练时的默认填充灰（**不能用 0**） |
| 坐标反算 | `(letterbox坐标 − 补边) ÷ 缩放` 换回原图坐标 |
| NCHW | 张量布局：Batch × Channel × Height × Width |
| 归一化 /255 | 把 0~255 像素压到 0~1（YOLO 训练时的做法） |
| NMS | 非极大值抑制，去掉重复框（**按类别分别做**） |
| IoU | 交并比，两框重叠程度的度量 |
| 置信度 | 模型对自己答案的把握，0~1 |
| task | 模型元数据的任务类型（detect / segment / pose / obb / classify） |
| 布局嗅探 | 由输出张量形状判断是 v5 还是 v8 风格 |
| 引用计数 | 共享会话的生命周期管理（归零才真正释放） |
| DFL | Distribution Focal Loss，v8 的框回归方式（导出图已含解码） |

## 5.7 附录 G：文档合并说明

本文件由以下文档合并而成（内容已全部并入，原文件已归档在 `docs/目标检测/`）：

| 原文件 | 并入位置 |
|---|---|
| `2026-09-25-YOLO目标检测插件.md`（154 行） | 第 1 章（摸底实测、决策点、被真实数据推翻的三处）+ 第 2 章 + 第 4.2 节 + 第 4.5 节（已知边界 13 条） |
| `2026-09-25-读码OCR-YOLO配置窗口上游图显示.md` | 第 3.3 节（接图）+ 第 4 章（配置窗口上游图修复） |

**未并入、但保留为参考的跨插件文档**：
`docs/code-changes/2026-09-25-插件配置视图样式统一.md`（按钮文字被裁）、`2026-09-25-试运行跨线程异常修复.md`（`LinkableValueEditor` 跨线程）、`2026-09-25-冒烟命令超时排障与规避.md`、`docs/图像预处理/图像预处理插件.md`、`docs/模板匹配/模板匹配插件.md`、`docs/创建ROI/创建ROI插件.md`（上下游接线表）。
