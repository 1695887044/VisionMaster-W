# 深度学习分割 · 内部结构

> 分类：图像处理 ｜ 来源：插件 Plugin.DlSegment ｜ 读者：改这个插件的人

本插件是第三个深度学习插件，与 `Plugin.DlAnomaly` 结构对称。**刻意没有抽公共 ONNX 库**——依据 `docs/深度学习/深度学习插件（分类-异常-分割）方案说明书.md` §5.1：每个 DL 插件各自持有一份会话实现，是为了不改动已验证的 Yolo、避免跨插件引用把三个插件绑成一条船。要抽 `Services/Core.Onnx` 属公共契约级改动，按 R12 需要架构评审，且应在第三个插件落地之后再做。

## 一、文件构成

| 文件 | 行数量级 | 职责 | 依赖 HALCON |
|---|---|---|---|
| `Plugin.DlSegment.csproj` | ~60 | 引用、内嵌手册、原生库投递 | 否 |
| `SegmentEnums.cs` | ~60 | 三个枚举（缩放模式 / 插值 / 前景口径），带 `[Display]` 中文名 | 否 |
| `SegmentContract.cs` | ~200 | 输出契约嗅探（形状判定 + 类别名解析） | 否 |
| `SegmentPostProcess.cs` | ~110 | `SegmentResizePlan`（Letterbox/Stretch 的几何计算，`PadGrayValue=114`） | 否 |
| `SegmentSession.cs` | ~230 | ONNX 会话获取（进程级缓存 + 引用计数 + 四条守卫）、模型元数据解析 | 否（只依赖 OnnxRuntime） |
| `SegmentEngine.cs` | ~420 | 一次分割的完整链路（建画布 → NCHW → 推理 → argmax → 缩回 → 区域化） | 是 |
| `DlModelState.cs` | ~15 | 三态枚举 `NotLoaded / Ready / Failed` | 否 |
| `DlSegmentPlugin.cs` | ~470 | 插件壳：端口、`[StepConfig]`、`RunAlgorithm`、视图绑定成员 | 是 |
| `SegmentView.xaml` + `.xaml.cs` | ~860 + ~160 | 两栏配置界面（左参数、右只读预览） | 是（`ImageReadOnly`） |

**没有独立的 ViewModel 类**：插件的 VM 成员就是 `DlSegmentPlugin` 自己，视图只做绑定与两个命令（浏览、试算）。这与 `DlClassify` / `DlAnomaly` 一致，也让"界面上看到的值"和"流程里用的值"天然是同一份。

## 二、三层职责

```
DlSegmentPlugin（壳）
   ├─ 读端口、校验、调引擎、写端口、发预览
   └─ 不碰 HALCON 细节，不碰 ONNX 细节
        ↓
SegmentEngine（链路）
   ├─ 建画布（HALCON）
   ├─ NCHW 张量（HALCON 指针读取 + float 归一化）
   ├─ 调 SegmentSession 拿结果张量（ONNX）
   └─ argmax + 缩回 + 区域化（HALCON）
        ↓
SegmentSession（会话）        SegmentContract（契约）
   ├─ 进程级缓存               ├─ 秩/形状判定
   ├─ 引用计数                 └─ 类别名解析
   └─ 四条守卫
```

分层判据：**没有 HALCON 的那一层必须能被单元测试直接调**（`SegmentContract.TryResolveContract` 只吃 `int[]`，就是为了这一点）。

## 三、一次分割的九步调用链

```
1. 取图：HImage? image = Image.ActualValue
2. 守卫：image == null → Fail("上游没有图像…")
3. 会话：EnsureSession() → TryAcquire(ModelPath) → 进程级缓存命中或新建
4. 尺寸：TryResolveInputSize(InputSize, Info) → 静态形状优先，动态必须手填
5. 几何：SegmentResizePlan.Compute(srcW, srcH, dstW, dstH, ResizeMode)
6. 画布：TryBuildCanvas(image, plan) → 3 通道 byte HImage（Letterbox 补 114 灰）
7. 张量：ToNchwFloat(canvas, W, H) → float[]，逐通道指针读取并 /255
8. 推理：session.Run([NamedOnnxValue]) → IDisposableReadOnlyCollection<DisposableNamedOnnxValue>
         按 Info.OutputName 取名取张量 → AsTensor<float>() → float[]
9. 后处理：TryArgMax → TryRestoreToSource(Nearest/Bilinear) → RegionToBin → Connection → 统计
```

