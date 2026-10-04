# 创建 ROI 插件（Plugin.CreateRoi）说明书

> ⚠️ **本文已归档，内容已并入 [创建ROI插件.md](创建ROI插件.md)（唯一主文档）。**
> 本件仅作历史留档，**不再更新**。注：原稿第 10~13 章（使用指南/FAQ/踩坑/一图总览）未写完，相关章节以主文档为准。
> 配套阅读：ROI 与模板匹配的坐标系衔接（`Domain_` / `Offset_` 两种解法）见 [../模板匹配/模板匹配插件.md](../模板匹配/模板匹配插件.md) 附录 A.2 与案例 6。

> 面向小白的完全讲解版：与《模板匹配插件》同一套讲法——
> 原理篇打基础、逐行精讲看懂代码、视觉零基础篇补常识。
> 读完后你应该能：看懂全部源码、独立用 ROI 裁图、理解"动态端口"这个核心机制。
>
> 配套阅读：ROI 与模板匹配的坐标系衔接（`Domain_` / `Offset_` 两种解法）见 [../模板匹配/模板匹配插件.md](../模板匹配/模板匹配插件.md) 附录 A.2 与案例 6。

---

## 目录（第一部分）

1. [这是什么插件？](#1-这是什么插件)
2. [原理篇：ROI 与图像裁剪是怎么工作的](#2-原理篇roi-与图像裁剪是怎么工作的)
3. [文件结构总览](#3-文件结构总览)
4. [数据模型详解（RoiItem.cs）](#4-数据模型详解)
5. [配置项、端口与"动态端口"机制](#5-配置项端口与动态端口机制)
6. [运行流程详解（RunAlgorithm：每一帧发生了什么）](#6-运行流程详解)
7. [涂擦系统详解（涂抹/擦除/游程编码持久化）](#7-涂擦系统详解)
8. [掩膜与抠图详解（BuildMaskAndResult）](#8-掩膜与抠图详解)
9. [配置界面详解（CreateRoiView + 四个视图辅助类）](#9-配置界面详解)
10. [手把手使用指南](#10-手把手使用指南)
11. [常见问题与排查（FAQ）](#11-常见问题与排查)
12. [代码里的"踩坑记录"汇编](#12-代码里的踩坑记录汇编)
13. [一图总览](#13-一图总览)

---

## 1. 这是什么插件

### 1.1 一句话说明

**创建 ROI（Region of Interest，感兴趣区域）= 在图像上画一个或几个"框"，告诉后续步骤"只看框里的，框外的别管"。**

生活比喻：

> 你拿到一张全班合影，但只关心第三排的三个同学。
> 你用手指把那块区域圈出来——这就是 ROI。
> 之后所有分析（认脸、数人数）都只在这个圈里进行，
> 圈外的背景、路人、黑板统统忽略。

### 1.2 它在流程中的位置

归在**「常用工具」分组**，是流程里最常用的"预处理/圈选"步骤：

```
[图像采集] → [创建 ROI] → [Blob 分析] / [模板匹配] / [颜色检查] → …
                  ↓
      输出：裁剪图（Crop_xxx）+ 合并区域（MaskRegion）+ 二值掩膜（MaskImage）
```

### 1.3 核心特性一览

| 特性 | 说明 |
|---|---|
| 多 ROI | 想画几个画几个，每个有独立名字，列表管理 |
| 三种形状 | 矩形（可旋转）/ 圆形 / 椭圆 |
| **动态输出端口** | 每个 ROI 自动生成 `Crop_名字` 等端口——这是本插件最核心的机制（见第 5 章） |
| 涂抹/擦除 | 画笔补充/修掉区域，比拖句柄更自由的"手涂" |
| 三种输出 | 裁剪图 + 带 domain 原图 + 裁剪原点坐标，下游按需取用 |
| 掩膜输出 | 合并区域 + 二值掩膜图，可做"排除模式"（框外才算） |
| 参数微调 | 列表选中 ROI 后可直接用数值框精调位置尺寸 |
| 可视化 | 掩膜合成图发布主界面，一帧看清全部 ROI |

### 1.4 与模板匹配插件的分工

| | 创建 ROI | 模板匹配 |
|---|---|---|
| 回答的问题 | "看哪里？" | "目标在哪？" |
| 框怎么来 | **人画的**（配置时画好，固定） | 算法**找出来的**（每帧都变） |
| 有无"学习" | 无 | 有（create_shape_model） |
| 典型用途 | 圈定检测区、裁小图分头处理 | 定位、给下游提供坐标 |

一句话：ROI 是**静态的框**（配置期确定），模板匹配是**动态的框**（运行期确定）。
产线上两者常配合：先用 ROI 圈出工位区域，再在区域内做模板匹配（搜索更快更稳）。

---

## 2. 原理篇：ROI 与图像裁剪是怎么工作的

### 2.1 一切从"区域"开始

复习第一部分 21 章的概念：图像是数字表格，**区域（HRegion）是"剪纸"**——
只有形状没有像素值，标记"哪些位置被选中"。

本插件的一切都围绕区域展开：

```
画布上画的框（矩形/圆/椭圆）
      ↓ 参数化保存（RoiItem.Params，见第 4 章）
运行时按参数重新生成区域（gen_rectangle2 / gen_circle / gen_ellipse）
      ↓ 多个区域合并（union）、涂擦修正（∪涂抹 −擦除）
合并有效区域（MaskRegion 端口）
      ↓ 两条应用路径
路径A：按 ROI 逐个裁图  →  Crop_xxx 端口
路径B：整图做掩膜抠图   →  MaskImage 端口
```

### 2.2 参数化存储：为什么不直接存区域对象？

`RoiItem.Params` 存的是**一串数字**（如矩形存 5 个数），
而不是 HALCON 的 HRegion 对象。原因（源码注释原话）：

> 用参数而非 Halcon 对象存储，保证 ROI 可序列化、可复现（运行时按参数重建区域）

- HRegion 是内存对象，**没法直接进 JSON**；
- 数字数组天然可序列化、跨电脑一致；
- 运行时 3 行代码就能按参数重新生成区域——**存"配方"，不存"成品"**。

这是整个视觉软件行业的通用做法（第一部分模板匹配的 Rect 5 参数也是同一思想）。

### 2.3 裁剪的两种姿势：Crop vs Domain

本插件给下游提供**两种**"看 ROI"的方式，这是它最精妙的设计：

**姿势一：真裁剪（Crop_xxx 端口）**

```
原图 1000×800                裁剪图 300×200
┌───────────────────┐       ┌─────────┐
│  ┌──────┐         │       │ ROI 内容 │   ← 只剩框内像素
│  │ ROI  │         │  ──▶  └─────────┘      坐标原点变了！
│  └──────┘         │        (0,0) = 原图的 (originRow, originCol)
│                   │
└───────────────────┘
```

- 图变小了，下游处理快；
- **但坐标系变了**：裁剪图的 (0,0) 不是原图的 (0,0)！
  所以插件同时输出 `OffsetRow_xxx` / `OffsetCol_xxx`（裁剪原点），
  下游换算：`全局坐标 = 本地坐标 + 偏移`；
- 换算麻烦——漏加一次偏移，结果就静默错一个"框左上角"的距离。

**姿势二：带 domain 的原图（Domain_xxx 端口）**

```
原图 1000×800（没变小，但盖了章）        下游处理时
┌───────────────────┐
│  ▓▓▓▓▓▓▓          │   ▓ = domain 圈内（有效）
│  ▓ROI▓            │   其余像素"在但不算"
│  ▓▓▓▓▓▓▓          │
│                   │   ★ 坐标系没动！(0,0) 还是原图左上角
└───────────────────┘
```

- 图的大小不变（内存不省），但 HALCON 算子**只在 domain 圈内干活**；
- **坐标系没变**——模板匹配在它上面搜索，返回的还是全局坐标；
- **下游插件一个都不用改**就能吃到 ROI（源码注释实测验证过）。

**怎么选？**

| 场景 | 用哪个 |
|---|---|
| 下游是"智能"插件（自己认 domain）：模板匹配、Blob、卡尺 | **Domain_xxx**（零换算）|
| 下游是"傻"处理（只管手里这张图）：脚本、导出、发送 | Crop_xxx + Offset 换算 |
| 只是想省算力缩小图 | Crop_xxx |

> 插件把三种端口全给了（Crop + Domain + Offset），下游按需选——
> **不替用户做选择题，但把每条路的路标都立好**。

### 2.4 掩膜抠图的数学：乘法

`MaskImage` 是一张 0/255 的二值图（有效区 255，其余 0）。
"抠图"就是**原图 × 掩膜 ÷ 255**：

```
原图像素   182    掩膜 255   182 × 255 ÷ 255 = 182   ← 保留
原图像素    90    掩膜   0    90 × 0 ÷ 255    =  0    ← 变黑
```

每个像素做一次乘法——这就是 `mult_image` 算子（第 8 章细讲，
包括为什么缩放因子必须写 `1/255.0` 而不是先除后乘的"截断白屏"坑）。

**排除模式（MaskInvert）**：把掩膜黑白反转——ROI 圈的反而变黑，**圈外保留**。
用途："这个区域是坏的，检测除它以外的所有地方"。

### 2.5 合并区域的一条铁律

源码 62 行注释写死了语义：

```
合并有效区域 = (ΣROI ∪ 涂抹) − 擦除     （不随排除模式反转）
```

- `MaskRegion` 永远是"**哪里有效**"（正语义）；
- 反转只发生在 `MaskImage`（二值图）那一层；
- **一个概念只在一处反转**——如果区域也跟着反转，下游拿到"排除后的区域"再乘一次反转，语义就乱套了。

> 这是"单一事实来源"原则（模板匹配说明书 20.5 铁律 2）的区域版：
> 正语义只有一份，反转是**视图**不是**数据**。

---

## 3. 文件结构总览

插件一共 5 个源文件（比模板匹配还精简）：

| 文件 | 行数 | 职责 | 通俗比喻 |
|---|---|---|---|
| `CreateRoiPlugin.cs` | 895 | **主体**：配置、端口（含动态端口）、画布同步、涂擦、掩膜、运行裁剪 | 车间主任 |
| `Models/RoiItem.cs` | 207 | ROI 条目数据模型 + 参数微调行包装（RoiParamEntry） | 每个框的档案卡 |
| `CreateRoiView.xaml` | 248 | 配置界面布局 | 操作台面板 |
| `CreateRoiView.xaml.cs` | 89 | 视图桥接 + **4 个值转换器/附加属性**（见 9.3） | 面板接线与小工具 |
| `Plugin.CreateRoi.csproj` | — | 工程文件 | 采购清单 |

依赖关系：

```
CreateRoiView.xaml(.cs) ──绑定──▶ CreateRoiPlugin（DataContext）
                                     │
     ┌───────────────┬───────────────┼──────────────┬─────────────┐
     ▼               ▼               ▼              ▼             ▼
RoiItem.cs      ImageEdit 控件   DrawingObjectInfo   VisionPluginBase  IDynamicOutputProvider
（档案卡）      （画布交互）      （画布对象包装）      （框架基类）      （★动态端口合同，见 5.3）
```

注意类声明多了个新合同：

```csharp
public class CreateRoiPlugin : VisionPluginBase, IPluginCustomViewProvider, IDynamicOutputProvider
//                                                                ★ 模板匹配没有这个
```

`IPluginCustomViewProvider` 是"我有配置界面"；`IDynamicOutputProvider` 是
"我的输出端口**运行期才定**"——这就是动态端口机制的入口（下一章主角）。

---

## 4. 数据模型详解（RoiItem.cs）

### 4.1 RoiItem：一个框的档案卡

```csharp
public class RoiItem : ObservableObject    // ObservableObject = Prism 的 INPC 基类
{
```

（INPC 是什么、三段式 setter 为什么存在，见模板匹配说明书 14.3——本插件用的
是 Prism 封装版 `SetProperty(ref _x, value)` 和 `OnPropertyChanged()`，
行为完全一致，只是写法短。）

#### 三个持久字段（随方案落盘）

| 属性 | 类型 | 说明 |
|---|---|---|
| `Name` | string | ROI 名字（默认 "ROI"）。**下游按名引用端口**（Crop_名字），也是唯一键 |
| `ShapeType` | DrawShapeType | 矩形 / 圆形 / 椭圆 |
| `Params` | double[] | 形状参数（见下表）|

#### Params 的参数约定（与 HALCON 算子一一对应）

```csharp
// 类注释原文：
// - Rectangle: [row, col, phi, length1, length2]（可旋转矩形）→ GenRectangle2
// - Circle:    [row, col, radius]                              → GenCircle
// - Ellipse:   [row, col, phi, ra, rb]                         → GenEllipse
```

| 形状 | [0] | [1] | [2] | [3] | [4] |
|---|---|---|---|---|---|
| 矩形 | 中心行 | 中心列 | 角度（弧度）| 半长 | 半宽 |
| 圆 | 圆心行 | 圆心列 | 半径 | — | — |
| 椭圆 | 圆心行 | 圆心列 | 旋转角 | 长半轴 | 短半轴 |

> 圆 3 个参数、矩形/椭圆 5 个——和模板匹配"统一 5 参数"不同，这里**存实际个数**。
> 为什么？因为这份参数要**直接喂给 Gen\* 算子**（`BuildRegion` 里
> `region.GenCircle(p[0], p[1], p[2])` 参数个数严格对应），不需要补位。

#### 三个只读显示属性（[JsonIgnore]，不落盘）

```csharp
public string DisplayText => $"{Name}  [{ShapeTypeCN}]";
// 列表里显示"ROI_1  [矩形]"——英文枚举名对现场不友好，中文化
public string ShapeTypeCN => ShapeType switch { ... };     // 枚举→中文
public IReadOnlyList<string> ParamNames => ShapeType switch { ... };
// 每个参数的中文名（"中心行(R)"…），与 Params 下标一一对应——给参数微调面板用
```

### 4.2 ParamEntries：参数微调的"翻译层"（本文件最精彩的设计）

界面要给每个参数一个数值框（"中心行(R)" [123.4]）。数值框绑什么？

**难点**：Params 是 `double[]`——**数组元素没法做 INPC 通知**（数组不支持绑定）。
解决办法：包一层"行对象"：

```
RoiItem（档案卡）
  ├── Params: double[5]              ← 数据本体（单一数据源）
  └── ParamEntries: 5 个 RoiParamEntry   ← 每行 = 一个数值框
        ├── Name = "中心行(R)"       ← 显示名
        ├── Index = 0                ← 对应 Params[0]
        ├── Text  ←→ 格式化显示（绑 TextBox）
        └── Value ←→ 读写 Params[Index]（真正的门）
```

**RoiParamEntry（207 行文件的后半）逐个看**：

```csharp
public double Value
{
    get  { return _owner.Params[_index]; }              // 读：实时取数组
    set
    {
        if (Math.Abs(_owner.Params[_index] - value) < double.Epsilon) return;
        // 值没变就返回（防抖）
        _owner.Params[_index] = value;                  // 写：写回数组本体
        OnPropertyChanged();                            // 通知自己的绑定
        _owner.NotifyParamEdited();                     // ★ 上抛"我改了"
        // → 插件的 OnSelectedRoiParamEdited 收到 → 同步画布句柄 + 刷新掩膜
    }
}
```

**Text（文本层）**：处理"人输入的是字符串"这件事：

```csharp
public string Format => Name.Contains("角") || Name.Contains("Phi") ? "F4" : "F1";
// 角度参数是弧度小量（如 0.5236），F1 只显示"0.5"——用 F4 四位小数
set {
    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        Value = v;                    // 合法输入：写回
    else
        OnPropertyChanged();          // 非法输入：按当前值还原显示（不崩、不静默）
}
// InvariantCulture：小数点永远是"."——防止德语/中文系统把","当小数点解析错
```

**拖拽高频场景的优化**（55~60 行注释讲得很清楚）：

- 拖拽句柄 → 插件整体替换 `roi.Params = 新数组` → **长度没变时不重建集合**，
  只对每个 entry `RaiseWithValue()`（刷新 Value/Text 通知）→
  数值框跟着拖拽实时跳动；
- 若每次替换都重建 ObservableCollection，WPF 刷新不可靠且卡。

**[JsonIgnore] 的必须性**（107~111 行注释）：

> RoiParamEntry 无无参构造且持有 owner 引用（纯 UI 包装，不持久化）。
> Newtonsoft 默认会填充 get-only 集合属性……不加此特性反序列化会抛
> "Unable to find a constructor"

——一个纯 UI 包装类，若不加 `[JsonIgnore]`，**加载方案时直接崩**。
这是"UI 层与数据层必须隔离"的活教材。

---

## 5. 配置项、端口与"动态端口"机制

### 5.1 配置项（[StepConfig]）

| 配置项 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `DisplayViewIndex` | int | 1 | 掩膜合成图发布到主界面窗口（0=不发布）|
| `MaskInvert` | bool | false | 排除模式：ROI 内变黑、圈外保留。setter 里 `ScheduleMaskPreview()`——改了立刻刷新预览 |
| `RoiList` | ObservableCollection\<RoiItem\> | 空 | **整个 ROI 列表随方案序列化**。setter 里 `RebuildDynamicOutputs()`——列表换血，端口跟着重建 |
| `SmearDrawData` | string | 空 | 涂抹区域持久化快照（游程编码文本，见第 7 章）|
| `SmearEraseData` | string | 空 | 擦除区域快照（同上）|

注意 `RoiList` 直接以 `[StepConfig] ObservableCollection` 持久化——
比模板匹配的"整库序列化成 JSON 字符串"更直接（框架支持复杂类型序列化时可用此法）。

### 5.2 固定端口

| 端口 | 方向 | 类型 | 说明 |
|---|---|---|---|
| `SrcImage` | 入 | HImage | 待处理图像 |
| `RoiCount` | 出 | int | **本轮实际成功输出**的裁剪图数（≠配置数！）|
| `MaskRegion` | 出 | HRegion | 合并有效区域（正语义，见 2.5）|
| `MaskImage` | 出 | HImage | 二值掩膜图（排除模式下反转）|

### 5.3 动态端口：本插件的核心机制（重点章节）

**问题**：模板匹配的输出端口是**编译期就定死**的（Score/Row/…）。
但本插件的 ROI 是用户配置时画的——画了 3 个框，就该有 3 个裁剪输出；
画 10 个就该有 10 个。**端口数量运行期才知道，怎么办？**

**答案**：动态端口。签了 `IDynamicOutputProvider` 合同后，
插件可以随时"增删自己的输出端口"。每个 ROI 生成 **4 个**端口：

| 端口名 | 类型 | 内容 |
|---|---|---|
| `Crop_{名字}` | HImage | 裁剪图（只含 ROI 内容，原点已搬走）|
| `Domain_{名字}` | HImage | 带 domain 的原图（全幅，只在 ROI 内有效，**全局坐标**）|
| `OffsetRow_{名字}` | double | 裁剪图原点在原图的行（global_row = local_row + 本值）|
| `OffsetCol_{名字}` | double | 列同上 |

例如画了名为 "ROI_0" 的框，下游接线面板上就会出现：
`Crop_ROI_0`、`Domain_ROI_0`、`OffsetRow_ROI_0`、`OffsetCol_ROI_0`。

**RebuildDynamicOutputs()（518 行）逐段读**：

```csharp
public void RebuildDynamicOutputs()
{
    ClearDynamicOutputs();                    // ① 先清掉旧端口（列表变了就重建）
    var snapshot = new List<DynamicPortInfo>();
    var seen = new HashSet<string>(StringComparer.Ordinal);   // 重名防御

    foreach (var roi in RoiList)
    {
        string portName = $"Crop_{roi.Name}";
        if (!seen.Add(portName)) continue;    // ② 重名 ROI 只建一个端口（兜底历史数据）

        AddDynamicOutput(new OutputPort<HImage>(portName, $"ROI '{roi.Name}' 的裁剪图"));
        snapshot.Add(new DynamicPortInfo { Name = portName, DataTypeName = ..., ... });
        // ③ 建端口 + 登记快照
        ...
    }
    if (StepData != null)
        StepData.OutputPortDefinitions = snapshot;
        // ④ ★ 把端口定义快照交给流程编译器——
        //    编译的运行实例靠这个快照重建端口，接线面板才有东西可连
}
```

**何时触发重建**（514 行注释）：

- 配置实例：`RoiList` 变化时（画布增删 ROI → `OnRoiListChanged` → 调它）；
- 编译实例：流程编译器在 `ApplyConfigValues` **之后**调它（从存盘快照恢复端口）。

> 与模板匹配"解析必须放 ApplyConfigValues"同一课：**运行实例不走配置界面**，
> 它的端口全靠快照恢复——不写第 ④ 步，编译后的流程就接不上线。

**为什么 Offset 拆成两个 double 端口**（556~561 行注释）：

> 拆成两个 double 而不是一个 HTuple：下游（脚本/变量/计算）可直接当标量连线，
> 也保住端口的编译期类型检查（不引入 object 多态端口）。

——方便连变量、类型安全。**端口设计以"下游好连"为准，不以"上游省事"为准。**

**运行期写动态端口的所有权细节**（596~609 行的两个 helper）：

```csharp
private bool SetImagePort(string portName, HImage? image)
{
    if (!Outputs.TryGetValue(portName, out var port)) return false;  // 端口不存在
    if (port is not OutputPort<HImage> typed) return false;          // 类型不符
    typed.Value = image;                                             // 接管！
    return true;                                                     // ★ 返回"我接管了"
}
```

返回值的意义：**"所有权到底移没移交"**。调用方据此决定是否自己 Dispose——
不移交还自己释放，泄漏；移交了再释放，下游拿到野句柄。
注释原话："否则 domain 图不是泄漏就是被提前释放"。

---

## 6. 运行流程详解（RunAlgorithm）

> 流程运行时每帧调用（394~507 行）。按执行顺序切 7 段。

### 第 1 段：输入检查 + 端口清账（396~408 行）

```csharp
var src = SrcImage.ActualValue;
if (src == null || !src.IsInitialized())
{
    Fail("输入图像为空或未初始化");
    return;
}
PreviewImage = src;           // 配置态试运行时，把上游图带给界面预览
_runtimeMaskedImg?.Dispose(); // 上一帧发布到主界面的合成图：自己管的自己清
_runtimeMaskedImg = null;
```

与模板匹配同样的原则：**上轮输出必须清账**。区别在于——
HImage 类端口（Crop/Domain/MaskImage）由基类 `AutoDisposeRoundOutputs` 轮首自动回收
（403 行注释），插件不用手清；**不走端口的**（发布主界面的合成图）才自己管自己清。

### 第 2 段：合并区域 + 两个区域级输出（410~418 行）

```csharp
var merged = BuildMergedRegion();   // (ΣROI ∪ 涂抹) − 擦除（第 6.2 节细讲）
MaskRegion.Value = merged;          // 所有权移交端口，基类轮首回收

var visual = BuildMaskAndResult(src, merged, MaskInvert, out var mask, out _);
MaskImage.Value = mask;             // 二值掩膜图（排除模式反转）
_runtimeMaskedImg = visual;         // 合成图不进端口——发主界面用，自管
```

一次合并，三个用途（区域/掩膜/合成图），**口径全部一致**——
配置界面掩膜预览用的也是同一个 `BuildMergedRegion`（2.5 节的铁律落地）。

### 第 3 段：发布主界面（420~423 行）

```csharp
if (DisplayViewIndex > 0 && visual != null && visual.IsInitialized())
    this.PublishPreview(visual, DisplayViewIndex + 1);
```

注释记录了一个 UX 修正：

> 此前选"不显示"仍会发布到窗口1；……此前逐 ROI 发到同一窗口互相覆盖，只能看到最后一个

——现在发**一张合成图**（全部 ROI + 涂擦一次看清），且 `DisplayViewIndex=0` 真正不发。

### 第 4 段：逐 ROI 裁剪主循环（425~501 行）

这是运行内核。每个 ROI 走一遍下面的流水线：

```csharp
var written = new HashSet<string>(StringComparer.Ordinal);  // 重名防御记账
int writtenCount = 0;

foreach (var roi in RoiList)
{
    var portName = $"Crop_{roi.Name}";
    if (!written.Add(portName)) continue;        // ① 重名只处理第一个（兜底）
    if (!Outputs.TryGetValue(portName, out var port)) continue;  // ② 端口不存在就跳过

    // ③ 先清零偏移端口：下面任何失败分支，下游都必须读到 0 而不是上轮残留
    SetValuePort($"OffsetRow_{roi.Name}", 0d);
    SetValuePort($"OffsetCol_{roi.Name}", 0d);

    HRegion? region = BuildRegion(roi);          // ④ 按参数重建区域
    if (region == null) { LogWarn("形状参数无效"); continue; }
```

第 ③ 步是防"幽灵值"的标准操作（与模板匹配轮首重置同理）：
**double 端口基类不回收，不清零的话失败帧会挂着上一轮的偏移**——
下游用旧偏移换算，错位且难查。

第 ④ 步 `BuildRegion`（863 行）：按 ShapeType 调 `GenRectangle2`/`GenCircle`/`GenEllipse`，
参数不足/形状未知/抛异常 → 返回 null（**单个 ROI 坏不阻断整体**，只记 Warn）。

继续主循环（擦除修正 + 原点计算）：

```csharp
    try
    {
        // ⑤ 擦除涂抹也作用于每个 ROI 的有效域——
        //    否则"擦掉的地方"仍原样出现在裁剪图里
        if (_smearErase != null && _smearErase.IsInitialized())
        {
            var cut = region.Difference(_smearErase);
            region.Dispose();
            region = cut;                        // 换新实例（旧的已释放）
        }

        // ⑥ 验空：擦得一点不剩就跳过
        HOperatorSet.AreaCenter(region, out HTuple area, out _, out _);
        if (area.D <= 0) { LogWarn("有效区域为空"); continue; }

        // ⑦ 裁剪原点 = 有效域外接矩形左上角
        //    不旋转图像 → 回变换只需平移：global = local + (row1, col1)
        HOperatorSet.SmallestRectangle1(region, out HTuple r1, out HTuple c1, out _, out _);
        double originRow = r1.Length > 0 ? r1.D : 0;
        double originCol = c1.Length > 0 ? c1.D : 0;
```

⑦ 的注释是理解 Domain 端口的关键（464~465 行）：

> 裁剪原点 = 有效域外接矩形的左上角（crop_domain 裁的就是它）。
> 不旋转图像，所以下游回变换只需平移

——如果裁剪支持旋转，回变换就要"平移+旋转"两步；
**刻意只平移**，把下游的换算压到最简单（一个加法）。

继续（三个输出的写端口）：

```csharp
        // ⑧ Domain 端口：带 domain 的原图（全局坐标）
        HOperatorSet.ReduceDomain(src, region, out HObject reduced);
        domain = new HImage(reduced);            // 独立句柄
        HImage? handedDomain = domain;
        if (SetImagePort($"Domain_{roi.Name}", handedDomain))
            domain = null;   // ★ 移交成功才置 null——finally 就不再释放它
        // 注释：new HImage 生成独立句柄，之后释放 reduced 不影响它

        // ⑨ 偏移端口（真正用于换算的值）
        SetValuePort($"OffsetRow_{roi.Name}", originRow);
        SetValuePort($"OffsetCol_{roi.Name}", originCol);

        // ⑩ Crop 端口：真裁剪
        HOperatorSet.CropDomain(reduced, out HObject croppedImg);  // 裁出外接矩形
        reduced.Dispose();
        crop = new HImage(croppedImg);
        croppedImg.Dispose();
        port.Set(crop);     // 免强转写动态端口
        writtenCount++;
        crop = null;        // ★ 同样：移交成功置 null
    }
    catch (Exception ex)
    {
        context?.Logger?.Warn($"{InstanceName} ROI[{portName}] 裁剪失败：{ex.Message}");
    }
    finally
    {
        region.Dispose();
        if (crop != null) crop.Dispose();    // 没移交成功的，这里兜底释放
        if (domain != null) domain.Dispose();
    }
}

RoiCount.Value = writtenCount;   // ★ 配置数 ≠ 成功数！下游按这个数循环才不越界
```

**所有权模式总结**（本段最值得学的代码思想）：

```
创建局部变量（crop/domain）持有新对象
    → 尝试移交端口
    → 移交成功：置 null（标记"已离家"）
    → finally：还不是 null 的（没移交出去的）负责释放
```

——"谁最后拿着，谁负责释放"的完整实现。配合 `SetImagePort` 的 bool 返回值，
**每条路径（成功/异常/端口缺失）都不会泄漏**。

### 6.2 BuildMergedRegion：合并区域的组装线（614~656 行）

```csharp
HRegion? merged = null;
foreach (var roi in RoiList)
{
    var region = BuildRegion(roi);
    if (region == null) continue;

    if (merged == null)
        merged = region;                 // 第一个：直接接管
    else
    {
        var union = merged.Union2(region);   // 之后：并集
        merged.Dispose();                    // 旧的两个都释放
        region.Dispose();
        merged = union;                      // 新合集顶上
    }
}

if (merged == null)
{
    merged = new HRegion();
    merged.GenEmptyRegion();   // ★ 防爆：显式初始化空区域（一个 ROI 都没画时）
}
```

> **为什么必须 GenEmptyRegion？**——`new HRegion()` 是"未初始化"状态，
> 后面 `RegionToBin`（掩膜）遇到它会抛异常。"空区域"和"没对象"是两回事——
> 这是 HALCON 新手最常踩的坑之一。

涂擦修正（与 2.5 的铁律公式一致）：

```csharp
if (_smearDraw != null && ...)   // ∪ 涂抹：手涂补充的区域也算有效区
{
    var u = merged.Union2(_smearDraw);
    merged.Dispose(); merged = u;
}
if (_smearErase != null && ...)  // − 擦除：手涂挖掉的部分无效
{
    var d = merged.Difference(_smearErase);
    merged.Dispose(); merged = d;
}
return merged;
```

注意全程"**边算边释放**"：每一步 Union/Difference 都产生新对象，
旧的立刻 Dispose——长列表×每帧执行，不这么做内存曲线就是锯齿飙升。

---

## 7. 涂擦系统详解（涂抹/擦除/游程编码持久化）

> 涂擦 = 用画笔在图上"手涂"补充区域（涂抹）或挖掉区域（擦除）。
> 比 ROI 句柄更自由——不规则形状全靠它。

### 7.1 两个区域属性（660~696 行）

```csharp
public HRegion? SmearDraw     // 累计涂抹区域（ImageEdit.SmearDraw 双向绑定）
{
    set
    {
        if (ReferenceEquals(_smearDraw, value)) return;
        _smearDraw?.Dispose();     // 控件笔画结束以"新实例"回写
        _smearDraw = value;        // → 这里释放被替换的旧实例
        OnPropertyChanged();
        SyncSmearData();           // ★ 同步持久化快照
        if (IsMaskPreview) RefreshMaskPreview();   // 掩膜模式立即刷新预览
    }
}
// SmearErase 同构
```

所有权约定（664 行注释）：**VM 是唯一所有者，控件只读参与并集**——
控件每画一笔生成新实例覆盖，旧实例回到 VM 这里释放。

### 7.2 持久化：游程编码（RLE）—— 一个精巧的存储设计

区域要随方案保存。选择：

- ❌ 序列化 HRegion 对象（HALCON 私有格式，不稳）
- ❌ 存 PNG 图（大，还要 base64）
- ✔ **存"游程编码文本"**——把区域记成一行行的"起止列"：

```
区域内容（第 100 行，列 10~20 被选中；第 101 行，列 10~25 被选中…）
↓ RLE
"100,10,20;101,10,25;102,10,25;..."
```

`RegionToData`（749 行）：

```csharp
HOperatorSet.GetRegionRuns(region, out HTuple rows, out HTuple colStart, out HTuple colEnd);
// HALCON 标准算子：直接给出区域的"每行起止列"——RLE 现成的！
var sb = new StringBuilder();
for (int i = 0; i < rows.Length; i++)
    sb.Append(rows[i].I).Append(',').Append(colStart[i].I).Append(',').Append(colEnd[i].I).Append(';');
var rle = sb.ToString();
return rle.Length <= 4096 ? rle : "gz:" + Convert.ToBase64String(GZipCompress(rle));
// ★ 超 4KB 自动 GZip+base64，"gz:" 前缀标识——大涂擦不让方案文件膨胀
```

`DataToRegion`（769 行）反向：`"gz:"` 先解压 → 逐段 `Split(';')`/`Split(',')` 解析 →
`GenRegionRuns` 按行起止重建区域。两代格式互认（无前缀 = 旧明文格式照常解析）。

> **为什么选 RLE 而不是 PNG**？——区域是"纯形状"，RLE 是它的**原生紧凑表示**
> （每行只需 3 个数），无损、可读（调试时能直接打开看）、无需图像编解码。
> 这与模板匹配掩膜选 PNG（那是"从图抠的"，图像更自然）形成对照：
> **数据形态决定存储形态**。

### 7.3 恢复顺序的深坑：RestoreSmear（727 行）

```csharp
public void RestoreSmear()
{
    _smearDraw = DataToRegion(_smearDrawData);      // ★ 直接写字段！
    _smearErase = DataToRegion(_smearEraseData);
    OnPropertyChanged(nameof(SmearDraw));           // 然后一次性通知
    OnPropertyChanged(nameof(SmearErase));
}
```

为什么不走属性 setter？注释里是血的教训（722~726 行）：

> 必须直接写字段后一次性通知，不能逐个走 SmearDraw/SmearErase setter——
> setter 内的 SyncSmearData 会把另一边的持久化快照用"尚未恢复的 null"覆盖成空，
> 表现为擦除区域重开配置即丢、下次确认时方案数据被静默抹掉

**事故复盘**：恢复 A 时走 setter → setter 里的 `SyncSmearData()` 把 B 的快照
也同步了一遍——而此时 B 的区域还是 null（没恢复到）→ **B 的持久化数据被空串覆盖**。
用户啥都没做，方案数据静默丢失。

> 教训（"初始化期间不要触发联动"）：setter 里的联动逻辑（SyncSmearData）
> 假设"另一个属性已就绪"。批量初始化时这个假设不成立——
> **初始化走字段，通知走批量**，是这类问题的标准解法。
> 与模板匹配的 `_seedingCanvas` 是同一思想的两种实现：
> 一个用哨兵跳过联动，一个绕开 setter。

---

## 8. 掩膜与抠图详解（BuildMaskAndResult）

> 源码 822~861 行。一个方法产出两样东西：二值掩膜 + 掩膜合成图。

```csharp
public HImage? BuildMaskAndResult(HImage src, HRegion mergedRegion, bool invert,
                                  out HImage? mask, out string message)
{
    // 1. 生成二值掩膜
    HOperatorSet.RegionToBin(mergedRegion, out HObject maskObj,
        invert ? 0 : 255,     // 区域内灰度：正常 255 / 反转 0
        invert ? 255 : 0,     // 区域外灰度：正常 0   / 反转 255
        width, height);
    mask = new HImage(maskObj);
    // region_to_bin：区域 → 二值图。一个算子完成"剪纸印到白纸上"
    // 反转逻辑浓缩在一个三目表达式里——正语义（mergedRegion）不变，反转只在这层

    // 2. 抠图 = 原图 × 掩膜 ÷ 255
    HOperatorSet.CountChannels(src, out HTuple channels);
    HObject maskForMul = maskObj;
    HObject? maskColor = null;
    try
    {
        if (channels.I == 3)
        {
            HOperatorSet.Compose3(maskObj, maskObj, maskObj, out maskColor);
            maskForMul = maskColor;
            // ★ 彩色图 3 通道：单通道掩膜没法直接乘 → 复制成 3 份拼成"灰色彩图"
        }
        HOperatorSet.MultImage(src, maskForMul, out maskedObj, 1.0 / 255.0, 0.0);
        // mult_image：result = src × mask × Mult + Add
        //              = src × mask × (1/255) + 0
    }
    finally
    {
        maskColor?.Dispose();   // 临时拼的 3 通道掩膜用完即释
        maskObj.Dispose();      // mask 端口已拿走独立副本，原件释放
    }

    message = $"ROI 数量: {RoiList.Count}" + (invert ? "（排除模式）" : "");
    return new HImage(maskedObj);   // 合成图（发主界面用）
}
```

**"1/255.0 截断白屏"坑**（837 行注释）：

> 采用矩阵乘法，缩放因子设为 1/255.0 杜绝截断白屏

如果你先 `÷255` 再乘（两步整数运算），掩膜 255 ÷ 255 = 1（int），
原图 182 × 1 = 182 看似对；但掩膜中间灰度 128 ÷ 255 = **0**（int 截断！）→
半透明区域全变黑。反过来，`mult_image` 是**一步浮点乘**：`182 × 128 × (1/255) = 91.4`
——平滑正确。**除法能并进乘法就别拆成两步**，尤其整数图像。

### 8.1 掩膜预览的防抖（145~168 行）

```csharp
private readonly DispatcherTimer _maskDebounce = new() { Interval = TimeSpan.FromMilliseconds(200) };

public void ScheduleMaskPreview()   // 拖拽句柄时高频触发
{
    if (!IsMaskPreview) return;
    _maskDebounce.Stop();           // 每次触发都重置 200ms 倒计时
    _maskDebounce.Start();          // → 停下来 200ms 后才真正重算一次
}
```

掩膜重算是**全图像素运算**（mult_image 对每个像素乘一遍），拖拽 ROI 时每秒触发几十次——
不防抖就每帧重算，界面卡成幻灯片。**防抖 = 只关心"最后一次"，前面全作废**。
200ms 是"肉眼不觉得延迟、重算不卡顿"的经验值。

---

## 9. 配置界面详解（CreateRoiView）

### 9.1 布局分区

```
┌──────────────────────────────────────────────────┐
│ [原图预览 ○] [掩膜预览 ○]     ← IsMaskPreview 单选  │
├──────────┬───────────────────────────┬───────────┤
│ ROI 列表  │   ImageEdit 画布           │ 参数微调    │
│ (ListBox)│   右键：新建矩形/圆/椭圆      │ (ParamEntries│
│ 选中联动  │   拖拽句柄改大小角度          │  数值框列表) │
│          │   Del 删选中 / 清空按钮       │            │
│          │   涂抹/擦除/清除 + 笔刷半径    │            │
├──────────┴───────────────────────────┴───────────┤
│ 显示窗口选择                                       │
└──────────────────────────────────────────────────┘
```

### 9.2 选中联动的"双向环"（79~125 行，本插件最精巧的交互代码）

列表选中 ROI ↔ 画布激活 ROI 要**双向同步**，但两个 setter 互相写对方就会**无限循环**：

```
点列表 → SelectedRoi setter → 写 CanvasActiveRoi
       → CanvasActiveRoi setter → 写 SelectedRoi → 又写 CanvasActiveRoi → …
```

解法：**两边 setter 都先判"值真的变了吗"**：

```csharp
// SelectedRoi setter（89 行）
if (!SetProperty(ref _selectedRoi, value)) return;   // 同值 → 直接返回，环断掉
...
if (!ReferenceEquals(CanvasActiveRoi, info))         // 再判一次才写对方
    CanvasActiveRoi = info;

// CanvasActiveRoi setter（119 行）对称地：
if (!SetProperty(ref _canvasActiveRoi, value)) return;
if (!ReferenceEquals(SelectedRoi, roi)) SelectedRoi = roi;
```

还有一个**重入陷阱**（86~88 行注释，非常精彩）：

> 先判断再退订：setter 会经 CanvasActiveRoi 联动回写同值重入，
> 若无条件先退订，同值路径短路后 ParamEdited 订阅被摘走不再补回
> ——参数微调就此断链

推演一遍：`SelectedRoi = A`（已是 A）→ 若"无条件先退订"执行了
`old.ParamEdited -= …` → 接着 `SetProperty` 返回 false 短路 →
**订阅已经摘了却永远没机会补回** → 之后改参数没人监听。
所以必须**先判同值短路，再做退订/订阅**。

> 这是事件订阅生命周期的经典坑：**退订和订阅必须成对出现在"确认要换"之后**，
> 不能出现在"可能短路"之前。

### 9.3 视图层的四个小工具（CreateRoiView.xaml.cs）

这个文件 89 行，除生命周期桥接外是 4 个可复用小件：

| 类 | 干什么 | 学习点 |
|---|---|---|
| `InverseBoolConverter` | 布尔反转（两个 RadioButton 互斥绑同一 bool）| IValueConverter 的最小示例 |
| `SmearModeConverter` | 枚举 ↔ 单选框勾选态 | `Binding.DoNothing`：ConvertBack 返回它 = "这次别写回源"——组互斥取消勾选时不污染源值（26~33 行注释）|
| `ListBoxAutoScroll` | 附加属性：选中变化自动滚动到可见 | **附加属性**写法：`RegisterAttached` + `ScrollIntoView`，XAML 里一行挂上 |
| `NumericInput` | 附加属性：TextBox 只放行数字和小数点 | `PreviewTextInput` + `e.Handled` 拦键——负号/字母直接进不了文本框 |

> 附加属性（Attached Property）是 WPF 的"外挂属性"：不改控件类，
> 就能给任意 TextBox/LB 挂上新行为。`NumericInput` 是最好的入门样例。

### 9.4 画布同步闭环（与模板匹配同款、但有升级）

```
画布右键新建 ROI
  → 控件 Add 进 CanvasRois
  → OnCanvasRoisChanged(Add 分支)：
      ① info.PropertyChanged += 监听拖拽
      ② NextFreeName 防重名（见下）
      ③ RoiList.Add(new RoiItem{ Name, ShapeType, Params=HTuples→double[] })
      ④ OnRoiListChanged → RebuildDynamicOutputs + ScheduleMaskPreview
拖拽句柄
  → info.HTuples 变（INPC）
  → OnCanvasRoiTuplesChanged → roi.Params = 新数组（不重建 entries，见 4.2）
  → ScheduleMaskPreview（防抖刷新）
数值框改参
  → RoiParamEntry.Value setter → roi.Params[i] = v → NotifyParamEdited
  → OnSelectedRoiParamEdited → info.HTuples = 反向写回 → 控件重绘句柄
```

三个方向（画布→数据、拖拽→数据、数值框→画布）**全部经由 RoiList 单一数据源**，
互相不直接对话——MVVM 的教科书闭环。

**NextFreeName：名字防重的完整实现**（333~367 行）：

```csharp
internal static string NextFreeName(string? desired, IEnumerable<string?> taken)
{
    // 规则：
    // "ROI_0/ROI_2" 在场，要 "ROI_1" → 不冲突直接用
    // 要 "ROI_0" → 找同词干最大序号 2 → 给 "ROI_3"
    // 无序号名 "缺陷区" 冲突 → "缺陷区_1"
}
```

为什么必须防重（196~198 行注释）：

> 控件命名序号每开一次配置界面就归零，重开配置再画必然撞上历史名字
> （动态端口静默去重 → 输出互相覆盖 + 内存泄漏 + 按名查找歧义）

——名字不只是显示，**它是端口名的一部分**（Crop_ROI_0）。重名 = 端口冲突 =
输出互相覆盖。`Initialize` 播种时还会对**历史数据**统一去重一遍（313~323 行，
旧版本控件 bug 遗留的重名在打开方案时就修好）。

### 9.5 两个 ICommand 的细节（258~285 行）

```csharp
public ICommand DeleteSelectedCommand => _deleteSelectedCommand ??=
    new RelayCommand(
        _ => DeleteSelectedRoi(),
        _ => Keyboard.FocusedElement is not TextBox);   // ★ CanExecute 守卫
```

Del 键绑定的命令：**焦点在文本框时不响应**（Del 是文本编辑键，删字 ≠ 删 ROI）。
`??=` 惰性初始化：第一次访问才创建命令（属性即命令的常用套路）。

清空命令：`MessageBox` 二次确认（不可恢复操作必须拦一道）+
`CanExecute = CanvasRois.Count > 0`（没东西可清时按钮置灰）。

---


