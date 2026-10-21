# _WebProbe2 — Watson 7.2.0 WebSocket 路由与鉴权探针（M0）

隔离实验目录：只做验证，不改任何生产代码/工程/文档。可随时整目录删除。

- 目标 DLL：`DLL\Watson\WatsonWebserver.dll`（**7.2.0.0**，`WatsonWebserver, Version=7.2.0.0, Culture=neutral, PublicKeyToken=null`）
- 依赖：`IpMatcher.dll` / `RegexMatcher.dll` / `Timestamps.dll` / `UrlMatcher.dll`（同目录，`<Reference>` + `HintPath` + `Private=true`）
- 探针工程：`net9.0` 控制台，SDK 9.0.318，运行时 9.0.20
- 监听：`127.0.0.1:18080`（占用时自动退到 18081/18082；默认配置专项探针用 18090）

## 0. 结论摘要（给 M0 一句话）

**Watson 7.2.0 可以承载"带鉴权的 WebSocket"，不需要换 BCL `HttpListener` + `System.Net.WebSockets`。**
但有 **3 个静默失效点**必须在生产里显式堵住（它们都不抛异常、不报错，只是"鉴权没生效/连不上"）：

| # | 静默失效点 | 实测表现 | 必须做的事 |
| --- | --- | --- | --- |
| 1 | `WebserverSettings.WebSockets.Enable` **默认 `false`** | 握手返回 **400**（`WebSocketException: The server returned status code '400' when status code '101' was expected`），路由注册与 `Start()` 全绿 | 显式 `settings.WebSockets.Enable = true` |
| 2 | `WebServer.WebSocket(path, handler)` 的 `auth` 参数**默认 `false`** | 连接完全不经过 `AuthenticateApiRequest`（`authCalls +0`），无论带不带 token | 所有业务 WS 路由显式写 `auth: true` |
| 3 | `auth:true` 但 `Routes.AuthenticateApiRequest == null` → **fail-open** | 未配置鉴权器时 `auth:true` 端点**照常连上并收到 hello** | 启动时断言 `AuthenticateApiRequest != null`（或统一在宿主初始化里赋值后再注册路由） |

## 1. 实际 API 签名（反射导出，`dotnet run -- --dump`）

> XML 文档只覆盖一部分成员；以下为**运行时反射**得到的真实签名（含默认参数），`dump.txt` 是完整导出。

```text
WatsonWebserver.Core.WebserverBase
  .ctor(WebserverSettings settings, Func<HttpContextBase, Task> defaultRoute)
  .ctor(String hostname, Int32 port, Boolean ssl, Func<HttpContextBase, Task> defaultRoute)
  Void  WebSocket(String path, Func<HttpContextBase, WebSocketSession, Task> handler, Boolean auth = False)
  Void  Get(String path, Func<ApiRequest, Task<Object>> handler, Boolean auth = False)
  Void  Start(CancellationToken token = null)
  Void  Stop()
  prop  Boolean IsListening { get; }
  prop  WebserverRoutes Routes { get; set; }
  prop  WebserverSettings Settings { get; set; }
  IEnumerable<WebSocketSession> ListWebSocketSessions()
  Boolean IsWebSocketSessionConnected(Guid guid)
  Task<Boolean> DisconnectWebSocketSessionAsync(Guid guid, WebSocketCloseStatus status, String reason)

WatsonWebserver.Core.Routing.WebserverRoutes
  prop Func<HttpContextBase, Task<AuthResult>> AuthenticateApiRequest { get; set; }   // 结构化鉴权（优先）
  prop Func<HttpContextBase, Task>            AuthenticateRequest    { get; set; }   // 旧式（无返回值）
  prop RoutingGroup PreAuthentication  { get; set; }
  prop RoutingGroup PostAuthentication { get; set; }
  prop Func<HttpContextBase, Task> Default / PreRouting / PostRouting / Preflight
  prop Func<HttpContextBase, Exception, Task> Exception

WatsonWebserver.Core.Routing.RoutingGroup
  prop StaticRouteManager    Static    { get; set; }
  prop ParameterRouteManager Parameter { get; set; }
  prop DynamicRouteManager   Dynamic   { get; set; }
  prop ContentRouteManager   Content   { get; set; }
  prop WebSocketRouteManager WebSockets{ get; set; }

WatsonWebserver.Core.Routing.StaticRouteManager   // ← 原始字节路由的唯一入口
  Void Add(HttpMethod method, String path, Func<HttpContextBase, Task> handler,
           Func<HttpContextBase, Exception, Task> exceptionHandler = null, Guid guid = null,
           Object metadata = null, OpenApiRouteMetadata openApiMetadata = null)

WatsonWebserver.Core.AuthResult
  prop AuthenticationResultEnum  AuthenticationResult  { get; set; }
  prop AuthorizationResultEnum   AuthorizationResult   { get; set; }
  prop Object                    Metadata              { get; set; }
  Boolean IsPermitted()

  enum AuthenticationResultEnum  { Success, NotFound, Expired, PermissionDenied, Invalid }
  enum AuthorizationResultEnum   { Permitted, DeniedImplicit, DeniedExplicit }

WatsonWebserver.Core.WebSockets.WebSocketSession
  prop Guid   Id; prop Boolean IsConnected; prop WebSocketState State
  prop String RemoteIp; prop Int32 RemotePort
  prop WebSocketRequestDescriptor Request; prop WebSocketSessionStatistics Statistics; prop Object Metadata
  Task SendTextAsync(String data, CancellationToken token = null)
  Task SendBinaryAsync(Byte[] data, CancellationToken token = null)
  Task<WebSocketMessage> ReceiveAsync(CancellationToken token = null)
  IAsyncEnumerable<WebSocketMessage> ReadMessagesAsync(CancellationToken token = null)
  Task CloseAsync(WebSocketCloseStatus closeStatus, String reason, CancellationToken token = null)

WatsonWebserver.Core.Settings.WebSocketSettings
  prop Boolean Enable                 { get; set; }   // ★ 默认 false
  prop Boolean EnableHttp1/EnableHttp2/EnableHttp3
  prop Int32   MaxMessageSize         { get; set; }   // 默认 16777216 (16 MiB)
  prop Int32   ReceiveBufferSize      { get; set; }   // 默认 65536
  prop Boolean AllowClientSuppliedGuid{ get; set; }   // 默认 false
  prop Int32   CloseHandshakeTimeoutMs{ get; set; }
  prop List<String> SupportedVersions { get; set; }
```

