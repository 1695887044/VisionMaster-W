---
name: vm-property-grid
description: >
  用 FlatPropertyGrid / CardPropertyGrid（基于 [SuperDisplay] 反射的属性面板）在没有专门视图的前提下
  渲染任意配置对象。多态属性（按枚举切换底层配置类，如通信协议 ModbusTcp/ModbusRtu/SiemensS7、
  IO 传输 TCP/串口、驱动 Modbus/S7）只需给切换字段标 RequireRefresh = true + 给被切换对象标 [PropertyItem]，
  **不需要专门搭一个窗口或视图**；**互斥字段绝不要平铺在同一个类上**（否则两套参数同时暴露）。
  用在：新增/修改弹窗的配置表单、判断该不该手写视图、属性面板切换后不刷新、字段不显示或显示成类型全名、
  嵌套对象摊不开、两套互斥参数同时出现、枚举下拉显示英文。
when_to_use: >
  当你要给一个配置类（插件参数、连接配置、设备参数、草稿模型）做编辑界面，
  或怀疑属性面板"改了不刷新 / 字段不见 / 两套参数一起冒出来 / 该显示子对象却只显示一行"时；
  也用于判断"这个弹窗要不要手写 XAML 视图"，以及给多态配置接入切换。
---

# VisionMaster 属性面板（FlatPropertyGrid / CardPropertyGrid）用法

一句话：**给对象上的字段贴 `[SuperDisplay]`，把它丢给 `EasyDialog.ShowPropertyGridSync/Async` 就是编辑界面。**
不要为每个配置类手写 XAML 窗口——手写视图只在"需要画布、图表、表格、专用交互"时才值得。

---

## 1. 最小用法（三行）

```csharp
var config = new MyConfig();                       // 任何 POCO / BindableBase 都行
var ok = EasyDialog.ShowPropertyGridSync("参数设置", config);   // 同步：返回 true = 用户点了确定
// 或在 async 链里：
var ok2 = await EasyDialog.ShowPropertyGridAsync("参数设置", config);
```

自定义宿主里直接用控件：

```csharp
var grid = new FlatPropertyGrid { BindingObject = config };   // 表格式（Tab + 行线）
var card = new CardPropertyGrid { BindingObject = config };   // 卡片式（Tab + Expander + 12 栅格）
```

两种网格的生成器、处理器、刷新逻辑**完全共用** `PropertyGridBase`；
差别只有"内容怎么摆"（`UseCardLayout`）。要新增渲染能力，改基类/生成器清单，**不要在两份里各改一次**。

---

## 2. 显示哪些字段（契约边界）

```csharp
// PropertyGridDefaults.GetVisibleProperties
public static List<PropertyInfo> GetVisibleProperties(object bindingObject)
    => bindingObject.GetType()
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.GetCustomAttribute<SuperDisplayAttribute>()?.Visible == true)
        .ToList();
```

**没有 `[SuperDisplay]` 的属性一律不显示。** 这条是刻意的：它把"随便一个对象"和"可编辑配置"分开。

---

## 3. `[SuperDisplay]` 常用字段

| 字段 | 作用 |
|---|---|
| `Name` | 显示名（中文标签） |
| `Group = new[]{"1. 基本设置"}` | Tab 分组；字符串**数字前缀决定页签顺序**（`"10"` 排在 `"2"` 后，按数值比较） |
| `GroupPath = "一级/二级"` | 旧写法，`Group` 优先；卡片式把第二段渲染成 Expander |
| `Order` | 组内排序 |
| `GroupOrder` | 组间排序（数值） |
| `ColSpan` | 卡片式占几列（满宽 12），扁平式忽略 |
| `IsReadOnly` | 可见但只读 |
| `Visible = false` | 隐藏（子类隐藏基类字段时**必须 `override`，不能 `new`**，否则两行都出来） |
| `RequireRefresh = true` | **该属性变化后整表重建**（多态切换开关，见第 5 节） |
| `Description` | 提示文本（ToolTip + 无障碍 HelpText） |

