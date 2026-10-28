# 2026-10-27 Excalidraw 插件安装 / R28 修正 / CodeGraph 重建

## 当前状态

| 项 | 状态 | 判据 |
|---|---|---|
| excalidraw 0.3.4 | 已落盘 + 已登记，**待重启生效** | `cache\zcode-plugins-official\excalidraw\0.3.4\.zcode-plugin\plugin.json` 可解析（name=excalidraw, mcp=excalidraw, ui=1）；两个注册表均已写入 |
| AGENTS.md R28 | 已改为"按客户端判" | 55949 bytes（上限 56000，余 51）；严格 UTF-8 解码通过；行数 425 |
| DSH 镜像 | 已同步 | `tools\sync_constraints.ps1 -Check` → `IN SYNC`，7 条规则，块大小 3508 不变 |
| CodeGraph 索引 | 全量重建完成 | 1980 文件 / 3415 类 / 24439 函数 / 673,017 行；`symbol_search` 28ms 命中 |

## 已做

### 1. 安装 excalidraw 0.3.4（手工，无 CLI 可用）

Desktop 没有独立 CLI（`resources\tools` 只有 `cua-helper`/`ripgrep`/`ugrep`），安装按市场清单手工走四步：

1. 从 `https://cdn-zcode.z.ai/zcode/official-plugin/plugins/excalidraw/0.3.4/plugin.zip` 下载，16,278,945 bytes，sha256 = `3ff2bbe7…ed7f3`，与 `marketplace.json` 的 `_artifact.sha256` 一致。
2. 解压到 `C:\Users\16958\.zcode\cli\plugins\cache\zcode-plugins-official\excalidraw\0.3.4\`（版本目录直接放插件内容，与 `node-repl-host` 等既有件同构）。
3. `cli\plugins\installed_plugins.json` 追加条目（id=`excalidraw@zcode-plugins-official`，scope=`user`，source=`url`）。
4. `cli\config.json` 的 `plugins.enabledPlugins` 追加 `"excalidraw@zcode-plugins-official": true`。

两个注册表改动前均备份为 `*.bak-20261027`。依赖 `node`（plugin.json 里 MCP 命令是 `node dist/server.mjs`），本机 `node v24.19.0` 已在 PATH。

### 2. 修正 R28

原文断言"本会话无 CodeGraph 工具"，与实测不符（`cli\config.json` 的 `mcp.servers.codegraph` 已启用、workspace 指向 `D:\C#\VM`、`symbol_search` 正常返回）。改为按客户端判：ZCode 端已挂 → 跑 `codegraph_reindex_workspace`；未挂的会话（DSH）交付必须写明"影响面为 grep/read 结论、非索引结论"。改写控制在 +16 bytes，未撞 56000 上限。

### 3. 重建 CodeGraph

`codegraph_reindex_workspace(force=true)`，21:30 起、21:59:33 结束，约 29 分钟。

## 未做

- **重启 ZCode 验证 excalidraw 真能出面板**：MCP server 与 UI surface 在会话启动时装配，当前会话工具列表里没有 excalidraw 的 MCP 工具，属预期，不是安装失败。
- 未跑 `tools\run_gates.ps1`：本轮不动 C#/插件产物，红线③的产物判据不适用；AGENTS.md 属注入层，`audit_root`/`audit_artifacts` 与本次改动无关。唯一受影响的 `sync_constraints` 已单独跑过 `-Check`。
- 未验证 excalidraw 的 node MCP server 握手（需重启后才有机会）。

## 下一步

1. 重启 ZCode → 侧栏应出现「Excalidraw 画板」surface；让它画一张流程图作为验收。
2. 若重启后仍未加载：核对 `cli\config.json` 的 `enabledPlugins` 键与 `installed_plugins.json` 的 `id` 是否被应用改写；回滚用 `*.bak-20261027`。
3. 后续大改动按新 R28 跑 `codegraph_reindex_workspace`——日常增量即可，不必 `force`。

## 踩过的坑

1. **`codegraph_reindex_workspace` 客户端 300s 超时 ≠ 服务端失败**。MCP 的 `timeoutMs` 是 300000，全量重建要 29 分钟；客户端放弃后服务端继续跑。判据看 `%USERPROFILE%\.codegraph\last-phase.<pid>.json` 的 `phase` 从 `index_embed` 变成 `serving`，以及该 PID 的 CPU 是否停止增长。
2. **重建期间连 `symbol_search` 也会一起超时**（同一服务端被占满）。不要反复重试，每次白等 300s；改成盯文件系统与进程。
3. **全量重建时进程工作集涨到 17GB，系统可用内存一度只剩 785MB**。重建期间别并行跑构建或断言宿主。
4. **改 AGENTS.md 任何一个字都会让 `sync_constraints -Check` 判漂移**：镜像块头部嵌了源文件的字节数 + mtime。必须重跑生成脚本，不许手改镜像侧（R9B）。
5. **AGENTS.md 已到 55929/56000**，任何改写都要先量字节增量再落笔，否则撞上限就得下沉分册。
6. **市场下载件与内置件不同构**：内置件（browser-use 等）在 cache 里带 `.zcode-plugin-seed.json`，市场下载件不需要，只有 `.zcode-plugin/plugin.json`。

## 追加：重建后 17GB 内存不释放

- **现象**：重建结束后 `codegraph-server`（PID 41200）CPU 归零，但工作集稳定在 17,146 MB，整机 32GB 只剩 1,894 MB。
- **原因**：`force=true` 全量重建要对 24,439 个函数跑 `--full-body-embedding`（默认 true）的 ONNX 推理，ORT 的 CPU arena 保留峰值分配、不还给系统；进程转入 `serving` 后这 17GB 私有提交一直挂着。
- **处置**：用 `EmptyWorkingSet`（psapi.dll，`OpenProcess` 权限 0x0500）强制还工作集 → WS 17,146 MB → 60 MB，可用内存 1.9GB → 4.0GB（随后 7.8GB），**进程与 MCP 连接都没断**（`symbol_search` 仍正常，耗时 28ms → 265ms，差值是页面回读开销）。
- **判据**：`PrivateMemorySize64` 仍是 17,447 MB，只是被换出；**别拿"私有提交没降"当没生效**，看 `WorkingSet64` 与系统 `FreePhysicalMemory`。
- **规避**：日常只跑增量（`force=false`），别把 `force=true` 当常规操作。复发时两条路：① 同法再 trim；② 给 MCP 参数加 `--graph-only`（不加载 ONNX、不生成嵌入，结构性工具全保留，帮助原文写明"Semantic search and similarity tools are unavailable"）或 `--full-body-embedding=false`（只嵌名字+签名）。trim 脚本：`%TEMP%\trim_codegraph_ws.ps1 <pid>`。
