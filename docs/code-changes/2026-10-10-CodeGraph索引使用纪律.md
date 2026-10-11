# 2026-10-10 CodeGraph 索引使用纪律（接入实测 + 固化规则）

- 日期：2026-10-10
- 范围：**工具链纪律**（不涉及产品代码）；影响对象：本工作区所有会话的 AI 助手（主会话与子智能体）
- 起因：CodeGraph MCP（v0.21.0）接入 `D:\C#\VM` 后，需要界定"什么时候信它、什么时候不用它、什么时候必须重建索引"。对应 AGENTS.md 第 8 节 R28/R29。

---

## 一、接入现状（2026-10-10 实测）

| 项 | 值 |
| --- | --- |
| 引擎 | `C:\Users\16958\.codegraph\bin\codegraph-server.exe` v0.21.0（Rust，Apache-2.0，github.com/codegraph-ai/CodeGraph） |
| 接线 | ZCode stdio MCP；workspace = `D:\C#\VM`，排除 bin/obj/.vs/packages/.git（`%USERPROFILE%\.zcode\cli\config.json`） |
| 数据 | `C:\Users\16958\.codegraph\graph.db`（~62 MB，RocksDB 系 .sst）+ `projects\vm-fb74\` |
| 新鲜度 | 当日新落代码已在索引内：`ParallelGroupEditModel`、`ProcessViewModel.RenameParallelGroup`（`ProcessViewModel.cs:571`）均命中 → **索引当前可信** |
| 性能 | 符号查询 20~40ms 返回 |
| 界面 | **无图形界面**。`--help` 仅五种模式：MCP / LSP（stdio）/ `--run-tool`（一次性运行）/ `--watch`（守护）/ `--serve`+`--connect`（常驻引擎）；输出全是 JSON/文本。"可视化"只存在于消费方（AI 会话转述、编辑器 LSP 集成——本次未验证存在对应编辑器扩展） |

## 二、三个盲区（实测证据）

| # | 盲区 | 实测 | 影响 |
| --- | --- | --- | --- |
| 1 | **XAML 不在索引** | 搜 `GridListViewStyle` 返回 5 条全是 `.cs`（`SolutionListView.xaml.cs`、`UIThemeSmokeTest\Program.cs` 等），`UI\Controls\Themes\Controls\GridView.xaml` 本体无任何节点 | R19-R22 的战场全在 XAML 层 → 该层一律 grep |
| 2 | **源生成器产物看不到** | `[StepConfig]` partial property 的生成半边（属性实现/钩子声明）编译期展开，索引只有手写半边 | R16-R18 相关排查一律 grep |
| 3 | **第三方源码稀释结果** | `DLL\HslCommunication`、`VM3.0-main` 已入索引；`find_entry_points` 首轮 15/15 全是第三方函数；宽泛词（`Process`）10 条里 8 条是第三方重载 | 查询词必须带项目前缀/具体类型名 |

另：`memory_store` 记忆库当前为空（0 条）——`memory_search` / `memory_context` 查不出东西，要用需先存。

## 三、固化规则

### R28 大改动后跑一次 `codegraph_reindex_workspace` 保持索引新鲜

- 触发判据同 R12 闭环范围：公共契约、多模块、大量文件增删/改名（`StepModel` 子类新增、契约签名变更等）落地后重建；
- 增量即可；解析器/索引数据异常时用 `force`（全量）；单文件小改可用 `codegraph_index_files` 点对点更新；
- **不重建 = 静默少返回**（新调用方/新符号查不到）——比报错更隐蔽，与红线③"产物陈旧"、R16 同类坑；索引陈旧时的"影响面为空"会被误读成"没人引用"。

### R29 查询分工：公共契约影响面优先 CodeGraph，XAML 与生成器相关一律 grep

**优先 CodeGraph**（面向公共契约/核心类型的结构查询）：

- "谁引用了 X / 谁调用了 X" → `codegraph_get_callers` / `codegraph_find_by_imports`
- 影响面 / 爆炸半径 → `codegraph_analyze_impact`（服务 R12 大改动闭环的影响面盘点）
- 精确定位同名符号 → `codegraph_symbol_search`（C# 里 `Process` 这类名字 grep 命中一片，符号查询一次落到文件行）
- 结构体检 → `codegraph_find_circular_deps` / `codegraph_find_dead_imports` / `codegraph_get_module_summary`

**一律 grep/Read、不用 CodeGraph**：

- XAML 层（`.xaml` 模板/样式/绑定/资源）——盲区①，R19-R22 全在此层
- 源生成器相关（`[StepConfig]` partial property、钩子、生成产物）——盲区②，R16-R18 相关排查
- `.ps1` / 清单 / JSON / 文档等非代码文件

**查询词纪律**：带项目前缀或具体类型名，避免被第三方源码淹没（盲区③）。

## 四、定位（一句话）

CodeGraph 是**探索期望远镜**：给"改契约前看爆炸半径"、"同名符号定位"、"结构体检"提供一次到位的结构化答案；**不替代守门体系**——对错判定仍归 `FlowCanvasChecks` / `UIThemeSmokeTest` / `ScadaChecks` / 渲染探针的 pass/fail 断言。
