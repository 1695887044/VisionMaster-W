# SCADA 运行态 Web · A′ 第三批（ImageView 图像通道）

- 日期：2026-10-22
- 分支：`feature/scada-web-v4-a-prime`
- 提交：`d96e9413`
- 关联：`docs/performance/2026-10-22-SCADA-Web-A-prime-性能测试报告.md`、`docs/meetings/2026-10-22-SCADA-Web-v4-剩余项评审-R1.md`

---

## 1. 本批改动

### 1.1 图像绑定不再进 10Hz 标量帧

- `Web/VisionMaster.ScadaWeb/ScadaWebValueResolver.cs`
  - `Hmi.ImageView` + `TargetProperty == "Source"` 直接跳过。
  - 旧行为：每 10Hz 对每个图像绑定做一次全尺寸 `HImage→BitmapSource`，再把类型名
    （`System.Windows.Media.Imaging.BitmapSource`）当 `text` 发；既打 UI 线程又上屏垃圾。
- 断言：`ScadaWebChecks` **B10b**（纯内存快照 + 假值源，断言 resolver 返回 0 项）。

### 1.2 imageToken

- `ScadaWebHost` 新增：
  - `_imageTokens`（token → `{SessionId, ExpiresAtUtc}`）与 `_currentImageToken`（SessionId → 最新 token）；
  - `IssueImageToken`：45 秒 TTL、每会话最多 3 个、超限淘汰最旧；
  - `TryResolveImageToken`：过期即删；
  - `RemoveImageTokens`：会话结束/撤销/停服时清理；
  - `RotateImageTokens`：presence 循环（2s）里剩余寿命 <15s 就发 `{type:"img.token", token, expiresIn:45}`；
  - `BuildHello` 增加 `imageToken` / `imageTokenExpiresIn`。
- **不复用 sessionToken**：8 小时登录凭据放 URL 会经历史/代理/Referer 泄漏；imageToken 只读图、短命、可轮换。

### 1.3 `/img` 路由

- `RegisterStaticRoutes` 注册 `GET /img`（`PreAuthentication.Static`）：
  - 只有静态路由能拿 `HttpContextBase.Response.Send(byte[])`；普通 `server.Get` 会把 `byte[]` JSON 序列化成 base64。
- `HandleImageAsync`：
  - URL：`/img?elementId=<guid>&version=<v>&t=<imageToken>`；
  - 校验 token → 会话仍在 → `elementId` 是当前页的 `Hmi.ImageView` 且有启用的 `Source` 绑定；
  - `CaptureImage` 在 UI 线程上取位图（`IScadaValueSource` 解析绑定；`HImage` 已在值源里转成冻结位图），
    `EncodePng` 在后台线程编码；
  - 成功：`Content-Type: image/png`、`Cache-Control: private, max-age=30`、
    `X-Content-Type-Options: nosniff`、`Referrer-Policy: no-referrer`，原样吐字节；
  - 失败：401 `E_IMG_TOKEN` / 401 `E_SESSION_EXPIRED` / 400 `E_BAD_REQUEST` /
    404 `E_IMG_EMPTY` / 500 `E_IMG_ENCODE`。
- 删除了旧的 `/img` 固定 404 占位路由。

### 1.4 客户端

- `wwwroot/app.js`
  - `state.imageToken`；`hello.imageToken` 与 `img.token` 消息更新；
  - `createContext` 增加 `imageToken()` / `imageUrl(elementId, version)`；
  - `createRecord` 支持可选 `dispose()`；`renderSnapshot` 在结构重建前释放旧渲染器。
- `wwwroot/elements-process.js`
  - 新增 `Hmi.ImageView` 渲染器：`<img>` + 占位提示；`Stretch` 映射到 `object-fit`；
    1fps 刷新（`Date.now()` 做 cache-busting）；`onerror` 回占位；`dispose` 清定时器。
- `ScadaChecks/WebRendererCoverageChecks.cs`：例外清单 4 → **3**（DataGrid / AxisPanel / AxisConsole）；
  `[WR3]` 上限 4 → 3。

## 2. 验证

| 项 | 结果 |
| --- | --- |
| `ScadaWebChecks` | **122 PASS / 0 FAIL** |
| `B10b` | `items=0`（图像不进标量帧） |
| `B51` | `token` 非空、`status=200`、`type=image/png`、PNG 魔数、127 字节 |
| `B51b` | 坏 token → `401` |
| `ScadaChecks` | **2438 PASS / 0 FAIL**；WR 例外 3 项 |
| `node --check app.js / elements-process.js` | 0 |

## 3. 已知限制（下一批）

1. **版本号只是 cache-busting**：服务端不跟踪图像内容版本，`version` 只用于让浏览器换 URL；
   没有 `ETag`/`304`，1fps 刷新时每次都会重新编码/传输。
2. **未降采样/未 JPEG**：当前全尺寸 PNG；现场 5MP 相机需要按图元显示尺寸缩到 960/480、
   或 JPEG q70–80，才能满足 0.5Mbps/客户端。
3. **token 在 URL**：已加 `Referrer-Policy: no-referrer` 与短 TTL；代理/日志仍可能记录 URL，
   现场需确认反向代理不记录 query。
4. **无浏览器视觉验收**：B51 只验 PNG 字节与状态码；`<img>` 解码/布局/占位切换未测。
5. **`/img` 用 query 而非路径段**：`/img?elementId=…`；与早期设计稿的 `/img/{id}/{v}` 不同，
   客户端已按 query 实现。
6. **1fps/元素**：多图同页按元素各自计时；没有全局 60KB/s 令牌桶。

## 4. 未做（继续按优先级）

- DataGrid 行数据 / AxisPanel/AxisConsole 实时轴数据（需要 `VM.Scada` 数据 Hub）。
- `cmd.result`（Dedup 三态 + 客户端持久化 requestId）。
- 快照分片/resync/控制通道。
- 24 槽的 8 个握手预留槽。
- 默认值一致性闸。
- 浏览器/真实网络/重连风暴/长稳。
