# 任务清单（点一下就跑）

> 用法：说「执行 `<计划ID>`，`<参数>`=…」，AI 只读一次计划，然后按步骤机械执行；中途不再问「下一步点哪」。
>
> 现在可用的是 L0（对话 + computer-use）。L2 独立执行器（`run-plan.ps1`）见 `README.md` §7，尚未实现。
>
> 参数名后带 `*` 的是必填；没写的用计划里的默认值。全部 47 个计划见 `plans/`，结构校验见文末。

## 方案

| 说法 | 计划 ID | 必填参数 | 示例说法 |
|---|---|---|---|
| 新建方案 | `solution-new` | `solution_name` | 执行 solution-new，solution_name=Demo_20261023 |
| 保存方案 | `solution-save` | — | 执行 solution-save（已落盘的方案直接写回） |
| 另存为 | `solution-save-as` | `parent_dir`、`expected_vms` | 执行 solution-save-as，parent_dir=D:\VMSolutions，expected_vms=D:\VMSolutions\Demo\Demo.vms |
| 新建并保存 | `solution-create-and-save` | `solution_name`、`parent_dir` | 方案保存：solution_name=Demo，parent_dir=D:\VMSolutions |
| 打开方案 | `solution-open` | `solution_path` | 执行 solution-open，solution_path=D:\VMSolutions\Demo\Demo.vms |
| 从清单打开 | `solution-list-open` | `solution_name` | 执行 solution-list-open，solution_name=Demo |
| 从清单移除 | `solution-list-remove` | `solution_name` | 执行 solution-list-remove，solution_name=Demo |
| 设为默认启动 | `solution-set-default` | `solution_name` | 执行 solution-set-default，solution_name=Demo |
| 把当前方案加入清单 | `solution-list-add-current` | `solution_name` | 执行 solution-list-add-current，solution_name=Demo |

> `solution-save` 只对**已落盘**方案有效；从未保存或散装老方案会自动走「另存为」文件夹选择，此时改用 `solution-save-as`。

## 流程与步骤

| 说法 | 计划 ID | 必填参数 | 示例说法 |
|---|---|---|---|
| 新建流程 | `flow-create` | `anchor_flow`（默认 主流程） | 执行 flow-create，anchor_flow=主流程 |
| 重命名流程 | `flow-rename` | `old_name`、`new_name` | 执行 flow-rename，old_name=新建流程，new_name=定位流程 |
| 删除流程 | `flow-delete` | `flow_name` | 执行 flow-delete，flow_name=定位流程 |
| 禁用流程 | `flow-disable` | `flow_name` | 执行 flow-disable，flow_name=定位流程 |
| 启用流程 | `flow-enable` | `flow_name` | 执行 flow-enable，flow_name=定位流程 |
| 流程注释 | `flow-edit-comment` | `flow_name`、`comment` | 执行 flow-edit-comment，flow_name=主流程，comment=上料检测 |
| 编译流程 | `flow-compile` | `flow_name` | 执行 flow-compile，flow_name=主流程 |
| 单次跑某流程 | `flow-run-once` | `flow_name` | 执行 flow-run-once，flow_name=主流程 |
| 循环跑某流程 | `flow-run-loop` | `flow_name` | 执行 flow-run-loop，flow_name=主流程 |
| 跑一次 | `run-once` | — | 执行 run-once |
| 循环跑 | `run-loop` | — | 执行 run-loop |
| 停止 | `run-stop` | — | 执行 run-stop |
| 加算子 | `step-add` | `tool_name`、`anchor_step`（可空） | 执行 step-add，tool_name=图像采集，anchor_step=图像采集 |
| 配步骤参数 | `step-configure` | `step_name`、`fields` | 执行 step-configure，step_name=图像采集，fields={"图像路径":"D:\\a.png"} |
| 配条件/等待 | `step-configure-condition` | `step_name`、`expression` | 执行 step-configure-condition，step_name=等待，expression=Global.Var1>10 |
| 变量绑定 | `step-bind-variable` | `step_name`、`port_name`、`source_name` | 执行 step-bind-variable，step_name=图像采集，port_name=图像路径，source_name=Global.ImagePath |
| 重命名步骤 | `step-rename` | `step_name`、`new_name` | 执行 step-rename，step_name=图像采集，new_name=取图 |
| 步骤注释 | `step-edit-comment` | `step_name`、`comment` | 执行 step-edit-comment，step_name=取图，comment=相机1 |
| 删除步骤 | `step-delete` | `step_name` | 执行 step-delete，step_name=取图 |

