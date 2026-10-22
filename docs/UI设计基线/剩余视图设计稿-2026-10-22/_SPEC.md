# 分片写作规范（给设计智能体）

目标：为剩余视图产出 **HTML 分片**，由主会话汇总成一份 `design.html`。

## 你必须做的事

1. 读你负责的每个视图的 `.xaml`（**只读，不许改任何 WPF 代码**），提取：
   - 真实字段名 / 列名 / 按钮文案 / 状态文案
   - 布局结构（几栏、有没有页签、有没有画布/表格/列表）
   - 已有的空态/错误态（没有就按设计补）
2. 为每个视图写一个 `<section class="view" id="view-NN">…</section>` 分片，存到
   `D:\C#\VM\docs\UI设计基线\剩余视图设计稿-2026-10-22\parts\<NN>-<slug>.html`。
3. 分片里**只写 section 本身**，不要 `<html>/<head>/<body>/<style>`，不要 `<script>`。

## 硬约束

| # | 约束 |
| --- | --- |
| 1 | **禁止行内颜色**（`style="color:#xxx"` / `background:#xxx`）。颜色只能用 `var(--token)` 或下面列出的 class。 |
| 2 | **禁止 `<style>` 块**。共享 CSS 已在 `_shell.html` 里，你只能用它的 class。 |
| 3 | **禁止外部资源**（`<img>`、`<link>`、`url()`、CDN）。图标用 Unicode 几何字符（▤ ▣ ◈ ⌕ ✕ ▾ ▸ ＋ − ⚠ ● ○ ■ □）。 |
| 4 | **不许编字段**。每个字段/按钮必须能在对应 `.xaml` 或 ViewModel 里找到；找不到就不画，或在 `.notes` 里注明"设计建议新增"。 |
| 5 | **不许造死按钮**。只画现有 `Command` 对应的按钮；纯视觉入口（如关闭/展开）可以画。 |
| 6 | 每个分片必须包含一个 `.notes` 块，写清：**覆盖的真实文件**、**改动要点**、**需要新增的接口**（没有就写"无"）。 |

## 可用 class（白名单）

外壳：`.mock` `.mock-bar`（内含 `.t` `.s` `.grow`）`.mock-body`（可加 `.tight` `.alt`）`.mock-foot`（内含 `.grow`）

分栏：`.split` `.split-aside` `.split-main` `.split-right`

工具条：`.toolbar`（可加 `.wrap`，内含 `.grow`）`.tool`（可加 `.primary` `.danger` `.icon` `.active`）`.sep`

表单：`.field` `.field-label`（可加 `.left`）`.input`（可加 `.ph` `.mono`）`.combo`（可加 `.mono`）`.check`（内含 `.box`，`.box.on` 表示勾选）`.hint`

页签：`.tabs` `.tab`（`aria-selected="true"` 表示选中）

表格：`.table` `.thead` `.tr`（可加 `.sel`）`.th` `.td`（可加 `.mono` `.muted` `.right`）— 列宽用 `style="width:80px"` 这种**只含尺寸**的行内样式是允许的

标签：`.tag`（可加 `.info` `.ok` `.warn` `.err` `.if`）`.dot`（可加 `.ok` `.warn` `.err` `.info`）

卡片：`.card` `.card-title` `.cards`（可加 `.three` `.four`）

键值：`.kv`（内含 `.k` `.v`，`.v` 可加 `.mono`）

列表：`.row`（可加 `.sel`，内含 `.name` `.meta` `.mono`）

空态：`.empty` `.empty-glyph` `.empty-title` `.empty-hint`

说明：`.notes`（内含 `h3` `ul` `li` `code`，`.warn` `.err`）

## 分片模板

```html
<section class="view" id="view-08">
  <div class="view-head">
    <h2>组态编辑器</h2>
    <span class="file">VisionMaster\Views\ScadaEditorView.xaml</span>
    <span class="size">724 行 · 4 hex</span>
  </div>
  <div class="view-body">

    <!-- 主态 -->
    <div class="mock">
      <div class="mock-bar">
        <span class="t">组态画面</span>
        <span class="s">页面：主界面 / 缩放 100%</span>
        <span class="grow"></span>
        <span class="tool icon">✕</span>
      </div>
      <div class="toolbar">
        <span class="tool active">选择</span>
        <span class="tool">矩形</span>
        <span class="sep"></span>
        <span class="tool">对齐</span>
      </div>
      <div class="mock-body alt">
        <!-- 画布示意：用 .empty 或简单的色块 + 文字 -->
      </div>
      <div class="mock-foot">
        <span>图元 12</span>
        <span class="grow"></span>
        <span>缩放 100%</span>
      </div>
    </div>

    <!-- 关键状态（空态 / 错误态 / 窄档），按需再加 mock -->

    <div class="notes">
      <h3>覆盖与要点</h3>
      <ul>
        <li>覆盖：<code>ScadaEditorView.xaml</code>（顶栏 + 画布 + 底部状态条）</li>
        <li>改动要点：……</li>
        <li>需要新增的接口：无</li>
      </ul>
    </div>
  </div>
</section>
```

## 质量口径（会被 UI / 前端智能体复查）

1. **布局**：至少给出「默认态」+ 一个「边界态」（空 / 错误 / 窄档），除非该视图确实没有边界态（要在 `.notes` 里写理由）。
2. **五态**：可交互控件（按钮/输入/页签）要有 hover 概念——静态稿里用 `.tool:hover` 已覆盖，你只需保证用的是 `.tool` 而不是裸 `<div>`。
3. **真实字段**：表格列名、表单标签、状态文案必须来自 XAML。
4. **不臆造数字**：示例数据要像真的（`0.932`、`128 ms`、`19:04:12.318`），不要 `xxx`、`123`。
5. **每个分片 60~140 行**，不要写成长篇大论；重点是把结构、状态、字段说清。
6. 分片之间**不要**互相引用，也不要假设别的分片存在。

## 编号与命名

用主会话给你的编号（`NN`）与文件名。编号会写进左侧目录，顺序即阅读顺序。
