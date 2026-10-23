# 任务清单（点一下就跑）

> 用法：说「执行 `<计划ID>`，`<参数>`=…」，AI 只读一次计划，然后按步骤机械执行；中途不再问「下一步点哪」。
>
> 现在可用的是 L0（对话 + computer-use）。L2 独立执行器（`run-plan.ps1`）见 `README.md` §7，尚未实现。

## 常用任务

| 说法 | 计划 ID | 必填参数 | 示例说法 |
|---|---|---|---|
| 新建方案 | `solution-new` | `solution_name` | 执行 solution-new，solution_name=Demo_20261023 |
| 保存方案 | `solution-save` | `save_path` | 执行 solution-save，save_path=D:\C#\VM\解决方案\Demo.vms |
| 打开方案 | `solution-open` | `solution_path` | 执行 solution-open，solution_path=D:\C#\VM\解决方案\Demo.vms |
| 从清单打开 | `solution-list-open` | `solution_name` | 执行 solution-list-open，solution_name=Demo |
| 新建并保存 | `solution-create-and-save` | `solution_name`、`save_path` | 方案保存：solution_name=Demo，save_path=D:\...\Demo.vms |
| 新建流程 | `flow-create` | `anchor_flow`（默认 主流程） | 执行 flow-create，anchor_flow=主流程 |
| 重命名流程 | `flow-rename` | `old_name`、`new_name` | 执行 flow-rename，old_name=新建流程，new_name=定位流程 |
| 加算子 | `step-add` | `tool_name`、`anchor_step`（可空） | 执行 step-add，tool_name=图像采集，anchor_step=图像采集 |
| 配步骤参数 | `step-configure` | `step_name`、`fields` | 执行 step-configure，step_name=图像采集，fields={"图像路径":"D:\\a.png"} |
| 配条件/等待 | `step-configure-condition` | `step_name`、`expression` | 执行 step-configure-condition，step_name=等待，expression=Global.Var1>10 |
| 变量绑定 | `step-bind-variable` | `step_name`、`port_name`、`source_name` | 执行 step-bind-variable，step_name=图像采集，port_name=图像路径，source_name=Global.ImagePath |
| 跑一次 | `run-once` | — | 执行 run-once |
| 循环跑 | `run-loop` | — | 执行 run-loop |
| 停止 | `run-stop` | — | 执行 run-stop |
| 加本地变量 | `variable-add-local` | `variable_name` | 执行 variable-add-local，variable_name=DemoCounter |
| 加通讯设备 | `comm-add-device` | `connection_name`、`ip` | 执行 comm-add-device，connection_name=PLC_01，ip=192.168.1.10 |
| 加相机 | `camera-add` | `driver`、`serial` | 执行 camera-add，driver=虚拟相机，serial=CAM-001 |
| 捕获配方 | `recipe-capture` | `recipe_name` | 执行 recipe-capture，recipe_name=ProductA |
| 新建组态页 | `scada-page-new` | `expected_page_name`（默认 画面_1） | 执行 scada-page-new |
| 拖图元 | `scada-add-element` | `element_name` | 执行 scada-add-element，element_name=按钮 |
| 运行组态 | `scada-run` | `page_name`（可空） | 执行 scada-run，page_name=画面_1 |
| 保存布局 | `layout-save` | `app_dir` | 执行 layout-save，app_dir=D:\C#\VM\VisionMaster\bin\Debug\net9.0-windows |
| 切模式 | `mode-switch` | `mode_key`、`expect_text` | 执行 mode-switch，mode_key=Ctrl+3，expect_text=基座 |
| 搜帮助 | `help-search` | `query` | 执行 help-search，query=图像采集 |

## 执行约定

1. 先校验计划：`powershell -NoProfile -ExecutionPolicy Bypass -File docs\AI操作计划\tools\validate-plans.ps1`。
2. 只读 `plans\<计划ID>.json`，不要临场改步骤；缺选择器就停下来报告，不要猜坐标。
3. 每一步执行后核对 `expect`；不成立立即停，截图并报「第 n 步期望 X 实际 Y」。
4. 高风险动作（删除、覆盖、运动、写变量）执行前必须二次确认；不要自动重试。
5. 执行完把结果写进 `artifacts\logs\<yyyyMMdd>\plan-<计划ID>.log`，不要落仓库根目录。
