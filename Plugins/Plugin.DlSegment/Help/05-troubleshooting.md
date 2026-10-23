# 深度学习分割 · 排错

> 分类：图像处理 ｜ 来源：插件 Plugin.DlSegment

按"错误文案 → 原因 → 处置"排，再按"不报错但结果不对"的症状排。第一条最有用：本插件的失败文案都带着可执行信息，先读原文。

## 一、按 ErrorMessage 原文查

| 文案（原文） | 原因 | 处置 |
|---|---|---|
| `上游没有图像：请把图像节点的输出连到本节点的 Image。整幅图分割需要一个输入。` | `Image` 端口没接，或上游本轮没出图 | 检查连线；若上游是条件分支，确认本轮走的是出图那条 |
| `尚未选择模型：请在节点配置里选择一个 .onnx 语义分割模型。` | `ModelPath` 为空 | 点「浏览」选模型 |
| `模型文件不存在：{路径}。请重新选择，或把模型放到该路径。` | 相对路径按宿主 exe 目录解析，换了机器/换了输出目录就找不到 | 重新浏览，或把模型放到同一相对位置 |
| `模型加载失败：{内层原因}。请确认：① 是合法的 .onnx（能被 onnxruntime 加载）；② onnxruntime.dll 已投递到宿主 exe 目录。` | ONNX Runtime 原生库缺失，或模型本身损坏/版本不兼容 | 见本篇"原生库"一节 |
| `当前进程是 32 位，而 ONNX Runtime 原生库只有 x64 版本。请把宿主改成 x64。` | 宿主被配成 AnyCPU+32 位优先 | 平台目标改成 x64 |
| `读到的输出：{名字}（秩 n，形状 [...]）` + `只认 4 维 [1,C,H,W]…不会替你猜` | 模型不是语义分割 | 换模型 / 换插件。**不要改预处理绕** |
| `C=1 是单类掩膜，用「阈值」或「Blob」更合适` | 二值掩膜模型 | 用阈值或 Blob 更轻更快 |
| `模型有 N 个输入，本插件只支持单输入（多输入需要把预处理写进插件，见 10-dev-architecture.md）` | 模型要求图像 + 提示/掩膜等多个输入 | 换单输入模型，或走别的框架 |
| `无法确定模型输入尺寸：模型是动态形状，请在节点配置里填写 InputSize（训练时用的输入边长）。` | 模型 H/W 为动态 | 填训练尺寸，常见 512 / 640 |
| `输出通道数 {C} 超过 255，byte 类别图装不下。` | C > 255 | 换类别更少的模型 |
| `输出长度 {len} 与 [1,{C},{H},{W}] 不符` | 模型的输出与元数据不一致（少见，通常是导出 bug） | 重新导出模型 |
| `类别序号 {i} 超出范围：模型只有 {C} 类（0~{C-1}）。` | `ClassIndex` 填大了 | 改成合法序号 |

## 二、"不报错但结果不对"

| 症状 | 最可能原因 | 确认方法 |
|---|---|---|
| 类别图几乎全 0 | 归一化与训练不一致；或模型期望 RGB 而输入是灰度 | 换成训练时同样的图源试一次 |
| 类别图全是同一个非 0 类 | 模型退化了 / 输入尺寸与训练差太多 | 在训练侧先验证模型本身 |
| 面积比预期小 | 用了 `Stretch` 且长宽比差异大 | 换 `Letterbox` 比较 `ForegroundRatio` |
| 片数异常多（几百） | 前景里零星噪声，`connection` 把它们都当独立块 | 上游加中值滤波，或下游按面积筛 |
| 片数恒为 1 但明显有多块 | 两块在边界上相连 | 上游先腐蚀/开运算再分割，或接受"算一整块" |
| 边缘总是差一两个像素 | 缩回原图用 `Nearest`，属于预期 | 需要亚像素边界请接亚像素轮廓节点，别指望分割 |
| 不同批次结果跳变 | 光照变了 | 先稳定光源，再谈模型 |
| 前几十帧正常，之后全错 | 模型被别的节点同时加载导致会话争用（同路径共享，不同路径各建一份） | 统一让同一个模型路径只出现在一个节点 |

## 三、"图没出来"

| 现象 | 原因 | 处置 |
|---|---|---|
| 右栏一直空态 | `DisplayViewIndex = 0`，或没执行过 | 改回 1~9，执行一次；试算也会带图 |
| 有图但是全黑 | 类别序号都在 0~2，直接看就很暗 | 接「结果显示」或预处理做灰度拉伸 |
| 预览是灰不是白 | 前景掩膜 `RegionMask` 不是类别图 | 确认预览源（本插件预览的是 `ClassMap`） |
| 报「热力图上屏失败」类警告 | 显示层异常，不影响输出端口 | 查 `Modules\` 里 `Core.Halcon.dll` 是否最新 |

## 四、原生库（`onnxruntime.dll`）

本插件工程有 `DeployOnnxRuntimeToHost` 构建目标，把 `DLL\OnnxRuntime\` 下所有文件 `xcopy` 到宿主 exe 目录。若你看到 `DllNotFoundException` / `LoadLibrary failed`：

1. 确认 `VisionMaster\bin\<配置>\net9.0-windows\onnxruntime.dll` 存在（**Windows 不递归搜 `Modules\`**，必须落在 exe 同目录）。
2. 确认位数一致：x64 宿主只能配 x64 原生库。
3. 手工补：把 `DLL\OnnxRuntime\` 下三个 dll 拷到宿主 exe 目录，重启宿主验证。

## 五、改源码时容易踩的坑

| 坑 | 后果 | 正确做法 |
|---|---|---|
| `OutputPort<T>` 初值当第二参传 | 12 条 CS1503 | 两参 `(name, description)` 或三参 `(name, description, initialValue)` |
| `using NamedOnnxValue x = ...` | CS1674（不实现 IDisposable） | `NamedOnnxValue v = ...; try {...} finally { (v as IDisposable)?.Dispose(); }` |
| `HOperatorSet.OverpaintGray(a, b, out c)` | CS1501（只有两参） | 两参版**就地修改第一个入参**，改完把原位图句柄换掉 |
| `HImage.GetImageTypeName()` | CS1061 | `GetImageType(img, out HTuple t)` 再取 `t[0].S` |
| 用双线性缩回类别图 | 产生 1.5 这种不存在的类 | 默认 Nearest；双线性只给显示用 |
| 忘记 `connection` 直接数区域 | `RegionCount` 恒为 1 | 先 `connection` 再 `count_obj` |
| 断言里统计输出端口不剔基类 | 数量对不上（多 `Success`/`ErrorMessage`） | 统计时排除这两个 |
| 断言里直接调 `RunAlgorithm` | 正常用例全假红 | 调 `Execute(context)` |

## 六、契约提醒

- `InputPort<T>.Value` 是 `object?`，读侧用 `ActualValue`；`OutputPort<T>.Value` 是 `object`，读侧用 `TypedValue`。
- 输出图交给端口后**不要自己 Dispose**：基类下一轮 `DisposePreviousRoundOutputs` 会释放。
- 中间 `HObject` 一律 `try/finally` 释放，尤其 `ZoomImageSize` / `Compose3` / `ChangeDomain` 产出的临时图。
