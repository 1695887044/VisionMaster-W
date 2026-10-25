# 图像采集插件：原始像素 → HImage 转换去掉 HGlobal 中转（A1）

- 类型：性能优化（消除每帧一次全帧内存拷贝与一组非托管分配）
- 起因：读《C#实现Halcon图像零拷贝》后对本仓图像链路做零拷贝改造评估，A1 是其中收益/风险比最高、且**不依赖 OpenCV** 的一条
- 涉及工程：`Plugins\Plugin.ImageAcquisition`
- 日期：2026-10-04

---

## 1. 改了什么

`ImageAcquisitionPlugin.ToHImage(byte[], int, int, int)` —— 网络推送与相机采集**共用的唯一转换核心**（`AcquireFromHub` 与 `AcquireFromCamera` 都走它）。

| | 改造前 | 改造后 |
| --- | --- | --- |
| 取指针方式 | `Marshal.AllocHGlobal(length)` + `Marshal.Copy(pixelData, 0, ptr, length)` | `GCHandle.Alloc(pixelData, GCHandleType.Pinned)` → `handle.AddrOfPinnedObject()` |
| 释放 | `Marshal.FreeHGlobal(pointer)` | `handle.Free()` |
| 每帧开销 | 1 次全帧拷贝 + 1 组非托管分配/释放 | 无拷贝（仅 pin/unpin） |

### 为什么 pin 到调用返回就可以解

`gen_image1` / `gen_image_interleaved` 是**复制**语义（文档 XML 注释："Create an image from a pointer to the pixels. Modified instance represents: Created image with new matrix."），返回前像素已进 HALCON 自己的内存。这正是它们与 `gen_image1_extern`（"引用 + 归还回调"）的区别。

因此本次改动的**生存期模型与原来完全一致**：pin 只需覆盖 HALCON 调用期间。附带的三件事全部不受影响：

- `byte[]` 队列语义（`CameraFrame` / `HubImageItem` 承载的是托管数组）；
- 溢出丢弃、`WaitNextFrame` 消费语义；
- 基类 `VisionPluginBase.AutoDisposeRoundOutputs` 的轮首回收约定。

也就是说：**这是一次纯粹的"少搬一趟"，不引入任何新的所有权协议。**

## 2. 两处容易写错的地方（本次都踩到了）

1. **必须用 `AddrOfPinnedObject()`，不是 `GCHandle.ToIntPtr()`**。
   `ToIntPtr` 返回的是"句柄令牌"（供 `GCHandle.FromIntPtr` 还原句柄用），**不是被钉住对象的数据地址**。用错了 HALCON 会拿到一个无关地址，表现为图像错乱或直接崩——而且编译期完全看不出来。

2. **`GCHandle` 是结构体，不实现 `IDisposable`**，所以不能用 `using`（`error CS1674`），必须 `try/finally` 保证 `Free()`。这条很重要：pin 住的数组会阻止 GC 压缩堆，漏 `Free` 是实打实的堆碎片。

## 3. 验证证据

### 3.1 直接单测（`[IA]` 段，逐像素断言）——全部通过

`FlowCanvasChecks\ImageAcquisitionChecks.cs` 直接反射调用 `ToHImage`，覆盖：

| 断言 | 结果 |
| --- | --- |
| 灰度 4x3x1 → 单通道、尺寸与行列不转置 | ✅ `(0,0)=10, (1,2)=16, (2,3)=21` |
| BGR 2x1x3 → 三通道；纯红/纯蓝落在 R/B 通道（不反色、不串像素） | ✅ `像0 R=255 G=0 B=0；像1 R=0 B=255` |
| BGRA 2x1x4 → 抽出 alpha 压成三通道 BGR | ✅ `像0 R=255 G=0；像1 G=255, ch=3` |
| 像素数据不足 / 2 通道 / 超大尺寸 → `ArgumentException` | ✅ 三条全过（入口校验未被绕过） |

这三条逐像素断言是本次改动的**关键正确性证据**：像素值、通道顺序、行列方向全部与改造前一致。

### 3.2 端到端冒烟（`[S]` 段，27 条）——全部通过

`FlowCanvasChecks\HttpImageSmoke.cs` 起真 HTTP 服务端，走"编码字节 → 宿主 WPF 解码 → ImageHub → 插件 `ToHImage` → 引擎"整条链路：

- `[S8]` 灰度图 POST → 200 且 `image=64x48x1`，输出端口 `Image` 是 HImage 且尺寸 64x48；
- `[S9]` 彩色图 POST → 200 且 `channels=3`，HImage 尺寸 64x48；
- `[S12]` 省略 outputs → 回传全部输出端口；流程结束后 Hub 槽已排空。

### 3.3 全量回归对比（改造前 vs 改造后）

| | 通过 | 失败 |
| --- | --- | --- |
| 改造前（`AllocHGlobal` 版） | 1017 | 19 |
| 改造后（`GCHandle` 版） | **1017** | **19** |

**失败清单逐条逐字相同**（19 条全部是既有失败，与本次改动无关）：

