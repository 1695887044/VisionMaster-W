# SCADA 运行态 Web · 浏览器级冒烟验证记录（2026-10-22）

- 分支：`feature/scada-web-v4-a-prime`
- 被测：真实 `ScadaWebHost` + `wwwroot` 客户端（A′ 第一~五批）
- 浏览器：ZCode IAB（Chromium，回环 `127.0.0.1`）
- 目的：证明协议层绿灯不是“客户端根本没跑起来”；**不**用它外推 16 端高负载或现场网络。
- 结论：**登录、渲染、值帧、按钮命令、静态资源全部通过；0 占位盒、0 客户端告警。**

## 1. 启动方式

压测夹具新增 `--serve`（浏览器验证入口，不跑压测客户端）：

```bat
Web\ScadaWebStress\bin\Debug\net9.0-windows\ScadaWebStress.exe ^
  --serve --serveSeconds 240 ^
  --elements 60 --variables 12 --boundElements 12 ^
  --changePct 100 --changeHz 5 --maxSessions 8 ^
  --outDir "%TEMP%\scada-serve"
```

宿主打印 `SERVE http://127.0.0.1:<port>/`，登录 `stress / pw`
（`AllowAnonymousView=false`、`AllowCommands=true`、`RequireConfirmForDangerous=false`）。
`--serve` 复用同一套真实 `ScadaWebHost`、真实快照/值帧循环与命令路由；
它只是把“造 N 条 ClientWebSocket”换成“留一个地址给浏览器”。

## 2. 观察结果

### 2.1 登录

- 首屏：`登录运行画面` 卡片、用户名/密码输入框、登录/匿名/演示入口；
- 填 `stress/pw` 点登录 → 运行画面：标题「压测页」、`在线 1`、`已连接`、`可操作`；
- 浏览器视口 1280×720，画布自动缩放 `scale=0.8`（顶栏提示“窗口较小，已启用最小缩放”）。

### 2.2 渲染

| 指标 | 实测 |
| --- | --- |
| `.scada-el` 数量 | **61**（60 图元 + 1 按钮） |
| `.scada-placeholder` 数量 | **0** |
| 注册渲染器 | **29** |
| `state.records` / `state.nodes` | 61 / 61 |
| `state.warned` | **空**（无 `warnOnce` 告警） |
| WebSocket | `readyState=1`，`attempt=0`，`lastCloseReason=""` |
| `imageToken` | 已下发 |

### 2.3 值帧

1.2 秒内前 6 个绑定值：

```
before: 580 581 582 583 584 585
after : 802 803 804 805 806 807
frameSeq: 296 -> 358
```

说明 5Hz 变化源 → 10Hz 值帧路径、增量帧应用、DOM 文本更新都在真实浏览器里生效。

### 2.4 静态资源

`fetch(..., {cache:'no-store'})` 全部 200：

| 路径 | 字节 |
| --- | --- |
| `/` | 5,172 |
| `/app.js` | 137,421 |
| `/elements-process.js` | 91,487 |
| `/elements-interactive.js` | 84,419 |
| `/api/catalog` | 131（空目录 + warning，压测宿主未注册 catalog） |

### 2.5 命令链路

点击「按钮」后，页面 WebSocket 实际发出两条：

```json
{"type":"cmd.element","id":"8e3bb47c-…","pageId":"034bb4fa-…","pageVersion":62,
 "runGeneration":1,"elementId":"79a16976-…","event":"Pressed"}
{"type":"cmd.element","id":"c19603d4-…","pageId":"034bb4fa-…","pageVersion":62,
 "runGeneration":1,"elementId":"79a16976-…","event":"Released"}
```

- 无 toast、无 `warned`、WS 保持 open；
- 说明页面上下文（pageId/pageVersion/runGeneration）来自快照、命令信封正确。

### 2.6 视觉复核

- 默认页面底色 `#FF1E1E1E` + 默认 `Hmi.Text.Foreground=#FF202020`：文本几乎不可见，
  首次截图只见 12 个数值条；
- 通过 `ScadaApp.getState().page.Background = '#FFFFFFFF'; ScadaApp.rerender()` 切白底后，
  48 个 `Hmi.Text` 与 12 个数值条全部可见（截图见会话产物）；
- **这是既有默认配色对（`ScadaPage.cs:25` / `PropertyPacks.cs:191`），不是本批回归**；
  压测夹具没设置页面底色，所以第一次截图看起来“只有数值条”。是否调整默认配色是 UI 设计决策，
  本批不动。

## 3. 未覆盖

- 16 端 × 3000 图元下的浏览器绘制/主线程阻塞（本冒烟只有 61 图元）；
- 真实网卡/交换机/WiFi、丢包重排；
- 长稳 >60 秒、内存/句柄曲线；
- WPF 与 Web 的逐图元截图对照（只做了“Web 自己能画出来”）。

## 4. 复现

1. 构建（R65：不要 `--no-build`）：
   ```bat
   dotnet build Web\ScadaWebStress\ScadaWebStress.csproj -c Debug
   ```
2. 按 §1 启动，记下 `SERVE http://127.0.0.1:<port>/`；
3. 浏览器打开该地址，登录 `stress/pw`；
4. 用开发者工具或 `window.ScadaApp.getState()` 复核 §2 的指标；
5. 验证结束 `Ctrl+C` 或等 `--serveSeconds` 到期（进程会打印 `SERVE_STOPPED`）。
