using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.ScadaWeb;
using VisionMaster.Services;

namespace ScadaWebChecks
{
    /// <summary>
    /// M2a 终审 P0 的回归断言（2026-10-21）。
    ///
    /// 这些断言只钉"终审抓到过、且编译期全绿"的静默缺陷：
    ///   R1 新画面版本基线 &gt; 0（否则三闸把已加载方案的所有命令判成缺 pageVersion）
    ///   R2 动作副本保留 ArrayIndex（否则写数组元素变成写整个变量）
    ///   R3 软件配置重载保持 Current / ScadaWeb 对象身份（否则运行期配置切换对已启动模块无效）
    ///   R4 客户端 caps 帧会更新会话只读态（否则 AllowCommands 热切换后按钮永远不可点）
    /// </summary>
    public static class M2RegressionChecks
    {
        public static int Failures { get; private set; }

        public static int Pass => _total - Failures;

        public static int Total => _total;

        private static int _total;

        public static void Run()
        {
            Failures = 0;
            _total = 0;

            Console.WriteLine();
            Console.WriteLine("--- M2RegressionChecks：M2a 终审 P0 回归 ---");

            // R1：Version 基线
            var page = new ScadaPage { Name = "版本基线" };
            Check("R1", page.Version > 0,
                $"新建画面 Version={page.Version}（必须 > 0，否则已加载方案的 pageVersion 被三闸当缺省拒绝）");

            // R2：ArrayIndex 深拷贝
            var sourceAction = new ScadaAction
            {
                Type = ScadaActionType.WriteVariable,
                VariableName = "数组",
                ArrayIndex = 3,
                Value = "1"
            };
            var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
            hook.Actions.Add(sourceAction);

            var copy = ScadaWebActionFilter.BuildAllowedCopy(
                hook,
                new HashSet<ScadaActionType> { ScadaActionType.WriteVariable },
                out var skipped);

            var copiedIndex = copy?.Actions.Count == 1 ? copy.Actions[0].ArrayIndex : int.MinValue;
            Check("R2",
                copiedIndex == 3 && sourceAction.ArrayIndex == 3 && skipped.Count == 0,
                $"副本 ArrayIndex={copiedIndex}（期望 3），原动作={sourceAction.ArrayIndex}，skipped={skipped.Count}");

            // R3：配置对象身份 + 原地更新
            var configOk = CheckConfigReloadIdentity();
            Check("R3", configOk.Ok, configOk.Detail);

            // R4：客户端 caps 只读态接线
            var jsPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "app.js");
            var js = File.Exists(jsPath) ? File.ReadAllText(jsPath, Encoding.UTF8) : string.Empty;
            var readsCapsReadOnly = js.Contains("capsReadOnly", StringComparison.Ordinal)
                                    && js.Contains("state.readOnly = capsReadOnly", StringComparison.Ordinal);
            var capsRefreshesTopbar = js.Contains("case 'caps':", StringComparison.Ordinal)
                                      && js.Contains("updateTopbar();", StringComparison.Ordinal);
            Check("R4", readsCapsReadOnly && capsRefreshesTopbar,
                $"app.js 读 caps.readOnly={readsCapsReadOnly}，caps 后刷新顶栏={capsRefreshesTopbar}，len={js.Length}");
        }

        private static (bool Ok, string Detail) CheckConfigReloadIdentity()
        {
            var service = new AppSettingsService();
            var before = service.Current;
            var beforeWeb = service.Current.ScadaWeb;

            var path = Path.Combine(AppContext.BaseDirectory, "AppConfig.json");
            var backup = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;

            try
            {
                File.WriteAllText(path,
                    "{\"ScadaWeb\":{\"Enabled\":true,\"AllowCommands\":true,\"Port\":18080}}",
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                service.Load();

                var sameRoot = ReferenceEquals(before, service.Current);
                var sameNode = ReferenceEquals(beforeWeb, service.Current.ScadaWeb);
                var applied = service.Current.ScadaWeb.AllowCommands && service.Current.ScadaWeb.Port == 18080;

                return (sameRoot && sameNode && applied,
                    $"根对象同一={sameRoot}，ScadaWeb 同一={sameNode}，新值生效={applied}（AllowCommands={service.Current.ScadaWeb.AllowCommands}，Port={service.Current.ScadaWeb.Port}）");
            }
            catch (Exception ex)
            {
                return (false, $"异常：{ex.GetType().Name}：{ex.Message}");
            }
            finally
            {
                try
                {
                    if (backup == null) File.Delete(path);
                    else File.WriteAllText(path, backup, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                    service.Load(); // 恢复内存里的 Current，别把测试用的 18080 留给后续断言
                }
                catch
                {
                    // 恢复失败不改变本条判据
                }
            }
        }

        private static void Check(string id, bool ok, string detail)
        {
            _total++;
            if (!ok) Failures++;

            Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + id + "  " + detail);
        }
    }
}