`HttpMethod` 是 `WatsonWebserver.Core.HttpMethod` **枚举**（成员大写：`GET / HEAD / PUT / POST / DELETE / PATCH / CONNECT / OPTIONS / TRACE / UNKNOWN`），
与 `System.Net.Http.HttpMethod` **同名冲突**——同时 `using System.Net.Http;` 时必须写全 `WatsonWebserver.Core.HttpMethod.GET`。

`WebserverBase.Get/Post/Put/...` **只有** `Func<ApiRequest, Task<object>>` 一种重载（没有 `Func<HttpContextBase, Task>` 的 HTTP 重载）；
要"原样吐字节"必须下沉到 `Routes.<Pre|Post>Authentication.Static.Add(...)`。

## 2. 六条实验结果

主日志：`run_final.txt`（`dotnet run` 全量输出）；默认配置专项：`run_default_ws.txt`；API 导出：`dump.txt`。

| # | 验证项 | 结果 | 关键证据 |
| --- | --- | --- | --- |
| **A** | `auth:true` 的 WS 连接会触发 `AuthenticateApiRequest` | **PASS** | 带 `Bearer test` → `authCalls +1`；不带 token → `authCalls +1`。**每次握手恰好 1 次**，且传入的是升级前的 `GET /ws-auth` 请求 |
| **B** | `auth:false` 的 WS 连接**不**触发鉴权（默认漏洞） | **PASS（漏洞确认）** | 不带 token `+0`、带 token `+0`，两次都直接连上收到 `hello` |
| **C** | 鉴权成功 → `auth:true` 端点完成握手并收到 `hello` | **PASS** | `A1`/`A2` 均 `connected, recv="hello"`；服务端 handler 内 `RetrieveHeaderValue("Authorization")` 读到 `Bearer test` |
| **D** | 鉴权失败 → `auth:true` 握手被拒；`auth:false` 仍可连 | **PASS** | 客户端异常原文见 §3；同时 `auth:false` 在"鉴权器返回拒绝"状态下照常 `connected, recv="hello"`（`authCalls +0`） |
| **E** | 内嵌字节数组能原样返回 | **PASS** | `GET /asset`（`PreAuthentication.Static`）→ `status=200 len=1024 contentType=application/octet-stream`，`bytes.SequenceEqual(源数组)==True` |
| **F** | `WebSocketSession.SendTextAsync` 是否允许并发 | **PASS（允许，内部已串行化）** | 4 条独立 OS 线程（`TaskCreationOptions.LongRunning`）× 250 条 = **1000 条全收到、服务端 0 异常**，耗时 9~15 ms；`WebSocketSession` 私有字段含 **`SemaphoreSlim _SendLock`** |

补充结论：**E 的两种供给方式差别很大**（见 §4），`F` 的"允许并发"是**库内 `SemaphoreSlim` 串行化**的结果，不是无锁。

## 3. 异常原文（逐字）

**D：`auth:true` + 鉴权器返回 `NotFound / DeniedImplicit`，客户端侧**

