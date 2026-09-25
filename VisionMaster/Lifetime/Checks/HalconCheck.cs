using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Core.Interfaces;
using HalconDotNet;

namespace VisionMaster.Lifetime.Checks
{
    /// <summary>
    /// 视觉引擎自检：确认 HALCON 的原生运行时与许可在这台机器上真的可用。
    ///
    /// 为什么需要它
    /// ---------
    /// 程序目录里只有 halcondotnet.dll（.NET 托管外壳，随程序一起发布）；真正干活的
    /// halcon.dll + 40 个原生库（约 470 MB）和 license 都来自系统安装的 HALCON
    ///（%HALCONROOT%\bin\x64-win64 挂在 PATH 上）。换到一台没装 HALCON 的电脑，
    /// 程序照样能启动、方案照样能打开，但第一次调用图像算子才抛 DllNotFoundException ——
    /// 用户看到的是一个英文异常堆栈，而不是"这台机器没装 HALCON"。
    ///
    /// 为什么判 Warning 而不是 Error
    /// ---------
    /// 缺 HALCON 只影响视觉功能，不影响看方案、配流程、调通讯。判 Error 会把整个软件
    /// 挡在门外，代价远大于收益 —— 所以只提示、不阻断，也不禁用任何功能入口。
    ///
    /// 为什么"先查环境、再真调一次"
    /// ---------
    /// 只查环境会误判（文件都在、license 过期，照样崩）；只真调一次说不清原因
    ///（"不可用"三个字对用户毫无帮助）。先查环境拿线索、再真调拿结论，
    /// 两者合起来才能给出"缺什么、去哪装、怎么验证"的可执行建议。
    /// </summary>
    public class HalconCheck : IStartupCheck
    {
        /// <summary>自检最长等待时间。见 <see cref="ExecuteAsync"/> 说明</summary>
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

        private readonly ILogService _log;

        public HalconCheck(ILogService log)
        {
            _log = log;
        }

        public string Name => "视觉引擎（HALCON）";

        /// <summary>
        /// 不可用时的详细说明（多行中文，供主界面出来后的弹窗显示）；可用时为 null。
        /// 与 <see cref="CheckResult.Message"/> 分开：Splash 上那一行要短，弹窗里要说透。
        /// </summary>
        public string? FailureDetail { get; private set; }

        public async Task<CheckResult> ExecuteAsync(CancellationToken ct)
        {
            // 探测要加载原生库并初始化引擎，放后台线程执行，别卡住 Splash 的帧泵
            var probe = Task.Run(Probe, ct);

            // 超时保护（接口契约要求"不无限阻塞启动"）。
            // HALCON 的许可是常见的长阻塞源：走网络许可服务器时，服务器不可达会让引擎初始化
            // 干等很久。自检的职责是"尽快告诉用户"，不是陪着一起等 —— 超时就按不可用报，
            // 把"去查许可配置"这条线索给用户，具体原因让他自己判断。
            if (await Task.WhenAny(probe, Task.Delay(ProbeTimeout)) != probe)
            {
                // 探测任务还在跑，不取消它（HalconDotNet 的调用不可中断）；
                // 但必须吃掉它之后可能抛出的异常，否则会变成未观察异常
                _ = probe.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);

                string root = Environment.GetEnvironmentVariable("HALCONROOT") ?? "";
                return Fail(
                    $"HALCON 引擎响应超时（{ProbeTimeout.TotalSeconds:0} 秒内未完成）",
                    "  引擎初始化长时间没有返回。常见原因是许可（license）走的是网络许可服务器，"
                    + "而服务器当前不可达；也可能是 HALCON 安装损坏。请先检查许可配置，"
                    + "必要时重新安装 HALCON 23.05。",
                    null, root, FindHalconDllDirectory(), FindLicenseFile(root));
            }

            return await probe;
        }

