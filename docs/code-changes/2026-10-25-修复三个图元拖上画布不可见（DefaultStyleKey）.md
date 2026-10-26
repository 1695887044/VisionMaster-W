# 修复：报表 / 工艺符号 / 管线三个图元拖上画布不可见

日期：2026-10-25　状态：**已修，`ScadaChecks` 3035 通过 / 0 失败**

## 症状

把「报表」「工艺符号」「管道连线」拖到组态画布上，图元被创建（图层面板里有），但画布上什么都看不见。

## 根因

WPF 查隐式样式走 `DefaultStyleKey`。这三个控件类都没有覆盖
`DefaultStyleKeyProperty` 的元数据，于是键还是基类 `ScadaElementBase` 的：

- `Themes/ReportElement.xaml`、`ProcessSymbolElement.xaml`、`PipeRunElement.xaml` 里的
  `<Style TargetType="{x:Type local:XxxElement}">` 永远匹配不上；
- 控件退回基类那个空模板 → 画布上是一个透明空壳。

**为什么断言是绿的**：断言里是显式赋样式（`control.Style = theme[typeof(XxxElement)]`），
显式赋值完全绕过 `DefaultStyleKey` 这条查找路径。模板、几何、读数全验过，但真机画布走的是隐式查找。
这与 R73 记的是同一类"症状极静默"的坑。

## 改动

1. `PipeRunElement` / `ProcessSymbolElement` / `ReportElement` 各加静态构造：
   ```csharp
   static XxxElement()
   {
       DefaultStyleKeyProperty.OverrideMetadata(
           typeof(XxxElement),
           new FrameworkPropertyMetadata(typeof(XxxElement)));
   }
   ```
2. **新增闸门** `ScadaChecks/ElementStyleKeyChecks.cs`：
   - `[DSK1]` 遍历 `ElementRegistry.All` 里所有 `ShowInToolbox` 的类型，造控件后**读实例上
     `DefaultStyleKey` 的实际值**，必须等于控件自己的类型；
   - 判据取运行时值而不是扫源码文本（写错类型的 `OverrideMetadata` 骗得过文本扫描）；
   - 反射只读框架的 `DefaultStyleKeyProperty` 字段，取不到时**判红不判跳过**（R38）。
   当前 `checked=48` 全部通过。
3. 顺带同步两处设置断言（本批新增「语音播报」组引起）：数值项 12 → 13（`SpeechRateText`）、
   页签 6 → 7（`GroupSpeech`）。

## 验证

- `VM.Scada.Controls` 编译 0 错误；`ScadaChecks` **3035 通过 / 0 失败**（`artifacts\logs\20261025\checks-dsk.log`）。
- 重建后的 `VM.Scada.Controls.dll` 已复制到 `VisionMaster\bin\Debug\net9.0-windows\`
  （`VisionMaster.exe` 本身仍在运行、apphost 被锁，所以宿主工程未整体重建）。
- **需要重启 `VisionMaster.exe`**：正在跑的进程里还是旧 DLL，重启后拖图元才可见。

## 教训

"断言全绿"不等于"真机可见"：凡是"控件外观从哪来"这类问题，判据必须落在
**真实查找路径**（隐式样式键）上，显式赋 Style 的断言证明不了这一层。
