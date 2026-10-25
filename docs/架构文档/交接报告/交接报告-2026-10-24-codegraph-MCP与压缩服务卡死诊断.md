# 交接报告 2026-10-24 · codegraph MCP 与压缩服务卡死诊断

> 触发：另一会话界面停在 `工具调用 · compress`，显示「深度求索中，用时 13 分 17 秒」不动。
> 本文只记录**观测到的进程/日志事实**与由此得出的推断，未做任何修改性动作。

## 1. 当前状态

两个独立故障同时存在，只有第一个会让人「看起来卡住」：

| 现象 | 事实 | 判据 |
|---|---|---|
| 会话停在 compress | 压缩后端 `billion-context`（node PID 49016，监听 `127.0.0.1:18787`）曾长时间处于**单线程同步阻塞** | 3 秒内消耗 2.88 秒 CPU（≈96% 单核）；`GET http://127.0.0.1:18787/` 6 秒超时；18787 端口堆积 **45 个 CloseWait** 连接（客户端已放弃、服务端未回收） |
| 该后端已自愈 | 16:37 复测：CPU 增量 0.5s/3s、CloseWait 归 0、`GET /` 返回 **200** | 说明是「一次超长同步任务」而非永久死锁 |
| codegraph MCP 已死 | `.codegraph\daemon.log` 最后一行 `16:32:17 Shutting down (idle timeout; clients=0)`，**之后没有任何新的 `Listening on` 行** | 守护进程 300s 空闲自退后再没起来 |
| 僵尸 MCP 进程 | 12 个 `codegraph-server.exe --mcp --workspace D:\C#\VM` 仍存活，父进程均为 ZCode PID **3128**，启动时间集中在 16:31:43–16:33:39 | 全部 CPU≈0（卡在启动阶段，未走到写日志那步） |

截图里那一串 `退出码 1` 就是 codegraph 工具调用：守护进程已退、新起的 `--mcp` 进程起不来 → 每次调用都失败 → 客户端重连又拉起一个新进程 → 2 分钟内堆出 12 个。

## 2. 已做

- 只做只读取证：进程表 / 端口连接状态 / `.codegraph\daemon.log` / `.codegraph\codegraph.db`（172 MB）/ ZCode 应用日志尾部。
- 排除了两个错误方向：
  - **不是模型卡住**。同机另一个会话的 `model-io-*.jsonl` 显示请求 3–15 秒稳定返回、`finishReason=tool-calls`，模型侧健康。
  - **不是 codegraph 在跑长任务**。12 个进程 CPU 全≈0，且 daemon.log 无新行——它们是卡死不是干活。

## 3. 未做

- **没能读到那个会话自己的转录**。`.zcode\cli\rollout\` 下只有 3 个 `model-io-*.jsonl`（本会话 + 一个已正常结束的会话）；活跃存储实际在 `.dsh\sessions\--D-C~0023-VM--\`（`.jsonl.zstd` 压缩，未解）。
  → 因此「它卡在 compress 上」是**推断**（依据：截图最后一步 + 压缩后端当时确实不可用），不是直接读到的会话状态。
- 没有杀任何进程、没有重启任何服务（属于破坏性动作，等确认）。

## 4. 下一步

1. 在那个会话里**主动中断当前轮**（Esc / 停止），不要继续等。
2. 清掉僵死的 MCP 进程，否则下一次 codegraph 调用仍会失败：
   ```powershell
   Get-Process codegraph-server -ErrorAction SilentlyContinue | Stop-Process -Force
   ```
3. 触发一次 codegraph 调用，确认 `.codegraph\daemon.log` 出现**新的** `Listening on \\.\pipe\codegraph-...` 行（这是唯一可信的「真的起来了」判据，别看出厂退出码）。
4. 压缩服务已恢复 200，可以重试 compress；但上下文本身很大，单次 compress 慢是正常的——判据是 18787 有没有继续堆 CloseWait。
5. 结构性建议：`codegraph daemon` 的 300s 空闲自退 + MCP 端无限重连，会稳定复现「越查越糟」。要么把空闲超时调大，要么在 MCP 客户端侧做重连退避。

## 5. 踩过的坑

- **`sess_75217c67` 是我自己**：图片缓存的目录名用的是当前会话 id，不是截图里那个会话的 id。按它去找转录会读到自己的日志，并得出完全相反的结论（「14 次请求都成功」）。
- **多行 `powershell -NoProfile -Command "..."` 在本机静默零输出**（两次）：既没有 stdout 也没有 stderr，看起来像命令没执行。改用 `-File` + 临时 `.ps1` 才拿到结果。与 R183 同源：零输出不能当成「没事」。
- **CPU 快照不能只看总量**：49016 累计 1261s CPU 看不出问题，要取 3 秒增量才看得出 96% 单核。
