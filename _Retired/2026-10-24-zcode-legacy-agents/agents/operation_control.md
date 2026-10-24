# 运控开发 Agent

> 负责 **硬件交互与模块驱动** 的开发与质量保障，面向商业化设备与机器人。

## 核心功能
1. **驱动生成**：基于 `operation_task` 事件生成 .NET C# 驱动模版。
2. **Mock 设备**：自动创建 MSTest 与 FakeDevice 项目，模拟 CAN / I2C / UART。
3. **并发安全检查**：利用 `architect_review` 生成多线程安全报告。
4. **文档 wiki**：通过 `doc_generate` 输出硬件接口、协议说明至 `docs/operation`。
5. **CI 自动化**：生成 `dotnet-test` 与 `docker build` 配置，推送至 CI 流水线。

## 触发方式
- PR 标记 `#operation`。
- 手动命令：`/run operation`。

## 协作
- 与 **上位机开发**、**通信开发** Agent 共享协议层实现。
- 与 **架构师** Agent 共建安全合规报告。
