# `_WebProbe3` — M0 线程模型探针

> 隔离实验，只存在于 `D:\C#\VM\_WebProbe3\`。**未修改任何生产代码 / 现有工程 / 现有文档。**
> 目标：验证「Web 命令 → 每会话有界队列 → 全局单消费者 → UI Dispatcher → 审计单写者」
> 在 .NET 9 WPF 下不会死锁、不会交错、不会跨线程异常。

---

## 1. 环境与复现命令

| 项 | 值 |
| --- | --- |
| OS | Microsoft Windows NT 10.0.26200.0 |
| 运行时 | .NET 9.0.20（`dotnet --version` = 9.0.318 SDK） |
| CPU | 16 逻辑核 |
| 工程 | `net9.0-windows` + `UseWPF=true` + `OutputType=Exe`（单文件 `Program.cs`，无 NuGet 依赖） |
| UI 线程 | 自建 STA 线程（`ManagedThreadId=5`），`Dispatcher.CurrentDispatcher` + `Dispatcher.Run()` |
| 消费者线程 | 线程池线程（`SynchronizationContext.Current == null`，实测 True） |

```bat
cd /d D:\C#\VM\_WebProbe3
dotnet build WebProbe3.csproj -c Debug
dotnet run --project WebProbe3.csproj -c Debug                 :: 正常模式 → 18 PASS / 0 FAIL，退出码 0
dotnet run --project WebProbe3.csproj -c Debug -- --mutation   :: 变异模式 → 15 PASS / 3 FAIL，退出码 1
```

**没有用 `--no-build`**（R65）。日志与证据：

| 文件 | 内容 |
| --- | --- |
| `out/run.log` | 正常模式完整输出（含汇总） |
| `out/run-mutation.log` | 变异模式输出（负对照） |
| `out/round1-commands.csv` | 1000 条命令的审计 CSV（1001 行 / 39,828 字节） |
| `out/round5-audit.csv` | 4 会话合并后的审计 CSV（1001 行 / 17,496 字节） |
| `out/round1-commands-mutation.csv` | 变异模式下被撕碎的 CSV（1646 行） |

> ⚠ 1ms 的“工作”用**自旋**模拟，不是 `Thread.Sleep(1)`：本机实测 `Thread.Sleep(1)` 平均
> **15.01ms**（min 2.52 / max 16.18，20 次采样）——被系统时钟粒度放大 15 倍。用它做负载会让
> 1000 条命令跑 15 秒，把“无死锁”误判成“超时”。

---

## 2. 实验内容（对应任务 1–5）

1. 独立 STA 线程建 `Dispatcher` 并 `Dispatcher.Run()`，主线程用 `ManualResetEventSlim` 等就绪。
2. `Channel.CreateBounded<Command>(256)`，`SingleReader=true` / `SingleWriter=false` / `FullMode=Wait`；
   两个生产者线程各 `TryWrite` 500 条（`Id` / `Producer` / `Payload`）。
3. 单个消费者循环 `await foreach (channel.Reader.ReadAllAsync())`，逐条
   `await dispatcher.InvokeAsync(() => Execute(cmd))`；`Execute` 里 `Debug.Assert(dispatcher.CheckAccess())`、
   用**唯一一个** `StreamWriter` 追加 CSV、自旋 1ms。
4. 生产结束 → `Writer.Complete()` → 等消费完 → flush/close → `InvokeShutdown()` → 主线程退出。
5. 逐条 PASS/FAIL；另有 Round 2b/3/4/5 与变异模式作为补充证据。

`Debug.Assert` 挂了**记录型 TraceListener**（默认的 `DefaultTraceListener` 会弹模态框把无头运行挂死）。

---

## 3. 六条结果（正常模式，`out/run.log`）

| # | 判据 | 结果 | 关键数字 |
| --- | --- | --- | --- |
| **A** | 1000 行全部写入、Id 唯一无丢 | **PASS** | records=1000，parsedIds=1000，unique=1000（0..999），executed=1000 |
| **B** | 每行 `CheckAccess=True`（无跨线程碰 UI） | **PASS** | 1000/1000 行 True；**执行线程数=1（tid=5）**；`Debug.Assert` 失败 0 |
| **C** | 严格 CSV 解析：行完整、列数一致、无交错 | **PASS** | header 正确；列数≠5 的行 **0**；含逗号/引号的 Payload 回读不符 **0** |
| **D** | 无死锁、总耗时 < 10s | **PASS** | **1086.29ms**（1000 条 × 1ms 自旋 + 管道开销），≈**920 条/秒** |
| **E** | 队列满时 `TryWrite=false`，不抛异常、已接受零丢失 | **PASS** | accepted=**8** / rejected=**992** / executed=**8**；accepted+rejected=1000；异常 0；已接受零丢失=True |
| **F** | 命令超时：2s token + 3s `Execute` 不会永久挂住 | **PASS** | 见下方 F1/F1b/F2/F3/F4/F5 —— **普通 `InvokeAsync(ct)` 不会截断等待（3004ms 才返回），必须用 `WaitAsync`** |

> 整轮探针墙钟耗时 **13,520ms**（其中 F1 的 3s 阻塞、F2 的 2s 等待 + 2s 复核就占了 ~7s；
> 六条判据本身没有一条依赖等待）。

**F 的完整语义矩阵（实测，不是推断）**

| 子项 | 场景 | 实测 |
| --- | --- | --- |
| **F1** | `InvokeAsync(ct=2s)`，回调已开始执行、要跑 3s | `outcome=completed`，**elapsed=3004.44ms**，`tokenFired=True`，`opStatus=Completed` → **取消对已执行的回调完全无效** |
| **F1b** | 同上但回调还在 `Pending`（前面堵一条 400ms） | `statusQueued=Pending` → `statusAfterCancel=Aborted` → `await` 抛 `OperationCanceledException`，**回调一次都没跑** |
| **F2** | `op.Task.WaitAsync(2s)` 包住 3s 工作 | `timeout@2005.82ms`，`statusAtTimeout=Executing`，**工作仍在 3013.31ms 跑完** → **超时截断的是“等待”，不是“执行”** |
| **F3** | 对 `Pending` 的 op 调 `Abort()` | 返回 **True**，回调不执行，`await` 抛取消 |
| **F4** | 对 `Executing` 的 op 调 `Abort()` | 返回 **False**，回调照常跑完 → **UI 线程外部抢不回控制权** |
| **F5** | 负对照：工作线程用**同步** `Dispatcher.Invoke`，而 UI 线程正阻塞等它 | **1.5s 不返回（死锁）**；释放后解开 → 这正是本设计用 `await InvokeAsync` 避开的那类死锁 |

**CSV 校验输出（原文摘录，`out/run.log:15-17`）**

```
[C] CSV 校验明细: headerOk=True body=1000 badShape=0 badPayload=0 badProducer=0 badAccess=0
[C] 首行: 0 | A | 5 | True | p,0,"quoted"#0, end
```

Payload 故意写成 `p,{id},"quoted"#{id%7}, end `（**内含逗号、双引号、尾随空格**），
写入侧按 RFC4180 双写引号，校验侧用状态机解析（`FieldStart / Unquoted / Quoted / AfterQuote`，
裸引号、闭合引号后夹字符、未闭合引号都直接抛）。所以 C 的“无交错”不是肉眼看出来的：
**任何撕裂/串行都会变成列数不符或 payload 回读不符**。

