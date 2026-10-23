# 开发说明（DatabaseQueryPlugin）

## 定位

`Plugin.Database` 程序集里的三个步骤之一，共用 `DatabasePluginBase` 底板。
本类只负责"查询"这一种语义：类型校验、输出映射；执行、事务、参数化都在宿主
`Services\Core.Database`，插件不引用任何 Provider。

宿主执行层是 `DbExecutorBase` + 两个 Provider（`SqliteDbExecutor` / `MySqlDbExecutor`）：
参数绑定、事务边界、截断、上限、错误文案只有一份；Provider 只给连接、会话设置、版本查询与清池。
MySQL/MariaDB 的密码用 DPAPI 加密存在 `databases.json`，插件与 `.vms` 都看不到。

## 继承关系

```text
VisionPluginBase (Core.Interfaces)
  └─ DatabasePluginBase (Plugin.Database)
       ├─ DatabaseQueryPlugin      RequiredKind = Query,    FailFast 默认 false
       ├─ DatabaseExecutePlugin    RequiredKind = Execute,  FailFast 默认 true
       └─ DatabaseWriteRowPlugin   RequiredKind = WriteRow, FailFast 默认 true
```

底板负责：profile/语句选择、参数端口重建、输出行、配置窗口测试、失败语义。
本类只给三样东西：`RequiredKind`、`CreateOutputRows()`、`RunAlgorithm()`。

## 配置模型（落 .vms 的字段）

| 字段 | 类型 | 作用 |
| --- | --- | --- |
| ProfileId | Guid | 数据库稳定身份（显示名可改） |
| StatementId | Guid | 命名语句稳定身份（全局唯一） |
| Parameters | ObservableCollection<DbStatementParameterBinding> | **参数模式快照**（名字/类型/必填/默认值） |
| FailFast | bool | 失败是否中断当轮 |
| TimeoutSeconds | int | 0 = 用 profile/语句上的值 |

`.vms` **不存 SQL、不存连接串、不存密码**。参数快照只用于编译期建端口，
服务端执行时会按语句当前定义再做一次权威校验。

## 为什么参数快照必须落盘

流程编译器对运行实例**只调 `ApplyConfigValues`**（`Engine\FlowCompiler.cs:883`），
不调 `Initialize`。那个时刻没有宿主、查不到语句定义；不存快照就建不出参数端口，
表现为运行期"参数缺失"且不报错。

所以 `ApplyConfigValues` 的顺序是固定的：

```text
_lastStepData = stepData
base.ApplyConfigValues(stepData)      // 灌 [StepConfig]，含 Parameters
RebuildParameterPorts()               // 按快照建 InputPort<object>，并把 InputValues 里的常量顶回端口
RebuildOutputRows()
```

**不能把重建放进 `Initialize`**：配置窗口会调它，运行实例不会。

## 端口重建的两个坑

1. **常量值会丢**：`base.ApplyConfigValues` 先跑，那时端口还不存在，它只把值写进
   `InputValues`。端口建好后必须按 `InputValues` 回灌一次（`IsLinked` 的跳过）。
2. **旧参数键会残留**：换语句时 `ApplyStatementSchema` 会把已不存在的参数名从
   `InputValues` 里删掉，否则 `.vms` 里留一堆无主键。

端口一律 `IsRequired = false`：必填语义由服务端按"定义 + 默认值"判；
端口层标必填会把"用默认值"误杀成"参数缺失"。

## 失败语义（最容易改坏的一处）

`VisionPluginBase.Execute` 默认**吞异常**：`Success = false`，流程继续。
所以只抛异常是停不了线的。本步骤的做法：

```csharp
protected override bool RethrowOnException => FailFast;

protected void HandleFailure(string error)
{
    if (FailFast) throw new DatabaseStepException(error);
    Fail(error);
}
```

- `FailFast = false`（查询默认）：`Fail()` 写 `ErrorMessage`，步骤红、线继续；
- `FailFast = true`：抛 → 基类记 `ErrorMessage` → `RethrowOnException` 放行 →
  本节点 Failed 且异常中断当轮。

`OperationCanceledException` 不在此列：服务层在令牌已取消时原样上抛，
基类也原样上抛，引擎记 `Skipped`（停止不是失败）。

## 输出映射

`RunAlgorithm` 调 `context.Database.TryQuery(StatementId, values, options, out result, out error)`：

| 输出 | 来源 |
| --- | --- |
| Scalar | `Rows[0][0]`，无行时 null |
| RowsJson | `BuildRowsJson(result)`（列名做键；数值/布尔/null 保形；字节转 Base64） |
| RowCount | `result.RowCount` |
| Columns | `string.Join(",", result.Columns)` |
| Truncated | `result.Truncated` |
| ElapsedMs | `result.ElapsedMs` |

`TryQuery` 返回 false 时走 `HandleFailure(error)`，**不要**自己拼错误文案：
服务端文案已经带错误类别、SQLite 错误码与下一步建议。

## 配置窗口

`GetConfigView` 返回 `DatabasePluginView`，DataContext 就是插件实例（与
DataRecord 同一范式）。窗口打开顺序：

```text
Initialize(stepData)          → 基类：StepData + ApplyConfigValues（建端口）
SetConfigContext(context)     → 宿主推 profile/语句候选 + IDatabaseAccess
```

`SetConfigContext` 只重拉候选，**不覆盖** `.vms` 里的参数快照。
选中语句时 `ApplyStatementSchema` 才按新定义重建参数表。

「测试语句」走 `TestAsync()`：`TryTestStatement(..., commitForReal: false)`，
事务内执行后回滚，结果只上屏、不抛。

## 不变量（改代码前先看）

1. 参数端口只在 `ApplyConfigValues` / `ApplyStatementSchema` / 手动换语句时重建；
2. 端口名 = 参数名（不含 `@`），与 SQL 占位符一一对应；
3. 插件源码里不许出现 `new SqliteConnection` / `SqlCommand` / SQL 字符串拼接；
4. 输出端口固定，不做"任意列映射成端口"；
5. 只读 profile + 写步骤在配置期就拒绝，不等到运行期。

## 断言覆盖

`DatabaseChecks` 的插件层用假 `IDatabaseAccess` 验：

- 端口按快照重建、常量回灌、换语句清旧键；
- `FailFast` 抛 / 不抛两条路径；
- 输出映射（含 0 行、Truncated、RowsJson 形态）；
- 确认校验（必填参数、只读、类型不匹配）；
- 测试语句 banner（成功/失败/已回滚）。

服务层用真 SQLite 临时文件验参数化、事务、截断、影响行数上限。
