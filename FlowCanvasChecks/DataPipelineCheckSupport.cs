using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Services;
// 裸写 ExecutionContext 会与 System.Threading.ExecutionContext 撞名（两个宿主都启用了隐式 using）
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 数据链路断言（结果上报 / CSV 记录 / Excel 报表）的公共支撑。
    ///
    /// 为什么单独放一份：
    ///  - 三套断言都要"临时目录 + 真 HTTP 服务 + 等异步队列排空"，各写一遍必然漂移；
    ///  - 本地 HTTP 服务刻意用裸 TcpListener 而不是 HttpListener：后者在非管理员账户下
    ///    可能因 URL ACL 起不来，断言会红在"环境没权限"而不是被测代码上（R38 的反面）。
    /// </summary>
    internal static class DataPipelineCheckSupport
    {
        /// <summary>分组标题（两个宿主共用：FlowCanvasChecks 走常规 gate，DataPipelineChecks 走独立无宿主工程）。</summary>
        public static void Section(string title) => Program.Section(title);

        /// <summary>一条断言（两个宿主共用）。</summary>
        public static void Check(string name, bool ok, string detail) => Program.Check(name, ok, detail);

        /// <summary>
        /// 跑一个用例并兜住异常：用例自身抛异常 = 这条用例红，不拖垮后面的用例。
        /// 为什么必须兜：变异反证时被测代码会输出畸形数据，断言若直接索引就会崩进程，
        /// 后面的用例一条都跑不到（"红了"与"崩了"必须能分清）。
        /// </summary>
        public static void SafeRun(string name, Action action)
        {
            try { action(); }
            catch (Exception ex) { Check($"{name}：用例未抛异常", false, $"{ex.GetType().Name}: {ex.Message}"); }
        }

        /// <summary>按行列取值（越界返回空串，供断言在畸形数据上给出可读失败而不是崩）。</summary>
        public static string Cell(IReadOnlyList<List<string>> rows, int row, int col)
            => row >= 0 && row < rows.Count && col >= 0 && col < rows[row].Count ? rows[row][col] : "";

        /// <summary>建一个独立临时目录（每个用例一份，互不污染）。</summary>
        public static string TempDir(string name)
        {
            string dir = Path.Combine(
                Path.GetTempPath(),
                "VMChecks",
                name + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>尽力删除临时目录（失败不抛：断言结果比清理更重要）。</summary>
        public static void DeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { }
        }

        /// <summary>轮询等待条件成立；返回 false = 超时（调用方必须把它断言成红，不许静默跳过）。</summary>
        public static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000)
        {
            long deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                if (condition()) return true;
                Thread.Sleep(20);
            }
            return condition();
        }

        /// <summary>
        /// 数据链路断言用的执行上下文（带会话/工作区，插件里的 context.LocalVariables 可用）。
        /// 返回接口而不是具体类：同一份检查源码要被 FlowCanvasChecks 与 DataPipelineChecks 两个宿主共用。
        /// </summary>
        public static IExecutionContext NewContext(StubLog log)
            => new ExecutionContext(log, new FlowSession { FlowName = "数据链路断言" }, new WorkspaceContext(), CancellationToken.None);

        /// <summary>
        /// 最小 CSV 解析（RFC 4180：逗号分隔、双引号包裹、内部引号翻倍、引号内可含换行）。
        /// 断言 CSV 列序/转义必须用它——按行 Split 会被"字段里有换行"直接骗过去。
        /// </summary>
        public static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool inQuotes = false;

            text = text.TrimStart('\uFEFF');
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else cell.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
                    else if (c == '\r') { /* 等 \n；单独 \r 不产生新行 */ }
                    else if (c == '\n')
                    {
                        row.Add(cell.ToString());
                        cell.Clear();
                        rows.Add(row);
                        row = new List<string>();
                    }
                    else cell.Append(c);
                }
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>读 UTF-8 文本（带不带 BOM 都按同一口径解码）。</summary>
        public static string ReadUtf8(string path)
            => File.ReadAllText(path, new UTF8Encoding(false));

        /// <summary>文件是否以 UTF-8 BOM 开头（CSV 记录插件的 Excel 友好契约）。</summary>
        public static bool HasUtf8Bom(string path)
        {
            using var fs = File.OpenRead(path);
            return fs.ReadByte() == 0xEF && fs.ReadByte() == 0xBB && fs.ReadByte() == 0xBF;
        }
    }

    /// <summary>测试用步骤数据：字典兜底的 IStepConfigData 最小实现（与 CreateRoiChecks 同款）。</summary>
    internal sealed class CheckStepData : IStepConfigData
    {
        public Guid StepId { get; } = Guid.NewGuid();
        public string Icon { get; } = "";
        public string StepName { get; set; } = "Check";
        public string Description { get; } = "";
        public Dictionary<string, object> InputValues { get; } = new();
        public void SetInputValue(string key, object value) => InputValues[key] = value;
        public void RemoveInputValue(string key) => InputValues.Remove(key);
        public bool IsLinked(string inputPortName) => false;
        public string GetLinkedAddress(string inputPortName) => null;
        public LinkReference GetLink(string inputPortName) => null;
        public void SetLink(string inputPortName, LinkReference link) { }
        public void RemoveLink(string inputPortName) { }
        public List<DynamicPortInfo> OutputPortDefinitions { get; set; } = new();
    }

    /// <summary>一次 HTTP 请求（断言可读：方法/路径/头/体）。</summary>
    internal sealed class LocalHttpRequest
    {
        public string Method = "";
        public string Path = "";
        public Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);
        public string Body = "";

        public string Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;
    }

    /// <summary>一次 HTTP 响应（状态码 + 体 + 可选延迟）。</summary>
    internal sealed class LocalHttpResponse
    {
        public int StatusCode = 200;
        public string Body = "";
        public int DelayMs = 0;

        public static LocalHttpResponse Ok(string body = "OK") => new() { StatusCode = 200, Body = body };
        public static LocalHttpResponse Status(int code, string body = "") => new() { StatusCode = code, Body = body };
    }

    /// <summary>
    /// 裸 TCP 的极简 HTTP/1.1 服务：只支持 POST + Content-Length + Connection: close。
    /// 足够验"URL/头/体/状态码/重试/幂等 ID"，又不受 HttpListener 的 URL ACL 限制。
    /// </summary>
    internal sealed class LocalHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Thread _acceptThread;
        private readonly Func<LocalHttpRequest, LocalHttpResponse> _handler;
        private readonly object _requestsLock = new();
        private readonly List<LocalHttpRequest> _requests = new();
        private volatile bool _stopping;

        public string Url { get; }
        public int Port { get; }

        public IReadOnlyList<LocalHttpRequest> Requests
        {
            get { lock (_requestsLock) return _requests.ToList(); }
        }

        public int RequestCount
        {
            get { lock (_requestsLock) return _requests.Count; }
        }

        public LocalHttpServer(Func<LocalHttpRequest, LocalHttpResponse> handler)
        {
            _handler = handler ?? (_ => LocalHttpResponse.Ok());

            // 先占 0 端口拿到系统分配的空闲端口，再交给 TcpListener 正式监听
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            Url = $"http://127.0.0.1:{Port}/upload";

            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "DataPipelineCheck.Http" };
            _acceptThread.Start();
        }

        private void AcceptLoop()
        {
            while (!_stopping)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch (SocketException) { break; }
                catch (ObjectDisposedException) { break; }

                try { HandleClient(client); }
                catch { /* 断言服务器自身不因单个连接异常退出 */ }
                finally { try { client.Close(); } catch { } }
            }
        }

        private void HandleClient(TcpClient client)
        {
            using var stream = client.GetStream();
            var request = ReadRequest(stream);
            if (request == null) return;

            lock (_requestsLock) _requests.Add(request);

            var response = _handler(request) ?? LocalHttpResponse.Ok();
            if (response.DelayMs > 0) Thread.Sleep(response.DelayMs);

            byte[] body = Encoding.UTF8.GetBytes(response.Body ?? "");
            string head =
                $"HTTP/1.1 {response.StatusCode} {Reason(response.StatusCode)}\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n\r\n";
            byte[] headBytes = Encoding.ASCII.GetBytes(head);
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        private static LocalHttpRequest ReadRequest(NetworkStream stream)
        {
            // 逐字节读头，直到 \r\n\r\n（请求头很小，不追求吞吐）
            var head = new StringBuilder();
            int state = 0;
            while (state < 4)
            {
                int b = stream.ReadByte();
                if (b < 0) return null;
                char c = (char)b;
                head.Append(c);
                state = (c, state) switch
                {
                    ('\r', 0) => 1,
                    ('\n', 1) => 2,
                    ('\r', 2) => 3,
                    ('\n', 3) => 4,
                    ('\r', _) => 1,
                    ('\n', _) => 2,
                    _ => 0,
                };
            }

            string[] lines = head.ToString().Split("\r\n", StringSplitOptions.None);
            if (lines.Length == 0) return null;
            string[] first = lines[0].Split(' ');
            var req = new LocalHttpRequest
            {
                Method = first.Length > 0 ? first[0] : "",
                Path = first.Length > 1 ? first[1] : "",
            };

            int contentLength = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                string key = lines[i].Substring(0, colon).Trim();
                string value = lines[i].Substring(colon + 1).Trim();
                req.Headers[key] = value;
                if (string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(value, out int len))
                    contentLength = len;
            }

            // 100-continue：HttpClient 对大请求会先等这个
            if (req.Headers.TryGetValue("Expect", out var expect) &&
                expect.IndexOf("100-continue", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                byte[] cont = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
                stream.Write(cont, 0, cont.Length);
                stream.Flush();
            }

            if (contentLength > 0)
            {
                var body = new byte[contentLength];
                int read = 0;
                while (read < contentLength)
                {
                    int n = stream.Read(body, read, contentLength - read);
                    if (n <= 0) break;
                    read += n;
                }
                req.Body = Encoding.UTF8.GetString(body, 0, read);
            }
            return req;
        }

        private static string Reason(int code) => code switch
        {
            200 => "OK",
            201 => "Created",
            204 => "No Content",
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            409 => "Conflict",
            500 => "Internal Server Error",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            _ => "Status",
        };

        public void Dispose()
        {
            _stopping = true;
            try { _listener.Stop(); } catch { }
            try { _acceptThread.Join(1000); } catch { }
        }
    }
}