可叠加的配套特性：
`[PropertyItem(Type = typeof(Control))]`（嵌套对象）、`[Required]`、`[RangeValidation(min,max)]`（数值框自动带上下限）、
`[RegexValidation(@"...")]`、`[Icon(IconCode = "M...")]`（标签前图标）、`[Permission(RequiredRole = "Admin")]`、
`[Command(Command = nameof(XxxCommand))]`（把这个 bool 渲染成"执行"按钮）。

---

## 4. 渲染结果由生成器决定（Priority 降序命中）

清单在 `PropertyGridDefaults.CreateGenerators()`，**顺序只表示注册顺序，命中看 `Priority`**：

| Priority | 生成器 | 命中条件 | 产物 |
|---|---|---|---|
| 10 | `NestedPropertyGridGenerator` | `[PropertyItem]` 且 Type 是 `Control`/`FrameworkElement` | 递归套一个**子属性网格**（跟随父级卡片/扁平形态） |
| 110 | `NumericGenerator` | int/long/float/double/decimal… | `NumericBox`（读 `[RangeValidation]` 做钳制） |
| 105 | `ComboSourceGenerator` | 标了 `[ComboSource]` | 可编辑下拉，候选挂在绑定对象自己身上 |
| 100 | `EnumGenerator` | 属性类型是 enum | 下拉，显示名取枚举成员上的 **`[Description]`** |
| 100 | `OptionSourceGenerator` | 标了 `[StepConfigOptions]` | 可编辑下拉（运行期候选，轴名/卡名等） |
| 100 | `StructValueGenerator` | string / 原始类型 | `TextBox` |
| 100 | `BoolStateGenerator` | bool | 开关（标 `[Command]` 则渲染成按钮） |
| 0 | `TypeGenerator` | 兜底（`[PropertyItem]` 指向自定义控件） | 实例化该控件，`DataContext` = 该子对象 |

**枚举显示名两条路不要搞混**（`R84`）：
- 走属性面板 `EnumGenerator` → 读 **`[Description("中文")]`**；
- 走 `InputPort<T>` 的 `LinkableValueEditor` → 读 **`[Display(Name = "中文")]`**。

---

## 5. 多态属性：不用专门搭窗口（本 skill 的重点）

### 5.1 做法

场景：一个"协议类型"下拉，用户选 ModbusTcp 显示 IP/端口，选 ModbusRtu 显示串口号/波特率。

```csharp
public class CommunicationConfig : BindableBase
{
    // ① 切换开关：值一变 → 整表重建
    [SuperDisplay(Name = "协议类型", Group = new[] { "1. 基本设置" }, Order = 2,
                  ColSpan = 12, RequireRefresh = true)]
    public CommunicationType Protocol
    {
        get => field;
        set { OnChanged(value); field = value; RaisePropertyChanged(); }   // OnChanged 换掉 Config
    }

    // ② 被切换的底层配置：标 [PropertyItem] → 递归摊成一整块子表单
    [SuperDisplay(Name = "底层链路参数", Group = new[] { "2. 链路配置" }, Order = 1, ColSpan = 12)]
    [PropertyItem(Type = typeof(System.Windows.Controls.Control))]
    public ConnectionConfigBase Config { get => field; set => SetProperty(ref field, value); }
        = new ModbusTcpConfig();

    private void OnChanged(CommunicationType t) =>
        Config = t switch                                   // 换成一个不同的**实例**
        {
            CommunicationType.ModbusTcp => new ModbusTcpConfig(),
            CommunicationType.ModbusRtu => new SerialConfig(),   // 串口类，字段完全不同
            CommunicationType.SiemensS7 => new SiemensS7Config(),
            _ => Config                                       // 未实现：安静保留，交给 Validate 报人话
        };
}
```

要点只有四条：

