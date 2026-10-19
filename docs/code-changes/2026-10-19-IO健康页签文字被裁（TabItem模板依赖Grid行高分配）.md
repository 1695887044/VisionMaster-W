# IO 健康页签文字被裁（TabItem 模板依赖 Grid 行高分配）

- 日期：2026-10-19
- 影响面：`UI/Controls/Themes/Dialog/Dialog.Navigation.xaml` 的 `DialogTabItem`；所有后续复用该页签样式的弹窗
- 关联：`AGENTS.md` §30.2（R133 / R134）、设计基线 `docs/UI设计基线/io-health/`

## 1. 现象

真机截图：IO 健康弹窗的「设备 / 总线 / 点位」三个页签只剩顶部一条文字，几乎看不见；页签区高度约 20px，与设计稿的 38~40px 不符。

## 2. 排查过程（含两次无效结论，勿重复踩）

### 第一轮：PowerShell 探针（结论无效）

用 `powershell -STA` 加载 `UI.dll`、合并 `Generic.xaml`、建 `TabControl` 测量：

```text
dict count=1 merged=0
Contains DialogTabItem = False
style object = NULL
```

据此一度判断"新分册没打进 DLL / 样式没生效"。

**这是假结论**：PowerShell 5.1 跑在 **.NET Framework 4.x**，而 `UI.dll` 目标框架是 **net9.0-windows**。`Assembly.LoadFrom` 会"成功"返回程序集对象，但类型无法实例化、资源字典加载为空。

### 第二轮：PowerShell + LoadComponent（直接报错）

```text
MethodInvocationException: 未能加载文件或程序集"System.Runtime, Version=9.0.0.0"
```

确认了上一轮的宿主问题。

### 第三轮：net9.0-windows 探针（有效）

临时工程（`net9.0-windows` + `UseWPF`，`ProjectReference` 指向 `VisionMaster.csproj`），按 `App.xaml` 的顺序先合并 Fluent、再合并 `Generic.xaml`，然后用 `Application.LoadComponent` 加载**真实视图** `IoHealthView.xaml` 并 `Measure/Arrange/UpdateLayout`：

```text
TabControl actual=1132x480 itemCount=3
header=设备 style=SET desired=50,38.5 actual=26x38.5
   ContentPresenter actual=26x16.5 margin=0,10,0,10 content=[设备]
```

**模板在隔离环境里是正常的（38.5px，文字完整）**，说明 `DialogTabItem` 的资源键、BAML、样式继承都没有问题；问题出在真机宿主下的高度分配。

## 3. 根因

`DialogTabItem` 的模板把高度拆成两段表达：

```xml
<Grid>
  <Grid.RowDefinitions>
    <RowDefinition Height="Auto" />   <!-- 文字 -->
    <RowDefinition Height="2" />      <!-- 指示条 -->
  </Grid.RowDefinitions>
  <ContentPresenter Grid.Row="0" Margin="0,10,0,10" ... />
  <Border x:Name="Indicator" Grid.Row="1" Height="2" ... />
</Grid>
```

高度完全依赖「Grid Auto 行的测量结果 + ContentPresenter 的上下 Margin」在宿主里被正确分配。`TabItem` 的宿主是 `TabPanel`，它按子项期望尺寸排列；这套组合在对话框窗口（自定义 `WindowStyle`、Prism 宿主、DPI 缩放）下不稳定，实测页签被压到文字以下。

**结论：模板本身没有语法/资源错误，错在"靠布局分配撑高度"这种写法对宿主过于敏感。**

## 4. 修法

改为**样式显式定高**，彻底不依赖 Auto 行分配：

```xml
<Style x:Key="DialogTabItem" TargetType="TabItem">
  <Setter Property="MinHeight" Value="38" />
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="TabItem">
        <Border x:Name="Root"
                Padding="0,10,0,8"
                BorderThickness="0,0,0,2"
                BorderBrush="Transparent">
          <ContentPresenter ContentSource="Header"
                            HorizontalAlignment="Center"
                            VerticalAlignment="Center" />
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property="IsSelected" Value="True">
            <Setter TargetName="Root" Property="BorderBrush"
                    Value="{StaticResource DialogAccentBrush}" />
            ...
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
```

要点：

1. **高度由 `MinHeight` 决定**，文字垂直居中靠 `Border.Padding`，不再有 `RowDefinition`；
2. **指示条用 `Border.BorderThickness` 的底边表达**，不再单独占一行；
3. 选中态只换 `BorderBrush`，不动布局。

## 5. 验证

- 探针（真实视图 + Fluent 已合并）：修复后 `TabItem desired=50,38 actual=26x38`，`ContentPresenter` 文字完整；
- `dotnet build VisionMaster\VisionMaster.csproj`：0 错误。

## 6. 未验证

- 真机重启后的截图对照（需用户重开弹窗确认）；
- 125% / 150% DPI 下的实际观感（`MinHeight` 为 DIP，理论上随 DPI 等比缩放）。

## 7. 可复用判据

- 自定义 `TabItem` / 列表项 / 徽章模板：**高度必须由样式显式给出**（`MinHeight` + `Border.Padding`），不许依赖 Grid Auto 行 + 子元素 Margin；
- 验证 .NET 9 WPF 资源/模板：**必须用 `net9.0-windows` 探针工程**，PowerShell 5.1 的"加载成功"不可信；
- 探针必须加载**真实视图**并合并与 `App.xaml` 相同顺序的主题字典，否则测不到宿主差异。

## 8. 补充（同日第二轮排查）

### 8.1 探针在三种宿主下均正常

用 `net9.0-windows` 探针加载真实视图，分别测「无窗口」「离屏真窗口（`Left=-4000`）」：

```text
--- NO-WINDOW ---  desired=50x38  actual=26x38  cp=26x16.5
--- IN-WINDOW ---  desired=50x38  actual=26x38  cp=26x17.5
```

说明**当前模板代码本身没有问题**，差异只可能来自运行中的进程或宿主环境。

### 8.2 新增第 0 步：先证明进程加载的是新 DLL

真机排查必须先比对：

- `Get-Process VisionMaster` 的 `StartTime`
- `VisionMaster\bin\Debug\net9.0-windows\UI.dll` / `VisionMaster.dll` 的 `LastWriteTime`

**进程启动早于 DLL 修改时间 = 跑的是旧代码**，必须完全退出（托盘退出）再启动。本仓还有大量 `bin\Debug` / `bin\Release` 副本（Release 常是数天前的旧产物），必须以宿主 exe 所在目录为准。

### 8.3 页签容器由 `TabPanel` 改为 `StackPanel`

`TabPanel` 承担"多行换行 + 特殊排列"，在对话框宿主里行为不可预测。本弹窗页签固定一行，改为：

```xml
<StackPanel x:Name="HeaderPanel" Orientation="Horizontal" IsItemsHost="True" ... />
```

配合 `MinHeight` + `Border.Padding`，把与宿主相关的两个变量都消掉。

### 8.4 排查流程已固化为 Skill

`.zcode/skills/wpf-ui-debug/SKILL.md`：第 0 步证明新代码 → 静态排查 → net9 探针 → 按症状定位，含 P1~P6 六个实测坑。