## 变量

| 说法 | 计划 ID | 必填参数 | 示例说法 |
|---|---|---|---|
| 加本地变量 | `variable-add-local` | `variable_name` | 执行 variable-add-local，variable_name=DemoCounter |
| 重命名变量 | `variable-rename` | `old_name`、`new_name` | 执行 variable-rename，old_name=DemoCounter，new_name=Counter |
| 删除变量 | `variable-delete` | `variable_name` | 执行 variable-delete，variable_name=Counter |

## 通讯与设备

| 说法 | 计划 ID | 必填参数 | 示例说法 |
|---|---|---|---|
| 加通讯设备 | `comm-add-device` | `connection_name`、`ip` | 执行 comm-add-device，connection_name=PLC_01，ip=192.168.1.10 |
| 删通讯设备 | `comm-delete` | `connection_name` | 执行 comm-delete，connection_name=PLC_01 |
| 测试连接 | `comm-test` | `connection_name` | 执行 comm-test，connection_name=PLC_01 |
| 加相机 | `camera-add` | `driver`、`serial` | 执行 camera-add，driver=虚拟相机，serial=CAM-001 |
| 删相机 | `camera-delete` | `camera_name` | 执行 camera-delete，camera_name=CAM-001 |

## 配方

| 说法 | 计划 ID | 必填参数 | 示例说法 |
|---|---|---|---|
| 捕获配方 | `recipe-capture` | `recipe_name` | 执行 recipe-capture，recipe_name=ProductA |
| 应用配方 | `recipe-apply` | `recipe_name` | 执行 recipe-apply，recipe_name=ProductA |
| 重命名配方 | `recipe-rename` | `old_name`、`new_name` | 执行 recipe-rename，old_name=ProductA，new_name=ProductB |
| 删除配方 | `recipe-delete` | `recipe_name` | 执行 recipe-delete，recipe_name=ProductB |

## 组态与模式

| 说法 | 计划 ID | 必填参数 | 示例说法 |
|---|---|---|---|
| 新建组态页 | `scada-page-new` | `expected_page_name`（默认 画面_1） | 执行 scada-page-new |
| 拖图元 | `scada-add-element` | `element_name` | 执行 scada-add-element，element_name=按钮 |
| 运行组态 | `scada-run` | `page_name`（可空） | 执行 scada-run，page_name=画面_1 |
| 切模式 | `mode-switch` | `mode_key`、`expect_text` | 执行 mode-switch，mode_key=Ctrl+3，expect_text=基座 |

## 布局与帮助

| 说法 | 计划 ID | 必填参数 | 示例说法 |
|---|---|---|---|
| 保存布局 | `layout-save` | `app_dir` | 执行 layout-save，app_dir=D:\C#\VM\VisionMaster\bin\Debug\net9.0-windows |
| 恢复默认布局 | `layout-reset` | — | 执行 layout-reset（会弹两次「确定」） |
| 搜帮助 | `help-search` | `query` | 执行 help-search，query=图像采集 |

## 执行约定

1. 先校验计划：`powershell -NoProfile -ExecutionPolicy Bypass -File docs\AI操作计划\tools\validate-plans.ps1`，期望 `plans: 47  windows: 26  elements: 205` 且末行 `OK`。
2. 只读 `plans\<计划ID>.json`，不要临场改步骤；缺选择器就停下来报告，不要猜坐标。
3. 每一步执行后核对 `expect`；不成立立即停，截图并报「第 n 步期望 X 实际 Y」。
4. 高风险动作（删除、覆盖、运动、写变量）执行前必须二次确认；不要自动重试。运动与轴操作**没有计划**，必须人在现场做（见 README 附录 B）。
5. 执行完把结果写进 `artifacts\logs\<yyyyMMdd>\plan-<计划ID>.log`，不要落仓库根目录。
