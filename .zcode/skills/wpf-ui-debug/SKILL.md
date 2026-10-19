---
name: wpf-ui-debug
description: 排查 WPF 主题/控件模板改动"不生效、被裁切、像没应用"的问题。用在：改了 XAML 样式或 ControlTemplate 但界面没变化、页签/列表项文字被挤没或只露一条、怀疑资源键找不到或样式被 App 级主题顶掉时。给出固定流程——先证明跑的是新代码 → 静态排查 → net9 探针量尺寸 → 按症状修，含 PowerShell 5.1 假结论、TabPanel/Grid Auto 行塌陷等实测坑。
---

# WPF 主题/模板问题排查

## 何时用

- 改了 `ControlTemplate` / `Style` / 资源字典，界面**看起来没变化**；
- 页签、列表项、徽章的文字**被挤没 / 只露一条 / 被裁切**；
- 怀疑样式没应用、资源键找不到、被 App 级主题覆盖；
- 别人说"我这边是好的"，但真机不对。

## 三条铁律

1. **先证明"跑的是新代码"**，再谈样式对不对。WPF 的 XAML 编译进程序集，运行中的进程不会热更新。
2. **所有结论必须落到数字**：`ActualWidth/ActualHeight/DesiredSize/MinHeight`。截图和肉眼判断只能当线索，不能当证据。
3. **探针必须是 `net9.0-windows` 工程**。PowerShell 5.1 是 .NET Framework 宿主，对 net9 程序集的"加载成功"是假象（见 P1）。

---

## 第 0 步：证明进程加载的是新 DLL

```powershell
$bin = 'D:\C#\VM\VisionMaster\bin\Debug\net9.0-windows'
foreach ($n in 'UI.dll','VisionMaster.dll','VisionMaster.exe') {
    $f = Get-Item (Join-Path $bin $n)
    '{0,-20} {1}  {2}' -f $n, $f.LastWriteTime, $f.Length
}
Get-Process VisionMaster -ErrorAction SilentlyContinue |
    ForEach-Object { 'PID={0} start={1}' -f $_.Id, $_.StartTime }
```

判据：

- **进程启动时间 < DLL 修改时间 → 进程跑的是旧代码**，必须完全退出（托盘退出）再启动；只关弹窗没用。
- 确认跑的是 Debug 还是 Release：`bin\Release` 往往是几天前的旧产物（本仓实测 10-18 vs 10-19）。
- 全仓可能有几十份 `UI.dll` 副本（各 Checks/Plugins 的 bin）。**以宿主 exe 所在目录的那份为准**。

> 这一步能解释大多数"改了没反应"。跳过它去改样式，等于蒙眼调参。

---

## 第 1 步：静态排查（不用起程序）

1. **资源键是否唯一**：全仓 grep `x:Key="你的键"`。资源字典**后合并者优先**，重复键会静默覆盖。
2. **合并顺序**：本仓 `App.xaml` 的顺序是
   `Core.Halcon → UI/Themes/Generic.xaml → PresentationFramework.Fluent`。
   Fluent **最后**合并 → 它的**隐式**样式会顶掉 UI 库的隐式样式（与 R19 的 GridView 事故同源）。
   **结论：要对抗 Fluent 的控件（TabControl/TabItem/ListView/GridViewColumnHeader 等）必须用显式键控样式**，不能只写 `<Style TargetType="...">`。
3. **BAML 是否真的打进 DLL**：

   ```powershell
   $asm = [Reflection.Assembly]::LoadFrom($dll)
   $s = $asm.GetManifestResourceStream('UI.g.resources')
   $r = New-Object System.Resources.ResourceReader($s)
   foreach ($e in $r) { $e.Key }
   ```

   能看到 `themes/dialog/xxx.baml` 才说明文件参与了编译（注意：PowerShell 5.1 只能做"枚举资源名"这种反射，不能实例化 net9 类型）。
4. **视图是否真的挂了样式**：`Style="{StaticResource 键}"` 的键名逐字核对；`StaticResource` 解析失败会在加载期抛异常，不会静默。

---

## 第 2 步：net9 探针（量尺寸）

临时工程放 `%TEMP%`，用完即删，**不进仓库**。

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="D:\C#\VM\VisionMaster\VisionMaster.csproj" />
  </ItemGroup>
