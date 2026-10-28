# 屏蔽 vm-tools MCP（项目约束 R186）

> 日期：2026-10-27 ｜ 类型：配置与约束（不涉及产品代码）
> 关联：`AGENTS.md` §41 / **R186**、`.zcode\config.json`、`tools\oneoff\register-vm-mcp.mjs`、台账 §1.2

## 1. 当前状态

`vm-tools`（本仓 `tools\vm-mcp\` 自建的 MCP，23 个工具）在本项目**已屏蔽**：不注册、不调用。
用户级配置 `%USERPROFILE%\.zcode\cli\config.json` 里已无该条目（现存 `codegraph`、`agenthub-memory`），
工作区级 `.zcode\config.json` 显式声明 `enabled: false`，注册脚本默认拒绝。

| 验证项 | 结果 |
|---|---|
| `.zcode\config.json` 解析 | `mcp.servers["vm-tools"].enabled = False` |
| 用户级配置 | `servers = codegraph, agenthub-memory`（无 vm-tools） |
| `node tools\oneoff\register-vm-mcp.mjs`（不带参数） | 拒绝并退出 **1**（`$LASTEXITCODE=1`） |
| `AGENTS.md` 体积 | 55929 字节（< 56000 上限，余量 71） |
| `tools\sync_constraints.ps1 -Check` | `IN SYNC`，exit 0 |
| `tools\audit_root.ps1` / `tools\audit_docs.ps1` | 见下方第 2.4 节 |

## 2. 已做

### 2.1 约束层（AGENTS.md）

§41 表尾追加 **R186**：本项目不注册、不调用 `vm-tools` MCP（源码留 `tools\vm-mcp\`），测试走 §39；
守门为注册脚本默认拒绝。改动后字节数 55929（距 56000 上限余 71），严格 UTF-8 解码通过、无 BOM。

### 2.2 配置层（工作区 `.zcode\config.json`）

新建 `<repo>\.zcode\config.json`，把 `vm-tools` 声明在工作区作用域并置 `enabled:false`。
作用域选择理由：用户级配置对同名服务器**覆盖**工作区级，所以只在用户级删除条目不够稳——
一旦有人重跑注册脚本，用户级条目回来就把工作区块盖掉。工作区块负责"项目内默认关"。

### 2.3 写入端闸门（`tools\oneoff\register-vm-mcp.mjs`）

脚本开头加默认拒绝：不带 `--force` 直接 `exit 1`，并打印工作区块位置与"用户级会覆盖"的说明。
理由同上——配置层挡不住"有人再跑一次注册脚本"，必须同时掐住写入端。

### 2.4 文档层

- `docs\工程纪律\分册-AI操作与exe测试踩坑.md`：§1.2 开工前检查清单加"vm-tools 已屏蔽"一条；
  §0 快速索引 + §2.C 新增 **C-39**（`cmd` 里 `& echo EXIT=%ERRORLEVEL%` 提前展开，误报闸门没拦住）；
  §4 最近更新补一行。
- 本文件（R177 落盘物）。

## 3. 未做

1. **未重开会话验证工具列表**：MCP 在会话启动时连接，配置改动对当前会话无效。
   当前会话本来就没有 `mcp__vm-tools__*` 工具（启动时用户级配置里已无该条目），
   但"工作区块被客户端读到并显示为 disabled"只能在重开会话后从 Settings → MCP 确认。
2. **未删除源码与产物**：`tools\vm-mcp\`（含 `bin\Debug\net9.0-windows\vm-mcp.exe`）按 R186 原样保留，随时可恢复。
3. **未跑 `tools\run_gates.ps1` 全量**：本轮没有改产品代码、插件源码或断言宿主，只改约束/配置/一次性脚本。
   需要时按 R182 单独跑（判据：任一红即 exit 1）。

## 4. 下一步

- 重开会话后看 **Settings → MCP**：`vm-tools` 应显示为 `disabled`；工具列表里不应出现 `mcp__vm-tools__*`。
- 将来要恢复：`node tools\oneoff\register-vm-mcp.mjs --force`，同时删掉 AGENTS.md 的 R186 与工作区 `.zcode\config.json` 的 `enabled:false`，
  然后重跑 `tools\sync_constraints.ps1`（否则 `run_gates` 的 `sync_constraints` 步判红）。
- 若要把这条约束写进"注入层"之外的更多入口，注意 AGENTS.md 只剩 71 字节余量——按文件自述的"满了就下沉"先挪分册，不要再挤。

## 5. 踩过的坑

1. **用户级覆盖工作区级**：ZCode 的 MCP 同名服务器以用户级为准。只做"工作区 `enabled:false`"会被用户级条目整条盖掉，
   必须同时掐住写入端（注册脚本）。
2. **改 `AGENTS.md` 会让 DSH-SYNC 镜像漂移**：镜像块里嵌了源文件的字节数与 mtime，
   改完必须重跑 `tools\sync_constraints.ps1`，否则 `run_gates.ps1` 的 `sync_constraints -Check` 步判红。
3. **`%ERRORLEVEL%` 内联取不到刚跑完的退出码**（C-39）：cmd 整行解析期就展开了。
   本轮的"闸门是否拦住"是用 PowerShell `$LASTEXITCODE` 复测的（=1）。
4. **`ls` 在本机 cmd 下不存在**：首轮探测用了 `ls` 直接报"不是内部或外部命令"，本机 shell 是 cmd，用 `dir`。
