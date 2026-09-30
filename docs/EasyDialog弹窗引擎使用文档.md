# EasyDialog 弹窗引擎 使用文档

> 适用版本：2026-10-01（含"窗口被外部关闭不再卡死弹窗系统"修复）
> 源码：`UI/Controls/CustomControl/Message/EasyDialog.cs`
> 命名空间：`UI.CustomControl`

---

## 1. 它是什么

一个**纯 C#、零 XAML** 的全局弹窗引擎：运行时动态构造一个透明遮罩 `Window`，把标题 / 内容 / 确定·取消按钮拼进去，`await` 用户点击后返回 `bool`。

它存在的理由不是"少写 XAML"，而是解决 WPF 里三个真实的坑：

| 坑 | 解法 |
|---|---|
| 在 `SelectionChanged` 等路由事件里弹窗会死锁 / 调度器挂起 | 弹窗创建排到 `DispatcherPriority.Background`，等当前 UI 事件彻底跑完再创建 |
| 对话框里弹窗被对话框自身遮挡 | Owner 取**当前激活窗口**（不是固定 MainWindow），遮罩跟随其位置与大小 |
| 多个弹窗并发互相打架 | 全局 `SemaphoreSlim(1,1)`，同一时刻只允许一个弹窗 |

---

## 2. API 速查

| 方法 | 返回 | 用途 |
|---|---|---|
| `ShowAsync(title, message, isModal = true)` | `Task<bool>` | 文本确认框（确定 / 取消） |
| `ShowSync(title, message, isModal = true)` | `bool` | 上者的同步版 |
| `ShowCustomAsync(title, FrameworkElement, isModal = true)` | `Task<bool>` | 承载任意自定义控件 |
| `ShowSync(title, FrameworkElement, isModal = true)` | `bool` | 上者的同步版 |
| `ShowTextInputAsync(title, defaultValue = "")` | `Task<(bool IsConfirmed, string Value)>` | 单行文本输入（打开即全选聚焦） |
| `ShowTextInputSync(title, defaultValue = "")` | `(bool, string)` | 上者的同步版 |
| `ShowPropertyGridAsync(title, object)` | `Task<bool>` | 属性表格编辑对象 |
| `ShowPropertyGridSync(title, object)` | `bool` | 上者的同步版 |
| `SetResult(bool)`（internal） | — | 供 `OverlayHost` 这类**外部承载控件**直接收口结果 |

返回值语义统一：**`true` = 确定，`false` = 取消 / Esc / 窗口被关闭**。

> ⚠️ `isModal` 目前是**保留参数，未生效**：引擎始终以遮罩 + 独占锁的方式呈现，传 `false` 不会得到非模态弹窗。别依赖它。

---

## 3. 四种典型用法

### 3.1 删除确认（最常见）

```csharp
if (!EasyDialog.ShowSync("删除模板", $"确定删除模板「{template.Name}」吗？此操作不可撤销。"))
    return;

if (!Store.TryRemove(template.Id, out var error))
    EasyDialog.ShowSync("删除模板失败", error ?? "删除失败");
```

### 3.2 文本输入（重命名）

```csharp
var (confirmed, name) = EasyDialog.ShowTextInputSync($"重命名图层 [{layer.Name}]", layer.Name);
if (!confirmed) return;

// 校验交给模型，界面不自己判一遍规则（否则迟早两处不一致）
if (!page.TryRenameLayer(layer, name, out string error))
    EasyDialog.ShowSync("无法重命名", error);
```

### 3.3 属性表格编辑对象（**必须用草稿**）

```csharp
// ★ 传进去的对象是"就地编辑"的：确定/取消都不回滚。
//   所以新建场景先 new 草稿，确认后才落库。
var draft = new SolutionModel();
if (!await EasyDialog.ShowPropertyGridAsync("创建新解决方案", draft))
    return;          // 取消：草稿直接丢弃，无副作用

draft.SolutionName = (draft.SolutionName ?? "").Trim();
if (draft.SolutionName.Length == 0) { Notifier.ShowWarning("方案名称不能为空"); return; }

service.Create(draft);
```

➡ 编辑**已有对象**时必须传副本，确认后再回填，否则"点取消但对象已被改脏"。

### 3.4 自定义控件

```csharp
var editor = new DialogArrayEditor { ElementTypeName = "Int32", Hint = "每行一个值" };
editor.Load(new[] { "1", "2" });

if (EasyDialog.ShowSync("编辑数组", editor))
    foreach (var text in editor.Values) { /* Convert.ChangeType ... */ }
```

---

## 4. PropertyGrid 显示哪些字段

`FlatPropertyGrid` 按 **`[SuperDisplay]`** 反射生成，**没有该特性的属性不显示**：

```159:162:UI/Controls/CustomControl/PropertyGrid/FlatPropertyGrid.cs
            var properties = BindingObject.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<SuperDisplayAttribute>()?.Visible == true)
                .ToList();
```

