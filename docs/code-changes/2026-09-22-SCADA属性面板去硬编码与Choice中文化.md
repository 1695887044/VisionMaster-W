# SCADA 属性面板：去硬编码与 Choice 中文化

- 日期：2026-09-22
- 范围：**属性面板/工具箱的两项"去硬编码"基建**——① 面板配色抽成共享语义令牌（不再把十六进制写死在 XAML 里）；② Choice 型属性下拉显示中文、写回英文（新增全局词汇表）
- 用户原话：
  > 「属性栏显示的颜色硬编码去除」
  > 「属性栏的要改成中文,后续再扩展中英切换」
- 前置：[SCADA 位按钮](2026-09-22-SCADA位按钮.md)、[SCADA 数值域可读可写](2026-09-22-SCADA数值域可读可写.md)、[SCADA 图元控件库](2026-09-17-SCADA图元控件库.md)

---

## 一、需求与决策

### 1. 需求拆解

用户这两句话看着是"改文案"，实则是两件不同性质的事，落点也不在一层：

| # | 需求 | 本质 | 落点 |
|---|---|---|---|
| T-A | 颜色硬编码去除 | **基建**：把写死的颜色字面量抽成语义令牌 | `UI/Controls/Themes/Colors.xaml` + 两个视图 XAML |
| T-B | 行内十六进制串去除 | **界面**：颜色行不再直接显示 `#FFF8C00` | `ScadaPropertyView.xaml` 的 `EditColor` 模板 |
| T-C | 属性栏改中文 | **基建**：Choice 落盘值配中文显示名 | 新增 `Scada\Models\ScadaChoiceNames.cs` |
| T-D | 中英切换 | **后续**：换词表即换语言 | 本轮只留接口，不实现 |

T-A 与 T-B 是"同一个诉求的两个层面"（用户拍板**两条都做**）：先抽令牌（改一处、全局面板跟着变），再去掉行内串（颜色行只留色块 + 取色板）。T-C 与 T-D 同理：先立词汇表，将来换表就是换语言。

### 2. 决策清单

