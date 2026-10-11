# 2026-10-08 Blob 弹窗卡死与复制像素失败（真机复盘：HwndHost 焦点仲裁 + 换图竞态）

## 一、症状（用户真机报告）

1. Blob 视图里**拖直方图阈值后整个界面卡死**——"只有直方图可以拖动"（直方图是纯 WPF FrameworkElement，卡死时它仍能拖；画布/参数面板/清单全冻）。
2. 右键「复制像素信息」报**「复制像素信息失败」**。

宿主日志（`Logs\2026-10-08.log`）钉住时间线：21:09:35 一次正常运行后 **45 秒零日志零异常**直到 21:09:46 用户强杀进程——无崩溃、无循环运行洪峰，纯 UI 冻结。

## 二、排查过程（四轮探针，全部保留结论）

复刻各嫌疑链路的独立 STA 探针（已用后清理）：

| 探针 | 复刻内容 | 结果 |
| --- | --- | --- |
| ① | ImageReadOnly 同尺寸连续换图 60 轮（预览回填同款链路，含 RenderAll/SetLut） | UI 泵 207/250 正常——**库层换图无罪** |
| ② | 宿主图集稳态（300 帧灌满 + 每轮插删 + GroupBy 全表扫描）+ 换图 | 205/250 正常——**图集联动无罪**（另核实：配置态预览根本不走 PublishPreview） |
| ③ | TextBox(PropertyChanged+NumberValidationRule) ↔ VM double ↔ 高频写 | vmCount=writes 无放大——**TextBox 绑定环无自激** |
| ③b | 完整三角：真 HistogramPlot(ThresholdLow TwoWay+AffectsRender) ↔ VM ↔ TextBox，8ms 双写 60 秒 | 放大系数=2（双写路径的预期值）且泵正常——**三角绑定环无自激** |
| ①可见窗口版 | 同①但窗口可见真实渲染 | 206/250 正常——**可见渲染路径也无罪** |

## 三、根因（证据收敛后的机制）

两个症状同源于**预览换图窗口期与用户操作的竞态**：

**卡死**：第一批给 HalconBase 加的 `HMouseDownForFocus`（左键按下即 `Focus()`）撞上 HSmartWindowControlWPF 的 **HwndHost** 本质。预览类弹窗 200ms 一轮换图，HALCON 窗口句柄存在生命周期窗口期；此窗口期内对 HwndHost 调 `Focus()` 走 Win32 `SetFocus`/HwndSource 焦点仲裁，可能停滞并挂起 UI 线程消息循环——**纯 WPF 元素（直方图）不经过 HwndHost，所以唯独它还能动**。这是排查中唯一与"二元现象"（直方图活、其余全冻）吻合的机制。用户看图时点过画布（滚轮缩放前必有一次左键点击）→ 焦点已被塞给 HwndHost → 拖直方图触发预览换图 → 窗口期竞态。

**复制失败**：`CopyPixelInfoToClipboard` 原从 `DisplayImageInfo.Image` 取像素——它是换图回调里的**快照引用**，预览 200ms 一轮释放旧图时，用户点菜单的瞬间它可能已指向被释放的 HImage → `GetGrayval` 抛 HalconException → catch 写失败提示。同一竞态面的第二个表现。

## 四、修复（HalconBase.cs 三处）

1. **`HMouseDownForFocus` 移除 `Focus()`**（方法留空 + 注释说明）：鼠标按下不再抢焦点。键盘可达性退回 Tab 键（`Focusable=true` 保留）；F/1 快捷键的便利不抵 HwndHost 焦点仲裁的停滞风险。这是行为收紧——快捷键仍可用（Tab 到画布后按 F/1），但不再隐式抢焦点。
2. **`CopyPixelInfoToClipboard` 竞态加固**：坐标仍用 DisplayImageInfo（纯托管值），**像素读数一律从当前 `HImage`（绑定源，与画布同生命周期）现取**；三通道场景临时 AccessChannel 逐个释放（不再复用类级缓存——缓存跟随的源图引用同样有窗口期）；W/H 现查。
3. **`CopyViewToClipboard` 前置守卫**：`hSmart.IsVisible` 检查（窗口未就绪给"画布未就绪"提示而非抛异常）。

## 五、验证

- 构建 0 错误；全量断言 `通过 1620 / 失败 4`（4 条均为既有基线项：相机模块注册表 / ONNX 类别话术 / 两个属性面板候选），Core.Halcon 段 **48/48 全绿**，不回退（上一轮 1595/4 → 净增 25 来自并行会话断言）。
- 探针目录（`_FreezeProbe`）已清理。

## 六、未决 / 用户侧动作

1. **必须重启 VisionMaster 并重建**（当前 PID 7916 正在运行，宿主 bin/Modules 的 Core.Halcon.dll 未刷新——本轮修复要 rebuild 后才生效）。重建后请重试原操作序列：打开 Blob 配置 → 拖直方图阈值 → 观察；若仍冻结，请告诉我"卡死时直方图是否还能拖"（本轮机制推断的最后一环需要真机复核）。
2. 根因中"焦点仲裁停滞"是从症状形态+排除法收敛的最优解释，未能在探针中直接复现（探针没有模态弹窗+真实鼠标点击序列）。若真机复测仍卡死，下一步将做带点击注入的完整弹窗探针。
3. `System.Drawing.Common` 依赖真相、双批改动清单等见前两份记录。
