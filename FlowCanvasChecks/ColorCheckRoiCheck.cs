using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Core.Halcon;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 「颜色序列检查」插件第一刀（ROI 采样区）的断言。
    ///
    /// 本轮要证明的唯一一件事
    /// ---------
    /// **画框 → 读出坐标 → 存进配置 → 重开能回填**。
    /// 这是整个插件化改造里最不确定的一环：图像上的框选由 Core.Halcon 的 ImageEdit 提供，
    /// 而"控件里的框 ↔ 配置里的形状参数"这条双向通道要自己接。它接不通，后面的算法与配方表都白搭。
    ///
    /// 为什么不用"跑一遍流程看结果"来验
    /// ---------
    /// ROI 的价值在**配置期**（打开节点时能看见上次框的框），运行期反而看不出对错。
    /// 所以这里直接驱动 ViewModel：模拟控件新建/拖拽回去的事件，再走一次 OnConfirm 存盘与 Initialize 回填。
    /// </summary>
    internal static class ColorCheckRoiCheck
    {
        private const string PluginTypeName =
            "Plugin.ColorCheck.ColorCheckPlugin, Plugin.ColorCheck, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

        public static void Run()
        {
            Section("[R2] 颜色序列检查（第一刀）：ROI 采样区的画框 / 存盘 / 回填");

            var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            var loadError = "";
            Assembly? asm = null;
            try
            {
                asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Plugin.ColorCheck")
                      ?? Assembly.LoadFrom(Path.Combine(repoRoot, @"Modules\Plugin.ColorCheck.dll"));
            }
            catch (Exception ex) { loadError = ex.Message; }

            Check("插件程序集可装载（含 WPF 视图）", asm != null, loadError.Length > 0 ? loadError : "Plugin.ColorCheck");
            if (asm == null) return;

            var pluginType = asm.GetType("Plugin.ColorCheck.ColorCheckPlugin", true)!;

            // 一个矩形：Halcon 约定 [中心行, 中心列, 角度, 半长, 半宽]
            var roi = new[] { 240.0, 440.0, 0.0, 100.0, 30.0 };

            var step = new ActionStep("", "颜色序列检查", PluginTypeName, "颜色序列检查_0");
            step.SetInputValue("RoiParams", roi);
            step.SetInputValue("PreviewImagePath", "");

            // ---------- 1. 打开配置：按配置回填画布 ----------
            var plugin = CreatePlugin(pluginType);
            plugin.Initialize(step);
            var canvasRois = GetCanvasRois(plugin);

            Check("打开配置时按已存参数回填出 1 个采样矩形",
                canvasRois.Count == 1,
                $"CanvasRois.Count={canvasRois.Count}");

            Check("回填的矩形坐标与配置一致（不是重新采样出来的近似值）",
                canvasRois.Count == 1 && SameNumbers(GetTuples(canvasRois[0]), roi),
                canvasRois.Count == 1 ? string.Join(",", GetTuples(canvasRois[0]).Select(d => d.ToString("0.#"))) : "无");

            // ---------- 2. 用户在图上新画一个框 → 回写配置 ----------
            var drawn = new[] { 200.0, 300.0, 15.0, 80.0, 25.0 };
            canvasRois.Add(MakeRoi(drawn, "采样区"));

            Check("在图上画框后，配置里的形状参数被同步回写",
                SameNumbers(GetParams(plugin), drawn),
                string.Join(",", GetParams(plugin).Select(d => d.ToString("0.#"))));

            // ---------- 3. 拖拽句柄 → 回写配置 ----------
            var dragged = new[] { 210.0, 305.0, 15.0, 90.0, 28.0 };
            // 走 HTuples 的 INPC 通道 —— 这正是控件拖拽句柄后回传的路径，不能绕开它换成集合替换
            ((DrawingObjectInfo)canvasRois[canvasRois.Count - 1]!).HTuples =
                dragged.Select(d => new HTuple(d)).ToArray();

            Check("拖拽句柄后，配置里的形状参数跟着更新",
                SameNumbers(GetParams(plugin), dragged),
                string.Join(",", GetParams(plugin).Select(d => d.ToString("0.#"))));

            // ---------- 4. 只认最后一个（画多个时） ----------
            var second = new[] { 100.0, 100.0, 0.0, 40.0, 10.0 };
            canvasRois.Add(MakeRoi(second, "采样区2"));

            Check("画了多个采样区时只认最后画的那个（不会永远停在第一个）",
                SameNumbers(GetParams(plugin), second),
                string.Join(",", GetParams(plugin).Select(d => d.ToString("0.#"))));

            // ---------- 5. 存盘往返：OnConfirm → 新实例 Initialize ----------
            plugin.OnConfirm(step);
            var persisted = step.InputValues.TryGetValue("RoiParams", out var raw) ? raw : null;
            Check("确认后形状参数进了步骤配置（能随方案落盘）",
                persisted != null, $"InputValues[RoiParams]={(persisted == null ? "缺失" : persisted.GetType().Name)}");

            var reopened = CreatePlugin(pluginType);
            reopened.Initialize(step);
            var reopenedRois = GetCanvasRois(reopened);

            Check("重开配置后回填的矩形与存盘前一致（画框 → 存 → 回填 闭环）",
                reopenedRois.Count == 1 && SameNumbers(GetTuples(reopenedRois[0]), second),
                reopenedRois.Count == 1
                    ? string.Join(",", GetTuples(reopenedRois[0]).Select(d => d.ToString("0.#")))
                    : $"回填了 {reopenedRois.Count} 个");

            // ---------- 6. 运行期：没框要明确报错，有框要能过 ----------
            var noRoi = CreatePlugin(pluginType);
            noRoi.Initialize(new ActionStep("", "颜色序列检查", PluginTypeName, "没框的"));
            var noRoiOutcome = RunWithImage(noRoi, repoRoot);
            Check("没框采样区时运行期明确报错（并说清怎么补）",
                !noRoiOutcome.Success && noRoiOutcome.Error.Contains("采样区"),
                $"Success={noRoiOutcome.Success} Err=[{noRoiOutcome.Error}]");

            // 这一条要用"真的框住线束"的采样区：第二刀之后运行期会真的跑算法，
            // 框在空白背景上会被对比度下限挡下来（那是对的，不是 bug）；
            // 第三刀又会跑判定，所以这里给一份全通配的配方（每位都是 * = 不检），
            // 本用例只关心"采样区这条链路通不通"，不关心判定。
            var okStep = new ActionStep("", "颜色序列检查", PluginTypeName, "框好的");
            okStep.SetInputValue("RoiParams", new[] { 240.0, 440.0, 0.0, 120.0, 100.0 });
            okStep.SetInputValue("RecipeText", "10 芯 = *,*,*,*,*,*,*,*,*,*\r\n");
            okStep.SetInputValue("DisplayViewIndex", 0);

            var okPlugin = CreatePlugin(pluginType);
            okPlugin.Initialize(okStep);
            var okOutcome = RunWithImage(okPlugin, repoRoot);
            Check("框好采样区（且真框在线束上）后运行期能过",
                okOutcome.Success, $"Success={okOutcome.Success} Err=[{okOutcome.Error}]");
        }

        /// <summary>拿一张真图跑一次（用 Cable2，640x480 彩色）</summary>
        private static (bool Success, string Error) RunWithImage(VisionPluginBase plugin, string repoRoot)
        {
            HOperatorSet.ReadImage(out HObject raw, Path.Combine(repoRoot, @"Image\颜色\cable2.png"));
            var image = new HImage(raw);
            raw?.Dispose();
            try
            {
                var portType = plugin.GetType().GetProperty("Image")!;
                ((IInputPort)portType.GetValue(plugin)!).Value = image;

                var ctx = new ExecutionContext(
                    new StubLog(), new FlowSession { FlowName = "颜色检查断言" },
                    new WorkspaceContext(), new CancellationTokenSource().Token);

                // Success 由引擎的 Execute 预置为 true（插件成功时自己不该去设它），
                // 这里直接驱动 RunAlgorithm，就得照引擎的做法先补上这一句，
                // 否则测到的是"没预置的默认值 false"，而不是插件的真实行为
                plugin.Success.Value = true;
                plugin.RunAlgorithm(ctx);

                return ((bool)plugin.Success.Value, plugin.ErrorMessage.Value as string ?? "");
            }
            finally { image.Dispose(); }
        }

        private static VisionPluginBase CreatePlugin(Type pluginType)
        {
            var plugin = (VisionPluginBase)Activator.CreateInstance(pluginType)!;
            plugin.InstanceName = "颜色序列检查_0";
            return plugin;
        }

        private static System.Collections.IList GetCanvasRois(VisionPluginBase plugin)
            => (System.Collections.IList)plugin.GetType().GetProperty("CanvasRois")!.GetValue(plugin)!;

        private static double[] GetParams(VisionPluginBase plugin)
            => (double[])plugin.GetType().GetProperty("RoiParams")!.GetValue(plugin)!;

        private static DrawingObjectInfo MakeRoi(double[] pars, string name)
            => new(DrawShapeType.Rectangle, pars.Select(p => new HTuple(p)).ToArray(), name);

        private static double[] GetTuples(object roi)
        {
            var tuples = (HTuple[]?)roi.GetType().GetProperty("HTuples")!.GetValue(roi) ?? Array.Empty<HTuple>();
            return tuples.Select(t => t.D).ToArray();
        }

        private static bool SameNumbers(double[] a, double[] b)
            => a.Length == b.Length && a.Zip(b, (x, y) => Math.Abs(x - y) < 1e-9).All(v => v);
    }
}
