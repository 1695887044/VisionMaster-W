# 开发说明（DatabaseExecutePlugin）

## 定位

`Plugin.Database` 的"执行"步骤：UPDATE / DELETE，输出影响行数。
与 Query / WriteRow 共用 `DatabasePluginBase`；本类只声明
`RequiredKind = Execute`、输出行、`RunAlgorithm`。

宿主执行层是 `DbExecutorBase` + `SqliteDbExecutor` / `MySqlDbExecutor`；
事务、参数绑定、影响行数上限只有一份，Provider 只负责连接与会话设置。

## 配置模型

与 Query 相同（ProfileId / StatementId / Parameters 快照 / FailFast / TimeoutSeconds）。
`.vms` 不存 SQL、不存连接串。

## 语句校验（保存期 + 执行期各一次）

`Core.Database.StatementValidator` 对「执行」类型的规则：

```text
首词必须是 UPDATE 或 DELETE
必须出现 WHERE（去注释/字符串后再判）
禁止 ATTACH / DETACH
禁止写 PRAGMA（PRAGMA 只允许在「查询」里用只读白名单）
只允许单条语句（分号后不许再有内容）
参数占位符与参数表双向一致
```

为什么执行期还要再校验：`.vms` 可能被旧版本写入、`databases.json` 可能被手工改，
保存期校验不能当成运行期保证。

## 事务与上限

`DatabaseService.TryExecute` → `DbExecutorBase.TryExecute`（SQLite / MySQL 共用）：

```text
Open(profile)                        // ReadWriteCreate；ReadOnly 档写语句在服务层已被拒
BeginTransaction()
BindParameters(statement, values)    // 缺必填/多余/类型不符 → Rollback + 返回 false
affected = ExecuteNonQuery()
if (maxAffected > 0 && affected > maxAffected) { Rollback; 返回 false }
Commit()
```

`maxAffected` 来自语句的 `MaxAffectedRows` 与 `DbCommandOptions.MaxAffectedRows`
的非零最小值。回滚后行数不变——`DatabaseChecks` 有专门的断言。

## 失败语义

```csharp
protected override bool RethrowOnException => FailFast;   // 本类默认 true
```

`HandleFailure(error)`：

- `FailFast = true`：抛 `DatabaseStepException` → 基类写 `ErrorMessage` → 放行 →
  本节点 Failed 且中断当轮；
- `FailFast = false`：只 `Fail(error)`，步骤红、流程继续。

不要改成"只抛不覆盖 `RethrowOnException`"：基类会把异常吞成 `Success = false`，
表现为"步骤红了、线照跑"——写库失败继续生产是这条链上最坏的静默错误。

## 取消

`DbCommandOptions.CancellationToken` 来自 `IExecutionContext.CancellationToken`。
服务层在令牌已取消时抛 `OperationCanceledException`，基类原样上抛，
`CompiledPluginNode` 记 `Skipped`（停止不是失败）。不要把它转成 `Fail`。

## 输出映射

```text
AffectedRows.Value = result.AffectedRows
ElapsedMs.Value    = result.ElapsedMs
```

`AffectedRows = 0` 是成功：条件没命中任何行不是错误。
下游要"必须改到至少一行"由流程用 `If` 判，不在插件里替用户决定。

## 配置窗口

与 Query 共用 `DatabasePluginView`：

- 左栏 profile/语句 + SQL 只读预览；
- 右栏参数行（`LinkableValueEditor`）+ 输出行 + 测试 banner；
- 「测试语句」走 `TryTestStatement(..., commitForReal: false)`，事务内执行后回滚。

只读 profile + 本步骤：`ConfigProblem` 直接返回"只读数据库不允许写操作"，
确认被拒（`OnConfirm` 抛 `InvalidOperationException`，宿主显示原因且不关窗）。

## 不变量

1. 一次调用一个事务；不暴露连接/事务/reader；
2. 影响行数上限超限必须回滚（不是"截断到上限"）；
3. 失败文案来自服务层，插件不自己拼；
4. 插件源码不许出现 Provider 类型；
5. `MaxAffectedRows = 0` 表示不限制，不是"必须 0 行"。

## 断言覆盖

`DatabaseChecks`：

- 保存期：无 WHERE 被拒、ATTACH 被拒、参数不一致被拒、只读档拒绝写语句；
- 执行期：影响行数超限回滚且数据不变、缺必填/多余/类型不符被拒、禁用语句被拒；
- 插件层：`FailFast` 抛/不抛、输出映射、只读与类型不匹配的配置期预判；
- 静态：插件工程/源码/产物三层 Provider 边界扫描。
