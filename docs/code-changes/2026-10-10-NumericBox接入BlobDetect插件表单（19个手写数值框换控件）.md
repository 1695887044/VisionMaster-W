# 2026-10-10 NumericBox 接入 BlobDetect 插件表单（A 项：19 个手写数值框换控件）

- 日期：2026-10-10
- 项目：UI（共享样式 1 处）+ Plugin.BlobDetect（视图 19 处）+ 探针 _LogViewProbe + 样式参考
- 范围：1 视图（`Plugins\Plugin.BlobDetect\BlobDetectView.xaml`）+ 1 共享样式（`UI\Controls\Themes\PluginConfigStyles.xaml` 加一条触发器）
- 类型：把新控件推向插件手写参数表单（A 项第一步）
- 口径：**构建** = `dotnet build`（Release）；**检查** = `FlowCanvasChecks.exe`（含 BlobDetectChecks）；**探针** = `_LogViewProbe.exe -c Release`

---

## 一、侦察结论（决定了"怎么换"）

| 问题 | 结论 |
| --- | --- |
| 插件表单里的数值输入长什么样？ | `<TextBox Style="{StaticResource PluginNumericBox}">` + `<TextBox.Text>` 绑定 + `NumberValidationRule`（带 `FieldName` / `Min` / `Max` / `IntegerOnly`） |
| 有多少处？ | **19 处**（BlobDetectView），全部同形 |
| 那个样式只服务它一家吗？ | **不是** —— `PluginNumericBox` 被 **6 个插件视图**共用：BlobDetect 19、Matching 12、Calibration 13、CaliperMeasure 11、PoseTransform 1 |
| 那么能把样式原地改成 NumericBox 吗？ | **不能** —— 另外 5 个视图还是 TextBox，样式 TargetType 一改它们全部解析失败 |
| 有检查盯着吗？ | `FlowCanvasChecks\BlobDetectChecks` 做静态扫描，断言视图里出现 `StaticResource PluginNumericBox`（不检查元素类型） |

**关键点（省掉一整套重复样式的那个发现）**：WPF 里样式的 `TargetType` 允许是**基类** —— `PluginNumericBox` 是 TextBox 样式，而 `NumericBox` 继承自 TextBox，所以**这份样式可以直接套在 NumericBox 上**。于是：
不改样式定义、不动另外 5 个视图、检查项的字符串扫描照样通过。

## 二、改了什么

### 2.1 BlobDetectView.xaml：19 个块

```xml
<!-- 改前 -->
<TextBox Grid.Column="1" Style="{StaticResource PluginNumericBox}">
  <TextBox.Text>
    <Binding Path="MinGray" UpdateSourceTrigger="PropertyChanged">
      <Binding.ValidationRules>
        <local:NumberValidationRule FieldName="下限灰度" Min="0" Max="65535" />
      </Binding.ValidationRules>
    </Binding>
  </TextBox.Text>
</TextBox>

<!-- 改后 -->
<ui:NumericBox Grid.Column="1" Minimum="0" Maximum="65535"
               Style="{StaticResource PluginNumericBox}">
  <ui:NumericBox.Value>
    <Binding Path="MinGray" UpdateSourceTrigger="PropertyChanged">
      <Binding.ValidationRules>
        <local:NumberValidationRule FieldName="下限灰度" Min="0" Max="65535" />
      </Binding.ValidationRules>
    </Binding>
  </ui:NumericBox.Value>
</ui:NumericBox>
```

- **区间两端与校验规则同源**（规则里的 Min/Max 抄到 `Minimum`/`Maximum`）；**规则保留**，作为"数据本身越界"（如 VM 程序化写入）的第二道提示。
- 原 `IntegerOnly="True"` 的 5 个（局部窗口宽/高、排除触边、缺陷个数上/下限）额外加 `DecimalPlaces="0"`。
- 视图新增 `xmlns:ui="clr-namespace:UI.CustomControl;assembly=UI"`。

### 2.2 共享样式：补一条 `IsInvalid` 红框

`PluginNumericBox` 的模板原本只认 `Validation.HasError`；而 NumericBox 的"非法输入"是控件自己的 `IsInvalid`（文本退回上一个有效值），不走 WPF 校验。补：

```xml
<DataTrigger Binding="{Binding IsInvalid, RelativeSource={RelativeSource TemplatedParent}}" Value="True">
    <Setter TargetName="Bd" Property="BorderBrush" Value="{StaticResource PluginErrorBrush}" />
    <Setter TargetName="Bd" Property="Background" Value="{StaticResource PluginInputErrorBackgroundBrush}" />
</DataTrigger>
```

用 **DataTrigger 而不是 Trigger**：TextBox 没有 `IsInvalid` 属性，`Trigger` 会在解析期因找不到属性报错，DataTrigger 只是永不命中 —— 所以这一条对另外 5 个视图零影响。

## 三、行为变化（要点，需知悉）

| 项 | 改前 | 改后 |
| --- | --- | --- |
| 越界输入 | 校验失败：**拒绝**、保留上一个合法值、红框 + 悬停中文原因 | **自动钳到区间端点**（值变成端点、输入框回显端点值） |
| 整数参数 | 规则里 `IntegerOnly` 拦截小数 | `DecimalPlaces=0`：显示整数、提交按整数落 |
| 小数解析 | WPF 绑定按当前区域设置转换 | 先当前区域、再不变区域 —— `1,5`（de-DE 习惯）与 `1.5` 都能认 |
| 微调 | 只能整段重打 | **上下键 / 滚轮步进**（滚轮要求焦点在框内） |