- `相机插件转交模块注册表后可见`（模块表 0 项）
- `[YOLO] 类别过滤填了非法值`（模型文件不存在，断言前置条件未满足）
- `Dialog* 公共键已成体系` / `两个属性面板动态候选生成器` / `卡候选的值仍是地址`（UI 契约，3 条）
- `[W] 线序检测方案`（`找不到序列号为「VIRTUAL-001」的相机`，需虚拟相机环境，10 条）
- 脚本预览帧投射（同属线序方案，3 条）

> 复现方式：`dotnet build Plugins\Plugin.ImageAcquisition\...` 后运行 `FlowCanvasChecks.exe`。改造前后各跑一次、对比输出即得上表。

### 3.4 产物投递

`Modules\Plugin.ImageAcquisition.dll` 已更新（时间戳与 `bin\` 一致），并核验 DLL 元数据字符串含 `AddrOfPinnedObject` / `GCHandle`、**不含** `AllocHGlobal` / `FreeHGlobal`——即投递的确是新版本，不是旧 DLL（AGENTS.md 红线③"产物陈旧"）。

## 4. 过程中发现的一处坑（值得记一笔）

用 `Copy-Item` 还原源文件做基线对比后，`dotnet build` **判定"已是最新"而跳过编译**——因为 `Copy-Item` 保留了源文件的旧时间戳，MSBuild 认为它比 DLL 更旧。表现是"构建成功但 DLL 没变"（核验 DLL 字符串才发现）。

**教训**：还原/覆盖源文件后若紧接着构建，先确认 DLL 时间戳真的更新了；必要时 touch 一下源文件强制重建。这与 AGENTS.md 红线③"产物陈旧"是同一类坑。

## 5. 性能实测（2026-10-04，Release，`FlowCanvasChecks.exe --perf`）

改造前/改造后两套实现**同进程交替测量**（排除机器状态与 HALCON 预热差异），"改造后"直调生产实现。每组 300 轮，取中位数：

| 场景 | 改造前 | 改造后 | 每帧省 | 30fps 折算 |
| --- | --- | --- | --- | --- |
| 灰度 1920×1080 (2MB) | 0.824 ms | 0.071 ms | **0.753 ms（91.4%）** | 22.6 ms/s |
| 彩色 1920×1080×3 (6MB) | 2.867 ms | 1.135 ms | **1.732 ms（60.4%）** | 52.0 ms/s |

**两点解读**：

- **A1 的收益是时间，不是托管分配。**`AllocHGlobal` 是非托管分配，不进 `GC.GetAllocatedBytesForCurrentThread`——实测两侧托管分配都≈0（65 B vs 64 B），这正是预期。省掉的是那一趟全帧 `Marshal.Copy`（2MB/6MB 的 memcpy）+ 一组非托管分配/释放。剩余耗时（0.071 / 1.135 ms）是 `GenImage*` 把像素复制进 HALCON 内存的**共有成本**，两侧一样。
- **改造后仍剩 1.135 ms（彩色）不是没省干净**——那是 HALCON `GenImageInterleaved` 的必然拷贝（交错→三平面重排），除非用 `GenImage*Extern`（见第 5 项，已评估不做）。

基准已固化为 `FlowCanvasChecks\PerfChecks.cs`（`--perf` 选择性运行，含回归护栏断言），重跑对比即可发现回归。**必须 Release 构建**（Debug 的 JIT 不做优化，数字失真）。

## 6. 未决问题 / 后续

| # | 事项 | 状态 |
| --- | --- | --- |
| 1 | ~~性能实测~~ | ✅ **已做**（见 §5），基准固化在 `PerfChecks.cs --perf` |
| 2 | A2（缩略图先缩小再转换） | ✅ **已做**，见 [A2 记录](2026-10-04-缩略图快路径先缩小再转换（A2）.md) |
| 3 | ~~A3（图集按需采集）~~ | ❌ **已决定不做**（2026-10-04 用户拍板）：会让报警留存静默变空、与"静默缺图最难查"的既有教训冲突。理由与替代方案见 [A2 记录 §5.1](2026-10-04-缩略图快路径先缩小再转换（A2）.md) |
| 4 | A4（SCADA 取值出口同款优化） | ✅ **已做**（2026-10-05），见 [A4 记录](../code-changes/2026-10-05-SCADA数据泵单次读取优化（A4）.md) |
| 5 | 输出侧 `GenImage1Extern` 真零拷贝 | **不建议做**：需 `clearProc` + GCHandle 注册表 + 跨非托管边界回调，为省一次拷贝引入生命周期协议，风险收益不对等 |
| 6 | 并发编辑冲突 | 本次验证期间 `CalibrationChecks.cs`（20:32）与 `CalibrationPlugin.cs`（20:28）被**另一个进程**改动，导致两次运行的 `[CAL]` 段断言数差 4 条（与本改动无关）。按 AGENTS.md R9 记录，未触碰该文件 |

---

*关联文档：改造评估见本轮会话；A2 已完成、A3 已关闭、A4 已完成，A 系列全部收口；OpenCV 桥接层方案待定。*
