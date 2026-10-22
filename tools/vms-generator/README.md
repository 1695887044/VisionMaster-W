# vms-generator（参考实现）

生成 `VisionMaster` 可打开的 `.vms` 文件：流程 / 变量 / 通讯 / 运动卡 / 组态。

## 运行

```powershell
cd tools\vms-generator
dotnet run
```

输出（绝对路径，按本机仓库）：

- `解决方案\综合检测线.vms`
- `解决方案\综合测试台.vms`
- `解决方案\柔性装配线.vms`
- `解决方案\视觉分拣线.vms`
- `解决方案\数据网关.vms`
- `解决方案\自动化产线.vms`

## 注意

- 插件 `PluginTypeName` 是本机 `Modules\` 下程序集的 AQN；换机器/升级插件后要重新反射 dump。
- 图片路径是 `D:\C#\VM\Image\...`，换机器要改。
- 生成后必须用 `SolutionService.LoadAsync` + `FlowCompiler` 逐流程验证；组态用 `tools\probes\_ScadaUiProbe` 渲染。
- 详细字段说明见 `docs\方案设计\AI生成VMS文件指南.md`。