"钳制"比"拒绝"更贴合工业参数录入（敲过头就停在边界，且越界值不可能进 HALCON —— 与原来"参数永远停在最近一次合法值"的初衷一致，只是更顺手）。

## 四、验证证据

| 项 | 结果 | 口径 |
| --- | --- | --- |
| 插件构建（红线③） | `Plugin.BlobDetect` Release `0 Error(s)`（只有既有的 `_disposed` 从未赋值警告）；`Modules\` 产物同步刷新 | `dotnet build Plugins\Plugin.BlobDetect -c Release` |
| 检查套件 | `FlowCanvasChecks` **通过 1886 / 失败 0**（"全部断言通过"），其中含 `BlobDetectChecks` 的静态扫描（"视图用平台的 `PluginNumericBox`" / "不本地定义 CardBorder/NumericBox 等键"） | `FlowCanvasChecks.exe -c Release` |
| 探针断言 | `[PASS] BlobDetect 数值框：NumericBox=19（期望 ≥19），灰度区间 [0,65535] 命中=True，圆度区间 [0,1] 命中=True，固定整数位=5（期望 ≥5）` | `_LogViewProbe.exe -c Release` |
| 渲染 | `docs/UI样式参考/blobdetect_numeric.png`（真视图 + 真插件 VM，落盘时回看过：① 二值化 下限/上限灰度、② 特征筛选 面积/圆度/长宽比/触边 都是新控件，**外观与改前逐字一致**） | 同上 |
| 全量回归（探针） | 15 条断言全 PASS（NumericBox 7 + StatusIndicator 5 + 卡片式开关 1 + Notifier 1 + BlobDetect 1） | 同上 |

## 五、事故与修正：全字段变红（当日用户反馈）

**现象**：真机上 19 个数值框**全部红框**（包括值完全合法的 128 / 30 / 0），且改值不生效。

**根因**：我把绑定目标从 `Text`（字符串）换成了 `Value`（NumericBox 的 double 依赖属性），
而两份 `NumberValidationRule` 都是按"原始文本"写的：

```csharp
var text = (value as string ?? string.Empty).Trim();
if (text.Length == 0) return new ValidationResult(false, $"{FieldName}不能为空");
```

WPF 的校验规则跑在"值写回源之前"，拿到的是**目标属性的原始值** —— 目标是 `Text` 时是用户输入的字符串，
换成 `Value` 后是**已转换的 double**；`double as string` → null → 空串 → 每个字段恒判"不能为空"。
后果不止"变红"：**校验不通过就不写回源，用户输入根本进不了插件参数**（比红框严重得多）。

> 上一版记录里"规则保留作为第二道提示"那句是错的 —— 我没有先验证规则在"数值型目标"下还能不能工作，就把它留下了。

**修法**：两份规则副本（`Plugin.BlobDetect` 与 `Plugin.CaliperMeasure`，同款代码）改成"字符串与已转换数值都认"：
字符串分支照旧；其余 `IConvertible` 走 `ToDouble` 直接校验数值。CaliperMeasure 那份目前仍绑 `Text`（走字符串分支），
一并修掉以免下次迁移再踩同一个坑。

**新增守门断言**（探针 `CheckBlobDetectNumericBoxes`，两条）：

1. 视图建好后 19 个框**都没有校验错误**；
2. 按绑定路径找到绑到 `MinGray` 的那个框，把 `Value` 改成 5 → **插件的 `MinGray` 属性必须变成 5、且无校验错误**。

第 2 条才是关键 —— 上一轮只数了"有几个 NumericBox"，**没验数据通路**，所以没抓住这次事故。

**验证**（用修好后的 DLL 重跑）：`[PASS] 数值框写入能落到插件参数、且全程无校验错误（红框事故守门人）  初始无校验错误=True；MinGray 框=True，写入后插件 MinGray=5，HasError=False`；探针全量 **16 PASS / 0 FAIL**。

**构建绕锁记录**：本次构建时 `Modules\Plugin.*.dll` 被 *VS 2022* 与运行中的 *VisionMaster* 占用（投递目标被锁，
`MSB3027`），用 `-p:ModulesDir="%TEMP%\vm_modules_skip\"` 把插件投递改到临时目录，编译与验证照常完成；
**真机要生效仍需关掉 VS 与实例后正常重建一次**（否则 `Modules\` 里还是旧插件 DLL）。

## 六、未决与观察

- **另外 5 个视图还是手写 TextBox**（Matching 12 / Calibration 13 / CaliperMeasure 11 / PoseTransform 1）：同样有"区域设置解析 + 无钳制"的坑，可用完全同一套手法逐个接（改视图、样式与检查都不用动）。**这是 A 项的剩余部分**。
- **Debug 产物仍未刷新**：`UI.dll` 被 VS 2022 与运行中的 VisionMaster 实例占用，本轮继续走 Release 验证；关掉后需补 `dotnet build -c Debug`。
- **真机观感未验**：渲染图是离屏的；上下键/滚轮步进的手感、以及"钳制时数值会跳变"是否被现场接受，要上真机试。
- **`PluginNumericBox` 这份样式现在同时服务 TextBox 与 NumericBox**：将来若要给 NumericBox 单独加单位后缀等元素，注意别把 TextBox 那 5 个视图带坏（模板里加元素可以，改 `PART_ContentHost` 不行）。