---

## 4. 补充证据（超出六条的实测）

### 4.1 有界队列真的在施压生产者（D2）

Round 1：两个生产者共 `TryWrite` 失败重试 **5,494,038** 次（容量 256 + 1ms/条，队列长期顶满）。
说明 256 深度对 1ms 级命令只是 **~275ms 的缓冲**，不是“容量”。

### 4.2 `FullMode=DropWrite` 的 `TryWrite` 返回值不可信（E2）

```
DropWrite: TryWrite 返回 true=100  实际可读出=4
```

容量 4、写 100 条：**100 次全返回 true，真正入队只有 4 条，静默丢 96 条**。
→ 生产必须用 `BoundedChannelFullMode.Wait`，否则“入队成功”是假的。

### 4.3 UI 线程被慢命令占住时的排队延迟（H1）

UI 线程被一条 800ms 命令占住（实测 830.07ms），随后排入 10 条命令：

| 命令 | 排队延迟 |
| --- | --- |
| 第 1 条 | **735.10ms** |
| 第 2–10 条 | **0.67ms**（每条之间几乎无间隔） |

→ 延迟**直接相加**在 UI 占用时长上；消费者串行 `await`，所以只有排在最前面的那条吃满阻塞。
**成本是“单条命令在 UI 线程上的时长”，不是队列深度。**

### 4.4 生产拓扑：4 条每会话有界队列 → 1 个全局泵（I1）

`4 会话 × 250 条`，每会话 `Bounded(cap=64, SingleWriter=true)`，全局泵用
`WaitToReadAsync + WhenAny` 合并，逐条 `await dispatcher.InvokeAsync`，写**同一个**审计 CSV：

```
sessions=4 x 250  capacity/session=64  executed=1000  elapsed=1017.32ms
audit writer thread(s)=1 (tid=5)
[PASS] I1 记录=1000 全局 Seq 严格递增=True 唯一(会话,命令)=1000 每会话 FIFO 保持=True
        CheckAccess 全 True=True 单写线程=True
```

**每会话 FIFO 保持**（同会话的 `CommandId` 严格递增）且**全局 Seq 严格递增 1..1000** ——
即“全局单泵”给出的是一条全局全序，审计顺序可复现。

### 4.5 变异模式（负对照：证明这些断言会红）

`--mutation` 只做两件生产禁止的事：① 领域调用**不走 dispatcher**，直接在消费者线程执行；
② 生产者线程**直接写同一个 `StreamWriter`**（第二/第三个写者）。

