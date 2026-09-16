# 轮询批量规划器 PollBatchPlanner（t5）

日期：2026-09-17
主线：通信流程重构 —— t5「批量轮询规划器」

## 一、需求与决策

### 需求
旧实现的变量轮询是**逐变量一条读命令**（`AdvancedCommunicationManager.PollVariables` 反射调 `Read<T>`），
N 个变量 = N 次设备往返。在 20~50ms 轮询周期下，网络被打满、扫描时间被拖长、且每次都要反射解析地址字符串。

已确认方案（用户拍板）：**方案二 轮询批量化（Block Read）**，并且与方案一（专用线程）一起做。
t4 已完成专用线程（`ConnectionWorker`），本任务交付它的消费端：把变量清单编译成"少量段读 + 兜底单读"。

### 关键决策
| 决策点 | 选择 | 理由 |
| --- | --- | --- |
| 规划时机 | **编译期一次性规划**，运行期零解析零反射 | 段划分、段内偏移、位序号、目标类型全部预算好；轮询热路径只剩 IO + 切片解码 |
| 分组键 | `协议 + 存储区`（`PollProtocol` + `PollAddress.GroupKey`） | 跨存储区/跨协议不可能共用一次块读（S7 分区、Modbus 功能码不同） |
| 合并策略 | 同组按 `Start` 排序后**贪心合并**：段跨度 ≤ `MaxSegmentUnits` 且相邻空洞 ≤ 间隙容忍 | 上限来自 HSL 的 `BuildReadModbusCommand`（位区 2000 / 寄存器 120）与 S7 PDU 保守值 110 |
| 间隙容忍 | S7 = 16 字节；Modbus 寄存器区 = 8 寄存器；Modbus 位区 = 64 点 | 太小则合并率低（仍是 N 次往返），太大则每轮白读大量无关数据 |
| 解码 | 段返回字节流 → 按段内偏移切片 → `HslHelper.ConvertToByType`（大端纪律已由 t3 统一） | 复用既有大端转换，避免二次实现字节序 |
| 位访问 | S7：段内取 1 字节 → 按 `BitOffset` 取位；Modbus 位区：`ReadBits` 返回 `bool[]` 直接取下标 | 位区不能用字节流解码（返回的是位打包数据） |
| 变长类型 | `string` / `byte[]` 必须来自**结构化地址配置**（`AddressConfig` 提供 `Length`）才参与批量 | 旧字符串地址推不出长度；长度错 = 读回来的内容整体错位 |
| 兜底 | 地址无法结构化解析 / 类型不可解码 / 类型字节数 > 地址跨度 / 单变量超过单请求上限 → 旧的按类型反射单读 | 保证"改不动的地方行为不变"，不因批量化丢变量 |
| 故障判定 | **本轮所有读都失败** 才返回 `false`（通信级故障，触发断线重连）；只要有一次读成功就按变量级错误消化 | 避免一个坏地址把健康连接打进无限重连循环；同时仍能识别真断链（真断链时所有读都失败） |
| 值域换算 | 工程值转换（`ConvertToEngineering`）**暂不接入**，与旧 `PollVariables` 行为保持一致 | 避免行为漂移；后续如需要单独评估 |

## 二、修改文件清单

| 文件 | 改动 |
| --- | --- |
| `Communication/Communications/Manager/PollBatchPlanner.cs` | **新增**（约 400 行）：规划器本体 |
| `Communication/Communications/Address/PollAddressResolver.cs` | 修复旧地址位访问歧义（见下） |

### 新增：PollBatchPlanner 组成
- `Build(variables, log)`：变量清单 → `PollBatchPlanner`
  - 跳过 `WriteOnly` 变量；解析 `ValueType`（剥 `Nullable`）；`PollAddressResolver.Resolve` 取结构化地址
  - 分支：可批量 / 兜底单读 / 跳过（写入编译期告警，不进热路径）
