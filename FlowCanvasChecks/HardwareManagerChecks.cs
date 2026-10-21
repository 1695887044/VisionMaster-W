using System;
using System.IO;
using System.Linq;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 硬件管理嵌入（批次 C）的静态守门。
    ///
    /// 这些判据不证明运行期行为，只钉住"评审确认过的三条硬约束"：
    /// ① 切页只停看门狗/点动，绝不 CancelAll；
    /// ② 三个域 VM 实现 IEmbeddedDialogPage，宿主负责 Opened/Closed；
    /// ③ 通讯只在脏时保存，配置区由 IsConfigEditable 拦住 TwoWay 直写。
    /// 真正的开关/切页/保存行为仍由真机与后续 HardwareManager 运行时断言覆盖。
    /// </summary>
    internal static class HardwareManagerChecks
    {
        public static void Run()
        {
            Program.Section("[HW] 硬件管理嵌入（批次 C）静态闸");

            var motionVm = Read(@"VisionMaster\ViewModels\DialogViewModels\MotionBoardViewModel.cs");
            var motionDebug = Read(@"VisionMaster\ViewModels\DialogViewModels\MotionBoardDebugTabViewModel.cs");
            var cameraVm = Read(@"VisionMaster\ViewModels\DialogViewModels\CameraSettingsViewModel.cs");
            var commVm = Read(@"VisionMaster\ViewModels\DialogViewModels\CommunicationSettingsViewModel.cs");
            var scanVm = Read(@"VisionMaster\ViewModels\DialogViewModels\ScanGroupEditorViewModel.cs");
            var hostVm = Read(@"VisionMaster\ViewModels\DialogViewModels\HardwareManagerViewModel.cs");
            var hostXaml = Read(@"VisionMaster\Views\DialogViews\HardwareManagerView.xaml");
            var cameraXaml = Read(@"VisionMaster\Views\DialogViews\CameraSettingsView.xaml");
            var commXaml = Read(@"VisionMaster\Views\DialogViews\CommunicationSettingsView.xaml");
            var motionXaml = Read(@"VisionMaster\Views\DialogViews\MotionBoardView.xaml");
            var shellVm = Read(@"VisionMaster\ShellViewModel.cs");
            var app = Read(@"VisionMaster\App.xaml.cs");
            var gate = Read(@"VisionMaster\Services\Hardware\HardwareEditGate.cs");

            var motionDeactivated = MethodBody(motionVm, "public void OnEmbeddedDeactivated()");
            Program.Check("HW1 运动切页只停刷新/看门狗，不调用 OnShellClosed/CancelAll",
                motionDeactivated.Contains("OnShellDeactivated()", StringComparison.Ordinal)
                && !motionDeactivated.Contains("OnShellClosed()", StringComparison.Ordinal)
                && !motionDeactivated.Contains("CancelAll(", StringComparison.Ordinal),
                motionDeactivated.Length == 0 ? "找不到 OnEmbeddedDeactivated" : "已隔离切页与完整关闭");

            var debugDeactivated = MethodBody(motionDebug, "public void OnShellDeactivated()");
            Program.Check("HW2 调试页切走只停看门狗+StopJog，不 CancelAll",
                debugDeactivated.Contains("StopJog(", StringComparison.Ordinal)
                && !debugDeactivated.Contains("CancelAll(", StringComparison.Ordinal),
                debugDeactivated.Length == 0 ? "找不到 OnShellDeactivated" : "只停点动");

            var debugClosed = MethodBody(motionDebug, "public void OnShellClosed()");
            Program.Check("HW3 完整关闭仍走 OnShellClosed（含条件 CancelAll）",
                debugClosed.Contains("OnShellDeactivated()", StringComparison.Ordinal)
                && debugClosed.Contains("CancelAll(", StringComparison.Ordinal),
                debugClosed.Length == 0 ? "找不到 OnShellClosed" : "关闭路径保留");

            Program.Check("HW4 三个域 VM 都实现 IEmbeddedDialogPage",
                Implements(cameraVm, "CameraSettingsViewModel")
                && Implements(commVm, "CommunicationSettingsViewModel")
                && Implements(motionVm, "MotionBoardViewModel"),
                "相机/通讯/运动");

            Program.Check("HW5 宿主负责 Opened/Closed/CanClose，不碰子 VM 的 RequestClose",
                hostVm.Contains("OnEmbeddedOpened(", StringComparison.Ordinal)
                && hostVm.Contains("OnEmbeddedClosed(", StringComparison.Ordinal)
                && hostVm.Contains("CanCloseEmbedded(", StringComparison.Ordinal)
                && hostVm.Contains("ClosePages()", StringComparison.Ordinal)
                && !hostVm.Contains("CameraSettingsViewModel.RequestClose", StringComparison.Ordinal)
                && !hostVm.Contains("CommunicationSettingsViewModel.RequestClose", StringComparison.Ordinal)
                && !hostVm.Contains("MotionBoardViewModel.RequestClose", StringComparison.Ordinal),
                "生命周期收口在宿主");

            Program.Check("HW6 通讯只在脏时保存（IsCommunicationDirty + SaveConfigAsync）",
                hostVm.Contains("IsCommunicationDirty", StringComparison.Ordinal)
                && hostVm.Contains("SaveConfigAsync(", StringComparison.Ordinal)
                && hostVm.Contains("MarkCommunicationClean(version)", StringComparison.Ordinal)
                && CountOf(commVm, "MarkCommunicationDirty()") >= 4,
                $"通讯 VM 置脏点={CountOf(commVm, "MarkCommunicationDirty()")}");

            Program.Check("HW7 宿主页承载三个嵌入视图并保留运动急停",
                hostXaml.Contains("Content=\"{Binding CameraPageView}\"", StringComparison.Ordinal)
                && hostXaml.Contains("Content=\"{Binding CommunicationPageView}\"", StringComparison.Ordinal)
                && hostXaml.Contains("Content=\"{Binding MotionPageView}\"", StringComparison.Ordinal)
                && hostXaml.Contains("MotionEmergencyStopCommand", StringComparison.Ordinal),
                "三个 ContentControl + 急停");

            Program.Check("HW8 三个子视图都有嵌入模式（隐藏自身标题栏/关闭）",
                cameraXaml.Contains("EmbeddedMode.IsEmbedded", StringComparison.Ordinal)
                && commXaml.Contains("EmbeddedMode.IsEmbedded", StringComparison.Ordinal)
                && motionXaml.Contains("EmbeddedMode.IsEmbedded", StringComparison.Ordinal),
                "相机/通讯/运动");

            Program.Check("HW9 配置区绑定 IsConfigEditable（拦住 TwoWay 直写）",
                cameraXaml.Contains("IsConfigEditable", StringComparison.Ordinal)
                && commXaml.Contains("IsConfigEditable", StringComparison.Ordinal)
                && motionXaml.Contains("IsConfigEditable", StringComparison.Ordinal),
                "相机/通讯/运动");

            Program.Check("HW10 旧硬件入口全部深链到宿主（不再直接开独立弹窗）",
                shellVm.Contains("ShowHardwareManager(\"camera\")", StringComparison.Ordinal)
                && shellVm.Contains("ShowHardwareManager(\"comm\")", StringComparison.Ordinal)
                && shellVm.Contains("ShowHardwareManager(\"motion\"", StringComparison.Ordinal)
                && shellVm.Contains("ShowHardwareManager();", StringComparison.Ordinal),
                "相机/通讯/运动/硬件管理");

            Program.Check("HW11 门禁走 AnyFlowRunning（覆盖单流程/HTTP 触发）",
                gate.Contains("AnyFlowRunning", StringComparison.Ordinal)
                && app.Contains("RegisterSingleton<IFlowRunState", StringComparison.Ordinal)
                && app.Contains("RegisterSingleton<IHardwareEditGate", StringComparison.Ordinal),
                "IFlowRunState + IHardwareEditGate 已注册");

            Program.Check("HW12 通讯关闭可 veto：CanCloseDialog/CanClosePages + 保存后版本校验",
                hostVm.Contains("CanCloseDialog()", StringComparison.Ordinal)
                && hostVm.Contains("SaveAndCloseAsync(", StringComparison.Ordinal)
                && hostVm.Contains("_closeSaveInProgress", StringComparison.Ordinal)
                && hostVm.Contains("MarkCommunicationClean(version)", StringComparison.Ordinal),
                "保存失败/期间新改动都不关");

            Program.Check("HW13 运动关窗：手动命令+流程运行中拒绝关闭",
                motionVm.Contains("HasPendingManualMotion", StringComparison.Ordinal)
                && motionVm.Contains("AnyFlowRunning()", StringComparison.Ordinal)
                && motionVm.Contains("CanCloseEmbedded", StringComparison.Ordinal),
                "CanCloseEmbedded 守住");

            Program.Check("HW14 配置模态/命令二次门禁",
                motionXaml.Contains("IsEnabled=\"{Binding IsConfigEditable}\"", StringComparison.Ordinal)
                && CountOf(commVm, "if (!IsConfigEditable)") >= 4
                && motionVm.Contains("if (!IsConfigEditable)", StringComparison.Ordinal),
                $"通讯二次门禁={CountOf(commVm, "if (!IsConfigEditable)")}");

            Program.Check("HW15 只读可查看：运行操作单独 gate，不再整页禁用",
                !hostXaml.Contains("IsEnabled=\"{Binding IsRuntimeOperable}\"", StringComparison.Ordinal)
                && cameraXaml.Contains("IsEnabled=\"{Binding IsRuntimeOperable}\"", StringComparison.Ordinal)
                && commXaml.Contains("CanOperateSelection", StringComparison.Ordinal)
                && motionXaml.Contains("DataContext.IsRuntimeOperable", StringComparison.Ordinal),
                "相机动作条/通讯批量/运动调试页");

            Program.Check("HW16 运动页不再自动 seed 凸轮表",
                !motionVm.Contains("CamTables.Add(MotionCamMath.CreateSeedTable())", StringComparison.Ordinal)
                && motionVm.Contains("Cam.LoadTables()", StringComparison.Ordinal),
                "查看不改方案");

            Program.Check("HW17 关窗按每张发起等待的卡撤销（重叠等待不被单槽覆盖）",
                motionDebug.Contains("_pendingManualDevices", StringComparison.Ordinal)
                && CountOf(motionDebug, "BeginPendingManual(device)") >= 3
                && CountOf(motionDebug, "EndPendingManual(device)") >= 3
                && debugClosed.Contains("_pendingManualDevices.Keys.ToArray()", StringComparison.Ordinal)
                && !motionDebug.Contains("_waitingDevice", StringComparison.Ordinal),
                $"发起登记={CountOf(motionDebug, "BeginPendingManual(device)")}，关窗按全部设备撤销");

            var confirmBody = MethodBody(scanVm, "private void ExecuteConfirm()");
            var scanCopyBody = MethodBody(scanVm, "private void ExecuteCopyToConnection()");
            var commDeleteBody = MethodBody(commVm, "private void ExecuteDelete(CommunicationConfig? config)");
            var scanClosedBody = MethodBody(commVm, "private void OnScanGroupsClosed(IDialogResult result)");
            Program.Check("HW18 扫描组提交前查实时门禁（先于活对象写入）",
                scanVm.Contains("CanCommitKey", StringComparison.Ordinal)
                && scanVm.Contains("Func<bool>", StringComparison.Ordinal)
                && commVm.Contains("ScanGroupEditorViewModel.CanCommitKey", StringComparison.Ordinal)
                && confirmBody.Contains("CanCommitNow()", StringComparison.Ordinal)
                && confirmBody.IndexOf("CanCommitNow()", StringComparison.Ordinal)
                   < confirmBody.IndexOf("_live.CopyFrom", StringComparison.Ordinal)
                && scanCopyBody.Contains("CanCommitNow()", StringComparison.Ordinal)
                && commDeleteBody.LastIndexOf("if (!IsConfigEditable)", StringComparison.Ordinal)
                   > commDeleteBody.IndexOf("EasyDialog.ShowSync", StringComparison.Ordinal),
                "确定/复制/删除都按提交时刻的门禁判");

            Program.Check("HW18b 扫描组“复制到其它连接”立即置脏（取消也不丢）",
                scanVm.Contains("MarkDirtyKey", StringComparison.Ordinal)
                && scanCopyBody.Contains("_markDirty?.Invoke()", StringComparison.Ordinal)
                && commVm.Contains("ScanGroupEditorViewModel.MarkDirtyKey", StringComparison.Ordinal)
                && scanClosedBody.Contains("MarkCommunicationDirty()", StringComparison.Ordinal)
                && !scanClosedBody.Contains("if (!IsConfigEditable)", StringComparison.Ordinal),
                "提交门禁在编辑器内，宿主回调无条件置脏");

            var closedBody = MethodBody(hostVm, "public void OnDialogClosed()");
            Program.Check("HW19 宿主关闭清空页面记录（二次打开必重建）",
                closedBody.Contains("ResetEmbeddedPages()", StringComparison.Ordinal)
                && hostVm.Contains("_cameraPage = null;", StringComparison.Ordinal)
                && hostVm.Contains("_commPage = null;", StringComparison.Ordinal)
                && hostVm.Contains("_motionPage = null;", StringComparison.Ordinal),
                "不假定 RegisterDialog 的生命周期");

            var tabLine = motionVm.Split('\n')
                .FirstOrDefault(l => l.Contains("TabLabels { get; }", StringComparison.Ordinal)) ?? string.Empty;
            Program.Check("HW20 电子凸轮不开放 UI（页签与视图都已下线）",
                tabLine.Length > 0
                && !tabLine.Contains("电子凸轮", StringComparison.Ordinal)
                && tabLine.Contains("点位列表", StringComparison.Ordinal)
                && !motionXaml.Contains("MotionBoardCamView", StringComparison.Ordinal)
                && !motionVm.Contains("\"cam\" => 3", StringComparison.Ordinal),
                tabLine.Length == 0 ? "找不到 TabLabels" : tabLine.Trim());

            var paletteBody = MethodBody(shellVm, "private void BuildCommandPalette()");
            Program.Check("HW21 命令面板不再暴露相机/运动/通讯入口",
                paletteBody.Contains("硬件管理", StringComparison.Ordinal)
                && !paletteBody.Contains("相机设置", StringComparison.Ordinal)
                && !paletteBody.Contains("运动板卡", StringComparison.Ordinal)
                && !paletteBody.Contains("通讯设置", StringComparison.Ordinal),
                paletteBody.Length == 0 ? "找不到 BuildCommandPalette" : "命令面板只保留硬件管理");
        }

        private static bool Implements(string source, string className)
            => source.Contains("class " + className, StringComparison.Ordinal)
               && source.Contains("IEmbeddedDialogPage", StringComparison.Ordinal);

        private static int CountOf(string source, string token)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }
            return count;
        }

        /// <summary>按签名取方法体（花括号配对）。找不到返回空串，由调用方判红。</summary>
        private static string MethodBody(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0) return string.Empty;

            var brace = source.IndexOf('{', start);
            if (brace < 0) return string.Empty;

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0) return source.Substring(brace, i - brace + 1);
                }
            }
            return string.Empty;
        }

        private static string Read(string relative)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
            }
            return string.Empty;
        }
    }
}
