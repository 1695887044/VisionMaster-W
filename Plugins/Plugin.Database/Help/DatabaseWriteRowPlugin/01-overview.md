# 数据库写行

## 这个步骤做什么

按命名语句**写一行**（`INSERT`，含 `ON CONFLICT` 的 UPSERT），
用于每件产品的结果落库、追溯记录、MES 中间表写入。

输出：

- `Accepted`：语句是否执行成功（`true` = 数据库受理了这次调用）；
- `AffectedRows`：实际影响行数（UPSERT 命中已有行时可能是 0）；
- `ElapsedMs`：本次执行耗时。

## 支持的数据库

SQLite（本地文件）与 MySQL/MariaDB（服务器）。两者步骤配置完全一样；
MySQL 的密码由宿主用 DPAPI 加密保存，`.vms` 不含连接串与密码。

## 前提：语句必须是「写一行」类型

在「硬件管理 → 数据库 → 命名语句」里新建语句，类型选「写一行」。
保存时校验：

1. 只允许 `INSERT`（`INSERT OR REPLACE` / `ON CONFLICT ... DO UPDATE` 都算）；
2. 只允许单条语句；
3. 参数表与 SQL 里的 `@name` 占位符一一对应。

`UPDATE` / `DELETE` 请用「数据库执行」；`SELECT` 请用「数据库查询」。

## 典型语句

```sql
INSERT INTO result(lot, product_code, score, ok, created_at)
VALUES (@lot, @code, @score, @ok, @created_at)
```

去重写入（同一批号只保留一条）：

```sql
INSERT INTO result(lot, score, ok)
VALUES (@lot, @score, @ok)
ON CONFLICT(lot) DO UPDATE SET score = excluded.score, ok = excluded.ok
```

## 失败语义：默认停线

`FailFast = true`（默认）：写库失败会抛，步骤变红并**中断当轮**。

为什么写步骤默认停线：追溯数据不能有静默缺口。少写一行比停线更难发现——
三个月后查不到那件产品，谁也不知道是哪天开始丢的。

需要"写失败也继续"的现场可以显式关掉，但要清楚代价。

## `Accepted` 与 `AffectedRows` 的区别

| 场景 | Accepted | AffectedRows |
| --- | --- | --- |
| 正常插入一行 | true | 1 |
| UPSERT 命中已有行并更新 | true | 1 |
| `ON CONFLICT DO NOTHING` 命中已有行 | true | 0 |
| SQL 报错 / 约束冲突 | 步骤失败 | — |

`Accepted = true` 只代表"数据库受理了这次调用"，不代表"一定新增了一行"。
需要判断是否真的新增，用 `AffectedRows > 0`。

## 节拍与批量

v1 是**单行同步写**：一次调用一个事务，写完才返回。
高节拍产线如果写库成为瓶颈，优先检查：

- 是否有不必要的索引/触发器；
- 是否能合并成一条多行 `INSERT`（用「数据库执行」类型语句）；
- 是否该走本地 CSV + 异步上报，而不是每件直写中央库。

v1 不提供后台攒批/静态写 Hub：那会把"写失败"变成不可观测的后台错误。