| # | 决策点 | 选择 | 依据 |
|---|---|---|---|
| A-1 | 令牌放哪张字典 | `UI/Controls/Themes/Colors.xaml`（已在 `Generic.xaml` 合并链第 5 行，全局可用） | 不新建文件、不动合并链；`App.xaml` 已含 `pack://application:,,,/UI;component/Themes/Generic.xaml` |
| A-2 | 令牌怎么命名 | **一律带 `Scada` 前缀**，且按"用在哪"而非"是什么颜色" | `PrimaryTextBrush` 已被 `ToolView.xaml`、`GlobalVariableView.xaml` 各自重定义成 `#0369A1` 作局部覆盖；不带前缀会撞名，撞名的后果是"改令牌改不动那一处" |
| A-3 | 视图怎么拿到令牌 | 每个 SCADA 视图在自己的 `UserControl.Resources` 里**再合并一次** `Colors.xaml`（"自带口粮"） | **断言宿主里没有 `Application.Current`**（`Program.cs` 写明），`App.xaml` 合并的字典取不到，`{StaticResource ScadaXxx}` 会抛 `XamlParseException`。合并是幂等的，无额外代价 |
| B-1 | 颜色行的入口 | **色块成为唯一入口**，删掉左侧十六进制文本框 | "手抄 `#AARRGGBB`"与取色板本是两套并存入口：想点色板的人不会背十六进制，会背的人又嫌色板碍事。精确值挂在色块悬停提示上；真要手打，弹窗里那个框还在 |
| B-2 | 色块怎么摆 | `HorizontalAlignment="Left"`、`44×20`，与 Bool 行的复选框同一左缘 | 几行颜色看下来是一条竖线，而不是"标签之后空一段、方块浮在右边" |
| B-3 | 弹窗偏移 | `HorizontalOffset` `-206` → **`-82`** | 弹窗宽 228、面板宽 260、色块左缘在面板内约 101px；默认"左边缘对齐色块"会让弹窗右边探出面板 60 多像素（面板贴窗口右沿，探出部分被裁掉）。`-82` 后弹窗落在面板内 19~247，左右各留十几像素边 |
| C-1 | 译名放哪一层 | **全局词汇表**（用户拍板），落 `Scada\Models\ScadaChoiceNames.cs` | ① 同一个值在不同属性上必须是同一个词（`Off` 在指示灯/泵/电机上是同一类意思），各属性各译一份早晚出现"这台叫『关』、那台叫『熄灭』"；② **中英切换 = 换这张表**，描述符与 XAML 一行不用动；③ 放 `Scada\Models\` 与 6 个既有 `DisplayName` 扩展方法（`ScadaAlign` / `ScadaAlarmEnums` / `ScadaActionType` / `ScadaRole` / `ScadaZMove`…）同源，Scada 工程零 WPF，不破 **D1** |
| C-2 | 描述符的 `Choices` 改不改 | **一行不改，仍是英文落盘值** | 候选是文件格式（进 .vms 的就是 `Circle`、`Output`），为好看改成中文会让旧 .vms 读不出来。**描述符是唯一真相（D2）** 在"文件格式"这一维上不许破 |
| C-3 | 面板怎么显示中文 | 走**两层候选**：`Choices`（落盘值）+ `ChoiceOptions`（值 + 中文名），下拉绑后者 | 模板要的是"值 + 名字"两列，`Choices` 只有值。这与既有 `ScadaActionTypeOption`（`Type` 写回 / `DisplayName` 显示）完全同构，不引进值转换器 |
| C-4 | 权限行的裸 string 候选怎么办 | **不覆写 `ChoiceOptions`，靠"认不出的值原样返回"通过** | 权限行候选本就是中文（"不限制 / 操作员 / 工程师 / 管理员"），过一遍词汇表原样出来。这条契约同时兜住"位按钮的中文候选"与"将来新加还没配译名的候选" |
| C-5 | 词表认不出怎么办 | **原样返回**，绝不返回空 | 若改成"查不到返回空"，权限下拉会当场变成四个空白项——比显示英文严重得多（用户根本不知道能选什么） |

### 3. 词汇表全清单（29 个值）

| 分组 | 英文落盘值 | 中文显示名 |
|---|---|---|
| 字重（12 处图元共用） | `Normal` / `SemiBold` / `Bold` | 常规 / 半粗 / 粗体 |
| 对齐（水平 + 垂直共用） | `Left` / `Center` / `Right` / `Justify` / `Top` / `Bottom` | 左对齐 / 居中 / 右对齐 / 两端对齐 / 顶部 / 底部 |
| 形状 | `Circle` / `Square` | 圆形 / 正方形 |
| 朝向 | `Horizontal` / `Vertical` | 水平 / 垂直 |
| IO 域 · 模式 | `Output` / `Input` / `InputOutput` | 输出 / 输入 / 输入输出 |
| IO 域 · 格式类型 | `Decimal` / `Hex` / `Binary` | 十进制 / 十六进制 / 二进制 |
| 状态灯 | `Off` / `On` / `Warning` / `Alarm` | 熄灭 / 点亮 / 警告 / 报警 |
| 设备与流动状态 | `Stopped` / `Running` / `Fault` | 停止 / 运行 / 故障 |
| 管路流向 | `LeftToRight` / `RightToLeft` / `None` | 从左到右 / 从右到左 / 不显示 |

**已知冲突（刻意共用一份译名）**：`Center` 同时供水平对齐与垂直对齐（都读作"居中"）；`Stopped/Running/Fault` 同时供泵、电机、管路的流动状态。行标签已说清是哪一维/哪台设备，表里再拆两份只会得到两条一模一样的译名。

**靠"原样返回"通过的三类值**：① 候选本就是中文（位按钮的 `置位/复位/取反/按下ON/按下OFF`）；② 运行期算出来的文案（权限行的"不限制"、坏值"未知角色(9)"）；③ 将来新加的候选还没配译名——显示成英文原值，难看但看得懂，绝不会一片空白。

---

## 二、修改文件清单

| # | 文件 | 动作 | 说明 |
|---|---|---|---|
| 1 | [Colors.xaml](../../UI/Controls/Themes/Colors.xaml) | 改 | 新增 24 支 `Scada*` 语义令牌（面板底/标题栏/白底表面/边框、五档文字、悬停与选中、语义红橙、ƒx 变量带…）；全部照抄改造前原值 |
| 2 | [ScadaPropertyView.xaml](../../VisionMaster/Views/ScadaPropertyView.xaml) | 改 | ① 全部硬编码色改引令牌（**0 处残留**）；② `UserControl.Resources` 自带合并 `Colors.xaml`；③ `EditColor` 删行内十六进制文本框、色块 44×20 左对齐、`Popup` 偏移 `-206→-82`；④ `EditChoice` 改绑 `ChoiceOptions` |
| 3 | [ScadaToolboxView.xaml](../../VisionMaster/Views/ScadaToolboxView.xaml) | 改 | 同上① ②（**0 处残留**） |
| 4 | [ScadaChoiceNames.cs](../../Scada/Models/ScadaChoiceNames.cs) | **新增** | `ScadaChoiceOption`（值 + 显示名）+ `ScadaChoiceNames`（29 条词汇表 + `DisplayName` + `ToOptions`） |
| 5 | [ScadaPropertyViewModel.cs](../../VisionMaster/ViewModels/ScadaPropertyViewModel.cs) | 改 | `Choices` 提为 `virtual` + 注释更新；新增 `virtual ChoiceOptions`；`ScadaRolePropertyRow.RefreshValue` 通知口径由 `nameof(Choices)` 改为 `nameof(ChoiceOptions)` |
| 6 | [Program.cs](../../ScadaChecks/Program.cs) | 改 | 新增 3 条断言 + 改造 1 条（Shape 行两层候选） |

### 唯一一处颜色合并

`#DCDFE6`（弹窗里取消按钮的描边）与 `#FFDCE0E6`（其余所有浅边框）本就是同一语义、只差 G 通道 1/255，统一到 `ScadaPanelBorderBrush`——肉眼不可辨，但从此只有一份。

