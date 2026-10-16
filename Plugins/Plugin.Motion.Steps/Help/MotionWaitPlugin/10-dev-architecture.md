# 等待到位：架构、宿主契约与不变量

> 分类：运动控制 ｜ 来源：插件 Plugin.Motion.Steps ｜ 读者：开发

S 档一页承担三个角色：架构与数据流、宿主白拿的行为、改这个文件时的红线。
用户页讲的"怎么填"这里不重复，只讲**这些行为由谁提供、为什么这么写、写错会怎样**。

## 一、这个插件在分层里的位置

运动控制三层，本文件属于最薄的那一层：

| 层 | 位置 | 职责 |
| --- | --- | --- |
| 契约层 | `Shard\Core.Interfaces\Motion\` | `MotionDeviceBase`（队列/门禁/看门狗/状态机）、`MotionAxisResolution`（轴名解析）、`AxisStatus`（状态快照） |
| 驱动层 | `Plugin.Motion.ZMotion` / `Plugin.Motion.Virtual` | 只写"这台设备怎么说话"，负责刷新快照 |
| 步骤层 | 本工程 7 个插件类 | 把一次设备交互摊成流程节点 |

本步骤是步骤层里唯一的**纯观察者**：全程不入队任何命令、不下发任何通信，
只在循环里读快照。`MotionWaitPlugin.cs` 全文 124 行，没有一行 SDK 相关。

## 二、RunAlgorithm 逐步（执行时间线）

| # | 代码动作 | 为什么必须在这一刻做 |
| --- | --- | --- |
| 1 | `Axis.ActualValue ?? string.Empty` | 端口的实际生效值在"读的这一刻"确定（链接值优先于手填值），取出后整步用同一个值 |
| 2 | `Position.Value = 0; ElapsedMs.Value = 0` | **开头清零**：本轮异常失败时下游不能拿到上一轮的旧位置（流程可重复执行，输出端口不是"曾经成功过"的存证） |
| 3 | `MotionAxisResolution.TryResolveAxis(...)` | 一次调用同时拿到"设备 + 轴配置"；失败原因文本由解析器给出，步骤不编自己的措辞 |
| 4 | `Math.Max(1, TimeoutMs)`、`Math.Clamp(PollIntervalMs, 1, 500)`、`Stopwatch.StartNew()` | 下限/夹取都在步骤侧做完，循环里不再重复算；计时从解析成功后才开始，解析耗时不计入等待 |
| 5 | 循环首行判 `context.CancellationToken.IsCancellationRequested` | **取消优先于一切判断**：流程停止时立刻退出，不等满超时 |
| 6 | `device.GetAxisStatus(mapping.PhysicalIndex)` | 读的是轮询线程刷好的快照，**不发起通信**；同一轮里位置/运动位/报警位来自同一采样时刻，互相自洽 |
| 7 | `status == null → Fail` | 快照为空 = 还没轮询到或已断开。给"未知"而不是拿 0 位置糊弄下游（附 `Descriptor.Caption` 与 `StateDetail`，能一眼看出是哪张卡） |
| 8 | `status.HasCriticalSignal → Fail`（急停输入 / 正限位 / 负限位 / 报警任一为真） | **硬信号优先于"到位"**：报警或撞停时轴可能恰好停在目标附近，只看 `InPosition` 会把撞停当到位。此判断必须排在到位判断**之前**，顺序颠倒即回归 |
| 9 | `status.InPosition && !status.Moving` → 写 `Position`/`Elapsed`，`Success = true` | 两个位**同时**成立才算到位（到位信号亮但还在动 = 还没停稳） |
| 10 | `watch.ElapsedMilliseconds > timeout` → 先写 `Position`/`Elapsed` 再 `Fail` | 超时要**带上当前位置**报错（判断走了多少），这是和其它失败出口最大的区别：失败也写位置 |
| 11 | `context.CancellationToken.WaitHandle.WaitOne(interval)`，吞 `ObjectDisposedException` | 等待间隔可被取消立刻唤醒；令牌已释放时不炸，下一轮循环会因取消而退出 |

## 三、状态有几份、谁是真源

| 状态 | 真源 | 本步骤读还是写 |
| --- | --- | --- |
| `InPosition` / `Moving` / `PositionMm` / 硬信号位 | **轴状态快照**（`MotionDeviceBase` 轮询线程按卡级 `PollIntervalMs` 刷新） | 只读 |
| 命令生命周期 | `MotionCommand`（基类队列） | **完全不经过本步骤**——它不等命令，等的是设备事实 |
| 界面填的值 | 步骤的 `InputValues`（`.vms`）与 `[StepConfig]` 配置段 | 由基类 `ApplyConfigValues` 灌进端口，`OnConfirm` 写回 |
| 本插件自己 | **无**（没有实例字段、静态字段、缓存；`Stopwatch` 是方法内局部变量） | — |

"插件里没有自己的状态"是这 7 个步骤的共同前提：流程可能被并行分组调度，实例上的可变字段都是竞态源。

**两个"轮询间隔"不要混**：快照刷新节奏由**卡级** `MotionDescriptor.Params.PollIntervalMs`（默认 10 ms）决定，
在「运动卡设置」维护；本步骤配置项 `PollIntervalMs`（默认 20、夹取 1~500）只决定"去看一眼"的频率。
把它设得比卡级还小只是空转；设大则最坏晚一个间隔才发现到位。

## 四、白拿的宿主/基类行为（作者不用写、也别重写）

| 行为 | 由谁提供 | 表现 |
| --- | --- | --- |
| 端口发现与注册 | `VisionPluginBase.EnsurePortsDiscovered`（反射 public 属性） | 耗时输出的属性名是 `ElapsedMs`、端口名显式写了 `Elapsed`——下游与存档都按端口名 |
| 灌值 / 写回 | `ApplyConfigValues` + `OnConfirm` | 参数面板显示已保存值；链接的口写回时移除常量键 |
| 参数面板 | 宿主 `AutoPortConfigView` | 不实现 `IPluginCustomViewProvider` 就自动有面板；**面板只列输入端口**，`[StepConfig]` 的 `TimeoutMs` / `PollIntervalMs` 因此暂无界面入口 |
| 轴名解析（全局唯一名 → 卡 + 物理轴号） | `MotionAxisResolution.TryResolveAxis` | 未注入服务时报 `运动卡服务尚未就绪：请在主程序内运行流程`；解析失败的原因文本全部由它给出 |
| 轴名下拉候选 | `StepConfigOptionKind.MotionAxisName` 动态候选（宿主按方案生成） | 候选随方案变化，不是扫描期取一次就冻存的静态清单 |
| 流程失败语义 | `VisionPluginBase.Fail(...)` | `Success=false` + `ErrorMessage`，后续步骤照常执行 |

## 五、MotionDescriptor 参数口径（与其他 6 个步骤页同一份说法）

- 卡级（`MotionParams`，「运动卡设置」维护、随方案落盘）：`DefaultVelocityMmPerS` 50、
  `DefaultAccelMmPerS2` 500、`PollIntervalMs` 10、`CommandTimeoutMs` 30000、`WatchdogTimeoutMs` 3000、
  `AutoReconnect` true、`MaxQueueDepth` 256。本步骤只消费"快照已经反映出来"的结果，不直接读这些值。
- 轴级（`AxisMapping`）：逻辑名（流程唯一引用）、物理轴号（**0 基**）、启用位、
  脉冲当量 `UnitsPerMm`（默认 1000）、软限位 ±1000000（默认）；另有轴配置弹窗的 7 个低频字段
  （编码器类型/PPU、驱动器、额定电流、跟随误差上下限、备注），本步骤全部不读。
- **引用一律用 Id 不用显示名**：`MotionDescriptor.Id`（Guid）是内部稳定身份，`DisplayName` 用户随便改不断流程；
  报错文案里的「运动卡「…」」取 `Caption`（显示名 → 地址 → "(未命名运动卡)" 回退），只是给人看的。
- `MotionDescriptor.ResolveAxisIndex` 已退役（单卡范围内查名，跨卡重名命中哪根看列表顺序）；
  解析必须走 `MotionAxisRegistry.TryResolve` / 插件侧的 `MotionAxisResolution`。

## 六、内存所有权

本步骤不创建任何可释放对象：`device` 由 `IMotionProvider` 管，快照 `AxisStatus` 是轮询线程产出的
纯数据对象（步骤只读引用、不持有跨轮、不负责释放），`MotionCommand` 根本不出现。
这与「轴使能」等入队型步骤形成对照——那些步骤要 `using` 命令对象，本步骤没有这个负担。

## 七、并行安全声明

纯读循环 + 无实例状态，可放进并行分组。两点代价要如实说：
① 等待期间该分支的线程被 `WaitOne` 占住（这是流程步骤的执行模型，不是本插件的资源泄漏）；
② 同卡多轴各放一步等待互不拖累——命令是**别的步骤**排队执行的，本步骤不占命令队列。

## 八、A 组不变量（违反就编译/闸门红）

| 症状（改坏了会看到什么） | 机理 | 守门断言名 |
| --- | --- | --- |
| "等哪根轴"由上游决定失效，链接上去不生效 | 端口声明了但从不读 `ActualValue` | `★每个插件的「轴 / 卡」都有可链接的输入端口（只靠 [StepConfig] 常量的话，编辑流程时赋不了值——它不暴露为端口）`、`★端口在运行期真的被读取（有端口却只读常量 = 摆设，链接上去也不生效）`（`FlowCanvasChecks\MotionZMotionChecks.cs`） |
| 老流程一编译就报"必填参数未配置" | 轴名口必须 `IsRequired = false` | `轴端口 IsRequired=false（老流程只填常量，不能因为没链接就判编译失败）` |
| 轴名下拉一片空白/退回手打文本框 | `OptionKind` 没声明，动态候选链断 | `★运动端口声明了 OptionKind（候选项随方案变化，不能在扫描期取一次就存下来）`、`★属性面板共用生成器清单里注册了动态候选生成器（抽到 Defaults 后不可能漏一边）` |
| 手册被自动生成页顶掉、放错目录串页 | Help 页没进 DLL / 目录名不等于类名 | `HM4 有 Help/*.md 的工程都带 EmbeddedResource（手册进得了 DLL）`、`HM2 多插件程序集不许放扁平手册（防串页）`、`HM3 Help 子目录名逐字等于插件类名（分册能被认出）`（`FlowCanvasChecks\HelpManualCheck.cs`）；分档页数由 HM13/HM16 台账闸门核对 |

## 九、B 组不变量（不报错，只出诡异现象）

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| 等待"看起来永远成功"，流程实际串行感消失 | 若判据退化成只读某条命令的状态，等待就与设备事实脱钩 | `★Move 下发成功 ≠ 已到位（命令已 Done，但轴仍在途中）`、`轮询推进后轴真的走到目标（位置 ≈ 20mm 且到位）`（`FlowCanvasChecks\MotionChecks.cs`——这两条钉住"到位必须另行等快照"，正是本步骤存在的理由） |
| 撞停被当成"走到位"传给下一步 | 硬信号判断被挪到到位判断**之后** | 无断言，靠人工核对第 8、9 行的先后顺序；报警链路本身由 `★轮询到伺服报警 → 状态转 Alarm` 反证 |
| 超时不报当前位置，排障全靠猜 | 超时分支没先写 `Position`/`Elapsed` 就 `Fail` | 无断言，靠人工核对（报错原文内嵌 `当前位置 … mm`） |
| 上一轮的 `Position`/`Elapsed` 冒充本轮读数 | 开头没清零 | 无断言，靠人工核对（时间线第 2 步） |
| 把 `TimeoutMs` 改成 0 存档后"每轮必超时" | `Math.Max(1, …)` 下限让它以 1 ms 立刻超时 | 无断言，靠人工核对 |
| 停止流程后本步骤还挂着等满超时 | 等待没用可取消的 `WaitHandle.WaitOne` | 无断言，靠人工核对第 5、11 步 |

## 十、改完这个文件必须做的动作

| 改了什么 | 必须同步 |
| --- | --- |
| 端口名/默认值 | 用户页《等待到位》与本页第五节；`.vms` 里 `InputValues` 的键会变，老方案旧键静默失效 |
| 到位判据（加/减条件） | 用户页"判据是两个位同时成立"的口径 + 本页第九节第 2 行 |
| 硬信号集合（`HasCriticalSignal` 面） | 「读轴状态」页的同一口径——两个步骤必须宽严一致，改一处不改另一处就是现场对不上话 |
| 错误文案 | `05-troubleshooting.md` 的原文对照（要求逐字一致，改了不改手册就是假绿） |
| `PollIntervalMs` 的夹取区间 | 用户页参数表与"比卡级还小只是空转"的说法 |
