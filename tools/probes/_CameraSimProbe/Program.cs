using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using Core.Interfaces;
using Plugin.Camera.Sim;

namespace CameraSimProbe;

/// <summary>
/// 一次性探针：用**真实相机驱动** SimCameraDevice 连**真实仿真器** LineSim，
/// 跑通 P1 出口判据（打开 / 采流 / 软触发即渲染 / 取帧 / 像素与真值 / 曝光生效）。
/// 用完即删，不进 sln、不进 gate。
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static void Check(string name, bool ok, string detail)
    {
        if (ok) { _pass++; Console.WriteLine($"[ok] {name} — {detail}"); }
        else { _fail++; Console.WriteLine($"[FAIL] {name} — {detail}"); }
    }

    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);

        // 只认 --linesim <path>；其它参数（run-probe.ps1 会塞 --nologo 之类）一律忽略
        string? explicitExe = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--linesim")
            {
                explicitExe = args[i + 1];
            }
        }

        string exe = explicitExe ?? FindLineSim();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            Console.WriteLine("[FAIL] 找不到 LineSim.exe。仿真器是独立产品，不在本仓库内，请先构建它：");
            Console.WriteLine(@"        cd D:\C#\LineSim && dotnet build LineSim.sln -c Debug -m:1 -nodeReuse:false");
            Console.WriteLine("       然后二选一：--linesim <exe 路径>，或设环境变量 LINESIM_EXE=<exe 路径>");
            return 2;
        }

        int cameraPort = FreePort();
        int motionPort = FreePort();
        Console.WriteLine($"仿真器：{exe}");
        Console.WriteLine($"端口：相机 {cameraPort} / 运控 {motionPort}");

        using Process sim = Process.Start(new ProcessStartInfo(
            exe,
            $"--serve --camera-port {cameraPort} --motion-port {motionPort} --trace")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        })!;
        sim.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine($"  [sim] {e.Data}"); };
        sim.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine($"  [sim!] {e.Data}"); };
        sim.BeginOutputReadLine();
        sim.BeginErrorReadLine();

        try
        {
            if (!WaitPort(cameraPort, 10000))
            {
                Console.WriteLine("[FAIL] 仿真器相机端口未在 10s 内就绪");
                return 2;
            }

            Environment.SetEnvironmentVariable(SimCameraDevice.EndpointEnvVar, $"127.0.0.1:{cameraPort}");

            var descriptor = new CameraDescriptor
            {
                Id = Guid.NewGuid(),
                SerialNo = "CAM1",
                DisplayName = "仿真 CAM1",
                DriverTypeKey = typeof(SimCameraDevice).AssemblyQualifiedName!,
            };
            descriptor.Settings = CameraSettings.CreateNew();
            descriptor.Settings.TriggerMode = CameraTriggerMode.Software;
            descriptor.Settings.TriggerSchemaVersion = CameraSettings.CurrentTriggerSchemaVersion;
            descriptor.Settings.ExposureTimeUs = 10000;

            var device = new SimCameraDevice(descriptor);

            // ── 1. 打开 ────────────────────────────────────────────────
            bool opened = device.Open();
            Check("打开相机", opened, $"state={device.State} detail={device.StateDetail}");
            Check("打开后在线", device.State == CameraConnectionState.Online, $"state={device.State}");
            Check("支持软件触发", device.SupportsSoftwareTrigger, "设计 §9.1 要求恒为 true");

            // ── 2. 采流 ────────────────────────────────────────────────
            bool streaming = device.StartStream();
            Check("开始采流", streaming, $"state={device.State}");
            Check("采流后状态为 Streaming", device.State == CameraConnectionState.Streaming, $"state={device.State}");

            // 反向：未触发时必须取不到帧（证明帧不是"随便给一张"）
            bool premature = device.WaitNextFrame(out _, 400, CancellationToken.None);
            Check("未触发时取帧超时（反向）", !premature, premature ? "未触发就拿到了帧 → 触发语义是假的" : "400ms 内无帧，符合预期");

            // ── 3. 软触发即渲染 ────────────────────────────────────────
            bool triggered = device.TriggerSoftware();
            Check("软触发被受理", triggered, $"detail={device.StateDetail}");

            bool got = device.WaitNextFrame(out CameraFrame frame1, 3000, CancellationToken.None);
            Check("触发后取到帧", got, got ? $"frameId={frame1.FrameId}" : "3s 内无帧");

            if (!got)
            {
                return Finish(device, sim);
            }

            Check("帧尺寸 1280×1024×1",
                frame1.Width == 1280 && frame1.Height == 1024 && frame1.Channels == 1,
                $"{frame1.Width}×{frame1.Height}×{frame1.Channels}");
            Check("帧像素长度 = w×h×ch",
                frame1.PixelData.Length == frame1.Width * frame1.Height * frame1.Channels,
                $"{frame1.PixelData.Length} 字节");
            Check("帧身份由设备回填",
                frame1.CameraId == descriptor.Id && frame1.SerialNo == "CAM1" && frame1.FrameId > 0,
                $"id={frame1.CameraId} serial={frame1.SerialNo} frameId={frame1.FrameId}");
            Check("帧 SourceName 只是文件名（不含真值）",
                !string.IsNullOrWhiteSpace(frame1.SourceName)
                && frame1.SourceName.StartsWith("linesim-CAM1-", StringComparison.Ordinal)
                && !frame1.SourceName.Contains("tick", StringComparison.OrdinalIgnoreCase),
                frame1.SourceName);

            (int min1, int max1, double mean1) = Stats(frame1.PixelData);
            Check("图像不是纯色（有背景梯度与工件）", max1 - min1 > 30, $"min={min1} max={max1} mean={mean1:F1}");

            // ── 4. 帧号递增 ────────────────────────────────────────────
            device.TriggerSoftware();
            bool got2 = device.WaitNextFrame(out CameraFrame frame2, 3000, CancellationToken.None);
            Check("第二次触发再取一帧", got2, got2 ? $"frameId={frame2.FrameId}" : "3s 内无帧");
            if (got2)
            {
                Check("帧号递增", frame2.FrameId == frame1.FrameId + 1, $"{frame1.FrameId} → {frame2.FrameId}");
            }

            // ── 5. 曝光下发真的生效 ────────────────────────────────────
            CameraSettings dark = device.ReadSettings();
            dark.ExposureTimeUs = 2000;
            bool appliedDark = device.ApplySettings(dark);
            device.TriggerSoftware();
            bool gotDark = device.WaitNextFrame(out CameraFrame frameDark, 3000, CancellationToken.None);

            CameraSettings bright = device.ReadSettings();
            bright.ExposureTimeUs = 50000;
            bool appliedBright = device.ApplySettings(bright);
            device.TriggerSoftware();
            bool gotBright = device.WaitNextFrame(out CameraFrame frameBright, 3000, CancellationToken.None);

            if (gotDark && gotBright)
            {
                double meanDark = Stats(frameDark.PixelData).Mean;
                double meanBright = Stats(frameBright.PixelData).Mean;
                Check("曝光下发被受理", appliedDark && appliedBright, $"2000us → {meanDark:F1}；50000us → {meanBright:F1}");
                Check("高曝光明显更亮（曝光真的参与渲染）",
                    meanBright > meanDark * 1.5,
                    $"均值 {meanDark:F1} → {meanBright:F1}（期望 ≥1.5 倍）");
            }
            else
            {
                Check("曝光对照取帧", false, "低/高曝光有一路没取到帧");
            }

            // ── 6. 仿真器侧计数与驱动侧一致 ────────────────────────────
            long received = device.ReceivedFrameCount;
            Check("驱动收到的帧数 = 成功取帧次数", received == 4, $"received={received}");

            string health = HttpGet(cameraPort, "/health");
            using (JsonDocument doc = JsonDocument.Parse(health))
            {
                JsonElement cam = doc.RootElement.GetProperty("cameras")[0];
                long triggers = cam.GetProperty("triggers").GetInt64();
                Check("仿真器侧触发计数 = 4", triggers == 4, $"triggers={triggers} body={Trim(health)}");
                Check("仿真器侧无丢帧", cam.GetProperty("dropped").GetInt64() == 0,
                    $"dropped={cam.GetProperty("dropped").GetInt64()}");
            }

            return Finish(device, sim);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 探针异常：{ex.GetType().Name}: {ex.Message}");
            _fail++;
            return 1;
        }
        finally
        {
            try
            {
                if (!sim.HasExited)
                {
                    sim.Kill(entireProcessTree: true);
                    sim.WaitForExit(3000);
                }
            }
            catch (InvalidOperationException)
            {
                // 已退出
            }
        }
    }

    private static int Finish(SimCameraDevice device, Process sim)
    {
        try { device.StopStream(); } catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { }
        try { device.Close(); } catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { }
        try { device.Dispose(); } catch (Exception ex) when (ex is ObjectDisposedException) { }

        Console.WriteLine();
        Console.WriteLine(_fail == 0
            ? $"RESULT: ALL_OK（{_pass} 条通过）"
            : $"RESULT: {_fail} FAILED（{_pass} 通过 / {_fail} 失败）");
        _ = sim;
        return _fail == 0 ? 0 : 1;
    }

    private static (int Min, int Max, double Mean) Stats(byte[] pixels)
    {
        int min = 255;
        int max = 0;
        long sum = 0;
        foreach (byte value in pixels)
        {
            if (value < min) min = value;
            if (value > max) max = value;
            sum += value;
        }

        return (min, max, pixels.Length == 0 ? 0 : (double)sum / pixels.Length);
    }

    private static string HttpGet(int port, string path)
    {
        using var client = new TcpClient();
        client.Connect("127.0.0.1", port);
        using NetworkStream stream = client.GetStream();
        byte[] request = Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
        stream.Write(request, 0, request.Length);
        stream.Flush();

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        string text = Encoding.UTF8.GetString(buffer.ToArray());
        int split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        return split < 0 ? text : text[(split + 4)..];
    }

    private static bool WaitPort(int port, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            try
            {
                using var client = new TcpClient();
                client.Connect("127.0.0.1", port);
                return true;
            }
            catch (SocketException)
            {
                Thread.Sleep(100);
            }
        }

        return false;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// 仿真器是独立产品（不在本仓库内）。查找顺序：
    /// 1) 环境变量 LINESIM_EXE；
    /// 2) 兄弟目录约定 D:\C#\LineSim\src\bin\Debug\net9.0-windows\LineSim.exe。
    /// 找不到返回空串，由调用方报错——绝不猜一个仓库内路径。
    /// </summary>
    private static string FindLineSim()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("LINESIM_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return Path.GetFullPath(fromEnv);
        }

        foreach (string candidate in ExternalCandidates())
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return string.Empty;
    }

    private static IEnumerable<string> ExternalCandidates()
    {
        const string relative = @"src\bin\Debug\net9.0-windows\LineSim.exe";
        yield return Path.Combine(@"D:\C#\LineSim", relative);

        string root = Directory.GetCurrentDirectory();
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(root); i++)
        {
            yield return Path.Combine(root, "LineSim", relative);
            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar)) ?? string.Empty;
        }
    }

    private static string Trim(string text) => text.Length <= 200 ? text : text[..200] + "…";
}
