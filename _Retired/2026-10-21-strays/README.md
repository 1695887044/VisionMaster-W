# 根目录游离文件（2026-10-21）

本目录是 **2026-10-21 根目录整理**时从仓库根移出的 5 个"不属于源码树"的文件（其中 `demo.cs` 已于 2026-10-22 删除，现存 4 个）：

| 文件 | 原用途 | 为什么不该在根目录 |
| --- | --- | --- |
| `communications.json` | 通讯配置（127.0.0.1:502 测试连接） | 运行期数据，属于交付目录或用户目录，不属于源码树 |
| ~~`demo.cs`~~ | **标定插件旧快照**（2026-10-22 复核纠正：不是"一次性演示代码"——内容是 `Plugins\Plugin.Calibration\CalibrationPlugin.cs` 的旧版本，100,071 字节 / 2160 行 / mtime 2026-10-05） | 不属于任何工程；且它声明了同名 `Plugin.Calibration.CalibrationPlugin`，会让"搜这个类"得到两处声明。**已于 2026-10-22 删除**，判据见下节 |
| `generated.xshd` | 语法高亮定义 | 工具产物，无工程引用 |
| `shot.png` | 截图 | 验证产物，应进 `artifacts/render/<date>/` |
| `a.html;` | 命令写坏留下的空文件 | 垃圾 |

## `demo.cs` 的处置（2026-10-22：已删除）

- **来自哪里**：2026-10-21 根目录整理时从仓库根（原路径 `D:\C#\VM\demo.cs`）移入本目录。更早两条记录：`docs\code-changes\2026-10-06-标定插件网格标定（分段仿射）.md:82`（登记为"标定插件旧快照，不在任何构建里，易误导检索"）、`docs\code-changes\2026-10-11-通信模块四项修复（JSON逐条隔离-拒绝识别-KeepAlive落地-Worker日志）.md:105`（"清理后根目录仅剩未验证的 demo.cs"）。
- **为什么删**：① 全仓无任何构建/脚本引用——`*.csproj` / `*.props` / `*.targets` / `*.ps1` / `*.json` 里既无 `_Retired` 编译项、也无 `demo.cs` 命中（`tools\root-layout.json` 只是把 `_Retired` 列为允许的目录）；② 它声明的是 `namespace Plugin.Calibration` + `class CalibrationPlugin`，与构建中的真身 `Plugins\Plugin.Calibration\CalibrationPlugin.cs`（3252 行）重复，且是更旧的 2160 行版本；③ 留着的唯一实际效果是让 `CalibrationPlugin` / `CalibrationAttempt` 这类检索多出一处误命中。
- **怎么恢复**：本目录不参与任何构建，删掉不影响任何产物；旧版内容可从 `Plugins\Plugin.Calibration\CalibrationPlugin.cs` 的 git 历史回溯。
- **何时可删**：**已于 2026-10-22 删除**（复核：app 成员；判据即上两条）。

## 处置建议

- `communications.json` 如果确认是现场要用的配置，请放回**交付目录**（与 `VisionMaster.exe` 同级），不要放回源码根；
- 其余 3 个（`generated.xshd`、`shot.png`、`a.html;`）可直接删除。

## 已直接删除的垃圾

根目录另有 15 个无意义文件（`$_.Groups[2].Value }'`、`$($c.R)`、`05'){` 等命令写坏的产物，
以及一个含 `mp.weixin.qq.com` 会话 cookie 的 curl cookie jar `2000`）已**直接删除**，未留档。
如果该 cookie 曾被提交过，相关凭据应视为已泄露。