- `MergeSegments`：按 `(Protocol, GroupKey)` 分组 → 按 `Start` 排序 → 贪心合并
- `CompileItems`：把变量挂到段上，预计算 `UnitOffset`、`SpanUnits`、`BitOffset`、目标类型、是否 string/byte[]
- `Poll(connection)`：一轮轮询（仅 Worker 线程调用，内部无锁）
  - 位区段 `ReadBits("x=1;100", span)`；寄存器段 `ReadBytes("x=3;100", span)`；S7 段 `ReadBytes("MB100", span)`
  - 解码：`UnitOffset × BytesPerUnit` 定位字节 → 切片 → `HslHelper.ConvertToByType`；位访问单独取位
  - `string` 用 `Encoding.ASCII.GetString(...).TrimEnd('\0')`（与写链路 `HslHelper.GetValueArray` 的 ASCII 编码对称）
  - 失败日志按 key 节流（5s），避免按轮询周期刷屏
- 公开产物：`SegmentCount`（= 每轮设备往返次数，可用于验证合并效果）、`FallbackCount`、`PollItemCount`

### 修复：PollAddressResolver 的 S7 位地址歧义
`M10.0`（第 0 位）与 `MB10`（整字节）在 HSL `S7AddressData.ParseFrom` 结果里**同为 `AddressStart=80`、位偏移 0**，
旧的判定 `bitAccess = bitRemainder != 0` 会把 `M10.0` 当整字节读 → 8 个位里任意一位为 1 就读出 `true`，**值域放大 8 倍**。
现在追加"地址串是否以 `.0`~`.7` 结尾"（`HasExplicitBitSuffix`）作为显式位地址依据，`FromS7Parsed` 增加 `explicitBitAddress` 参数。
影响面：仅旧字符串地址回退路径（结构化配置走 `S7Address.IsBitType`，本来就正确）。

## 三、验证结果

- 编译：`dotnet build VisionMaster.sln` → `EXIT=0`，932 警告（存量），`VM.Communication.dll` 正常产出，本次新增代码 0 警告 0 错误
- 首次构建出现 3 个 `CS2001`（`Plugin.ImageScript` / `Plugin.CSharpScript` 的 WPF `_wpftmp` 临时工程找不到生成的 `.g.cs`），
  **复编译即消失** —— 属于构建残留/并发构建问题，与本次改动无关（同一现象此前在 `PreProcessingPlugin` 并发编辑时也出现过）
- 运行时效果（段数对比、值正确性）需 t6 接线后实测，本步骤仅交付可编译的规划器单元

## 四、已知边界

1. **尚未接线**：规划器还是"孤儿"——`AdvancedCommunicationManager` 仍走旧的 `PollVariables`（逐变量 Timer 反射）。
   t6 负责：`PollAction = planner.PollItemCount > 0 ? planner.Poll : null`，并在变量注册/注销时重建计划。
2. **工程值转换未接入**：`CurrentValue` 仍是原始值（与旧行为一致）；`DeviceAddressBase.EnableConversion` 在本链路暂不生效。
3. **变长类型依赖结构化配置**：`string`/`byte[]` 走旧字符串地址时直接跳过（旧逻辑本来就是 `Read<T> where T:struct` 必抛异常，属净改善但仍是"不轮询"）。
4. **Modbus 位区上限 2000 / 寄存器区 120** 来自 HSL 内部分包；若单变量跨度超上限（如 S7 超长字符串 >110 字节）→ 数值类型退单读、变长类型跳过并告警。
5. **间隙容忍是经验值**：地址稀疏（如每隔 100 寄存器一个变量）时合并率低，会退化成接近逐变量的往返次数——此时应调 `GapOf` 常量，或理解为"该场景本就该多读几次"。
6. **32/64 位字序仍为 ABCD**（沿用 t3 结论，与 HSL 默认 `DataFormat` 一致）；对端若是 CDAB 等字序需另配转换。
7. **单测缺失**：合并/解码逻辑尚无单元测试（工程内暂无通信测试工程），目前依赖编译验证 + t6 后的现场实测。