第 9 步细分为：

```
9a 校验形状：[1,C,H,W] 且长度 = C*H*W，否则明确报错（不截断不补零）
9b 逐像素 argmax：C 个通道里取最大值的下标，C>255 提前拒绝
9c 填 byte[]：类别序号写进字节数组（这个数组就是"类别图"）
9d 缩回原图：GCHandle.Alloc(byte[], Pinned) → GenImage1 → ZoomImageSize(Nearest|Bilinear)
9e 前景掩膜：RegionToBin(classMap, threshold=1) → 像素值 ≥1 的进掩膜
9f 区域化：Connection → CountObj 得片数
9g 统计：AreaCenter 累加各类像素 → ClassRatios / DominantClass / ForegroundRatio
9h 摘要：SegmentEngine.DescribeCounts 拼人读文本
```

## 四、资源释放的四层

| 层 | 谁负责释放 | 说明 |
|---|---|---|
| 上游图（`Image` 端口上的 HImage） | 上游 / 端口容器 | 本插件只读，不 Dispose |
| 中间态（画布、缩放图、掩膜、GCHandle） | `try/finally` 就地释放 | 尤其 `ChangeDomain`/`Compose3`/`ZoomImageSize` 每次产出新对象 |
| 输出端口上的图（`ClassMap`/`RegionMask`/`Region`） | 基类下一轮 `DisposePreviousRoundOutputs` | **本插件不 Dispose**，否则端口上留野指针 |
| ONNX 会话 | 引用计数归零时 `Dispose` | 同一路径多个节点共享一份 |

`OverpaintGray` 是个特例：它**就地修改第一个入参**，没有 out 版本。Letterbox 合成时必须写成"把合成结果赋回变量、把原句柄置 null"，否则 finally 里会把正在用的图释放掉。

## 五、每个决策为什么这么定

| 决策 | 为什么 |
|---|---|
| 独立成插件，而不是分类插件的"分割模式" | 输入预处理相同但输出契约、后处理、下游消费方式全不同；合起来会出现"这个插件在干什么取决于配置" |
| 只认秩 4 `[1,C,H,W]`，其它拒绝 | 检测输出 `[1,84,8400]` 被 argmax 会得到"看着有模有样但完全无意义"的图，且不报错。宁可加载期失败 |
| `PadGrayValue = 114` 而不是 0 | 黑边是模型训练时没见过的强边缘，会让边缘区域的分类塌掉；114 是 ImageNet 归一化后的中性灰 |
| 归一化 `/255` | 多数分类/分割模型训练时都做 `/255`；少除或多除模型照样跑、不报错，只是精度静默劣化——这种错最难查 |
| 缩回原图默认 `Nearest` | 双线性会对类别序号取平均，产生 1.5 这种不存在的类。类别是离散标签，不是灰度 |
| argmax 在 CPU 上做，不用 ONNX 的 ArgMax 算子 | 避免要求模型导出时带 ArgMax（多数训练框架默认不导出），插件端做兼容性更好 |
| 类别 0 一律当背景 | 语义分割的事实标准；让 `Region`/`ForegroundRatio` 有明确口径 |
| 会话进程级缓存 + 引用计数 | 同一模型被多个节点用时不重复加载（一份 50MB 模型加载要几百毫秒、占几百 MB） |
| `ModelSummary` 的 getter 会去 `EnsureSession` | 让模型卡在打开界面时就能显示真实状态；但**因此 `EnsureSession` 里绝不能发 `ModelSummary` 通知**（否则 getter 自我重入） |
| 试算与执行共用 `TrySegment` | 两套实现必然分叉，而分叉的表现是"界面对、跑起来不对"，最难查 |
| 本插件不做 OK/NG | 判定线是工艺参数，写进方案会导致"改阈值要重发方案"；交给「逻辑判断」节点便于审计 |
| 不做实例分割 | YOLOv8-seg 要 `[1,4+C+32,N]` + 原型掩膜、检测头解码、掩膜级 NMS，"约等于再做一个半 Yolo"，单独插件更合适 |
| 视图不另建 ViewModel | 插件本身就是 VM（`IPluginConfigView` 契约要求 `Initialize`/`OnConfirm`/`OnCancel` 在插件上）；再抽一层会出现两份状态 |

