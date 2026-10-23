# 开发说明（DatabaseWriteRowPlugin）

## 定位

`Plugin.Database` 的"写一行"步骤：INSERT / UPSERT，输出 `Accepted` / `AffectedRows` /
`ElapsedMs`。三个步骤共用 `DatabasePluginBase`，本类只声明
`RequiredKind = WriteRow`、输出行、`RunAlgorithm`。

## 为什么写步骤默认 FailFast = true

追溯数据不能有静默缺口。写失败继续跑，损失在三个月后才以"查不到这件"的形式暴露，
且无法判断从哪天开始丢。默认停线是产品决策，不是实现细节。

对应实现：

```csharp
public DatabaseWriteRowPlugin() : base(failFastDefault: true) { }
protected override bool RethrowOnException => FailFast;
```

`VisionPluginBase.Execute` 默认把异常吞成 `Success = false`（流程继续）。
只 `throw` 而不覆盖 `RethrowOnException` 等于没停线——这是最容易改坏的一处，
`DatabaseChecks` 有"FailFast=true 必须抛 DatabaseStepException"的断言钉住。

## 配置模型

| 字段 | 说明 |
| --- | --- |
| ProfileId | 数据库稳定身份 |
| StatementId | 语句稳定身份 |
| Parameters | 参数模式快照（名字/类型/必填/默认值） |
| FailFast | 默认 true |
| TimeoutSeconds | 0 = 用 profile/语句值 |

`.vms` 不含 SQL / 连接串 / 密码。

## 端口重建

与 Query 完全一致（见 `DatabaseQueryPlugin` 的开发说明）：

```text
ApplyConfigValues:
  _lastStepData = stepData
  base.ApplyConfigValues(stepData)   // [StepConfig] + InputValues
  RebuildParameterPorts()            // 按快照建端口 + 回灌常量
  RebuildOutputRows()
```

必须放 `ApplyConfigValues`：流程编译器对运行实例只调它（`Engine\FlowCompiler.cs:883`）。
换语句时 `ApplyStatementSchema` 重建参数表并清掉 `.vms` 里已不存在的参数键。

## 执行链

```text
RunAlgorithm(context)
  values  = BuildParameterValues()        // 每个参数从同名 InputPort 取值
  options = BuildOptions(context)         // 超时 + CancellationToken
  context.Database.TryExecute(StatementId, values, options, out result, out error)
     → DatabaseService.TryResolve         // 语句/档案启用、存在性
     → StatementValidator.Validate        // 保存期规则的再校验
     → DbExecutorBase.TryExecute        // 事务 + 参数绑定 + 影响行数上限（SQLite / MySQL 共用）
  Accepted.Value     = true
  AffectedRows.Value = result.AffectedRows
  ElapsedMs.Value    = result.ElapsedMs
```

`TryExecute` 同时服务 Execute 与 WriteRow 两种语句；服务端只拒绝 `Query` 类型。
本类不做类型判断——语句类型在保存期已与步骤类型绑定校验。

## Accepted 的语义

`Accepted = true` 表示"数据库受理了这次调用"，不是"新增了一行"：

- 普通 INSERT 成功 → true / 1；
- `ON CONFLICT DO NOTHING` 命中已有行 → true / 0；
- 失败 → 步骤失败（FailFast 时停线）。

不要改成 `Accepted = AffectedRows > 0`：那会把"已存在、无需更新"误判成写失败，
下游若按 Accepted 做 NG 处理会整批误判。

## 服务端保证

- 一次调用一个事务，失败整条回滚（MySQL 需 InnoDB；MyISAM 表不支持事务，回滚不生效）；
- 参数只走 `DbParameter`（名字 + `DbType` + 值），没有字符串拼接入口；
- 影响行数超过语句上限时回滚（不是截断）；
- 超时/取消 → `OperationCanceledException` 原样上抛 → 引擎记 `Skipped`；
- 审计日志：profile 名、语句名、行数、耗时、操作者，**不记参数值**；
- 只读档：SQLite 在连接串层强制；MySQL 靠语句类型白名单（建议账号只授 SELECT）。

Provider 分工：`DbExecutorBase` 统一事务/绑定/上限，`SqliteDbExecutor` 与
`MySqlDbExecutor` 只负责连接串、会话设置、版本查询与清池。MySQL 密码用 DPAPI
加密存在 `databases.json`（`PasswordCipher`），`.vms` 与插件都看不到。

## 配置窗口

与另外两个步骤共用 `DatabasePluginView`：

- profile/语句下拉来自 `PluginConfigContext.Database`（宿主单例）；
- 参数行用 `LinkableValueEditor`，可常量可链接；
- 「测试语句」走 `TryTestStatement(..., commitForReal: false)`，banner 明示已回滚。

## 不变量

1. 参数端口只在 `ApplyConfigValues` / 换语句时重建；
2. 端口名 = 参数名（不含 `@`）；
3. `Accepted` 与 `AffectedRows` 语义不能合并；
4. 写步骤默认停线，改默认值属于产品口径变更，必须过评审；
5. 插件不引用 Provider、不拼 SQL、不持有连接。

## 断言覆盖

- 插件层：成功输出映射、`FailFast` 抛/不抛、必填参数确认拦截、换语句清旧键；
- 服务层：INSERT 落库、UPSERT 行为、约束冲突分类、事务回滚、影响行数上限；
- 静态：插件工程/源码/产物三层不出现 Provider 类型与 `Core.Database` 引用。
