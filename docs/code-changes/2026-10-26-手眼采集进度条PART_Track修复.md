# 手眼采集进度条 PART_Track 修复（HE-2）

> 日期：2026-10-26。范围：`Plugin.HandEye` 的 `SampleProgressStyle` 模板，
> 以及 `UIThemeSmokeTest/PluginViewSmoke.cs` 的渲染级断言。只改这一条，未动其他 P1。

## 一、当前状态

- **HE-2 已修**：`SampleProgressStyle` 的外层 Border 已命名为 `PART_Track`，内层保持 `PART_Indicator`。
- **渲染级断言已加**：`UIThemeSmokeTest --plugin-views Plugin.HandEye` 会检查
  `PART_Track` 存在且 `PART_Indicator.ActualWidth > 0`；修复前红、修复后绿。
- 冒烟里先补 3 行有效数据，让进度条从 `Collapsed` 进入布局（空表时整块不显示，直接查会
  误判成"条坏了"）。
- `Modules\Plugin.HandEye.dll` mtime 晚于 `HandEyeView.xaml`（红线③）。

## 二、已做

### 2.1 代码改动

`Plugins\Plugin.HandEye\HandEyeView.xaml:73`：

```diff
-                            <Border x:Name="Track"
+                            <Border x:Name="PART_Track"
                                     CornerRadius="3"
                                     Background="{StaticResource DialogBorderLightBrush}">
                                 <Border x:Name="PART_Indicator"
```

WPF `ProgressBar` 的模板契约是：**外层 `PART_Track` 量宽度，内层 `PART_Indicator` 被设置 Width**。
`SetProgressBarIndicatorLength()` 用 `PART_Track.ActualWidth` 算指示长度；缺 `PART_Track` 时
`Track` 为 null，整段逻辑不执行，`PART_Indicator` 的 `Width` 保持 `Auto`、`ActualWidth` 为 0。

### 2.2 渲染断言

`UIThemeSmokeTest\PluginViewSmoke.cs`：

- 新增 `EnsureHandEyeSamples(plugin)`：冒烟专用，反射给 HandEye 补 3 行有效数据，
  让进度条父 Border 从 `Collapsed` 变 `Visible`；
- 新增通用检查：遍历视图里的 `ProgressBar`，`ApplyTemplate()` 后把 `Value` 设到量程中点，
  `UpdateLayout()`，然后断言 `PART_Track != null && PART_Indicator.ActualWidth > 0`。
  这条检查对将来任何带 ProgressBar 的插件视图都生效。

### 2.3 先红后绿证据

**修复前**（`UIThemeSmokeTest.exe --plugin-views Plugin.HandEye`，退出码 1）：

```text
[FAIL] Plugin.HandEye.HandEyePlugin ProgressBar 指示条可见（PART_Track + PART_Indicator）
       (value=0.5 PART_Track=null PART_Indicator=0)
```

日志：`artifacts\logs\20261026\handeye-view-before.log`。

**修复后**（同一命令，退出码 0）：

```text
[PASS] Plugin.HandEye.HandEyePlugin ProgressBar 指示条可见（PART_Track + PART_Indicator）
       (value=0.5 PART_Track=88 PART_Indicator=44)
```

日志：`artifacts\logs\20261026\handeye-view-after.log`。

### 2.4 构建与产物

- `dotnet build UIThemeSmokeTest\UIThemeSmokeTest.csproj -c Debug -m:1 -nodeReuse:false`：
  `已成功生成` / `0 个错误`（`build-handeye-view-smoke.log`）。
- `dotnet build Plugins\Plugin.HandEye\Plugin.HandEye.csproj -c Debug -m:1 -nodeReuse:false`：
  `已成功生成` / `0 个错误`（`build-handeye-xaml-after.log`）。
- `HandEyeView.xaml` = 10:57:21，`Modules\Plugin.HandEye.dll` = 10:57:30 → 产物不旧。

## 三、未做

- 没有把 `--plugin-views` 接进 `tools\run_gates.ps1` 的默认流程：该检查目前靠
  `UIThemeSmokeTest.exe --plugin-views Plugin.HandEye` 手动跑；默认 gate 里的
  `HandEyeChecks` 进度①~⑥ 仍只查 VM 数值，查不到模板缺 part。
  **建议下一步**：在 `HandEyeChecks` 里加一条静态 XAML 扫描（`SampleProgressStyle` 的
  `ControlTemplate` 必须同时有 `PART_Track` 与 `PART_Indicator`），让 gate 也覆盖；
  或者把 `--plugin-views` 作为 `run_gates.ps1` 的一步（会拉长 gate 时间）。
- 没有改其他 P1（HE-3、CAL-1~4）与 P2/P3。
- 没有启动真 `VisionMaster.exe`；本条是视图渲染断言，不需要真机。

## 四、下一步

下一条：**HE-3（NaN/Infinity 入表 → 求解抛 `ArithmeticException`）**。计划：

1. `HandEyePaste.TryParseRows` / `HandEyeCsv.TryParse` 入表处拒绝非有限数（带行号/列号）；
2. `TryBuildSamples` 统一扫 `double.IsFinite`，点名"成对数据第 N 行含非法数值"；
3. `TryRotationLog` / `JacobiEigenSymmetric` 入口判有限性，返回 false 而不是让 `Math.Sign(NaN)` 抛；
4. `SolveForPreview` 加 try/catch，把兜底异常转成中文失败文案；
5. 补断言：粘贴/CSV 输入 `NaN`/`Infinity` 必须失败且点名行，配置界面「求解」不抛异常。

## 五、踩过的坑

- **WPF 的 part 名字是反的**：`PART_Track` 是轨道（量宽度），`PART_Indicator` 是填充（被设宽度）。
  仓库 `UI/Controls/Themes/Fluent/FluentControls.xaml:762-781` 把填充命名为 `PART_Track`、
  没有 `PART_Indicator`，是同一族的错；不要照抄。修 HandEye 时只改外层 Border，内层保持
  `PART_Indicator`。
- **空表时进度条父 Border 是 Collapsed**：直接量 `PART_Indicator.ActualWidth` 会得到 0，
  看起来像"模板又坏了"。冒烟里必须先补几行有效数据，让 `HasSamples=true`。
- **只查 VM 的 `SampleProgress` 不够**：现有进度①~⑥ 全绿也发现不了"值对但条不显示"；
  必须看渲染后的 `PART_Indicator.ActualWidth`（本次 before/after 就是 0 vs 44）。
