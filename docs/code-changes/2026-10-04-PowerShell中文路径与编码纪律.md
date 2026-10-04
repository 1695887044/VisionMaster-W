# 2026-10-04 PowerShell 中文路径与编码纪律（踩坑记录 + 固化规则）

- 日期：2026-10-04
- 范围：**工具链纪律**（不涉及产品代码）；影响对象：所有用 `powershell.exe` 做批量文件操作的人/AI 助手
- 起因：本轮工作里同一个坑连踩三次，其中一次**写坏了仓库源文件**（已无损恢复）

---

## 一、三次事故（同一根因）

| # | 事故 | 表现 | 根因 |
| --- | --- | --- | --- |
| 1 | 用 `Get-Content -Raw` + `-replace` + `Set-Content` 改 `Services\Core.Halcon\Controls\ImageGallery.cs` | 文件出现字面 `\r\n`，且**中文注释/字符串被双编码损坏**（严格 UTF-8 解码失败、93 处 U+FFFD） | 无 BOM 的 UTF-8 被 PS 5.1 按 **ANSI(GBK)** 读 → 读到的已是乱码字符串；`Set-Content` 又按另一种编码写回 → **有损** |
| 2 | 纯 ASCII 脚本里混进中文**注释** | 解析报错："字符串缺少终止符 / 意外的标记"（看起来像语法错误，其实是编码） | 同上：`.ps1` 无 BOM → 5.1 按 ANSI 读，中文注释被误读后破坏词法 |
| 3 | `powershell -Command "…中文路径…"` | `Test-Path` 收到 null / "文件名、目录名或卷标语法不正确" | 命令行经 **cmd 代码页**转换，中文参数被吃 |

三次的**共同真相**：
- `powershell.exe`（Windows PowerShell **5.1**）对**无 BOM 文本**一律按系统 ANSI 代码页（本机 zh-CN → GBK/936）解码；
- cmd 的命令行参数同样按代码页转换；
- 只有**显式指定 UTF-8** 的读写才可靠。

---

## 二、固化规则（以后照做）

### R1 `.ps1` 文件必须纯 ASCII

代码、字符串、**注释**一律不许出现中文（注释里出现也会破坏解析）。
需要中文常量时用码点拼：

```powershell
$cal = [string][char]0x6807 + [char]0x5B9A + [char]0x63D2 + [char]0x4EF6   # 标定插件
```

### R2 中文"数据"放 UTF-8 清单文件，脚本显式读

路径、文件名、搜索词等，写进一个 UTF-8 文本（用编辑器工具生成），脚本这样读：

```powershell
$utf8 = New-Object Text.UTF8Encoding($false)
$items = @([IO.File]::ReadAllLines($listPath, [Text.Encoding]::UTF8) | Where-Object { $_.Trim().Length -gt 0 })
```

### R3 绝不把中文放命令行

`powershell -Command "…中文…"`、`cmd /c move 中文路径` 一律禁止 → 改走 `-File`（ASCII 脚本）+ R2 的清单文件。

### R4 不用 `Get-Content`/`Set-Content` 往返编辑仓库文件

- 改文件优先用**编辑器工具**（Edit/Write，编码安全）；
- PowerShell 只做**批量机械操作**（搬家、改名、扫描）；
- 必须写回文本时：

```powershell
[IO.File]::WriteAllText($p, $text, (New-Object Text.UTF8Encoding($false)))   # UTF-8 无 BOM，与原文件一致
$text = $text.Replace('](旧名.md)', '](../新目录/旧名.md)')                    # 只用 String.Replace，保换行
```

  **禁止**在替换串里插 `\r\n` 字面量（正则替换不支持 `\r\n` 转义，会把字面反斜杠写进文件）。

### R5 写完必须自检（三步，一步都不能省）

```powershell
# ① 严格 UTF-8 解码（不抛才算过）
$null = (New-Object Text.UTF8Encoding($false, $true)).GetString([IO.File]::ReadAllBytes($p))
# ② 行数/大小合理
(Get-Item $p).Length; ([IO.File]::ReadAllLines($p)).Count
# ③ 用 Read 工具回读一眼中文
```

### R6 救命绳：编辑工具自带"改动前快照"

写坏了不要慌——Edit/Write 工具改动前会在
`%USERPROFILE%\.zcode\cli\artifacts\<会话ID>\*-tool-result-*.json`
里存一份 `files[0].beforeContent`（**整份原文**）。还原方式：

1. 用 UTF-8 读该 JSON，取出 `beforeContent`（JSON 字符串，`\n` 等转义已由反序列化处理）；
2. **换行转换**：快照把换行规范成了 LF，还原 `.cs/.xaml` 等 CRLF 文件时要 `Replace("`n", "`r`n")`（先确认原文件没有混合换行）；
3. `[IO.File]::WriteAllText($p, $text, UTF8-无BOM)` 写回，再走 R5 自检。

> 本轮 `ImageGallery.cs` 就是靠它无损恢复的（0 处 U+FFFD、460 行、CRLF 一致），恢复后重新用编辑工具做的修改。

---

## 三、安全范式（复制即用）

```
① 用编辑器工具写一个 UTF-8 清单（例如 move_list.txt，每行一条）：
     MOVE|docs\插件方案说明书\标定插件方案说明书.md|docs\标定插件\标定插件方案说明书.md
     DEL|docs\插件方案说明书\README.md
     DELDIR|docs\插件方案说明书
     CHECK|docs\标定插件\标定插件方案说明书.md

② 写一个**纯 ASCII** 的 .ps1：读清单（R2）→ 逐行执行（MOVE/DEL/CHECK…）
   → 结果写进 UTF-8 报告文件（不往控制台打中文）

③ 用 Read 工具读报告验收（控制台代码页不会再干扰判断）
```

本轮 `docs\图像采集\` 归集、`docs\标定插件\` / `docs\坐标变换插件\` 搬迁，全是按这套跑的：**零解析错误、零断链、零残留**。

---

## 四、附：为什么不是"换一台机器/换个编码就行"

- 换成 PowerShell 7（`pwsh`）确实默认 UTF-8 无 BOM 也能读对，但**本机工具链与 CI 走的是 `powershell.exe` 5.1**；脚本还可能在别的机器执行——**纯 ASCII 是唯一跨环境稳的写法**。
- 给 `.ps1` 加 BOM 也能救 5.1 的解析，但"BOM 会丢"是常事（复制粘贴、某些工具写文件），不如 R1 从根本上规避。
- `chcp 65001` 只影响控制台显示，**不影响**文件读取与命令行转换——别指望它。