        private CheckResult Probe()
        {
            string root = Environment.GetEnvironmentVariable("HALCONROOT") ?? "";
            string? dllDir = FindHalconDllDirectory();
            string? licenseFile = FindLicenseFile(root);

            // 版本号只是锦上添花，拿不到不影响判定
            string version = "";
            try
            {
                HOperatorSet.GetSystem("version", out HTuple hv);
                // 用 .S 而不是 ToString()：后者会把字符串元组连引号一起打出来（HALCON "23.05"）
                version = hv?.S ?? "";
            }
            catch
            {
                // 忽略：下面的真调用才是判定依据
            }

            // 判定：造一张 8x8 的图再扔掉。
            // 这一步会真正加载 halcon.dll 并触发许可检查，最接近"用户点一下图像功能"。
            try
            {
                HOperatorSet.GenImageConst(out HObject probe, "byte", 8, 8);
                probe.Dispose();
            }
            catch (Exception ex)
            {
                return Fail(Classify(ex), BuildAdvice(ex, root, dllDir, licenseFile), ex, root, dllDir, licenseFile);
            }

            FailureDetail = null;
            string desc = string.IsNullOrEmpty(version) ? "引擎就绪" : $"HALCON {version} 引擎就绪";
            _log.Info($"[启动自检] 视觉引擎可用：{desc}" +
                      $"（HALCONROOT={(string.IsNullOrEmpty(root) ? "未设置" : root)}）");
            return CheckResult.Ok(desc);
        }

        /// <summary>
        /// 统一的失败出口：组装弹窗文案 + 落日志 + 返回 Warning 级结果。
        /// 集中在一处，是为了让"失败时一定会写日志、一定会准备好弹窗文案"成为结构上的保证 ——
        /// 分散成几个分支的话，将来新增一种失败太容易漏掉其中一项。
        /// </summary>
        private CheckResult Fail(string reason, string advice, Exception? ex,
            string root, string? dllDir, string? licenseFile)
        {
            var sb = new StringBuilder();
            sb.AppendLine("本机的 HALCON 视觉引擎不可用，图像相关功能目前无法运行。");
            sb.AppendLine();
            sb.AppendLine($"原因：{reason}");
            sb.AppendLine();
            sb.AppendLine("怎么处理：");
            sb.AppendLine(advice);
            sb.AppendLine();
            sb.AppendLine("受影响的：图像采集、图像处理、图像脚本、流程运行等所有视觉功能。");
            sb.AppendLine("不受影响的：方案编辑、流程编排、通讯配置、组态画面 —— 这些照常可用。");
            sb.AppendLine();
            sb.AppendLine("诊断信息（排查时可提供给售后或供应商）：");
            sb.AppendLine($"  HALCONROOT      : {(string.IsNullOrEmpty(root) ? "(未设置)" : root)}");
            sb.AppendLine($"  PATH 中的 HALCON: {dllDir ?? "(没找到含 halcon.dll 的目录)"}");
            sb.AppendLine($"  license 文件    : {licenseFile ?? "(未找到)"}");
            sb.AppendLine($"  错误详情        : {(ex == null ? "(自检超时，无异常信息)" : ex.GetType().Name + ": " + ex.Message)}");

            FailureDetail = sb.ToString().TrimEnd();
            _log.Error($"[启动自检] 视觉引擎不可用：{reason}"
                       + (ex == null ? "" : $" —— {ex.GetType().Name}: {ex.Message}"));

            return CheckResult.Fail(CheckLevel.Warning, reason);
        }