</Project>
```

```csharp
[STAThread]
static void Main()
{
    var app = new Application();
    // 顺序必须与 App.xaml 一致
    app.Resources.MergedDictionaries.Add(new ResourceDictionary {
        Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml") });
    app.Resources.MergedDictionaries.Add(new ResourceDictionary {
        Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml") });

    var view = (FrameworkElement)Application.LoadComponent(
        new Uri("/VisionMaster;component/Views/DialogViews/你的视图.xaml", UriKind.Relative));

    // ① 无窗口量一次
    view.Measure(new Size(1180, 640));
    view.Arrange(new Rect(0, 0, 1180, 640));
    view.UpdateLayout();
    Report("NO-WINDOW", view);

    // ② 放进离屏真窗口再量（复现 HwndHost/DPI/主题宿主）
    var win = new Window { Width = 1180, Height = 640, WindowStyle = WindowStyle.None,
                           Left = -4000, Top = -4000, ShowInTaskbar = false, Content = view };
    win.Show();
    win.UpdateLayout();
    Report("IN-WINDOW", view);
}
```

`Report` 至少打印：控件的 `ActualWidth/ActualHeight`、每个容器的 `DesiredSize`、`MinHeight`、以及模板内 `ContentPresenter` 的 `ActualWidth/ActualHeight` 与 `Margin`。

**判据**：文字高度约 16~18px 时，容器高度应 ≥ 36；`ContentPresenter.ActualHeight` 必须接近文字高度，否则就是被裁。

---

## 第 3 步：按症状定位

| 症状 | 最可能原因 | 修法 |
| --- | --- | --- |
| 高度被压到只剩一条 | 模板用 `Grid` 的 `Auto` 行 + 子元素 `Margin` 撑高度 | 改 `MinHeight` + 模板根 `Border.Padding`；指示条用 `Border.BorderThickness` 底边，不再占一行 |
| 宽度被压到几乎为 0 | `ItemsHost` 测量约束异常 / 子项 Margin 过大 | 换 `StackPanel` 作 ItemsHost；核对 Margin |
| 文字像没渲染 | `ContentSource` 写错、Foreground 与背景同色、字体缺失 | 探针里打印 `ContentPresenter.Content` |
| 整块完全没变化 | 样式没应用（键名/合并顺序/被隐式样式顶掉） | 改显式键控样式；核对合并顺序 |
| 只在真机坏、探针好 | 宿主差异（Prism 对话框、DPI、主题合并顺序） | 探针改成"离屏真窗口 + 相同合并顺序"；仍不复现则先查第 0 步 |

---

## 已知坑（全部本轮实测）

### P1 · PowerShell 5.1 不能验证 net9 程序集

`Assembly.LoadFrom("...net9.0-windows\\UI.dll")` 会**看似成功**，但：

- `ResourceDictionary.Source = pack://.../Generic.xaml` 加载后 `Count=0/1`、`MergedDictionaries=0`；
- 资源查找返回 `null`；
- `Application.LoadComponent` 直接报 `System.Runtime, Version=9.0.0.0` 找不到。

据此得出"样式没生效 / 没打进 DLL"是**假结论**（本轮白排查两轮）。只能用它枚举资源名，不能实例化类型。

### P2 · 自定义 `TabItem` 模板不要靠 Grid Auto 行撑高度

```xml
<!-- 反面写法：高度靠 Auto 行 + Margin 分配，宿主一变就可能塌 -->
<Grid>
  <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="2"/></Grid.RowDefinitions>
  <ContentPresenter Grid.Row="0" Margin="0,10,0,10"/>
  <Border Grid.Row="1" Height="2"/>
</Grid>
```

正面写法：

```xml
<Setter Property="MinHeight" Value="38"/>
<ControlTemplate TargetType="TabItem">
  <Border x:Name="Root" Padding="0,10,0,8" BorderThickness="0,0,0,2" BorderBrush="Transparent">
    <ContentPresenter ContentSource="Header" HorizontalAlignment="Center" VerticalAlignment="Center"/>
  </Border>
  <!-- IsSelected → Root.BorderBrush = 强调色 -->
</ControlTemplate>
```

### P3 · `TabPanel` 的排列不可预测，固定单行时用 `StackPanel`

`TabPanel` 负责多行换行与特殊排列；在对话框宿主里出现过页签被压到文字以下。页签固定一行时，直接把 ItemsHost 换成 `StackPanel Orientation="Horizontal"`。

### P4 · Fluent 在 App 级最后合并 → 必须显式键控样式

`App.xaml` 合并顺序 `Core.Halcon → UI Generic → Fluent`。凡与 Fluent 同类型的控件（`TabControl`、`TabItem`、`ListView`、`GridViewColumnHeader`、`Button`…），**隐式样式会被 Fluent 顶掉**。解法与 `GridListViewStyle` 一致：定义显式键（如 `DialogTabControl` / `DialogTabItem` / `GridListHeaderStyle`），视图显式挂。

### P5 · 视图 XAML 与主题 XAML 分属两个程序集

- 视图在 `VisionMaster.dll`，主题在 `UI.dll`；
- 只重建一个、或只关弹窗不重启进程，都会出现"一半新一半旧"的怪象；
- 判据仍是第 0 步的 mtime 对比。

### P6 · 负 Margin 的头部容器会放大宿主差异

`<StackPanel Margin="0,0,0,-1">` 这类"盖住 1px 边框"的写法会让面板比父容器高 1px，在部分宿主里触发裁剪。能用 `Border.BorderThickness` 表达就不要用负 Margin。

### P7 · 默认焦点框与 ToolTip 是"旧风格"的漏网之鱼

改了控件模板和配色后，界面仍会冒出两个原生样式：

- **焦点框**：WPF 默认 `FocusVisualStyle` 是黑色/虚线矩形，在圆角卡片上非常突兀。解法：定义 `DialogFocusVisual`（2px 强调色圆角环、`Margin="2"` 外扩），可聚焦控件显式挂 `FocusVisualStyle`。
- **ToolTip**：默认是方角 + 系统灰边。解法：`DialogToolTipStyle`（`CornerRadius="8"`、1px 描边、12 号字、`Padding="8,6"`），用 `<ToolTip Style="...">` 显式挂。

两个样式都定义在 `UI/Controls/Themes/Dialog/Dialog.Misc.xaml`，**必须显式键控**（Fluent 在 App 级最后合并，隐式样式会被顶掉）。

另外：对话框打开时焦点默认落在第一个可聚焦元素（通常是标题栏关闭按钮），焦点环会"自己出现"；不想让它抢焦点就用 `FocusManager.FocusedElement` 指到内容区，别用 `Focusable="False"`。

---

## 验收

- 探针输出：目标控件 `ActualHeight ≥ 36`、`ContentPresenter` 高度≈文字高度；
- `dotnet build VisionMaster\VisionMaster.csproj` 0 错误；
- **完全重启**宿主进程后截图对照；
- 至少看 100% 与 150% 两种 DPI（`MinHeight` 是 DIP，应等比缩放）。

## 案例

`docs/code-changes/2026-10-19-IO健康页签文字被裁（TabItem模板依赖Grid行高分配）.md`