1. **切换字段标 `RequireRefresh = true`**。
2. **被切换的对象是另一个实例**（不是原地改字段）。`NestedPropertyGridGenerator` 在构建时把 `BindingObject` 设成当时那个实例，原地改字段不会换绑。
3. **被切换对象标 `[PropertyItem(Type = typeof(Control))]`**（或 `FrameworkElement`）。`Type` 只需是这两个"哨兵"之一——真正的渲染是递归子网格，不是实例化这个类型。
4. **两个字段可以在不同页签**。`RequireRefresh` 的语义是**整表重建**，不是"只重建触发者所在的页签"。

### 5.1.1 反面：互斥字段绝不要平铺（2026-10-19 真机事故）

属性面板**没有"按另一个字段隐藏同组字段"的能力**——`Visible = false` 是编译期常量，不是条件显示。
所以下面这种写法一定错：

```csharp
// ❌ 错：TCP 与串口字段平铺在一个类上，界面会同时显示两套
public sealed class IoBusEditModel : BindableBase
{
    [SuperDisplay(Name = "传输类型")] public IoTransportKind TransportKind { get; set; }  // 普通自动属性，没有通知
    [SuperDisplay(Name = "TCP 主机")] public string Host { get; set; }
    [SuperDisplay(Name = "串口")]    public string PortName { get; set; }
}
```

真机症状：选"TCP"时串口参数还挂在那儿，选"串口"时 IP/端口还在。用户的第一句反馈通常是
"这个添加功能有问题，为什么串口和 TCP 参数一起暴露了"。

正确写法是把每种取值的字段集做成**独立类**，外层只留公共字段 + 一个切换字段 + 一个嵌套对象：

```csharp
public abstract class IoLinkEditModel : BindableBase { /* 各自 ApplyTo(descriptor) */ }
public sealed class TcpLinkEditModel    : IoLinkEditModel { Host / Port / 连接超时 / 读超时 }
public sealed class SerialLinkEditModel : IoLinkEditModel { PortName / BaudRate / DataBits / Parity / StopBits }

public sealed class IoBusEditModel : BindableBase
{
    [SuperDisplay(Name = "传输类型", RequireRefresh = true)]
    public IoTransportKind TransportKind
    {
        get => _kind;
        set { if (SetProperty(ref _kind, value)) Transport = value == IoTransportKind.Serial ? _serialDraft : _tcpDraft; }
    }

    [SuperDisplay(Name = "链路参数")]
    [PropertyItem(Type = typeof(System.Windows.Controls.Control))]
    public IoLinkEditModel Transport { get => _transport; private set => SetProperty(ref _transport, value); }
}
```

**两条容易漏的细节**：

- **切换字段必须走 `SetProperty`**。写成普通自动属性 `public X Kind { get; set; }` 的话根本不发
  `PropertyChanged`，`RequireRefresh = true` 等于没写——这是本轮第二个错误，改模型时最容易顺手写回去。
- **保留另一种取值的草稿实例**（`_tcpDraft` / `_serialDraft`）。否则用户填完 TCP、切去看串口、再切回来，
  IP 就没了。写回描述符时两套都要写（描述符本就并存两套字段，运行期只看 `TransportKind`）。

### 5.1.2 怎么验证"真的只显示该显示的字段"

不要只断言"对象类型换对了"——`Transport is SerialLinkEditModel` 通过而字段仍平铺的话，界面照样错。
**直接查网格会渲染哪些字段**：

```csharp
var visible = PropertyGridDefaults.GetVisibleProperties(model.Transport).Select(p => p.Name).ToList();
Assert(visible.Contains(nameof(SerialLinkEditModel.PortName)));
Assert(!visible.Contains(nameof(TcpLinkEditModel.Host)));       // 关键的反向断言
```

`GetVisibleProperties` 就是 `PropertyGridBase` 渲染时用的同一个入口，所以这条断言与界面行为同源。
范例见 `IoChecks/Program.cs` 的 `TestBusLinkSwitching` / `TestDriverConfigSwitching`（18 条）。