常用特性：

| 特性 | 作用 |
|---|---|
| `[SuperDisplay(Name = "解决方案名称")]` | 显示名（没有它该属性不显示） |
| `Visible = false` | 隐藏（子类用 `override` 重贴，**不能用 `new`**，否则渲染两行） |
| `IsReadOnly = true` | 可见但不可编辑（如"版本号"） |
| `GroupPath = "运动/限位"` | 按 `/` 分层，第一段作为 Tab 分组 |
| `Order` / `GroupOrder` | 字段内 / 分组间排序 |
| `RequireRefresh = true` | 该属性变化后整表重建（多态切换用） |

---

## 5. 必须知道的六条约定

1. **就地编辑，取消不回滚** —— 见 3.3，一律走草稿/副本。
2. **全局单弹窗** —— 第二个调用会排队等第一个结束，不要用来做并发提示。
3. **同步版有重入风险** —— `ShowSync` 在 UI 线程用 `Dispatcher.PushFrame` 阻塞；**不要**在 `SelectionChanged`、`Loaded` 等敏感路由事件里用，一律用 `Async`。
4. **参数顺序是 `(title, message)`** —— 历史上有人写反（标题栏显示报错句、正文显示"提示"）。建议显式命名参数：`ShowSync(title: "提示", message: "...")`。
5. **关闭即取消** —— Esc、Owner 关闭、任何外部 `Close()` 都返回 `false`（引擎已订阅 `Closed` + `PreviewKeyDown`）。
6. **无 WPF 上下文时返回 `false`** —— `Application.Current == null`（设计器 / 单测 / 应用已关闭）时弹不出窗，直接按取消处理，不抛异常。

---

## 6. 引擎内部机制（排障用）

```
调用 → Application.Current == null ? false
     → 等 _dialogLock（串行，保证单弹窗）
     → Dispatcher.InvokeAsync(Background)：建遮罩窗、跟随 Owner、注册 Closed/Esc
     → await tcs.Task
     → finally：解绑事件、关窗、释放锁（整段 try/catch，异常也必须放锁）
```

- **结果收口**：按钮 / Esc / `Closed` 三路都指向同一个 `TaskCompletionSource`（本窗口闭包捕获，不串台）。
- **为什么 finally 必须 try/catch**：清理阶段的 `Dispatcher` 回调在应用关闭时可能抛异常，一旦抛了锁就放不掉 —— **之后所有弹窗永久失效**。这是本次修复的重点。
- **`SetResult(bool)`** 走静态"当前 tcs"，供 `OverlayHost` 这类外部承载控件使用，引擎结束后自动置空。

---

## 7. 现有调用点速查

| 位置 | 用法 |
|---|---|
| `ShellViewModel.cs`（新建方案） | `ShowPropertyGridAsync` + 草稿 + 校验 |
| `ProcessViewModel.cs` / `FlowListViewModel.cs` | `ShowTextInputAsync`（步序 / 流程重命名） |
| `ScadaToolboxViewModel.cs` / `ScadaLayerViewModel.cs` / `ScadaEditorViewModel.cs` | `ShowTextInputSync`、`ShowSync` 确认删除 |
| `GlobalVariableManagerViewModel.cs` | `ShowSync` 提示（已改用命名参数） |
| `VariableBindingViewModel.cs` | `ShowSync` 类型不匹配提示、`ShowTextInputSync` 索引选择 |
| `ScanGroupEditorViewModel.cs` | `ShowSync` 删除 / 覆盖确认 |
| `CameraSettingsViewModel.cs` | 属性表格类弹窗 |
| `DialogArrayEditor` | 由 `ShowSync(title, FrameworkElement)` 承载 |

---

## 8. 本次修复记录（2026-10-01）

| 问题 | 修复 |
|---|---|
| 窗口被外部关闭时 `_tcs` 永不完成 → `await` 永久挂起 → 锁不释放 → **整个弹窗系统一次性死掉** | 订阅 `overlayWindow.Closed` → `TrySetResult(false)` |
| `finally` 里 `Dispatcher` 回调抛异常会导致锁不释放 | 清理段整包 `try/catch`，异常也放锁 |
| 静态 `_tcs` 与窗口脱钩，并发/未来改动会串台 | 结果改由**本窗口闭包捕获的局部 tcs** 收口；静态字段仅保留给 `SetResult` 兼容外部承载控件 |
| 无键盘取消、无默认焦点 | Esc = 取消；无输入控件时默认焦点给「确定」 |
| 无 WPF 上下文时 NRE | `Application.Current == null` 直接返回 `false` |
| 新建方案：空名 / 重名 / 失败静默，成功无提示（`ShellViewModel`） | 补 Trim+空名+重名校验、`Create` 结果检查、成功 toast、异常保护 |
| 新建方案弹窗让用户填版本号 | `SolutionModel.Version` 改 `IsReadOnly = true` |
