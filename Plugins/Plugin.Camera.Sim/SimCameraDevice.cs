using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

using Core.Interfaces;

namespace Plugin.Camera.Sim;

/// <summary>
/// 产线仿真相机驱动（设计 §9.1）。
///
/// 定位：把 LineSim 的"解析合成成像"接进上位机的采集链路。与
/// <see cref="Plugin.Camera.Network.NetworkCameraDevice"/> 的区别是**取图方向相反**：
/// 网络相机是"外部推、宿主收"，本驱动是"宿主拉、仿真器出"——
/// 因此 <see cref="SupportsSoftwareTrigger"/> 必须为 true：触发那一刻仿真器才渲染，
/// 图像与轴位置严格同 tick，这是整个方案第一条不可让步约束的可执行形态。
///
/// 本类只写"仿真相机与真机的差异点"，环形队列、状态机、心跳看门狗、帧号与身份回填
/// 全部由 <see cref="CameraDeviceBase"/> 固化。差异点三处：
///   1) 没有物理句柄——"连接"的实质是一次 <c>GET /health</c> 握手 + 心跳保活；
///   2) 帧不是设备主动推的，而是本驱动读 <c>GET /camera/{serial}/stream</c> 长连接解出来的；
///   3) 曝光/增益要显式下发给仿真器（<c>POST /camera/{serial}/config</c>），否则改了也白改。
/// </summary>
[Display(
    Name = "产线仿真相机",
    GroupName = "相机",
    Description = "连到 LineSim 产线仿真器取图；支持软件触发，触发瞬间按世界状态解析合成成像",
    ShortName = "\uf1c5"
)]
public sealed class SimCameraDevice : CameraDeviceBase
{
    /// <summary>仿真器默认地址（设计 §8.1 端口表的唯一登记处）。</summary>
    public const string DefaultHost = "127.0.0.1";

    /// <summary>仿真器相机流默认端口。</summary>
    public const int DefaultPort = 19500;

    /// <summary>覆盖端点用的环境变量，取值 <c>host:port</c>（设计 §9.1「端点来源」）。</summary>
    public const string EndpointEnvVar = "VM_LINESIM_CAM";

    private const int ProtoVersion = 1;

    private readonly object _ioGate = new();

    private HttpClient? _http;
    private CancellationTokenSource? _cts;
    private Task? _heartbeatTask;
    private Task? _streamTask;
    private double _exposureUs;
    private double _gain;

    public SimCameraDevice(CameraDescriptor descriptor) : base(descriptor)
    {
        CameraSettings settings = ReadSettings();
        _exposureUs = settings.ExposureTimeUs;
        _gain = settings.Gain;
    }

    /// <summary>仿真器端点（host:port）。默认常量，环境变量可覆盖。</summary>
    public static (string Host, int Port) ResolveEndpoint()
    {
        string? raw = Environment.GetEnvironmentVariable(EndpointEnvVar);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (DefaultHost, DefaultPort);
        }

        string text = raw.Trim();
        int colon = text.LastIndexOf(':');
        if (colon <= 0 || colon == text.Length - 1)
        {
            return (DefaultHost, DefaultPort);
        }

        string host = text[..colon];
        return int.TryParse(text[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) && port > 0
            ? (host, port)
            : (DefaultHost, DefaultPort);
    }

    /// <summary>
    /// 需要心跳证据：HTTP 无连接，仿真器进程没了也收不到任何"掉线通知"，
    /// 只能靠"多久没收到证据"反推（与网络相机同理）。
    /// </summary>
    protected override bool RequiresHeartbeat => true;

    /// <summary>Open 成功后直接 Online：握手已经确认仿真器在，且该相机存在。</summary>
    protected override CameraConnectionState StateAfterOpen => CameraConnectionState.Online;

    /// <summary>软件触发是本驱动存在的理由，必须为 true。</summary>
    public override bool SupportsSoftwareTrigger => true;