### 5.2 为什么必须是整表重建（血泪教训）

2026-10-19 的真机 Bug：`Protocol` 在「1. 基本设置」，`Config` 在「2. 链路配置」。
属性面板一度为省一次物化被改成"只重建该属性所属的页签"（`RebuildTabForProperty`）——
于是 `Protocol` 的变化只重建了「基本设置」，**「链路配置」里的子网格仍然绑着旧实例**：

- 先改协议、再第一次点开「链路配置」→ 正常（懒加载那刻才构建，取到新实例）；
- 先点开过「链路配置」、再改协议 → **永远不刷新**，屏幕上还是 IP/端口，而且来回切页签也不自愈
  （旧页签仍在 `_builtTabs` 里，懒构建不会再跑一次）。

现在 `RequireRefresh` 分支固定调 `UpdatePropertyGrid(preserveSelectedTab: true)`，
按页签标题保住当前选中项（整表重建会换掉所有 TabItem 实例，不保就跳回第一页）。

> **给这个控件加"优化"之前先问一句：被驱动的对象会不会在别的页签？**
> 只要可能是，就只能整表重建。

### 5.3 什么时候**确实**需要手写视图

| 需要手写 XAML | 用属性面板就够 |
|---|---|
| 画布 / 图像预览（ROI、标记、HALCON 窗口） | 纯字段表单（连接参数、算法阈值、设备配置） |
| 表格（`ListView + GridView`、`DataGrid`）与行内编辑 | 嵌套对象（`[PropertyItem]` 自动递归） |
| 自定义曲线 / 图表 | 按枚举切换字段集（`RequireRefresh` + 两个子类） |
| 需要实时联动多个区域（拖滑块看预览） | 需要分组/排序/校验（`Group`/`Order`/`[Required]`） |

判断依据：**界面元素与字段是一一对应 → 属性面板；界面要表达"关系/图形/时序" → 手写视图。**

---

## 6. 值怎么进来、怎么出去（"取消"语义）

属性面板是**即时写属性**的：用户一改就写进了对象，点"取消"**不会回滚**。
所以"确定才生效"的业务一律走**草稿/副本**：

```csharp
// 复制 → 编辑副本 → 确定才回写
var copy = live.Clone();
if (!EasyDialog.ShowPropertyGridSync($"编辑 [{live.Name}]", copy)) return;   // 取消 = 副本丢弃
live.CopyFrom(copy);
```

`ExecuteAdd` 则相反：新建一个实例直接编辑，取消就整个丢掉，不注册到管理器。

---

## 7. 刷新与线程

- **普通字段不用管**：WPF 绑定自己更新。
- **只有 `RequireRefresh = true` 的属性**会触发整表重建（先等 50ms 让下拉关闭/布局稳定，再异步投递到 UI 线程）。
  这样做是为了不每敲一个字就重绘（会抢走输入焦点）。
- **跨线程安全**：面板直接订阅 `INotifyPropertyChanged`，事件在**属性变更源线程**上跑（连接线程/轮询线程/引擎线程），
  基类会先封送 UI 线程再处理。数据源是 WPF 依赖对象，非 UI 线程读会抛 `VerifyAccess`。
- **换数据源**：给 `BindingObject` 赋新对象会自动重绑通知并整表重建。

---

## 8. 常见坑速查

