# 报警条跑马灯位移被冻结（ControlTemplate 里的 Freezable 被共享冻结）

> 日期：2026-10-26　类型：缺陷修复（已改代码 + 已实测）　触发来源：SCADA 组态画面发糊排查时在日志里发现
> 证据：`artifacts\logs\20261026\{build-*,run-scadachecks}.log`、`artifacts\render\20261026\scada-runtime-alarmbanner.png`

## 1. 当前状态

**已修复并验证。** 运行态每拍（约 200ms）抛两条

```
[ERR ] [节拍] 一个节拍订阅者抛出异常，已跳过（其余订阅者不受影响）：
      无法在对象"System.Windows.Media.TranslateTransform"上设置属性，因为它处于只读状态。
```

自 2026-10-26 00:09 起就在刷（约 5 条/秒），表现为**报警条跑马灯纹丝不动 + 日志被刷满**。

修复后实测（真机 06:55:42 启动 → 06:56:58 打开运行态 → 06:59:55 退出）：

| 判据 | 修复前 | 修复后 |
|---|---|---|
| 运行态 `TranslateTransform` 错误 | 约 5 条/秒 | **0 条** |
| 该时段 `[ERR]` 总数 | — | **0 条** |
| 断言宿主 `ScadaChecks` | — | **3087 通过 / 0 失败**，新增断言 `frozen=False / shared=False / X=7 / threw=False` |

## 2. 已做

### 2.1 根因（探针实证，非推断）

WPF 在**模板封存时会把 `ControlTemplate` 里的 Freezable 冻结**，而且**所有实例共享同一个冻结对象**。
独立探针（`net9.0-windows`，同一份模板实例化两次）实测：

```
IsFrozen = True
second instance = <同一 hash>  frozen=True  sameRef=True
WRITE THROWS = 无法在对象"System.Windows.Media.TranslateTransform"上设置属性，因为它处于只读状态。
```

`Themes/AlarmBannerElement.xaml:127-129` 在模板里声明了 `<TranslateTransform/>`，
`AlarmBannerElement.ApplyTicker`（`AlarmBannerElement.cs:373`）每拍写 `shift.X` → 抛异常。
异常被 `ScadaBeatSource.OnTick` 按设计吞成一行日志（其余订阅者不受影响），所以**静默**。

即便不抛，两个报警条实例也会共用同一个位移（`sameRef=True`）——那是第二个潜在缺陷，一并消除。

### 2.2 改动

| 文件 | 改动 |
|---|---|
| `Scada.Controls\Elements\AlarmBannerElement.cs` | 新增 `EnsureTickerShift(TextBlock)`：模板给的那份若已冻结（或不是 TranslateTransform），换成本实例新造的可写实例；`OnApplyTemplate` 里提前换好（让"套上模板后一定可写"成为可断言的不变式），`ApplyTicker` 里再兜一次（模板被重套/被外部换掉也不抛） |
| `ScadaChecks\Phase1UiChecks.cs` | 新增一条断言：两个 banner 实例的 `PART_TickerText.RenderTransform` 必须可写（真写一次 `X` 并读回）且**不是同一个对象** |

模板 XAML 刻意不改：它声明"有个位移"是设计意图，冻结是 WPF 的封存行为，不是模板写错了。

### 2.3 验证

1. **变异反证（R65③）**：把 `&& !shift.IsFrozen` 去掉（还原成旧行为）→ 重建 → 跑断言 →
   `[×失败] … → frozen=True / shared=True / X=0 / threw=True`，汇总 1 项失败、退出码 1；
   改回 → 重建 → `[√通过] … → frozen=False / shared=False / X=7 / threw=False`，3087/0 全绿。
2. **端到端**：真机开运行态（报警条上有 1 条报警在跑跑马灯）→ 日志 0 条该错误（修复前同场景 5 条/秒）。
3. 产物判据（红线③）：`VisionMaster\bin\...\VM.Scada.Controls.dll` 内可搜到 `EnsureTickerShift`，
   DLL mtime（06:52:41）≥ 源码 mtime（06:49:32）。

## 3. 未做

- **没跑 `tools\run_gates.ps1` 全量**：另一个会话在同一时间段反复构建/跑 ScadaChecks（见 §5），
  全量闸会互相锁文件。已单独跑完本次受影响最直接的 `ScadaChecks`（3087/0）。
- **没修另一个独立缺陷**（本次跑测中暴露）：`ScadaEditorViewModel.OnHistoryChanged` 在后台线程
  `RaiseCanExecuteChanged()` → `ButtonBase.UpdateCanExecute` 抛"调用线程无法访问此对象"，
  在 `RunSoundAlarmChecks` 路径上偶发（同一构建三次跑只崩一次，属竞态）。与本次改动无关，建议单独立项。
- 没验证跑马灯"看起来在滚"的像素级位移（只有 1 条报警时位移幅度小；断言已证明 `X` 写入生效）。

## 4. 下一步

1. 等并发会话安静后跑一次 `tools\run_gates.ps1` 全量收尾。
2. `ScadaEditorViewModel.OnHistoryChanged` 的跨线程 `RaiseCanExecuteChanged` 单独立项（R100 同族：
   UI 线程之外抛异常会退进程，这里目前只是被断言宿主捕获）。
3. 若以后再有模板部件要"代码里改值"，先问一句：它是 Freezable 吗？是就按本文件的办法换成本实例。

## 5. 踩过的坑

- **模板里的 Freezable 是冻结且跨实例共享的**：`Style.Setter` 会冻结是老知识，`ControlTemplate`
  同样会——判据不能靠记忆，用 `IsFrozen` / 两次实例化比引用。写值前一律先确认可写。
- **异常被节拍源吞掉**：`ScadaBeatSource.OnTick` 的设计是"坏一个订阅者不打死其余"，代价是这类缺陷
  只表现为日志刷屏。看到 `[节拍] … 已跳过` 就要当成"某个功能整条不工作"，不是噪声。
- **两个会话同时构建同一工程**：本次撞了两次——`MC2000`（`obj\...\VM.Scada.Controls.dll` 被另一个构建占用）
  与 `MSB3027/MSB3021`（`ScadaChecks.exe` 被运行中的宿主锁定）。判据：构建前先看
  `Get-Process dotnet,MSBuild,ScadaChecks`，有就等。
- **两个 ScadaChecks 同时跑**：证据 PNG（`artifacts\scada-editor-toolbar.png`）被对方锁住，
  一次跑出 5 条假红。判据：跑断言宿主前确认没有第二个同名进程。
- **日志文件被宿主独占**：`[IO.File]::ReadAllLines` 会抛 IOException；
  要用 `[IO.File]::Open($f,'Open','Read','ReadWrite')` + `StreamReader` 读。