## 六、端口发现与配置持久化

- 输入/输出端口表由基类 `VisionPluginBase` 反射 `Inputs`/`Outputs` 惰性发现，**`Outputs` 里包含基类自带的 `Success` 与 `ErrorMessage`**。
- `[StepConfig]` 属性的发现顺序是 `MetadataToken`（即声明顺序），配置面板按这个顺序排。
- 持久化：`OnConfirm` 时遍历 `_configProps` 写回步骤配置；枚举存**成员名**，改成员名会让旧方案读不回来。
- 夹取钩子用 `partial void OnXxxChanging(ref T value)`，在 setter 内生效，从 `.vms` 读回来时也会走一遍。

## 七、构建与投递

```bash
dotnet build Plugins/Plugin.DlSegment/Plugin.DlSegment.csproj -m:1 -nodeReuse:false \
    -p:ModulesDir='D:\C#\VM-artifacts\modstage\'
```

- `-m:1 -nodeReuse:false`：本机多进程构建会静默失败（报"生成失败 / 0 个错误"）。
- `-p:ModulesDir=`：宿主开着时会锁住 `Modules\Plugin.DlSegment.dll`，改投递到暂存目录绕开。**不要杀宿主进程**。
- 原生库投递由 csproj 的 `DeployOnnxRuntimeToHost` 目标完成（`xcopy DLL\OnnxRuntime → $(HostBinDir)`），**不能省**：Windows 不递归搜索 `Modules\`。
- 断言宿主里跑时若用 `-p:BuildProjectReferences=false`，插件 DLL 不会重编，**必须手工把新 DLL 拷到 `FlowCanvasChecks/bin/Debug/net9.0-windows/`**，否则断言跑的是旧代码（症状：改了实现断言仍红）。
- 手册改了任何一页内容，产物就算脏 → 收尾用 `--no-incremental` 重建，并核 `Help` 内嵌资源数 == `Help/` 下 `.md` 数。

## 八、手册打包核验

```bash
ls Plugins/Plugin.DlSegment/Help/*.md | wc -l
tr '\0' '\n' < Plugins/Plugin.DlSegment/bin/Debug/net9.0-windows/Plugin.DlSegment.dll \
  | grep -a "Plugin\.DlSegment\.Help\." | sort -u | wc -l
```

两个数必须相等。**不要用 `grep -o "Plugin.X.Help.[0-9A-Za-z.-]*"`**：中文文件名会让 ASCII 正则截断，得出"少一页"的假结论。

## 九、与 DlAnomaly 的差异点

| 维度 | DlAnomaly | DlSegment |
|---|---|---|
| 输出契约 | 秩 2 分数 + 秩 4/3 热力图（两份，按秩识别） | 单份秩 4 `[1,C,H,W]` |
| 后处理核心 | 取最大值、归一化热力图 | 逐像素 argmax + 类别图 |
| 额外守卫 | 热力图边长 32~8192（区分检测输出） | C ≥ 2（拒绝单类掩膜）、C ≤ 255（byte 装得下） |
| 输出形态 | 一张热力图 | 类别图 + 掩膜 + 区域 + 占比数组 |
| 典型下游 | 判定线 | 面积测量 / 逻辑判断 / 结果显示 |

对齐点：会话层、建画布、NCHW、缩放计划、`DlModelState`、视图骨架——这六处两边几乎逐行相同。改其中一处请同步另一处，或据此提出抽公共库的评审。
