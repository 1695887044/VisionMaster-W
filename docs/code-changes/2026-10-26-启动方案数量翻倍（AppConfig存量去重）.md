# 启动时方案数量显示成 16384（AppConfig 存量重复自愈）

日期：2026-10-26
范围：Core（AppSettingsService）、VisionMaster（ConfigCheck）、ScadaChecks（断言）

## 1. 现象

启动自检与「方案列表」弹窗显示“方案清单 16384 项”，而磁盘上只有一个方案。
`VisionMaster\bin\Debug\net9.0-windows\AppConfig.json` 涨到 27.4 MB，`Solutions` 里是同一路径的 16384 份副本。
历史备份里还能看到 4096 / 16384 / 65536——**全是 2 的幂**，这是“每次 Load 翻一倍”的指纹。

## 2. 根因

旧版 `AppSettingsService.Load()` 用 `JsonConvert.PopulateObject(json, Current, ...)` 原地填充，
而 Newtonsoft 对已存在的 `List<T>` 是**追加**而不是替换。`Load()` 在一次启动里会被调用多次
（构造器、ConfigCheck、模块初始化），于是清单 1 → 2 → 4 → …；任意一次 `Save()` 把翻倍结果写回磁盘。

10-25 已加 `ReplaceCollectionsContractResolver`（集合走替换）并补了 `[SL]` 幂等断言，
**追加缺陷不再发生**；但磁盘上那份 16384 重复还在，没有任何代码负责清理，所以显示仍然不对。

## 3. 修复

### 3.1 加载时清理存量（`Core\AppSettingsService.cs`）

`Load()` 在 `PopulateObject` 之后调用 `NormalizeLoadedCollections()`：

- 方案清单按 `Path` 去重（忽略大小写），保留第一次出现的顺序与 `Name/Comment`；
- 空 `Path` 的条目丢掉（打不开，也不该占计数）；
- `Path` 首尾空白 Trim（手工编辑 JSON 的常见形态）；
- 工具箱收藏按 id 去重（区分大小写）。

新增 `LastLoadNormalized`：本次 Load 是否真的清理过（宿主据此决定回写）。
同时补 `Solutions` / `Toolbox` / `Favorites` 的 null 防御（JSON 显式写 null 的节）。

### 3.2 启动时回写一次（`VisionMaster\Lifetime\Checks\ConfigCheck.cs`）

`Load()` 之后若 `LastLoadNormalized == true`，调用 `Save()` 把干净清单写回磁盘，
并落一行 Info：`[启动自检] AppConfig 已清理重复/空白条目（方案清单现 N 项）`。
回写失败只 Warn、不影响启动（内存里已经是干净清单，下次再清）。

为什么不让 `Load()` 自己 Save：Load 在断言与模块初始化里也会被调用，
“读配置顺手写盘”会把只读安装/并发场景变复杂；写盘留给明确知道“现在在启动链上”的 ConfigCheck。

## 4. 验证

- 构建：`dotnet build ScadaChecks\ScadaChecks.csproj -m:1 -nodeReuse:false` → **0 个错误**。
- `ScadaChecks.exe` → **3087 通过 / 0 失败**。新增断言：
  - 同一路径 3 份（含大小写变体、首尾空白）+ 1 份空白路径 → Load 后只留 1 份；
  - 保留第一次出现的 `Name`（`first`），另一个不同路径保留；
  - 路径首尾空白被 Trim；
  - 工具箱收藏去重；
  - `LastLoadNormalized=true`；清理 + 回写后再 Load → `false` 且条数不变（幂等）。
- **真宿主证据（2026-10-26 06:55 启动）**：

  ```
  06:10 / 06:20 启动 → [启动自检] 配置与日志环境: 方案清单 16384 项      ← 修复前
  06:55:50      启动 → [启动自检] AppConfig 已清理重复/空白条目（方案清单现 1 项）
                       [启动自检] 配置与日志环境: 方案清单 1 项          ← 修复后
  ```

  同一时刻 `VisionMaster\bin\Debug\net9.0-windows\AppConfig.json`：27.4 MB → 2.5 KB，
  `Solutions` 16384 条（1 个不同路径）→ 1 条。
- 文档索引：`tools\gen_docs_index.ps1` 已重跑（`docs/code-changes entries=488 unchanged`）。
- 收尾闸：`docs_index` 曾报 STALE（新文档未入索引），已由脚本重生成；
  全量 `run_gates.ps1` 在宿主运行期间无法完成构建（`VisionMaster.dll` / `VM.Scada.Controls.dll`
  被运行中的 `VisionMaster.exe` 锁定），关闭宿主后复跑即可。其余宿主在本轮改动前一轮为全绿。

## 5. 边界

- 去重只按 `Path`，不会删除“路径不存在”的条目：网络盘掉线/文件被临时移走仍应保留（与启动方案不清配置同一口径）。
- 两个不同方案如果被手工配成同一个 `Path`，会被合并成一条（这是重复，不是两种配置）。
- `AppConfig.json` 里的其它字段原样保留（`PopulateObject` 只覆盖 JSON 出现的属性）。
- 宿主 bin 里仍有 `AppConfig.json.bak-smartline`（14.0 MB）与 `.bak-web-20261025`（7.0 MB）。
  即使再被整体拷回覆盖，下次启动也会被本次自愈逻辑清成唯一项——不再需要人工去重。

## 6. 回退

删除 `NormalizeLoadedCollections()` 与 `LastLoadNormalized`，并去掉 `ConfigCheck` 里的回写块即可；
存量文件会退回“原样读出 16384 项”。