```text
System.Net.WebSockets.WebSocketException: The server returned status code '401' when status code '101' was expected.
  | WebSocketErrorCode=NotAWebSocket
```

**D：同一次握手的服务端响应体（用 `HttpClient` 手发 Upgrade 头抓的原文）**

```text
status=401  authCalls +1
body={"Error":"NotAuthorized","StatusCode":401,"Description":"Authentication is required or has failed."}
```

即：拒绝时 Watson **自动**写 401 + 结构化 `ApiErrorResponse`，宿主不需要自己拼响应。

**专项：`WebSockets.Enable` 保持默认 `false`（`dotnet run -- --default-ws`）**

```text
[default-ws] WebSockets.Enable = False
[default-ws] listening http://127.0.0.1:18090  (IsListening=True)
[default-ws] RESULT: connect FAILED:
  System.Net.WebSockets.WebSocketException: The server returned status code '400' when status code '101' was expected.
  | WebSocketErrorCode=NotAWebSocket
```

**专项：`auth:true` 但 `AuthenticateApiRequest = null`（fail-open）**

```text
[CLIENT] auth:true, AuthenticateApiRequest = null -> connected, recv="hello"
```

## 4. 静态资源怎么供（E 的完整口径）

| 供给方式 | 实测结果 | 适用 |
| --- | --- | --- |
| `Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/asset", Func<HttpContextBase,Task>)` | `200 / 1024 B / 原样字节 / authCalls +0` | 公开资源（登录页、图标、健康检查） |
| `Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/asset-auth", ...)` | 鉴权拒绝 → `401 / 100 B`；鉴权通过 → `200 / 1024 B / 原样字节 / authCalls +1` | **需要鉴权的静态资源（推荐）** |
| `WebserverBase.Get("/asset-api", Func<ApiRequest,Task<object>>)` 返回 `byte[]` | `200` 但 body 是 **JSON 字符串（base64）**：`"Vk0tQVNTRVQtQkVHSU5vdn2..."`，**1370 B**（源 1024 B），`rawMatch=False` | **不要用**：该重载必然走 `ISerializationHelper.SerializeJson`，供二进制会多一层 base64 + 引号 |

结论：**二进制/EmbeddedResource 一律走 `Static` 路由管理器**，不要走 `Get/Post` 的 `ApiRequest` 重载。

## 5. 对 M0 的结论

1. **Watson 可用于带鉴权的 WebSocket，不必换 BCL。** `auth:true` + `Routes.AuthenticateApiRequest` 在**握手阶段**就把未授权连接挡在 101 之外（401 + 结构化错误体），业务 handler 根本不会被调用（`D1` 日志里没有 `[WS] handler START`）。
2. **鉴权粒度是"整条路由"，不是"每帧消息"。** 连接建立后不再回调鉴权器（`A1` 只 `+1` 一次），所以"运行中吊销 token"需要宿主自己调 `DisconnectWebSocketSessionAsync` 或让 handler 自行校验。
3. **handler 内仍能拿到握手头**（`ctx.Request.RetrieveHeaderValue("Authorization")` 实测读到 `Bearer test`），需要"连接绑定用户身份"时可用 `WebSocketSession.Metadata` 存。
4. **三个静默失效点（§0）是 M0 的主要风险**，全部属于"编译期全绿、运行期不报错"型；建议做成启动断言而不是代码评审约定。
5. **不需要每连接单发送循环**：`SendTextAsync` 内部有 `SemaphoreSlim _SendLock`，4 线程并发 1000 条零丢失零异常。真正需要"单循环"的是**接收侧**（`_ReceiveState` 表明接收有互斥），handler 里只保留一个 `ReceiveAsync` 循环即可。

## 6. 复现

```bat
cd /d "D:\C#\VM\_WebProbe2"
dotnet build
dotnet run                      :: 主实验 A-F（127.0.0.1:18080）
dotnet run -- --default-ws      :: 专项：WebSockets.Enable 保持默认 false（:18090）
dotnet run -- --dump            :: 导出真实 API 签名
```

判据看末行 `RESULT: 9 PASS / 0 FAIL`；任一 FAIL 会以 `[FAIL] <id> ...` 列出并让进程退出码为 1。

## 7. 目录内容

| 文件 | 说明 |
| --- | --- |
| `WebProbe2.csproj` | `net9.0` 控制台，5 个 `<Reference>`（`Private=true`），无 NuGet 依赖 |
| `Program.cs` | 全部实验逻辑（服务端 + `ClientWebSocket` 客户端 + 反射导出） |
| `dump.txt` | `--dump` 的完整输出（API 签名证据） |
| `run_final.txt` | 主实验最终日志（9 PASS / 0 FAIL，两次运行结果一致） |
| `run_default_ws.txt` | `WebSockets.Enable=false` 专项日志（400） |
