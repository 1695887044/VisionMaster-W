using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using HslCommunication;
using HslCommunication.Core;
using HslCommunication.ModBus;
using HslCommunication.Profinet.Siemens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VirtualLineConsole
{
    /// <summary>
    /// 智能装配检测线的外置虚拟设备控制台。
    ///
    /// 职责：
    ///   · Modbus TCP 503：产线 PLC（触发 HR0、确认 HR1、启动/停止/判定线圈、良品/不良/节拍寄存器）
    ///   · Modbus TCP 504：压力/仪表站（IR0 温度、IR10 压力，CDAB）
    ///   · Siemens S7 1102：S7-1200 仿真（DB1.0 结果字、M0.0 心跳、M1.0 就绪）
    ///   · HTTP 19001：MES 收件口（ResultUpload 的 /api/upload），并统计节拍
    ///
    /// 用途：让方案里的通讯步骤、触发判断、结果上报、报表链路都能真跑，
    /// 同时给出写请求与上报间隔，做流程性能/组态流畅度观察。
    /// </summary>
    internal static class Program
    {
        private const int PlcPort = 503;
        private const int MeterPort = 504;
        private const int S7Port = 1102;
        private const int HttpPort = 19001;

        private static ModbusTcpServer _plc;
        private static ModbusTcpServer _meter;
        private static SiemensS7Server _s7;
        private static TcpListener _http;
        private static Timer _timer;
        private static int _tick;
        private static bool _auto = true;
        private static int _trigger;
        private static int _uploadCount;
        private static readonly object Sync = new object();
        private static readonly List<DateTime> UploadTimes = new List<DateTime>();
        private static readonly ManualResetEvent Quit = new ManualResetEvent(false);

        private static void Main()
        {
            try { Console.Title = "智能装配检测线 · 虚拟设备控制台"; } catch { }
            Console.OutputEncoding = Encoding.UTF8;
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; Quit.Set(); };

            Console.WriteLine("============================================================");
            Console.WriteLine(" 智能装配检测线 · 虚拟设备控制台");
            Console.WriteLine(" Modbus PLC   : 127.0.0.1:" + PlcPort + "  (HR0 触发 / HR1 确认 / 线圈 0/1/2)");
            Console.WriteLine(" Modbus Meter : 127.0.0.1:" + MeterPort + " (IR0 温度 / IR10 压力, CDAB)");
            Console.WriteLine(" S7-1200      : 127.0.0.1:" + S7Port + " (DB1.0 / M0.0 / M1.0)");
            Console.WriteLine(" MES HTTP     : http://127.0.0.1:" + HttpPort + "/api/upload");
            Console.WriteLine(" 命令: auto | manual | trigger | reset | status | q");
            Console.WriteLine("============================================================");

            if (!StartAll()) return;
            _timer = new Timer(OnTick, null, 500, 500);
            RunCommands();

            _timer?.Dispose();
            try { _plc?.ServerClose(); } catch { }
            try { _meter?.ServerClose(); } catch { }
            try { _s7?.ServerClose(); } catch { }
            try { _http?.Stop(); } catch { }
            Console.WriteLine("已全部关闭。");
        }

        private static bool StartAll()
        {
            _plc = new ModbusTcpServer { StationCheck = false };
            _meter = new ModbusTcpServer { StationCheck = false };
            _s7 = new SiemensS7Server();

            if (!TryBind(PlcPort)) { Console.WriteLine("[!!] 503 被占，请先关闭占用进程"); return false; }
            if (!TryBind(MeterPort)) { Console.WriteLine("[!!] 504 被占，请先关闭占用进程"); return false; }
            if (!TryBind(S7Port)) { Console.WriteLine("[!!] 1102 被占，请先关闭占用进程"); return false; }

            _plc.ServerStart(PlcPort);
            _meter.ServerStart(MeterPort);
            _s7.ServerStart(S7Port);

            try
            {
                _http = new TcpListener(IPAddress.Loopback, HttpPort);
                _http.Start();
            }
            catch (Exception ex) { Console.WriteLine("[!!] HTTP 19001 启动失败: " + ex.Message); return false; }
            ThreadPool.QueueUserWorkItem(_ => HttpLoop());

            Console.WriteLine("[OK] 三个设备服务与 MES 收件口已就绪。");
            return true;
        }

        private static bool TryBind(int port)
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    s.Bind(new IPEndPoint(IPAddress.Any, port));
                    return true;
                }
            }
            catch { return false; }
        }

        private static void OnTick(object state)
        {
            int tick = Interlocked.Increment(ref _tick);
            bool heartbeat = tick % 2 == 0;
            try
            {
                // PLC：自动模式每 2 秒给一个触发；手动模式由 trigger 命令给
                if (_auto && tick % 4 == 0) Interlocked.Increment(ref _trigger);
                _plc.Write("0", (ushort)Math.Min(_trigger, ushort.MaxValue)); // HR0
                _plc.Write("0", true);                                        // 线圈 0 = 启动
                _plc.Write("1", false);                                       // 线圈 1 = 停止

                float temp = (float)(25.0 + 6.0 * Math.Sin(tick * 2.0 * Math.PI / 120.0));
                float pressure = (float)(0.45 + 0.12 * Math.Sin(tick * 2.0 * Math.PI / 90.0));
                float torque = 1.45f + (float)(0.05 * Math.Sin(tick * 2.0 * Math.PI / 45.0));
                _meter.Write("x=4;0", temp);      // IR0 温度
                _meter.Write("x=4;10", pressure); // IR10 压力
                _meter.Write("x=4;20", torque);   // IR20 拧紧扭矩
                _meter.Write("4", (ushort)(tick % 1000)); // HR4 计数

                _s7.Write("DB1.0", (ushort)(_trigger & 0x7FFF));
                _s7.Write("M0", heartbeat ? (byte)1 : (byte)0);
                _s7.Write("M1", (byte)1);
            }
            catch { /* 单拍失败不影响服务 */ }
        }

        private static void RunCommands()
        {
            while (true)
            {
                if (Quit.WaitOne(0)) break;
                string line = Console.ReadLine();
                if (line == null) { Quit.WaitOne(); break; }
                line = line.Trim().ToLowerInvariant();
                if (line.Length == 0) continue;
                if (line == "q" || line == "quit" || line == "exit") break;
                switch (line)
                {
                    case "auto": _auto = true; Log("自动触发已开启（每 2 秒）"); break;
                    case "manual": _auto = false; Log("自动触发已关闭，使用 trigger 命令"); break;
                    case "trigger": Interlocked.Increment(ref _trigger); Log("手动触发 -> " + _trigger); break;
                    case "reset": _trigger = 0; ResetUploadStats(); Log("触发与统计已复位"); break;
                    case "status": PrintStatus(); break;
                    default: Console.WriteLine("支持: auto | manual | trigger | reset | status | q"); break;
                }
            }
        }

        private static void PrintStatus()
        {
            lock (Sync)
            {
                double avg = 0;
                if (UploadTimes.Count > 1)
                {
                    var span = UploadTimes.Last() - UploadTimes.First();
                    avg = span.TotalMilliseconds / Math.Max(1, UploadTimes.Count - 1);
                }
                Console.WriteLine("---- 状态 ----");
                Console.WriteLine("  自动模式 : " + (_auto ? "开" : "关"));
                Console.WriteLine("  触发计数 : " + _trigger);
                Console.WriteLine("  上报次数 : " + _uploadCount);
                Console.WriteLine("  平均节拍 : " + (avg > 0 ? avg.ToString("F0", CultureInfo.InvariantCulture) + " ms" : "—"));
                Console.WriteLine("  最近上报 : " + (UploadTimes.Count == 0 ? "—" : UploadTimes.Last().ToString("HH:mm:ss.fff")));
                Console.WriteLine();
            }
        }

        private static void ResetUploadStats()
        {
            lock (Sync)
            {
                _uploadCount = 0;
                UploadTimes.Clear();
            }
        }

        private static void HttpLoop()
        {
            while (true)
            {
                TcpClient client;
                try { client = _http.AcceptTcpClient(); }
                catch { break; }

                ThreadPool.QueueUserWorkItem(_ => HandleHttp(client));
            }
        }

        private static void HandleHttp(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 3000;
                    var stream = client.GetStream();
                    var buffer = new MemoryStream();
                    var chunk = new byte[4096];
                    int headerEnd = -1;
                    while (headerEnd < 0)
                    {
                        int read = stream.Read(chunk, 0, chunk.Length);
                        if (read <= 0) return;
                        buffer.Write(chunk, 0, read);
                        var bytes = buffer.GetBuffer();
                        headerEnd = FindHeaderEnd(bytes, (int)buffer.Length);
                    }

                    var all = buffer.ToArray();
                    string headerText = Encoding.ASCII.GetString(all, 0, headerEnd);
                    var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    var requestLine = lines.Length > 0 ? lines[0].Split(' ') : new string[0];
                    string path = requestLine.Length >= 2 ? requestLine[1] : "/";
                    int contentLength = 0;
                    for (int i = 1; i < lines.Length; i++)
                    {
                        if (lines[i].StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(lines[i].Substring("Content-Length:".Length).Trim(), out contentLength);
                    }

                    int bodyStart = headerEnd + 4;
                    var bodyBytes = new byte[contentLength];
                    int have = Math.Min(contentLength, Math.Max(0, all.Length - bodyStart));
                    if (have > 0) Array.Copy(all, bodyStart, bodyBytes, 0, have);
                    int got = have;
                    while (got < contentLength)
                    {
                        int read = stream.Read(bodyBytes, got, contentLength - got);
                        if (read <= 0) break;
                        got += read;
                    }

                    string body = contentLength > 0 ? Encoding.UTF8.GetString(bodyBytes, 0, got) : string.Empty;

                    if (string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteHttp(stream, 200, "{\"ok\":true,\"service\":\"VirtualLineConsole\"}");
                        return;
                    }

                    if (string.Equals(path, "/api/upload", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(path, "/api/inspect", StringComparison.OrdinalIgnoreCase))
                    {
                        int count;
                        double avg;
                        lock (Sync)
                        {
                            _uploadCount++;
                            UploadTimes.Add(DateTime.Now);
                            while (UploadTimes.Count > 200) UploadTimes.RemoveAt(0);
                            count = _uploadCount;
                            avg = UploadTimes.Count > 1
                                ? (UploadTimes.Last() - UploadTimes.First()).TotalMilliseconds / (UploadTimes.Count - 1)
                                : 0;
                        }

                        string summary = Summarize(body);
                        Console.WriteLine("{0:HH:mm:ss.fff} [MES] #{1} {2} 节拍≈{3}ms  {4}",
                            DateTime.Now, count, path, avg > 0 ? avg.ToString("F0", CultureInfo.InvariantCulture) : "—", summary);
                        WriteHttp(stream, 200, "{\"ok\":true,\"count\":" + count + "}");
                    }
                    else
                    {
                        WriteHttp(stream, 404, "{\"ok\":false,\"error\":\"not found\"}");
                    }
                }
                catch { /* 单个请求失败不影响服务 */ }
            }
        }

        private static int FindHeaderEnd(byte[] bytes, int length)
        {
            for (int i = 0; i + 3 < length; i++)
            {
                if (bytes[i] == 13 && bytes[i + 1] == 10 && bytes[i + 2] == 13 && bytes[i + 3] == 10)
                    return i;
            }
            return -1;
        }

        private static void WriteHttp(NetworkStream stream, int status, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            string reason = status == 200 ? "OK" : status == 404 ? "Not Found" : "Error";
            string head = "HTTP/1.1 " + status + " " + reason + "\r\n"
                        + "Content-Type: application/json; charset=utf-8\r\n"
                        + "Content-Length: " + bytes.Length + "\r\n"
                        + "Connection: close\r\n\r\n";
            var headBytes = Encoding.ASCII.GetBytes(head);
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static string Summarize(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "(empty)";
            try
            {
                var obj = JsonConvert.DeserializeObject<JObject>(body);
                if (obj == null) return body.Substring(0, Math.Min(120, body.Length));
                var keys = new[] { "sn", "result", "product", "defect", "yield", "time" };
                var parts = new List<string>();
                foreach (var k in keys)
                {
                    var v = obj[k];
                    if (v != null) parts.Add(k + "=" + v);
                }
                return parts.Count > 0 ? string.Join(" ", parts) : body.Substring(0, Math.Min(120, body.Length));
            }
            catch { return body.Substring(0, Math.Min(120, body.Length)); }
        }

        private static void WriteJson(HttpListenerContext ctx, int status, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        private static void Log(string text)
            => Console.WriteLine("{0:HH:mm:ss.fff} [CMD] {1}", DateTime.Now, text);
    }
}
