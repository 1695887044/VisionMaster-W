# UI 设计基线（Web 形式）

本目录存放**以 Web 形式表达的设计基线**，用于界面设计与确认。

## 定位（必读）

- 这里的 HTML/截图/原型是**设计媒介**，**不是交付物**：
  - 不进入任何工程构建，不参与打包，不部署；
  - 不作为"另一个客户端"维护；
  - 产品交付物只有四类：WPF 代码、令牌映射表、截图证据、差异清单（见 `AGENTS.md` §30.1）。
- 设计基线是**只读**的：确认后冻结版本；需要改设计时升版本（`v1` → `v2`），并同步更新已复刻的桌面端页面。

## 流程

```text
设计基线（Web 形式） → 令牌映射 → 桌面端复刻 → 截图验收
```

没有设计基线，不得直接改桌面端样式。

## 目录约定

```text
docs/UI设计基线/
  README.md                 本文件（总约定）
  <页面名>/
    README.md               版本号、确认日期、确认人、覆盖范围、已知偏差
    design.html             可点击设计基线（自包含，无外部依赖）
    evidence/               桌面端复刻后的对照截图（改前 / 改后 / 差异标注）
```

## 现有基线

| 页面 | 版本 | 覆盖范围 | 状态 |
| --- | --- | --- | --- |
| `process-panel/` | v2 | `ProcessView.xaml`（步骤树 + 底部统计） | 已复刻，证据 `process-panel/evidence/after-render-F.png` |
| `io-health/` | v1 | `IoHealthView.xaml`（设备 / 总线 / 点位三页签） | 待用户确认 |
| `app-shell/` | v2 | `Shell.xaml` 及三场景工作区（调试 / 运行 / 组态 + 自由布局）、单列检查器、可收起控制台、运行条、运行驾驶舱、命令面板；v1 归档在 `design-v1.html` | 待用户确认 |
| `image-acquisition/` | v2 | `Plugin.ImageAcquisition\ImageAcquisitionView.xaml`（配置窗口内容区 + 960×680 宿主外壳；v1 归档在 `design-v1.html`） | 已复刻，证据 `image-acquisition/evidence/after-render-v2-*.png`；待用户确认 |
| `logic-operators/` | v1 | 流程画布逻辑节点视觉语言：41 个算子 / 6 族、IF/SWITCH/PARALLEL/WHILE 容器、3 个组合示例、节点状态矩阵、逐算子 WPF 映射 | 待用户确认 |

## 插件改造 SOP 与清单

- [插件自定义视图Web化改造SOP.md](插件自定义视图Web化改造SOP.md)：适用范围、前置条件、逐步操作、涉及文件、验证、回滚、注意事项、单插件记录模板、DoD。
- [插件自定义视图改造清单与实施计划.md](插件自定义视图改造清单与实施计划.md)：22 个自定义视图插件盘点（ImageAcquisition 已完成，剩余 21 个）、分批计划、9 个不改造项目清单。

## 对应关系

- 颜色：CSS 变量 ↔ `UI/Controls/Themes/Colors.xaml` 语义令牌；
- 间距/圆角/字号/阴影：按 `AGENTS.md` §30 的刻度一一对应；
- 组件：HTML 控件 ↔ UI 库里的 `Dialog*` / Fluent 样式（映射表随每个页面记录）。

## 允许偏差

字体度量、滚动条宽度、阴影层数、动效曲线允许与 Web 基线存在细微差异，但必须逐条写进该页面的 `README.md`，并由用户裁定是否接受。