```
[FAIL] A records=1035 parsedIds=995 unique=995 executed=1000      ← 5 条命令记录被撕掉
[FAIL] B CheckAccess=True 行=0/1035 执行线程数=1 (6) Debug.Assert 失败=1000
[FAIL] C 列数≠5 行=39                                            ← 交错写
RESULT: 15 PASS / 3 FAIL
```

同一份代码、只违反两条纪律，A/B/C 精确变红；其余 15 条仍绿 → 这三条断言是**活的**，
不是恒真。变异产物 `round1-commands-mutation.csv` 有 **1646 行**（正常应为 1040 行左右），
多出来的就是被撕开的残行。

---

## 5. 对 M0 的结论

### 5.1 全局命令泵是否可行 —— **可行，且不需要锁**

- **单写者成立的前提是“所有领域调用都在 UI 线程执行”**，不是“泵只有一条循环”。
  实测：无论 1 个通道 2 个生产者（Round 1），还是 4 个会话通道 1 个泵（Round 5），
  执行线程集合恒为 `{5}`、`CheckAccess` 恒 True、CSV 零撕裂。
- **泵必须跑在“没有 SynchronizationContext”的线程上**（线程池线程即可）。若把
  `await foreach` 起在 UI 线程上，`await` 会捕获 `DispatcherSynchronizationContext` 并把续体
  投回 UI 线程——**（推论，本探针未单独实测）** 不会立刻死锁，但泵就退化成“UI 线程上的一个循环”，
  此时任何同步等待（`.Result` / `.Wait()`）都会变成死锁。
- **两条禁令（都有实测支撑）**：
  - 工作线程**绝不用**同步 `Dispatcher.Invoke` 派发领域调用（F5：1.5s 直接卡死）；
  - UI 线程上**绝不对** `DispatcherOperation` 用 `.Result` / `.Wait()`（F1/F4 表明正在执行的操作
    无法被中止，同步等它就是把自己焊死）。
- **审计单写者不需要额外的锁或队列**：把 `ScadaAuditWriter` 的每次写都放在泵的
  `InvokeAsync` 回调里（即 UI 线程），`StreamWriter` 就永远只有一个写线程。变异模式 ② 展示了
  违反它的后果（39 行撕裂、5 条记录消失）。

### 5.2 超时该怎么写

| 需求 | 写法 | 依据 |
| --- | --- | --- |
| 只限制**等多久** | `await op.Task.WaitAsync(timeout)`，`catch (TimeoutException)` | F2：2005ms 准时抛，工作仍在 3013ms 跑完 |
| 想在**排队阶段**就取消 | `InvokeAsync(cb, priority, ct)`，ct 在 Pending 时有效 | F1b：`Pending → Aborted`，回调不执行 |
| 想中止**正在跑**的领域调用 | **做不到** | F1/F4：`Abort()` 返回 false，token 无效 |
| 语义口径 | **超时 ≠ 没执行**。`WaitAsync` 超时后操作仍在 UI 线程跑完，审计必须由**回调内部**写，不能由调用方在超时后补写 | F2 的 `workCompletedAt=3013ms` |

因此生产的正确形态是：**命令自带软预算（客户端侧 `WaitAsync` 给 Web 请求一个明确的
“已受理/执行中”答复）+ 泵侧看门狗（记录超过 N 秒的命令并告警）**，而不是指望 `Abort()` 把
UI 线程从一段慢领域代码里救出来。

### 5.3 背压该怎么写

| 策略 | 写法 | 依据 |
| --- | --- | --- |
| 让网络线程**等一下**（推荐） | `await channel.Writer.WriteAsync(cmd, ct)` + `FullMode=Wait` | 真实背压，零丢失；`TryWrite` 自旋重试 550 万次只是空转烧 CPU（D2） |
| 明确**拒绝**（需要快速失败时） | `TryWrite` 返回 false → 给客户端一个显式错误；`FullMode` 必须是 `Wait` | E：rejected=992 全部被记账，accepted+rejected=1000，已接受零丢失 |
| 想“丢最旧/丢新” | **不要用 `DropWrite`/`DropOldest`** | E2：`TryWrite` 返回 true 却静默丢 96/100 |
| 容量 | 每会话**小**（如 16–64），不是 256 | 深度=纯延迟：Round 1 在 256 深度下 p50 排队 **275ms**；H1 慢命令 830ms → 首条排队 735ms |

### 5.4 本探针**没有**覆盖的部分（不要据此下结论）

- `ScadaRuntime` / `ScadaActionDispatcher` / `ScadaAuditWriter` 的真实耗时与真实异常路径
  （本探针用 1ms 自旋 + 合成 CSV 代替）；
- WPF 渲染/布局对 UI 线程的争用（本探针没有窗口，只有 dispatcher）；
- 磁盘 I/O 抖动对审计写入的影响（`StreamWriter` 只有 64KB 缓冲，未做 fsync）；
- 多窗口/多 dispatcher（本仓主界面是单 `Application` 单 UI 线程）；
- 进程退出时的在途命令（本探针是“生产结束 → Complete → 排空 → 关 dispatcher”的干净顺序；
  生产需要考虑 `OnExit` 时 `Cancel()` + 排空预算，否则最后几条命令会丢）。