### 词汇表 API 契约

```csharp
public static string DisplayName(string? value);   // 落盘值 → 中文名；认不出的原样返回；null/空 → 空串
public static IReadOnlyList<ScadaChoiceOption> ToOptions(IReadOnlyList<string> values);  // 顺序原样保留
```

`ToOptions` 顺序**原样保留**：候选顺序是描述符定的（如状态灯按 熄灭→点亮→警告→报警 由轻到重），这里只加一列名字，不该顺手重排。

### 下拉框绑法（`EditChoice`，与动作类型下拉同构）

```xml
<ComboBox DisplayMemberPath="DisplayName"
          ItemsSource="{Binding ChoiceOptions}"
          SelectedValue="{Binding Value, Mode=TwoWay}"
          SelectedValuePath="Value" />
```

`DisplayMemberPath` 决定看到什么（`圆形`），`SelectedValuePath` 决定写回什么（`Circle`）。选中项按 `Value` 匹配，所以中文显示名不参与写回，**落进 .vms 的永远是英文原值**。

---

## 三、踩到的两个坑

### 坑 1：断言宿主里没有 `Application.Current`

- 现象：全量断言在加载 `ScadaPropertyView.xaml` 时抛 `XamlParseException`——`无法找到名为"ScadaPanelBackgroundBrush"的资源`。
- 根因：断言宿主是控制台程序，**没有 `Application.Current`**，`App.xaml` 合并的资源字典取不到，`{StaticResource ScadaXxx}` 无处可寻。
- 修复：两个 SCADA 视图各自在 `UserControl.Resources` 里再合并一次 `/UI;component/Themes/Colors.xaml`（"自带口粮"）。合并是幂等的，运行态下与 `App.xaml` 的那次重叠不产生额外代价。→ 恢复全绿。