        /// <summary>
        /// 在 PATH 里找真正放着 halcon.dll 的那个目录。
        ///
        /// 只看 PATH、不看 HALCONROOT：决定原生库能否被加载的是 PATH（Windows 的 DLL 搜索顺序），
        /// HALCONROOT 只影响 license 的查找位置。把两者混为一谈，给出的修复建议就会指错地方
        ///（典型症状：用户明明配了 HALCONROOT，却还是加载失败）。
        /// </summary>
        private static string? FindHalconDllDirectory()
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string segment in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string dir = segment.Trim().Trim('"');
                if (dir.Length == 0) continue;
                try
                {
                    if (File.Exists(Path.Combine(dir, "halcon.dll"))) return dir;
                }
                catch
                {
                    // 非法路径段（含通配符、非法字符等）直接跳过
                }
            }
            return null;
        }

        /// <summary>
        /// 找候选的 HALCON 许可文件：先看 HALCON_LICENSE_FILE 环境变量，再看 HALCONROOT\license。
        ///
        /// 找不到不等于一定不能用（许可也可能由许可服务器提供），所以这个结果只用于
        /// 把建议写得更准，不单独作为判定依据 —— 判定永远交给真调用。
        /// </summary>
        private static string? FindLicenseFile(string halconRoot)
        {
            string envFile = Environment.GetEnvironmentVariable("HALCON_LICENSE_FILE") ?? "";
            if (envFile.Length > 0 && File.Exists(envFile)) return envFile;

            if (string.IsNullOrWhiteSpace(halconRoot)) return null;
            try
            {
                string dir = Path.Combine(halconRoot, "license");
                if (!Directory.Exists(dir)) return null;
                return Directory.EnumerateFiles(dir, "license*.dat").FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>把异常翻译成一句人话（Splash 那一行要短，所以这里只给结论、不给处置办法）</summary>
        private static string Classify(Exception ex) => ex switch
        {
            DllNotFoundException => "找不到 halcon.dll —— 本机没有可用的 HALCON 运行时",
            BadImageFormatException => "HALCON 的位数与本程序不一致（本程序是 64 位）",
            TypeInitializationException => "HALCON 引擎初始化失败",
            _ => LooksLikeLicense(ex) ? "HALCON 许可（license）不可用" : "HALCON 引擎调用失败"
        };

        /// <summary>HALCON 的许可问题没有独立异常类型，只能在消息里认关键字</summary>
        private static bool LooksLikeLicense(Exception ex)
        {
            string msg = (ex.Message ?? "") + " " + (ex.InnerException?.Message ?? "");
            return msg.IndexOf("license", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.Contains("#2106")
                || msg.Contains("#2009");
        }

        private static string BuildAdvice(Exception ex, string root, string? dllDir, string? licenseFile)
        {
            if (ex is BadImageFormatException)
                return "  装成了 32 位的 HALCON，请改装 64 位（x64-win64）版本，然后重启本程序。";

            if (ex is DllNotFoundException)
            {
                // 三种情况分开说：目录已配好但加载仍失败 / 完全没装 / 装了但 PATH 没配。
                // 它们的处置办法完全不同，笼统说"请安装 HALCON"会让已经装了的用户白折腾一遍。
                if (dllDir != null)
                    return $"  PATH 里已经有 {dllDir}，但仍加载失败：请确认该目录下的 halcon.dll 完整（可重新安装 HALCON），"
                         + "并确认没有被杀毒软件拦截。";

                if (string.IsNullOrEmpty(root))
                    return "  本机没有安装 HALCON。请安装 HALCON 23.05（64 位）；"
                         + "若本机已有 HALCON，请把它安装目录下的 bin\\x64-win64 加入系统 PATH 环境变量，"
                         + "然后重启本程序。";

                return $"  检测到 HALCONROOT={root}，但 PATH 里没有放 halcon.dll 的目录。"
                     + $"请把 {Path.Combine(root, "bin", "x64-win64")} 加入系统 PATH 环境变量后重启本程序。";
            }

            if (LooksLikeLicense(ex))
            {
                if (licenseFile == null)
                {
                    string licenseDir = Path.Combine(
                        string.IsNullOrEmpty(root) ? "<HALCONROOT>" : root, "license");
                    return "  找到了 HALCON，但没找到许可文件。请把供应商提供的 license_*.dat 放到"
                         + $" {licenseDir} 目录，或用环境变量 HALCON_LICENSE_FILE 指定它的位置。";
                }

                return $"  许可文件已找到（{licenseFile}），但引擎认定它无效：常见原因是已过期、"
                     + "绑定的机器变了（换机或换网卡），或授权数量已占满。请联系供应商更新许可。";
            }

            return "  请先确认 HALCON 安装完整（halcon.dll 与同目录的原生库齐全），必要时重新安装"
                 + " HALCON 23.05；仍无法解决，请把上面的诊断信息发给售后。";
        }
    }
}
