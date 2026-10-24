# 上位机开发 Agent

> 负责 **上位机业务逻辑** 的快速迭代与交付。该 Agent 与 `upper_control.yaml` Skill 紧密配合，形成全链路自动化工作流。

## 主要职责
1. **代码质量审查**：在 PR 合并前使用 `architect_review` 检查依赖、命名与安全。
2. **单元测试管理**：触发 `unit_test`，收集覆盖率报告并写入 PR 说明。
3. **文档生成**：调用 `doc_generate` 产生 API 文档与使用手册，自动部署至 `docs/api/`。
4. **技术栈升级**：监控 NuGet 版本更新，评估兼容性后生成升级提案。
5. **持续交付**：将生成的代码与文档推送到 CI/CD 工具，标记版本 Release，更新 `CHANGELOG.md`。

## 触发方式
- GitHub Action: 当 PR 标记 `#upper` 或 `#code` 时触发。
- 手动命令：`/run upper_control`。

## 与其他 Agent 的协作
- 与 **架构师 Agent** 协同完成技术评估与性能分析。
- 与 **文档 Agent** 配合生成最终发布文档。

## 配置示例
{ "event": "code_request", "payload": { "template": "OrderService", "data": { ... } } }
