# 状态色令牌接进图元（Indicator 试点）+ 报警条默认 36

日期：2026-10-26　状态：试点完成；`ScadaChecks` **3070 通过 / 0 失败**

## 做了什么

1. **报警条新实例默认高度 140 → 36**（跑马灯是默认形态）。老 `.vms` 不受影响。
2. **`HmiState*` 五个令牌进 `ScadaTokens.xaml`**，并有断言 `[ST1]` 直接读文件核对键与取值。
3. **`Hmi.Indicator` 试点：颜色默认改由令牌提供**
   - `IndicatorElement.xaml` 的 Style 里加两条 Setter：
     `<Setter Property="OnColor" Value="{StaticResource HmiStateRunningBrush}"/>`、
     `<Setter Property="OffColor" Value="{StaticResource HmiStateStoppedBrush}"/>`。
   - 效果：新拖入的指示灯默认取令牌值；用户改颜色 → 局部值覆盖；改令牌 → 未被覆盖的图元跟着变。

## 试点的关键结论（写下来免得下次再试一遍）

**`[ScadaProperty(DefaultValue = "")]` 走不通**：把颜色默认值清空后，断言立刻红两条——
① 描述符快照不一致；② `DependencyProperty.UnsetValue 不是 OffColor 的有效值`
（描述符落地时把空串解析成了 `UnsetValue` 又写回 DP）。

**可行的做法是"样式 Setter 提供默认、属性默认值保留字面量"**：
WPF 里样式 Setter 的优先级高于 DP 默认值，所以外观由令牌决定；
字面量 DefaultValue 只留在元数据里（面板"默认值"列与校验用），不再影响实际显示。
这样既做到"改令牌生效"，又不触发 UnsetValue。

## 还没迁的（按同法照做即可）

`Hmi.Lamp`（三色）、`Hmi.Symbol`（Accent/Stopped/Fault）、`Hmi.Tank`/`Hmi.Pump`/`Hmi.Valve`/`Hmi.Motor`
（各自状态色）、`Hmi.ProgressBar`（填充）、`Hmi.Gauge`（指针/越限）。
每个只需在对应 `Themes/*.xaml` 的 Style 里加 Setter，**不用改属性声明**。

## 建议的闸（下一步）

把 `[ST1]` 升级成"**每个声明了状态色的图元，其主题 Style 必须含对应的令牌 Setter**"：
判据取"`Themes/<Element>.xaml` 里出现 `HmiState*Brush`"，静态扫描即可，
这样漏迁一个就会红——否则"改了令牌没生效"永远是靠人发现。
