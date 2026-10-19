using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using Newtonsoft.Json;
using Plugin.Camera.Hikvision;
using Plugin.ImageAcquisition;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 相机触发链路断言（2026-10-19 接通触发模式）。
    ///
    /// 守四层：
    ///   ① 映射：CameraTriggerMode → GenICam 节点值（尤其"本仓 0=软触发 / GenICam 0=连续"的语义差）；
    ///   ② 迁移：旧方案（无 TriggerSchemaVersion）必须按 Continuous 迁移，不能把旧流程改成软触发后全部超时；
    ///   ③ 采集步骤：软触发必须先 TriggerSoftware 再 WaitNextFrame；硬触发/连续不得下发软触发；
    ///      驱动不支持软触发时要明确失败，不能退化成"等图超时"；
    ///   ④ 超时文案：必须带触发模式，现场才知道去查 PLC/光电还是相机状态。
    /// </summary>
    internal static class CameraTriggerChecks
    {
        public static void Run()
        {
            Section("[TRIG] 相机触发：模式映射 / 旧配置迁移 / 采集步骤软触发");

            RunPlanMapping();
            RunMigration();
            RunAcquisitionBehavior();
        }

        // ==================================================================
        //  ① 映射
        // ==================================================================
        private static void RunPlanMapping()
        {
            var continuous = HikvisionTriggerPlan.From(new CameraSettings { TriggerMode = CameraTriggerMode.Continuous });
            Check("连续采图 → GenICam TriggerMode=0（触发关闭）",
                continuous.TriggerMode == 0 && continuous.TriggerSource == null && continuous.TriggerActivation == null,
                $"mode={continuous.TriggerMode} source={continuous.TriggerSource} act={continuous.TriggerActivation}");

            var software = HikvisionTriggerPlan.From(new CameraSettings
            {
                TriggerMode = CameraTriggerMode.Software,
                TriggerDelayUs = 250,
            });
            Check("软触发 → GenICam TriggerMode=1 + TriggerSource=Software（不是 0）",
                software.TriggerMode == 1 && software.TriggerSource == "Software" && software.TriggerActivation == null,
                $"mode={software.TriggerMode} source={software.TriggerSource} act={software.TriggerActivation}");
            Check("软触发延时原样下发",
                Math.Abs(software.TriggerDelayUs - 250) < 0.01, software.TriggerDelayUs.ToString("0.###"));

            var rising = HikvisionTriggerPlan.From(new CameraSettings
            {
                TriggerMode = CameraTriggerMode.RisingEdge,
                TriggerSourceLine = 2,
                TriggerDelayUs = 100,
            });
            Check("上升沿 → TriggerMode=1 + Line2 + RisingEdge",
                rising.TriggerMode == 1 && rising.TriggerSource == "Line2" && rising.TriggerActivation == "RisingEdge",
                $"mode={rising.TriggerMode} source={rising.TriggerSource} act={rising.TriggerActivation}");

            var falling = HikvisionTriggerPlan.From(new CameraSettings
            {
                TriggerMode = CameraTriggerMode.FallingEdge,
                TriggerSourceLine = 3,
            });
            Check("下降沿 → TriggerMode=1 + Line3 + FallingEdge",
                falling.TriggerMode == 1 && falling.TriggerSource == "Line3" && falling.TriggerActivation == "FallingEdge",
                $"mode={falling.TriggerMode} source={falling.TriggerSource} act={falling.TriggerActivation}");

            var clampHigh = HikvisionTriggerPlan.From(new CameraSettings
            {
                TriggerMode = CameraTriggerMode.RisingEdge,
                TriggerSourceLine = 9,
                TriggerDelayUs = -5,
            });
            Check("线号 9 → Line3、负延时 → 0（映射层兜底）",
                clampHigh.TriggerSource == "Line3" && clampHigh.TriggerDelayUs == 0,
                $"source={clampHigh.TriggerSource} delay={clampHigh.TriggerDelayUs}");

            var clampLow = HikvisionTriggerPlan.From(new CameraSettings
            {
                TriggerMode = CameraTriggerMode.RisingEdge,
                TriggerSourceLine = -3,
            });
            Check("线号 -3 → Line0（映射层兜底）",
                clampLow.TriggerSource == "Line0", clampLow.TriggerSource ?? "null");

            var nullPlan = HikvisionTriggerPlan.From(null);
            Check("空配置 → 连续采图（不抛、不误开触发）",
                nullPlan.TriggerMode == 0 && nullPlan.TriggerSource == null, $"mode={nullPlan.TriggerMode}");

            Check("枚举数值保持追加式（旧方案存盘不错位）",
                (int)CameraTriggerMode.Software == 0
                && (int)CameraTriggerMode.RisingEdge == 1
                && (int)CameraTriggerMode.FallingEdge == 2
                && (int)CameraTriggerMode.Continuous == 3,
                $"{string.Join(",", Enum.GetValues(typeof(CameraTriggerMode)).Cast<CameraTriggerMode>().Select(v => $"{v}={(int)v}"))}");
        }

        // ==================================================================
        //  ② 旧配置迁移
        // ==================================================================
        private static void RunMigration()
        {
            var oldSoftware = new CameraSettings
            {
                TriggerSchemaVersion = 0,
                TriggerMode = CameraTriggerMode.Software,
                TriggerSourceLine = 9,
                TriggerDelayUs = -5,
            }.Normalized();
            Check("旧配置（Software，无版本）→ Continuous（保持升级前真实行为）",
                oldSoftware.TriggerMode == CameraTriggerMode.Continuous && oldSoftware.TriggerSchemaVersion == 1,
                $"{oldSoftware.TriggerMode}/v{oldSoftware.TriggerSchemaVersion}");

            // 真走一遍 Newtonsoft：旧 .vms 里没有 TriggerSchemaVersion 字段，反序列化后必须是 0。
            var oldJson = "{\"TriggerMode\":0,\"ExposureTimeUs\":10000}";
            var fromJson = JsonConvert.DeserializeObject<CameraSettings>(oldJson);
            var migratedJson = fromJson!.Normalized();
            Check("旧 JSON（无 TriggerSchemaVersion）反序列化后 → Continuous",
                fromJson.TriggerSchemaVersion == 0
                && migratedJson.TriggerMode == CameraTriggerMode.Continuous
                && migratedJson.TriggerSchemaVersion == 1,
                $"raw=v{fromJson.TriggerSchemaVersion}/{fromJson.TriggerMode} → v{migratedJson.TriggerSchemaVersion}/{migratedJson.TriggerMode}");
            Check("迁移时线号夹到 3、负延时归 0",
                oldSoftware.TriggerSourceLine == 3 && oldSoftware.TriggerDelayUs == 0,
                $"line={oldSoftware.TriggerSourceLine} delay={oldSoftware.TriggerDelayUs}");

            var oldRising = new CameraSettings
            {
                TriggerSchemaVersion = 0,
                TriggerMode = CameraTriggerMode.RisingEdge,
            }.Normalized();
            Check("旧配置（RisingEdge，无版本）→ Continuous（旧值本来就不生效）",
                oldRising.TriggerMode == CameraTriggerMode.Continuous, oldRising.TriggerMode.ToString());

            var freshSoftware = CameraSettings.CreateNew();
            freshSoftware.TriggerMode = CameraTriggerMode.Software;
            freshSoftware = freshSoftware.Normalized();
            Check("新配置（带版本）→ 保留用户选择的 Software",
                freshSoftware.TriggerMode == CameraTriggerMode.Software && freshSoftware.TriggerSchemaVersion == 1,
                $"{freshSoftware.TriggerMode}/v{freshSoftware.TriggerSchemaVersion}");

            var clone = new CameraSettings
            {
                TriggerSchemaVersion = 1,
                TriggerMode = CameraTriggerMode.RisingEdge,
                TriggerSourceLine = 2,
                TriggerDelayUs = 123.5,
            }.Clone();
            Check("Clone 复制触发字段（ApplySettings 落盘不丢）",
                clone.TriggerMode == CameraTriggerMode.RisingEdge
                && clone.TriggerSourceLine == 2
                && Math.Abs(clone.TriggerDelayUs - 123.5) < 0.01
                && clone.TriggerSchemaVersion == 1,
                $"{clone.TriggerMode}/line={clone.TriggerSourceLine}/delay={clone.TriggerDelayUs}");
        }

        // ==================================================================
        //  ③ 采集步骤行为
        // ==================================================================
        private static void RunAcquisitionBehavior()
        {
            // 软触发：先下发触发，再等帧
            var (softPlugin, softDevice) = RunAcquisition(
                new CameraSettings { TriggerMode = CameraTriggerMode.Software },
                supportsSoftware: true, supportsHardware: true, triggerResult: true, waitResult: true);
            try
            {
                Check("软触发：先 TriggerSoftware 再 WaitNextFrame",
                    softDevice.Calls.SequenceEqual(new[] { "TriggerSoftware", "WaitNextFrame" }),
                    string.Join(" → ", softDevice.Calls));
                Check("软触发：取图成功", softPlugin.Success.Value is true,
                    softPlugin.ErrorMessage.Value as string ?? "");
            }
            finally { softPlugin.Dispose(); }

            // 硬触发：不得下发软触发，只等帧
            var (hardPlugin, hardDevice) = RunAcquisition(
                new CameraSettings { TriggerMode = CameraTriggerMode.RisingEdge, TriggerSourceLine = 0 },
                supportsSoftware: true, supportsHardware: true, triggerResult: true, waitResult: true);
            try
            {
                Check("上升沿：不调 TriggerSoftware，只 WaitNextFrame",
                    hardDevice.Calls.SequenceEqual(new[] { "WaitNextFrame" }),
                    string.Join(" → ", hardDevice.Calls));
                Check("上升沿：取图成功", hardPlugin.Success.Value is true,
                    hardPlugin.ErrorMessage.Value as string ?? "");
            }
            finally { hardPlugin.Dispose(); }

            // 连续采图：同样不得下发软触发
            var (contPlugin, contDevice) = RunAcquisition(
                new CameraSettings { TriggerMode = CameraTriggerMode.Continuous },
                supportsSoftware: true, supportsHardware: true, triggerResult: true, waitResult: true);
            try
            {
                Check("连续采图：不调 TriggerSoftware，只 WaitNextFrame",
                    contDevice.Calls.SequenceEqual(new[] { "WaitNextFrame" }),
                    string.Join(" → ", contDevice.Calls));
                Check("连续采图：取图成功", contPlugin.Success.Value is true,
                    contPlugin.ErrorMessage.Value as string ?? "");
            }
            finally { contPlugin.Dispose(); }

            // 软触发但驱动不支持软触发（支持硬件）：必须明确失败，不能等图超时
            var (noSoftPlugin, noSoftDevice) = RunAcquisition(
                new CameraSettings { TriggerMode = CameraTriggerMode.Software },
                supportsSoftware: false, supportsHardware: true, triggerResult: false, waitResult: true);
            try
            {
                Check("驱动不支持软触发 → 明确失败（不退化等图超时）",
                    noSoftPlugin.Success.Value is false
                    && (noSoftPlugin.ErrorMessage.Value as string ?? "").Contains("不支持软触发")
                    && noSoftDevice.Calls.Count == 0,
                    noSoftPlugin.ErrorMessage.Value as string ?? "");
            }
            finally { noSoftPlugin.Dispose(); }

            // 软触发命令被拒绝：同样明确失败，不再等帧
            var (rejectPlugin, rejectDevice) = RunAcquisition(
                new CameraSettings { TriggerMode = CameraTriggerMode.Software },
                supportsSoftware: true, supportsHardware: true, triggerResult: false, waitResult: true);
            try
            {
                Check("软触发命令被拒绝 → 明确报软触发失败，不再 WaitNextFrame",
                    rejectPlugin.Success.Value is false
                    && (rejectPlugin.ErrorMessage.Value as string ?? "").Contains("软触发失败")
                    && rejectDevice.Calls.SequenceEqual(new[] { "TriggerSoftware" }),
                    $"{rejectPlugin.ErrorMessage.Value as string ?? ""} / {string.Join(" → ", rejectDevice.Calls)}");
            }
            finally { rejectPlugin.Dispose(); }

            // 网络相机式：两个能力都不支持 = 触发由外部客户端决定，直接等帧
            var (netPlugin, netDevice) = RunAcquisition(
                new CameraSettings { TriggerMode = CameraTriggerMode.Software },
                supportsSoftware: false, supportsHardware: false, triggerResult: false, waitResult: true);
            try
            {
                Check("外部驱动（网络相机）：不调 TriggerSoftware，直接 WaitNextFrame",
                    netDevice.Calls.SequenceEqual(new[] { "WaitNextFrame" }) && netPlugin.Success.Value is true,
                    string.Join(" → ", netDevice.Calls));
            }
            finally { netPlugin.Dispose(); }

            // 外部驱动超时：文案不能写"本步骤已下发触发"（它根本没下发）
            var (netTimeoutPlugin, _) = RunAcquisition(
                new CameraSettings { TriggerMode = CameraTriggerMode.Software },
                supportsSoftware: false, supportsHardware: false, triggerResult: false, waitResult: false);
            try
            {
                var err = netTimeoutPlugin.ErrorMessage.Value as string ?? "";
                Check("外部驱动超时文案说明触发由外部决定",
                    netTimeoutPlugin.Success.Value is false && err.Contains("外部触发"),
                    err);
            }
            finally { netTimeoutPlugin.Dispose(); }

            // 超时文案必须带触发模式
            var (timeoutPlugin, _) = RunAcquisition(
                new CameraSettings { TriggerMode = CameraTriggerMode.RisingEdge },
                supportsSoftware: true, supportsHardware: true, triggerResult: true, waitResult: false);
            try
            {
                var err = timeoutPlugin.ErrorMessage.Value as string ?? "";
                Check("等图超时文案带触发模式（上升沿 + 排查提示）",
                    timeoutPlugin.Success.Value is false && err.Contains("上升沿") && err.Contains("PLC"),
                    err);
            }
            finally { timeoutPlugin.Dispose(); }

            var (contTimeoutPlugin, _) = RunAcquisition(
                new CameraSettings { TriggerMode = CameraTriggerMode.Continuous },
                supportsSoftware: true, supportsHardware: true, triggerResult: true, waitResult: false);
            try
            {
                var err = contTimeoutPlugin.ErrorMessage.Value as string ?? "";
                Check("等图超时文案带连续采图",
                    contTimeoutPlugin.Success.Value is false && err.Contains("连续采图"),
                    err);
            }
            finally { contTimeoutPlugin.Dispose(); }
        }

        private static (ImageAcquisitionPlugin Plugin, FakeCameraDevice Device) RunAcquisition(
            CameraSettings settings,
            bool supportsSoftware,
            bool supportsHardware,
            bool triggerResult,
            bool waitResult)
        {
            // 这些用例模拟"新建/已迁移"的配置，显式标成当前版本；旧配置迁移另有专门断言。
            settings.TriggerSchemaVersion = CameraSettings.CurrentTriggerSchemaVersion;
            var device = new FakeCameraDevice(settings)
            {
                SupportsSoftwareTrigger = supportsSoftware,
                SupportsHardwareTrigger = supportsHardware,
                TriggerSoftwareResult = triggerResult,
                WaitNextFrameResult = waitResult,
            };
            var plugin = new ImageAcquisitionPlugin
            {
                Mode = AcquisitionMode.Camera,
                CameraSerial = FakeCameraDevice.Serial,
                DisplayViewIndex = 0,
            };
            var context = new ExecutionContext(
                new StubLog(),
                new FlowSession { FlowName = "触发断言" },
                new WorkspaceContext(),
                CancellationToken.None)
            {
                Cameras = new FakeCameraProvider(device),
            };
            plugin.Execute(context);
            return (plugin, device);
        }

        // ==================================================================
        //  桩：相机设备 / 相机仓库
        // ==================================================================
        private sealed class FakeCameraDevice : ICameraDevice
        {
            public const string Serial = "FAKE-TRIGGER";

            public FakeCameraDevice(CameraSettings settings)
            {
                Settings = settings;
                Descriptor = new CameraDescriptor
                {
                    SerialNo = Serial,
                    DisplayName = "触发断言相机",
                    Settings = settings,
                };
            }

            public CameraDescriptor Descriptor { get; }
            public CameraSettings Settings { get; set; }
            public bool SupportsSoftwareTrigger { get; set; }
            public bool SupportsHardwareTrigger { get; set; }
            public bool TriggerSoftwareResult { get; set; }
            public bool WaitNextFrameResult { get; set; }
            public List<string> Calls { get; } = new();

            public CameraConnectionState State { get; set; } = CameraConnectionState.Streaming;
            public string StateDetail => "测试";
            public long OverflowCount => 0;
            public long DroppedFrameCount => 0;
            public long ReceivedFrameCount => 1;
            public int PendingFrameCount => 0;

            public event EventHandler StateChanged { add { } remove { } }

            public bool Open() => true;
            public void Close() { }
            public bool StartStream() => true;
            public void StopStream() { }
            public bool ApplySettings(CameraSettings settings) { Settings = settings.Normalized(); return true; }
            public CameraSettings ReadSettings() => Settings.Clone();
            public bool TriggerSoftware()
            {
                Calls.Add("TriggerSoftware");
                return TriggerSoftwareResult;
            }
            public void NotifyAlive() { }
            public bool PushFrame(CameraFrame frame, out string error) { error = string.Empty; return true; }
            public bool TryGetLatest(out CameraFrame frame) { frame = null!; return false; }

            public bool WaitNextFrame(out CameraFrame frame, int timeoutMs, CancellationToken ct)
            {
                Calls.Add("WaitNextFrame");
                if (!WaitNextFrameResult)
                {
                    frame = null!;
                    return false;
                }
                frame = new CameraFrame
                {
                    FrameId = 1,
                    SerialNo = Serial,
                    Width = 2,
                    Height = 2,
                    Channels = 1,
                    PixelData = new byte[4],
                    SourceName = "fake://frame1",
                };
                return true;
            }

            public void ResetCounters() { }
            public void Dispose() { }
        }

        private sealed class FakeCameraProvider : ICameraProvider
        {
            private readonly FakeCameraDevice _device;
            public FakeCameraProvider(FakeCameraDevice device) => _device = device;

            public IReadOnlyList<CameraDescriptor> Cameras => new[] { _device.Descriptor };
            public bool TryGetDevice(Guid cameraId, out ICameraDevice device) { device = _device; return true; }
            public bool TryGetDeviceBySerial(string serialNo, out ICameraDevice device) { device = _device; return true; }
        }
    }
}
