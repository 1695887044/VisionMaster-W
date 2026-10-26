# vms-generator（参考实现）

生成 `VisionMaster` 可打开的 `.vms` 文件：流程 / 变量 / 通讯 / 运动卡 / 组态。

## 运行

```powershell
cd tools\vms-generator
dotnet run
# 智能装配检测线（6 工位 / 15 页组态 / 全图元）
dotnet run -- --smart-line
```

输出（一个方案一个文件夹，文件夹名逐字等于 `SolutionName`）：

- `解决方案\综合检测线演示\综合检测线演示.vms`
- `解决方案\综合测试台\综合测试台.vms`
- `解决方案\柔性装配线\柔性装配线.vms`
- `解决方案\视觉分拣线\视觉分拣线.vms`
- `解决方案\数据网关\数据网关.vms`
- `解决方案\自动化产线\自动化产线.vms`
- `解决方案\压测方案-3000节点\压测方案-3000节点.vms`（`--stress`）
- `解决方案\压测方案-3000节点-单流程\压测方案-3000节点-单流程.vms`（`--stress --giant`）

每个文件夹内都是 `<方案名>.vms` + `Image\` `Alarms\` `History\` `Logs\`；
`.vms` 里只出现 `Image\文件名` 与 `History\...`，落盘前自检任何盘符路径 / `..\` 即失败。

## 注意

- 插件 `PluginTypeName` 是本机 `Modules\` 下程序集的 AQN；换机器/升级插件后要重新反射 dump。
- 样图源在 `D:\C#\VM\Image\...`（`SourceImageRoot` + `SourceImageMap`）；生成期复制进方案自己的
  `Image\`，`.vms` 只记相对路径。换机器只需改这两处，方案本身可整包搬走。
- 生成后必须用 `SolutionService.LoadAsync` + `FlowCompiler` 逐流程验证；组态用 `tools\probes\_ScadaUiProbe` 渲染。
- 详细字段说明见 `docs\方案设计\AI生成VMS文件指南.md`。
