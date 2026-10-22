# SCADA 运行态 Web · A′ 第四批（轴只读数据 + DataGrid 行数据，WR 例外清零）

- 日期：2026-10-22
- 分支：`feature/scada-web-v4-a-prime`
- 提交：`133a3a0`（轴）、`1c1728d`（DataGrid）
- 结果：**29 类图元全部有 Web 渲染器；WR 例外清单 0 项**

---

## 1. 轴只读数据（AxisPanel / AxisConsole / AxisPointTable）

### 宿主

- `ScadaWebHost` 新增可选 `IScadaAxisSource`（`VM.Scada` 已有契约，`MotionModule` 注册）。
- `CaptureLiveState`（UI 线程）新增 `BuildAxisJson`：
  - 遍历当前页 `Hmi.AxisPanel` / `Hmi.AxisConsole` / `Hmi.AxisPointTable`；
  - 从 `Properties["AxisOptions"]` 用 `ScadaAxisOptionsCodec.Parse` 取候选轴；
  - 逐轴 `TryGetInfo` / `TryGetSample` / `TryGetPoints`（点位表在 UI 线程读，避免与示教写并发）；
  - 消息：`{type:"axis", seq, runGeneration, pageId, pageVersion, elements, axes, points}`。
- 发送：每 2 拍一次（**5Hz**），只发给已经拿到当前页结构的会话。
- `ScadaWebModule` 解析 `IScadaAxisSource` 并传入宿主；缺失时为 null，轴图元显示“—”。

### 客户端

- `app.js`：`state.axis`、`createRecord` 支持 `updateAxis`、`handleMessage` 的 `axis` 分支
  （按 pageId 丢弃旧页消息；结构重建后补发最近一份）。
- `elements-process.js`：`createAxisRenderer` 注册 `Hmi.AxisPanel` / `Hmi.AxisConsole` /
  `Hmi.AxisPointTable`：
  - 只读字段：位置 F3、速度 F2、状态、使能、到位、运动、报警、正/负限位、原点、急停；
  - 点位表：`ShowPoints` + `PointsRows`（1..16），P{Index}·Name / 位置 F3 / 速度 F2；
  - **不渲染任何命令按钮**（A 范围）；轴选择只存在客户端，默认第一根候选轴；
  - 无数据时显示“—”，不显示 0（避免把“不知道”伪装成“在原点”）。
- `ScadaChecks` WR 例外 3 → 2（移除 AxisPanel/AxisConsole；AxisPointTable 是别名，不在工具箱集合）。

### 断言

- `ScadaWebChecks` **B52**：独立宿主 + 假 `IScadaAxisSource`；
  断言 `axes=1`、`name=X`、`position=12.345`、`valid=true`、`points=1`、`noCommands=true`。

## 2. DataGrid 行数据

### 领域契约

- 新增 `Scada/Models/IScadaRuntimeDataProvider.cs`：
  - `TryGetGrid(Guid elementId, out ScadaGridSnapshot)`；
  - `ScadaGridSnapshot { Columns, Rows, RecordedCount }`（列/行复用 `ScadaDataGrid` 的模型）。

### 宿主实现

- `ScadaRuntimeHost` 实现 `IScadaRuntimeDataProvider`：
  - 只在 UI 线程调用；
  - 只找**运行窗口**的 `CanvasHost` / `PopupCanvasHost`（`ScadaRuntimeWindow` 已公开）；
  - `EnumerateControls()` 里按 `ElementId` 找 `DataGridElement`，返回 `Columns` / `Buffer` /
    `RecordedCount` 的当下快照；
  - **不遍历编辑器画布**，避免读到同名 ElementId 的设计期控件。
- `App.xaml.cs` 注册 `IScadaRuntimeDataProvider` 到同一个 `ScadaRuntimeHost` 单例。

### Web 宿主

- `ScadaWebHost` 新增可选 `IScadaRuntimeDataProvider`。
- `BuildGridJson`：当前页 `Hmi.DataGrid` 逐元素取快照，消息：
  `{type:"grid", seq, runGeneration, pageId, pageVersion, elements:[{elementId, columns, rows, recorded}]}`
  - `columns`: `header/width/align/format/monospace`；
  - `rows`: `cells/errorCell/timestamp`。
- 发送：每 5 拍一次（**2Hz**），只发给已拿到当前页结构的会话。

### 客户端

- `app.js`：`state.grid`、`updateGrid`、`grid` 分支（pageId 闸 + 结构重建后补发）。
- `elements-process.js`：`Hmi.DataGrid` 渲染器：
  - 表头 + 可滚动行区 + 底部“已记录 N 条 / 只读”；
  - 列宽 0 = 余量；`align`/`monospace`/`errorCell` 红色；
  - 无 `grid` 消息时用 `GridColumns` 画表头并显示“等待第一条记录”；
  - 最多渲染 100 行，超出显示“还有 N 行”；
  - 不写确认变量、不做 Web 侧二次采样。
- `ScadaChecks` WR 例外 2 → **0**；`[WR1]` 工具箱 28 类缺 0 类。

### 断言

- `ScadaWebChecks` **B53**：独立宿主 + 假 `IScadaRuntimeDataProvider`；
  断言 `columns=2`、`rows=2`、`errorCell=1`、`recorded=2`。

## 3. 验证

| 项 | 结果 |
| --- | --- |
| `dotnet build VisionMaster.ScadaWeb` | 0 错误 |
| `dotnet build VisionMaster.csproj` | 0 错误 |
| `Web\ScadaWebChecks` | **124 PASS / 0 FAIL**（B52/B53 新增） |
| `ScadaChecks` | 2437 PASS / 1 FAIL；WR 全绿、例外 0；唯一失败是外部进程正在改的 `FlowListView.xaml`（与本批无关） |
| `node --check app.js / elements-process.js` | 0 |

## 4. 已知限制

1. **轴选择不跟随桌面**：`SelectedAxisId` 是控件私有 DP、不在快照里；Web 默认第一根候选轴，
   多轴面板可能显示与桌面不同的轴。A 范围下无命令风险；开 Web 运动前必须补只读桥。
2. **点位表 5Hz 全量**：当前每 5Hz 发全部候选轴的点位；轴多/点位多时需按 Version 增量。
3. **DataGrid 只发控件最近 N 行**：`Buffer` 是显示缓冲，不是历史库；重连只能拿到当前缓冲。
4. **2Hz/5Hz 数据消息不走 45KB 帧预算**：它们是独立小消息；表格行多时需加单条上限/降采样。
5. **无浏览器视觉验收**：B52/B53 只验协议字段；布局/滚动/`<img>` 解码未测。
6. **DataGrid 依赖运行窗口控件**：未运行/窗口未创建时没有行数据（显示“等待第一条记录”），
   属预期；切页瞬间可能短暂显示旧页数据（pageId 闸会丢弃旧消息）。

## 5. 仍未完成

- `cmd.result`（Dedup 三态 + 客户端持久化 requestId）。
- 快照分片/resync/控制通道。
- 24 槽的 8 个握手预留槽。
- 默认值一致性闸。
- 浏览器/真实网络/重连风暴/停服中压测/长稳。