### 坑 2：`Scada.Controls` 没对 `ScadaChecks` 声明 `InternalsVisibleTo`

- 影响：断言驱动 `protected override` 的事件处理器不可行，必须靠公开包装方法（承前已有约束，本窗口未新增触发）。

---

## 四、验证结果

### 1. 编译

- `dotnet build VisionMaster.sln -v q --nologo` → **已成功生成，0 个错误**。

### 2. 断言

新增 3 条 + 改造 1 条，基线 **1456 → 1459**：

| # | 断言 | 钉住的事实 |
|---|---|---|
| 1 | Choice 候选里凡是英文的都有中文译名 | 判据取"纯 ASCII"：中文候选（位按钮）天然通过，英文候选必须有译名。**给将来加图元的人兜底**——漏配一个译名，表现是"面板上孤零零一个英文单词"，没人会主动查，放这里当场就红 |
| 2 | 权限行的中文候选过词汇表原样出来 | `Value == DisplayName`（不限制/操作员/工程师/管理员）。这条正是"认不出的值原样返回"要保住的：若改成"查不到返回空"，权限下拉会变成四个空白项 |
| 3 | 词汇表：认得的值翻中文，认不得的原样返回 | `Output→输出`、`Circle→圆形`、`置位→置位`、`不限制→不限制`、未配译名的新值→原值、`null→""` |
| 4（改造） | Shape 行候选分两层 | `Choices` 仍是 `Circle\|Square`（落盘值），`ChoiceOptions` 才配上 `圆形\|正方形`；并追加"改成正方形后模型里仍是 `Square`"（最怕的是中文被写进 .vms） |

**最终：通过 1459，失败 0，全部断言通过。**

### 3. 复核确认

- 两个 SCADA 视图 `#[0-9A-Fa-f]{6,8}` 正则命中 **0 处**（硬编码色清零）。
- `Colors.xaml` 含 **24 支 `Scada*` 令牌** + 5 支通用令牌。
- 4 条 T-C 相关断言过滤复核全绿（含"写回的是落盘值 Square，不是显示名「正方形」"）。

---

## 五、已知边界（未做 / 欠债）

| # | 项 | 状态 |
|---|---|---|
| 1 | **中英切换（T-D）** | 用户明示"后续再扩展"。本轮只留了"换词表即换语言"的接口（`ScadaChoiceNames.Table`），**未实现**。届时做法：把 `Table` 按语言取不同的一份，`DisplayName` 加一个语言参数或换静态表；描述符与 XAML 一行不用动 |
| 2 | **非 Choice 型属性的标签中文化** | 属性**行标签**（`DisplayName`）仍来自描述符，未纳入本轮；本轮只解决"候选值显示什么" |
| 3 | **画面上图元的默认文案** | 图元自身的文字（按钮上的"启动"等）不属属性面板范畴 |
| 4 | **`Scada` 工程零 WPF 约束** | 词汇表刻意放领域层（不破 D1），因此它给不出"随语言实时切换并通知界面"的能力——真要动态切换，通知机制得由 VM 层承担 |

---

## 六、下一件

用户本窗口两条指令（去硬编码 + 属性栏中文化）已全部落地并通过验证。按承前"**先深后广**"主线：

- **先深**（N-1 取色板 / N-2 数值域可读可写 / N-3 位按钮）已全部闭环。
- **后广**（横向扩图元数量）尚未启动：字符 IO 域 / 符号 IO 域 / 文本开关 / 位指示灯 / 字指示灯 / 图形开关 / 功能键 / 字按钮；画面级机制（软键盘 / 弹出画面 / 模板画面 / 画中画…）。
- 中英切换（T-D）作为独立关注点排在"后广"之后，届时只需换 `ScadaChoiceNames` 那张表。
