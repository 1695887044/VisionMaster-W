# UI 样式参考（离屏渲染图）

这些图不是手绘稿，而是**真控件 + 真主题**的离屏渲染（`RenderTargetBitmap`）。
生成器是仓库根目录的探针 `_LogViewProbe`（与 `_StyleProbe` 同一套做法：合并 UI 主题 + Fluent → 建真视图 → 泵 Dispatcher → 出图）。

**为什么留这些图**：控件的观感缺陷（隐形输入框、被压扁的卡片、空壳胶囊）在 XAML 源码里看不出来，只有渲染出来才知道。
每张图都对应代码里一段"为什么这么写"的注释 —— 改 UI 前先看图，再读那段注释。

## 索引

| 图 | 控件 / 场景 | 对应改动 | 关键尺寸口径 |
| --- | --- | --- | --- |
| `logview.png` | LogConsole + 日志面板工具条（宽 1000） | 搜索框给回真外形（白底 + 1px 描边 + 占位"正文或来源"）；等级筛选从下拉框改成筹码条（等级色圆点 + 实时条数）；「跟随最新」开关 | 工具条两行（74px）：搜索行 32 + 筹码行 23 + padding 12；搜索框定宽 200；筹码高 23 |
| `logview_narrow.png` | 同上，窄 560 | 筹码独占整行；窗口变窄不再裁内容 | 560 仍两行；<520 筹码折两行 |
| `logview_typed.png` | 同上，输入"图像采集"后 | 占位提示让位、列表真被过滤到 1 条 | 搜索防抖 200ms |
| `levelfilter_variants.png` | 等级筛选三形态对照（A 纯筹码 / B 等级色+计数 / C 多选） | 设计决策留痕（选了 B） | 筹码高 23、圆角 11 |
| `pg_layouts.png` | FlatPropertyGrid vs CardPropertyGrid 三档宽度 | 卡片式 chrome 减重 292→196px（页签条 180→128、内容留白 24→14、卡片 32,24→20,16）；**网格里的数值参数已换成 NumericBox**（外观刻意与原来的裸文本框逐字一致） | 表格 360 / 卡片 540 / 卡片 360 |
| `numericbox.png` | NumericBox（新控件）六态：普通 / 单位 / 满量程 / 只读 / 固定小数位 / 非法输入 | 新增控件；属性网格的数值参数由 `NumericGenerator` 自动换用它 | 高 32、圆角 4；`[RangeValidation]` → 自动上下限 |
| `connection_status.png` | 连接状态胶囊（`DialogStatusPillTemplate`）× 五种 `ConnectionState` | 圆点/文案/悬停详情三件事从模板收进 `StatusIndicator`（该控件此前零消费）；顺带修掉模板里 `DialogIconFont` 的跨兄弟字典引用 | 灯点 8px；Busy 档运行时脉动 |
| `blobdetect_numeric.png` | BlobDetect 插件表单（真视图 + 真插件 VM） | 19 个手写数值 TextBox → `ui:NumericBox`（样式仍用平台的 `PluginNumericBox`，外观不变、行为升级）；另 5 个插件视图未动 | 区间来自原 XAML 校验规则（灰度 0~65535、圆度 0~1…） |
| `preprocess_flat.png` | PreProcessingView 三栏（表格态，真视图 + 真插件 VM） | 参数面板默认态 | 内容区 1120 = 算子库 240 + 预览 * + 参数 360 |
| `preprocess_card.png` | 同上（卡片态） | 参数列 360→540，预览列仍有 ~385 | 卡片固定开销 196 |
| `easydialog.png` | EasyDialog 弹窗（含遮罩层） | 同步路径死锁修复 + 资源全部走 TryResource + 标题/正文/输入框描边改走 Fluent 令牌 | 卡片 MinWidth 350 / MaxWidth 700；遮罩 `#64000000` |
| `notifications.png` | Notifier 通知卡片（OverlayHost 右上角栈） | 判空 Application.Current + 同屏上限 5 条（丢最旧）+ 等级色画刷冻结 | 卡片 MinWidth 280 / MaxWidth 420；右上 Margin 0,20,20,0 |

> 注（2026-10-10）：索引表里提到的 `DialogIconFont` 已随「分册作用域」整改上移到第一层令牌字典 `UI\Controls\Themes\Colors.xaml`（原定义在 `Themes\Dialog\Dialog.Text.xaml`，各分册不再跨册引用它）；详见 `docs/code-changes/2026-10-10-变量管理弹窗打不开（Dialog分册跨册BasedOn断链）.md`。

## 重新出图

```
dotnet build _LogViewProbe\_LogViewProbe.csproj -c Debug
_LogViewProbe\bin\Debug\net9.0-windows\LogViewProbe.exe
```

图会直接写回本目录（探针里的 `OutputDir` 常量）。探针同时跑两条控制台断言，顺带当回归用：

- `[PASS] 卡片式开关：…切后卡片可见=True，卡片网格实际宽=530`（插件配置壳的布局切换链路）
- `[PASS] Notifier 上限：塞 8 条 → 可见 5 条（期望 5），最后一条=storm 7`（通知容量上限）

> 探针本身是临时工程（不在任何 `.sln` 里）。功能改动与"为什么"的完整记录在 `docs/code-changes/`：
> `2026-10-07-日志控制台（LogConsole）控件修复…md`、`2026-10-09-CustomControl目录审查整改…md`、
> `2026-10-09-插件配置壳参数面板布局切换（CardPropertyGrid接线）.md`。