| 症状 | 原因 | 处理 |
|---|---|---|
| 字段完全不显示 | 没标 `[SuperDisplay]`，或标了 `Visible = false` | 补特性 |
| **两套互斥参数同时出现**（串口与 TCP、Modbus 与 S7） | **字段平铺在同一个类上**——属性面板没有条件显示能力 | 拆成独立子类 + `RequireRefresh` + `[PropertyItem]`，见 5.1.1 |
| 切换下拉后界面不变 | 切换字段没标 `RequireRefresh`；**或切换字段是普通自动属性（不发 `PropertyChanged`）**；或原地改了子对象字段而不是换实例 | 三条都做：`RequireRefresh` + `SetProperty` + 换实例 |
| 切走再切回，已填参数丢了 | 切换时直接 new 新实例，没保留草稿 | 按取值缓存 `_tcpDraft` / `_serialDraft` |
| 子对象只显示一行/一个空框 | 没标 `[PropertyItem]`，或 `Type` 不是 `Control`/`FrameworkElement` | 补 `[PropertyItem(Type = typeof(Control))]` |
| 切了以后只有部分页签变了 | 有人把 `RequireRefresh` 改回"只重建单个页签" | 恢复整表重建，见 5.2 |
| 同一字段出现两行 | 子类用 `new` 重贴 `[SuperDisplay]` | 改 `override` |
| 枚举显示英文 | 成员上没标 `[Description]`（属性面板走 Description，不是 Display） | 补 `[Description("中文")]` |
| 数字框没有上下限 | 属性上没标 `[RangeValidation]` | 补特性 |
| 属性面板打开一片空白 | 宿主没合并 `UI;component/Themes/Generic.xaml`（控件找不到隐式样式） | 宿主合并主题；断言宿主可挂窗口级字典 |

---

## 9. 参考位置

| 内容 | 文件 |
|---|---|
| 公共基类（依赖属性、管线、刷新、线程兜底） | `UI/Controls/CustomControl/PropertyGrid/Core/PropertyGridBase.cs` |
| 生成器清单 / 处理器管线 / 可见性契约 | `UI/Controls/CustomControl/PropertyGrid/Core/PropertyGridDefaults.cs` |
| 扁平 / 卡片两种布局 | `UI/Controls/CustomControl/PropertyGrid/FlatPropertyGrid.cs`、`CardPropertyGrid.cs` |
| 弹窗入口 | `UI/Controls/CustomControl/Message/EasyDialog.cs`（`ShowPropertyGridAsync/Sync`） |
| 多态属性实例（通讯） | `Core/CommunicationContracts/CommunicationConfig.cs`（`Protocol` + `Config`） |
| 多态属性实例（IO，含草稿保留与可见字段断言） | `VisionMaster/ViewModels/DialogViewModels/IoConfigViewModel.cs`（`TransportKind` + `Transport`、`DriverId` + `DriverConfig`） |
| 可见字段断言范例 | `IoChecks/Program.cs` 的 `TestBusLinkSwitching` / `TestDriverConfigSwitching` |
| 弹窗引擎说明 | `docs/基础设施/EasyDialog弹窗引擎使用文档.md` |

---

## 10. 给自动化会话的检查清单（照做即可）

新增一个"按类型切换字段集"的配置界面时，按顺序自问：

1. 这些字段里，有没有**两组互斥**的（选了 A 就不该出现 B）？
   → 有：**不要平铺**，拆成 `AXxxEditModel` / `BxxxEditModel` 两个类（都继承一个抽象基类，基类负责 `ApplyTo(descriptor)`）。
2. 外层切换字段有没有 `[SuperDisplay(..., RequireRefresh = true)]`？
3. 它的 setter 是不是 `if (SetProperty(ref _x, value)) Child = Pick(value);`（**不是** `public X X { get; set; }`）？
4. 嵌套对象有没有 `[PropertyItem(Type = typeof(System.Windows.Controls.Control))]`？
5. 切换时是"换新实例"还是"原地改字段"？→ 必须换实例。
6. 来回切换会不会丢已填参数？→ 按取值缓存草稿。
7. 写回模型/描述符时，两套字段是否都保留了（运行期只看当前那套，但另一套不该被清空）？
8. 有没有断言"当前取值下的**可见属性集**不含另一套的字段"（用 `PropertyGridDefaults.GetVisibleProperties`）？

以上 8 条都满足，属性面板就会自动按切换显示，**不需要写任何 XAML 视图**。
