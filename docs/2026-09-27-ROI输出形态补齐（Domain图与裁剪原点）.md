# 2026-09-27 ROI 输出形态补齐：`Domain_{名}` 与裁剪原点 `OffsetRow/Col_{名}`

上一轮讨论的结论落地：给 `Plugin.CreateRoi` 补齐两类输出，让"区域一旦裁剪，后续插件坐标回不去"这个问题有两种解法——
**首选不用回变换（domain 图），次选有据可依地回变换（裁剪原点）**。

---

## 1. 问题

`CreateRoi` 原本只有一个动态输出 `Crop_{名}`：`reduce_domain` + **`crop_domain`**。
`crop_domain` 会把图像裁到 ROI 的外接矩形，**原点被搬走**，于是：

- 下游所有坐标都变成 ROI 局部坐标；
- 想变回全局坐标，需要知道"搬走了多少"——而插件**没有把这个数输出来**；
- 结果就是：要么硬编码偏移（ROI 一改就漂移），要么干脆回不去。

## 2. 实测依据（决定方案的那张表）

用 `find_shape_model` 在同一张图的四种形态上搜索同一个目标（真实位置 300,300）：

| 搜索图 | 结果 |
| --- | --- |
| ① 原图（无 domain） | 找到 **(300,300)** |
| ② 带 domain(200~400)，目标在内 | 找到 **(300,300)** ← 坐标仍是**全局** |
| ③ 带 domain(0~150)，目标在外 | **未找到** ← find 只在 domain 内搜索 |
| ④ 裁剪图（201×200，原点被搬走） | 找到 **(100,100)** ← 局部坐标 |

结论：**domain 既限制搜索范围（提速 + 避免相似背景误匹配），又不改变坐标系**。
所以"带 domain 的原图"能让下游插件**零改动、零回变换**地吃到 ROI。

顺带一个被实测钉死的细节：`crop_domain` 裁的是外接矩形、**不旋转图像**，
因此裁剪图的回变换**永远只是平移**——两个标量就够，不需要完整的 hom_mat。

## 3. 方案

| 输出 | 类型 | 下游要改吗 | 坐标系 | 定位 |
| --- | --- | --- | --- | --- |
| `Crop_{名}`（既有） | HImage | 不用 | **局部** | 省内存/省传输；必须回变换 |
| **`Domain_{名}`（新增）** | HImage | **零改动** | **天然全局** | **首选**：所有吃 HImage 的插件白捡 ROI |
| **`OffsetRow_{名}` / `OffsetCol_{名}`（新增）** | double | 自己加 | 手动回全局 | 兜底：必须用裁剪图时 |

`Region_{名}`(HRegion) 本次**未做**——它最灵活（可做差集/形态学/给脚本），
但要下游逐个加端口，属于长期建设，已记进待办（见 §6）。

### 为什么 Offset 是两个 double 而不是一个 HTuple / hom_mat

- 两个标量可直接当标量连线（脚本、变量、计算节点都能用），不必拆数组；
- 保住端口的编译期类型检查——`FlowCompiler.TryValidateLink` 靠 `DataType` 做校验，
  若为了"一个端口通吃 HImage/HRegion"而声明成 `object`，这道闸门就失效了
  （正是 `2026-09-17-端口系统编译期类型检查` 那轮改造专门要消灭的运行时归因错乱）。

## 4. 实现

改动集中在 `Plugins/Plugin.CreateRoi/CreateRoiPlugin.cs`：

### 4.1 `RebuildDynamicOutputs()`：每个 ROI 长出 4 个端口

```
Crop_{名}          HImage   裁剪图（既有，行为不变）
Domain_{名}        HImage   带 domain 的原图（只在 ROI 内有像素，坐标仍为全局）
OffsetRow_{名}     double   裁剪图左上角在原图中的行
OffsetCol_{名}     double   裁剪图左上角在原图中的列
```

四类都写进 `StepData.OutputPortDefinitions` 快照（编译器据此接线）。

### 4.2 `RunAlgorithm()`：与裁剪共用一次 region 计算

```csharp
// 裁剪原点 = 有效域外接矩形的左上角（crop_domain 裁的就是它）
HOperatorSet.SmallestRectangle1(region, out HTuple r1, out HTuple c1, out _, out _);

HOperatorSet.ReduceDomain(src, region, out HObject reduced);
domain = new HImage(reduced);              // 独立句柄，之后释放 reduced 不影响它
if (SetImagePort($"Domain_{roi.Name}", domain)) domain = null;   // 所有权移交端口
SetValuePort($"OffsetRow_{roi.Name}", originRow);
SetValuePort($"OffsetCol_{roi.Name}", originCol);
HOperatorSet.CropDomain(reduced, out HObject croppedImg);
reduced.Dispose();
```

