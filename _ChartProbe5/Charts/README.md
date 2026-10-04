# VM.Charts — ScottPlot 5 的 MVVM 封装

一个把 **ScottPlot 5.1.x** 包成 MVVM 友好的绘图控件库。
约定:**ViewModel 里零 ScottPlot 类型**——VM 只提供数据/状态,控件负责把数据变化翻译成绘图调用。

## 文件

| 文件 | 内容 |
|---|---|
| `ChartView.cs` | 控件主体(适配器 + 渲染调度 + 交互 + 主题) |
| `ISeriesVm.cs` | 实时信号契约 + `SignalSeriesVm`(定采样率、滑动缓冲、线程安全) |
| `IScatterSeriesVm.cs` | XY 数组契约 + `ScatterSeriesVm`(外部按索引填数、`MaxIndex` 推进、`SetData` 整组替换) |
| `IChartAnnotation.cs` | 阈值线 `ThresholdLineVm`(可拖拽)/ 事件标记 `EventMarkerVm` |
| `ExtraSeries.cs` | 柱状 `BarSeriesVm` / 环形 `PieSeriesVm` / 直方图 `HistogramSeriesVm` / 函数 `FunctionSeriesVm` / 区域填充 `FillYSeriesVm` / 热力图 `HeatmapSeriesVm` |
| `GaugeVm.cs` | 仪表盘单项 |
| `ChartTheme.cs` | 颜色主题(优先 Fluent 令牌,无则内置同款色) |
| `ChartRenderScheduler.cs` | 全局渲染调度器(所有图共用一个 30ms 计时器,弱引用,零唤醒源空转) |

## 快速上手

```csharp
// VM(零 ScottPlot 类型)
var ch1 = new SignalSeriesVm("ch1", "CH1 传感器", Colors.Blue, 200, 8); // 200Hz, 8 秒容量
ch1.StartTime = DateTime.Now;   // 绝对时间轴

// View(代码或 XAML 绑定)
var chart = new ChartView { AutoScroll = true, AutoScrollSeconds = 4 };
chart.SetBinding(ChartView.SeriesSourceProperty, new Binding("LiveSeries"));

// 采集线程(任意线程)
while (running) { ch1.Append(value); Thread.Sleep(5); }
```

四个 Source 依赖属性都接受 **集合或单个对象**(`object` 型注册,内部自动归一化):
`SeriesSource`、`AnnotationsSource`、`ExtrasSource`、`GaugesSource`。

## 支持图形 / 行为

- **实时信号**(Signal:缓冲区原址引用,零拷贝)、**XY 散点/折线**
- **阈值线**(可拖拽,拖动回写 VM)、**事件标记竖线**
- **柱状图 / 环形图 / 直方图 / 函数曲线 / 区域填充 / 热力图**
- **悬浮数据提示**(`ShowDataTips`,默认开):光标所到 X 时刻,**所有可见曲线的当前值**以读数表列出(信号 O(1) 读取,散点走 `GetNearestX`),带绝对时间;不再只是坐标。
- **双 Y 轴**(`YAxisIndex` + `Y2Label` + `CrosshairOnY2` 第二根光标)
- **绝对时间轴**(`StartTime` → X 刻度显示 HH:mm:ss)
- **最近点吸附**(`SnapToNearest`,光标落到最近数据点并带出系列名)
- **跨图光标同步**(`SyncGroup="同名"`)
- **跟随模式**(`AutoScroll` + 右下角状态 chip,点击暂停/恢复,双击复位)
- **主题跟随**(`ChartTheme`,`RefreshTheme()` 重刷)、**高 DPI 适配**、**0.45 防呆兜底**(连续 5 次渲染异常降级为提示)

## 关键设计点(改代码前先读)

1. **渲染是拉模型**:采集线程只写数据,控件 30ms 轮询 `Version` 有变化才渲染——渲染频率与数据频率解耦,无跨线程事件。
2. **所有 VM 的 INPC 都可能来自后台线程**:控件内部已统一 `Dispatcher` 封送,宿主不要另加。
3. **仪表盘半径以"1 个数据单位的像素数"为基准**:纯仪表图会自动 AutoScale 到其数据范围,不要手动 `SetLimits` 到 ±10。
4. **ScottPlot 5 的 Signal/Scatter 不支持渲染未写入区间**:分别用 `MaxRenderIndex` / `MaxIndex` 截断,`NaN` 会直接抛异常。
5. **右键菜单**(默认中文,按图类型自适应):
   - **复制图像 / 另存为 PNG / 复位视图** —— 所有图通用;
   - **显示测量卡尺** —— 仅折线/曲线图(`Measurable = true`,默认);饼图/柱状图/仪表/热力图等统计图置 `Measurable = false`,不显示该项;卡尺为两条可拖拽静态线(2.5px),两线间常驻 ΔX / 频率 / 各曲线 ΔY,位置经 `MeasureCursorAX/BX` 双向属性读写;
   - **✔ 光标同步 / 轴范围同步** —— 同 `SyncGroup` 组内联动,右键可开关(光标默认开、轴范围默认关);
   - **导入数据... / 导出数据 CSV...** —— 导入解析后触发 `CsvImportRequested` 事件(宿主把列数据送往自己的 VM 系列),导出按公共时间轴写出全部可见信号系列;
   - `MenuItemsSource`(集合或单项 `ChartMenuItemVm`):业务项追加在末尾,点执行其 `ICommand` —— VM 定义菜单,零 View 代码;
   - `ConfigureMenu(m => ...)`:完全自定义(Clear/Add),调用后自动重建被跳过;
   - `PlotControl` 是底层控件的只读转义舱口(约定只许 View 层使用)。
6. **升级换库只重写 `ChartView.cs`**:全部 ScottPlot 类型都关在这个文件里。

## 版本要求

- ScottPlot 5.1.59(含 `ScottPlot.WPF`,net462+ 可用)
- .NET Framework 4.7.2+ / .NET 6+
- WPF(控件基于 `UserControl` 组合,无主题字典依赖,任何宿主可用)
