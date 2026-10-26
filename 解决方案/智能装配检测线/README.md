# 智能装配检测线（演示 / 性能测试方案）

## 方案概况

- 14 条流程：Home / Calib / Main / ST1–ST6 / AI_Extension / Recipe / Diag / AlarmReset / End
- 6 工位：上料读码、视觉定位、精密测量、表面检测、装配压装、识别追溯
- 15 个组态画面（含 3 个弹窗）：总览、工艺流程、6 个工位页、报表、报警、诊断、配方
- 95 个变量快照，10 条报警，3 条变量事件
- 1 份方案级标定资产 `LineCalib`：`Calib` 流程计算并写入，ST3 用「标定引用」按资产加载，生产节拍不重算
- 覆盖全部 50 个内置图元 TypeKey；覆盖全部可运行插件
- 插件停用项：YOLO / DlAnomaly / DlClassify / DlSegment / Matching / HandEye / ImageScript / Database 三类（缺模型或连接配置，步骤默认停用）

## 运行方式（推荐）

1. 启动虚拟设备控制台：
   `..\..\tools\VirtualLineConsole\bin\Debug\net48\VirtualLineConsole.exe`
2. 用本目录作为工作目录启动 `VisionMaster.exe`（方案内 `Image\` 与 `History\` 都是相对路径）：
   `..\..\VisionMaster\bin\Debug\net9.0-windows\VisionMaster.exe`
   或双击本目录的 `启动产线.cmd`。
3. 登录 `admin / admin123`，选中 `Main`，点「运行主流程」或「循环主流程」。
   （交付时已把宿主的 `StartupSolutionPath` 指向本方案，启动即加载。）

## 虚拟设备

| 服务 | 端口 | 用途 |
| --- | --- | --- |
| Modbus TCP PLC | 503 | HR0 触发、HR1 确认、线圈 0/1/2、HR4/6/8 计数与节拍 |
| Modbus TCP Meter | 504 | IR0 温度、IR10 压力、IR20 扭矩（CDAB） |
| Siemens S7 | 1102 | DB1.0 结果字、M0.0 心跳、M1.0 就绪 |
| MES HTTP | 19001 | `/api/upload` 收件、控制台打印节拍 |

## 数据落点

- `History\Main.产线主记录\yyyy-MM\yyyy-MM-dd.csv`：逐件追溯账本
- `History\Main.生产报表导出\yyyy-MM\报表\`：Excel 报表
- `Alarms\`：报警历史
- `Logs\`：运行日志

## 已知边界

- 方案内 CSV/Excel 的目录模板是相对路径，按宿主进程当前目录解析；请用本目录作为工作目录启动宿主（`启动产线.cmd` 已处理）。
- 设计期渲染探针中 `Hmi.SubPage` 与 `Hmi.StateView` 可能显示占位或“未找到文本列表”，运行态由宿主注入页面解析器与文本列表。
- 深度学习 / 模板匹配 / 手眼标定 / 数据库步骤已按要求保留但默认停用，接入模型与连接后在属性面板启用即可。