三个要点：

1. **不重复算 region** —— domain 与 crop 共用同一次 `reduce_domain`，只是 crop 多走一步 `crop_domain`；
2. **所有权明确** —— `SetImagePort` 返回"端口有没有接管"，调用方据此决定是否自己释放，
   避免 domain 图不是泄漏就是被提前释放；
3. **轮首清零 offset** —— `double` 端口基类不回收（非 `IDisposable`），
   本轮任一失败分支都要让下游读到 0 而不是上一轮的脏值。

### 4.3 新增两个私有助手

`SetImagePort(string, HImage?)`、`SetValuePort(string, double)`：
动态端口取出后按 `OutputPort<T>` 强类型写入，不用 `object` 强转。

## 5. 验证

### 5.1 编译

```
dotnet build Plugins\Plugin.CreateRoi\Plugin.CreateRoi.csproj -v m -clp:ErrorsOnly
→ 已成功生成。0 个错误

dotnet build VisionMaster.sln -v m -clp:ErrorsOnly
→ 已成功生成。80 个警告 0 个错误
```

### 5.2 新增断言（`FlowCanvasChecks/CreateRoiChecks.cs`，跨插件端到端 6 条）

真值：特征图案在 **(150,200)**，ROI 矩形中心 (150,200) 半长 80 → 外接矩形左上角 **(70,120)**。

```
[√通过] 【输出形态】Domain_{名} 存在且幅面与原图一致（说明没有裁剪、坐标系没动）
      →  520×400（原图 520×400）
[√通过] 【输出形态】Domain_{名} 只有 ROI 内有像素（domain 面积 = ROI 面积）
      →  domain=25921.0，期望 25921
[√通过] 【输出形态】OffsetRow/Col = 裁剪图左上角在原图中的位置（70,120）
      →  Offset=(70,120)，期望 (70,120)
[√通过] 【核心】domain 图上做模板匹配 → 得到的就是全局坐标（下游零回变换）
      →  匹配得 (150,200)，真值 (150,200)
[√通过] 【输出形态】裁剪图上匹配得到的是局部坐标（原点已被搬走）
      →  局部 (80,80)，期望 (80,80)
[√通过] 【核心】局部坐标 + Offset 能还原成与 domain 图一致的全局坐标
      →  (80+70, 80+120) = (150,200)，真值 (150,200)
```

两条【核心】是**跨插件端到端**的：同一个目标，分别喂 domain 图与裁剪图给模板匹配，
两条路必须都回到同一个全局真值 (150,200)。这比"端口存在性"断言更能证明方案成立。

### 5.3 顺带修的既有断言

新增端口后，两条硬编码端口名集合的断言会红：

- `【端口】快照同步到 StepData（编译器据此接线）`
- `【重名防御】去重后端口与快照同步重建`

改为按"每个 ROI 四种输出"生成期望（`ExpectedPortNames(...)`），
将来再加形态只改一处，不用再改断言。

### 5.4 冒烟回归

```
通过: 691  失败: 15
```

15 条失败**全部在并行任务范围**（相机插件 1 条 + 线序检测/脚本投射 14 条），
CreateRoi 段 0 失败。

## 6. 已知边界 / 后续

1. **`Region_{名}`(HRegion) 未做** —— 需要显式区域对象的场景（脚本、差集、形态学）还拿不到。
   建议等有明确需求再补；补的时候下游要逐个加端口。
2. **不是所有算子都认 domain** —— HALCON 主流算子认（`find_shape_model` / `threshold` 已实测），
   但第三方/自写算子不一定。推广前建议把"哪些算子认 domain"实测一遍，别当默认真理。
3. **domain 图不省内存** —— 图像数据还在，只是 domain 外不参与运算。
   需要省内存/省传输的场景仍要用 `Crop_` + `Offset_`。
4. **显示观感** —— 带 domain 的图显示时 domain 外是黑的。这是 ROI 的直观表达，
   但第一次见的人可能以为是图坏了，配置界面最好有说明。
5. **迁移提示** —— 已用裁剪图学过模板的，换到 domain 图后**模型原点会变，必须重新学习**，
   否则表现为"定位整体偏一个 ROI 左上角"。
6. **Offset 是可选端口** —— 若下游连了裁剪图却没连 Offset，坐标就是错的且不报错。
   这是 domain 路线存在的理由之一；真要兜住，需要在配置/编译期做"用了 Crop_ 但没连 Offset"的提示。