    protected override bool OpenCore()
    {
        (string host, int port) = ResolveEndpoint();

        var http = new HttpClient
        {
            BaseAddress = new Uri($"http://{host}:{port}/"),
            Timeout = TimeSpan.FromSeconds(5),
        };

        string serial = Descriptor.SerialNo ?? string.Empty;
        if (string.IsNullOrWhiteSpace(serial))
        {
            SetDetail("未配置序列号：仿真相机的序列号必须等于仿真器里的相机名（如 CAM1）");
            http.Dispose();
            return false;
        }

        try
        {
            using HttpResponseMessage response = http.GetAsync("health").GetAwaiter().GetResult();
            string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                SetDetail($"仿真器 /health 返回 {(int)response.StatusCode}：{Trim(body)}");
                http.Dispose();
                return false;
            }

            if (!HealthContainsCamera(body, serial))
            {
                SetDetail($"仿真器里没有相机 {serial}（已启动的相机见 /health 的 cameras 数组）");
                http.Dispose();
                return false;
            }

            lock (_ioGate)
            {
                _http = http;
                _cts = new CancellationTokenSource();
                _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_cts.Token));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            SetDetail($"连不上仿真器 {host}:{port}（{ex.Message}）；先跑 LineSim --serve");
            http.Dispose();
            return false;
        }

        NotifyAlive();
        SetDetail($"已连接 LineSim {host}:{port} / {serial}");
        return true;
    }

    protected override void CloseCore()
    {
        HttpClient? http;
        CancellationTokenSource? cts;
        Task? heartbeat;
        Task? stream;

        lock (_ioGate)
        {
            http = _http;
            cts = _cts;
            heartbeat = _heartbeatTask;
            stream = _streamTask;
            _http = null;
            _cts = null;
            _heartbeatTask = null;
            _streamTask = null;
        }

        try { cts?.Cancel(); } catch (ObjectDisposedException) { /* 已释放 */ }

        // 不 Join 收流线程：Close 可能由 UI 线程调用，等一个正在 ReadAsync 的任务会卡住界面。
        // 任务自己会因取消而退出，且退出路径里不再碰 _http（下面先 Dispose 也一样安全）。
        _ = heartbeat;
        _ = stream;

        http?.Dispose();
        cts?.Dispose();
    }

    protected override bool ApplySettingsCore(CameraSettings settings)
    {
        _exposureUs = settings.ExposureTimeUs;
        _gain = settings.Gain;

        HttpClient? http = _http;
        if (http is null)
        {
            // 未连接时只记住参数：连接后由第一次 /config 生效
            return true;
        }

        string payload = string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"exposureUs\":{_exposureUs},\"gain\":{_gain}}}");

        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = http.PostAsync($"camera/{Descriptor.SerialNo}/config", content)
                .GetAwaiter().GetResult();
            string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                SetDetail($"下发曝光失败：{(int)response.StatusCode} {Trim(body)}");
                return false;
            }

            SetDetail($"曝光 {_exposureUs.ToString("F0", CultureInfo.InvariantCulture)}us / 增益 {_gain.ToString("F2", CultureInfo.InvariantCulture)}");
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            SetDetail($"下发曝光失败：{ex.Message}");
            return false;
        }
    }

    protected override bool StartStreamCore()
    {
        CancellationToken token;
        HttpClient http;

        lock (_ioGate)
        {
            if (_cts is null || _http is null)
            {
                SetDetail("未连接，无法开始采流");
                return false;
            }

            token = _cts.Token;
            http = _http;
            _streamTask = Task.Run(() => StreamLoopAsync(http, token), CancellationToken.None);
        }

        SetDetail("已开始采流（等待触发）");
        return true;
    }

    protected override void StopStreamCore()
    {
        // 只停收流，不断连接：下一次 StartStream 还能用
        CancellationTokenSource? cts;
        lock (_ioGate)
        {
            cts = _cts;
            _streamTask = null;
        }

        // 收流任务挂在 _cts 上，与连接同生命周期；这里不取消它，靠基类关掉 _acceptFrames 后
        // PushFrame 自然被拒（帧被丢弃但不影响状态机）。真正取消发生在 CloseCore。
        _ = cts;
        SetDetail("已停止采流");
    }

    /// <summary>软件触发：让仿真器按**当前 tick 的世界状态**渲染一帧并排进相机队列。</summary>
    public override bool TriggerSoftware()
    {
        HttpClient? http = _http;
        if (http is null)
        {
            return false;
        }

        try
        {
            using HttpResponseMessage response = http.PostAsync($"camera/{Descriptor.SerialNo}/trigger", null)
                .GetAwaiter().GetResult();
            string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                SetDetail($"软触发失败：{(int)response.StatusCode} {Trim(body)}");
                return false;
            }

            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("ok", out JsonElement ok) || ok.ValueKind != JsonValueKind.True)
            {
                SetDetail($"软触发被拒：{Trim(body)}");
                return false;
            }

            NotifyAlive();
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            SetDetail($"软触发失败：{ex.Message}");
            return false;
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken token)
    {
        HttpClient? http = _http;
        if (http is null)
        {
            return;
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, token).ConfigureAwait(false);
                using HttpResponseMessage response = await http.GetAsync("health", token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    NotifyAlive();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                // 仿真器没了：不在这里改状态，交给基类心跳看门狗判（超时 → HandleLost）
                _ = ex;
            }
        }
    }

    private async Task StreamLoopAsync(HttpClient http, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using HttpResponseMessage response = await http
                    .GetAsync($"camera/{Descriptor.SerialNo}/stream", HttpCompletionOption.ResponseHeadersRead, token)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    SetDetail($"取流失败：{(int)response.StatusCode}");
                    await Task.Delay(500, token).ConfigureAwait(false);
                    continue;
                }

                await using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                while (!token.IsCancellationRequested)
                {
                    (string headerJson, byte[] pixels) = await ReadFrameAsync(stream, token).ConfigureAwait(false);
                    PushDecodedFrame(headerJson, pixels);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                SetDetail($"取流断开（{ex.Message}），1s 后重连");
                try
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private static async Task<(string HeaderJson, byte[] Pixels)> ReadFrameAsync(Stream stream, CancellationToken token)
    {
        int headerLength = await ReadInt32Async(stream, token).ConfigureAwait(false);
        if (headerLength <= 0 || headerLength > 64 * 1024)
        {
            throw new IOException($"帧头长度异常：{headerLength}");
        }

        byte[] headerBytes = new byte[headerLength];
        await ReadExactlyAsync(stream, headerBytes, token).ConfigureAwait(false);

        int pixelLength = await ReadInt32Async(stream, token).ConfigureAwait(false);
        if (pixelLength <= 0 || pixelLength > 64 * 1024 * 1024)
        {
            throw new IOException($"像素长度异常：{pixelLength}");
        }

        byte[] pixels = new byte[pixelLength];
        await ReadExactlyAsync(stream, pixels, token).ConfigureAwait(false);
        return (Encoding.UTF8.GetString(headerBytes), pixels);
    }

    private static async Task<int> ReadInt32Async(Stream stream, CancellationToken token)
    {
        byte[] buffer = new byte[4];
        await ReadExactlyAsync(stream, buffer, token).ConfigureAwait(false);
        return buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24);
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), token).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException($"期望读 {buffer.Length} 字节，只读到 {offset}");
            }

            offset += read;
        }
    }

    private void PushDecodedFrame(string headerJson, byte[] pixels)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(headerJson);
            JsonElement root = doc.RootElement;

            int width = root.GetProperty("w").GetInt32();
            int height = root.GetProperty("h").GetInt32();
            int channels = root.GetProperty("ch").GetInt32();
            long seq = root.TryGetProperty("seq", out JsonElement seqElement) ? seqElement.GetInt64() : 0;

            var frame = new CameraFrame
            {
                Width = width,
                Height = height,
                Channels = channels,
                PixelData = pixels,
                Timestamp = DateTime.Now,
                // SourceName 只放"文件名"语义（契约明写不参与解码）；这里绝不放 tick/位置等真值（S15）
                SourceName = string.Create(CultureInfo.InvariantCulture, $"linesim-{Descriptor.SerialNo}-{seq:D6}.png"),
            };

            if (!PushFrame(frame, out string error))
            {
                SetDetail($"送帧被拒：{error}");
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            SetDetail($"帧头解析失败：{ex.Message}");
        }
    }

    private static bool HealthContainsCamera(string body, string serial)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("cameras", out JsonElement cameras) || cameras.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement camera in cameras.EnumerateArray())
            {
                if (camera.TryGetProperty("serial", out JsonElement value)
                    && string.Equals(value.GetString(), serial, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Trim(string text) => text.Length <= 160 ? text : text[..160] + "…";

    /// <summary>协议版本（与 LineSim 的 <c>hello.proto</c> 同源）。</summary>
    public static int Proto => ProtoVersion;
}
