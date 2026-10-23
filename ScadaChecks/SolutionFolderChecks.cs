using System;
using System.IO;
using System.Linq;
using VisionMaster.Models;
using VisionMaster.Services;

namespace ScadaChecks
{
    /// <summary>
    /// [SF] 方案文件夹化保存（2026-10-23）。
    ///
    /// 需求：保存“方案A”时生成一个以方案命名的文件夹，配置、图片、报警、日志都在里面；
    /// 再次打开时按文件夹内容一一对应。这里钉住四件事：
    ///  ① 命名规则（非法字符 / 结尾点 / 保留设备名 / 空名）；
    ///  ② 保存落点（&lt;位置&gt;\方案A\方案A.vms）与目录骨架（Image / Alarms / History / Logs）；
    ///  ③ 读取口径（方案根 = .vms 所在目录；图片相对路径与日志目录都相对它解析）；
    ///  ④ 冲突与另存为（一个文件夹只放一个方案；另存为把资产一起带走）。
    ///
    /// 放在 ScadaChecks 而不是 SolutionProbe：Probe 是一次性的方案生成脚手架，
    /// 本组是常规闸门，必须跟着 <c>tools\run_gates.ps1</c> 每次都跑。
    /// </summary>
    internal partial class Program
    {
        private static void SolutionFolderChecks()
        {
            Section("[SF] 方案文件夹化保存（目录约定 / 日志归属 / 保存-读取一致）");

            var root = Path.Combine(Path.GetTempPath(), "SolutionFolder_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            var previousProvider = LogService.SolutionRootProvider;
            LogService? log = null;

            try
            {
                // ---------- 1. 命名规则 ----------
                Check("[SF1] 文件夹名 = 方案名",
                    string.Equals(SolutionPaths.GetSolutionFolder(root, "方案A"),
                        Path.Combine(root, "方案A"), StringComparison.Ordinal),
                    SolutionPaths.GetSolutionFolder(root, "方案A"));

                Check("[SF2] 配置文件与文件夹同名（<方案名>.vms）",
                    string.Equals(Path.GetFileName(SolutionPaths.GetConfigFilePath(root, "方案A")),
                        "方案A.vms", StringComparison.Ordinal),
                    SolutionPaths.GetConfigFilePath(root, "方案A"));

                var sanitized = SolutionPaths.SanitizeName("A/B:C*?\"<>|");
                Check("[SF3] 非法字符换成下划线（不抛异常、不产生子目录）",
                    sanitized == "A_B_C______" && sanitized.IndexOf('/') < 0 && sanitized.IndexOf('\\') < 0,
                    sanitized);

                Check("[SF4] 结尾的空格与点被去掉（Windows 会静默吞掉）",
                    SolutionPaths.SanitizeName("方案A. ") == "方案A"
                    && SolutionPaths.SanitizeName("方案A .") == "方案A",
                    SolutionPaths.SanitizeName("方案A. ") + " / " + SolutionPaths.SanitizeName("方案A ."));

                Check("[SF5] 空名回落兜底名",
                    SolutionPaths.SanitizeName("   ") == SolutionPaths.FallbackName,
                    SolutionPaths.SanitizeName("   "));

                Check("[SF6] 保留设备名加前缀（CON / CON.txt 都不能用）",
                    SolutionPaths.SanitizeName("CON") == "_CON"
                    && SolutionPaths.SanitizeName("CON.txt") == "_CON.txt",
                    SolutionPaths.SanitizeName("CON") + " / " + SolutionPaths.SanitizeName("CON.txt"));

                // ---------- 2. 保存落点与目录骨架 ----------
                var service = new SolutionService();
                var solution = new SolutionModel { SolutionName = "方案A" };

                Check("[SF7] 保存位置校验通过",
                    SolutionPaths.TryValidateSaveTarget(root, "方案A",
                        out var folder, out var file, out var validateError),
                    validateError);

                Check("[SF8] 目标形如 <位置>\\方案A\\方案A.vms",
                    string.Equals(folder, Path.Combine(root, "方案A"), StringComparison.Ordinal)
                    && string.Equals(file, Path.Combine(folder, "方案A.vms"), StringComparison.Ordinal),
                    file);

                var saved = service.SaveAsync(solution, file).GetAwaiter().GetResult();
                Check("[SF9] 保存成功且配置文件真的落地",
                    saved.Success && File.Exists(file), saved.Message);

                var structure = SolutionPaths.EnsureStructure(file, out var structureError);
                Check("[SF10] 目录骨架建全（Image / Alarms / History / Logs）",
                    structure && SolutionPaths.StructureFolderNames.All(
                        n => Directory.Exists(Path.Combine(folder, n))),
                    structureError);

                solution.SolutionFilePath = file;
                Check("[SF11] 方案根 = 配置文件所在目录",
                    string.Equals(SolutionPaths.GetSolutionRoot(file), folder, StringComparison.Ordinal),
                    SolutionPaths.GetSolutionRoot(file) ?? "(null)");

                Check("[SF12] 日志目录 = <方案根>\\Logs",
                    string.Equals(SolutionPaths.GetLogDirectory(file),
                        Path.Combine(folder, "Logs"), StringComparison.Ordinal),
                    SolutionPaths.GetLogDirectory(file) ?? "(null)");

                Check("[SF22] 规范落点被判为文件夹布局（再次保存直接写回，不再弹位置）",
                    SolutionPaths.IsFolderLayout(file, "方案A"), file);

                // ---------- 3. 读取一致 ----------
                var loaded = service.LoadAsync(file).GetAwaiter().GetResult();
                Check("[SF13] 打开后方案名与文件夹里的配置一致（.vms 是唯一配置源）",
                    loaded.Success && loaded.Data?.SolutionName == "方案A", loaded.Message);

                // 图片：导入落在 <方案根>\Image，.vms 里只存相对路径；整目录拷走仍能解析
                // 1×1 透明 PNG：SF26 要真解码一次，随便几个字节会让 TryLoad 以“解码失败”返回 false。
                var png = Path.Combine(root, "sample.png");
                File.WriteAllBytes(png, Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
                var imported = SolutionAssetService.TryImport(file, png, out var relative, out var importError);
                Check("[SF14] 图片导入落到方案 Image 目录并返回相对路径",
                    imported
                    && string.Equals(relative, "Image\\sample.png", StringComparison.Ordinal)
                    && File.Exists(Path.Combine(folder, relative)),
                    importError);

                Check("[SF15] 相对路径能按方案根解析回来（换机器拷文件夹不丢图）",
                    SolutionAssetService.TryResolve(file, relative, out var absolute, out var resolveError)
                    && string.Equals(absolute, Path.Combine(folder, "Image", "sample.png"), StringComparison.Ordinal),
                    resolveError);

                // 运行窗口与属性面板手里只有方案目录（SolutionRoot），没有 .vms 路径。
                // 资产服务必须两种都收——只收文件路径的话，目录会被再剥一层，解析到方案文件夹的上一级。
                Check("[SF26] 资产服务同时接受方案根目录（运行窗口/属性面板的调用形态）",
                    SolutionAssetService.TryResolve(folder, relative, out var fromRoot, out var rootError)
                    && string.Equals(fromRoot, Path.Combine(folder, "Image", "sample.png"), StringComparison.Ordinal)
                    && SolutionAssetService.TryLoad(folder, relative, out var fromRootImage, out _)
                    && fromRootImage != null,
                    rootError);

                Check("[SF27] 文件路径与根目录归一化到同一根",
                    string.Equals(SolutionAssetService.NormalizeSolutionRoot(file), folder, StringComparison.Ordinal)
                    && string.Equals(SolutionAssetService.NormalizeSolutionRoot(folder), folder, StringComparison.Ordinal),
                    SolutionAssetService.NormalizeSolutionRoot(folder) ?? "(null)");

                // ---------- 4. 冲突：一个文件夹只放一个方案 ----------
                //
                // 正常路径下不会撞车：文件夹名就是方案名，两个不同名的方案天然各占一个文件夹。
                // 真正要拦的是"这个文件夹已经是别人的"——里面有另一个名字的 .vms
                // （手工改名 / 从别处拷来的），再往里存就会变成两个方案挤一间。
                var mixedParent = Path.Combine(root, "mixed");
                Directory.CreateDirectory(Path.Combine(mixedParent, "混装"));
                File.WriteAllText(Path.Combine(mixedParent, "混装", "别的方案.vms"), "{}");

                Check("[SF16] 文件夹里已有另一个方案的 .vms 时拒绝保存",
                    !SolutionPaths.TryValidateSaveTarget(mixedParent, "混装",
                        out _, out _, out var conflictError)
                    && conflictError.Contains("别的方案"),
                    conflictError);

                Check("[SF17] 同一方案重复保存不拒绝（正常覆盖）",
                    SolutionPaths.TryValidateSaveTarget(root, "方案A", out _, out _, out _),
                    "");

                // 长路径：MAX_PATH 260 的余量。必须在校验阶段拒绝，不能等写盘抛系统异常。
                var longName = new string('长', 130);
                Check("[SF24] 路径超过 240 字符直接拒绝",
                    !SolutionPaths.TryValidateSaveTarget(root, longName, out _, out _, out var longError)
                    && longError.Contains("路径过长"),
                    longError);

                // 保存失败后的清理原语：只删空目录，非空目录绝不动（里面可能是用户资产）。
                var emptyFolder = Path.Combine(root, "空壳");
                Directory.CreateDirectory(emptyFolder);
                var deletedEmpty = SolutionPaths.TryDeleteEmptyFolder(emptyFolder);

                var nonEmptyFolder = Path.Combine(root, "非空");
                Directory.CreateDirectory(nonEmptyFolder);
                File.WriteAllText(Path.Combine(nonEmptyFolder, "keep.txt"), "x");
                var keptNonEmpty = !SolutionPaths.TryDeleteEmptyFolder(nonEmptyFolder)
                                   && Directory.Exists(nonEmptyFolder)
                                   && File.Exists(Path.Combine(nonEmptyFolder, "keep.txt"));

                Check("[SF25] 保存失败清理只删空目录（非空目录与用户文件不动）",
                    deletedEmpty && !Directory.Exists(emptyFolder) && keptNonEmpty,
                    $"emptyDeleted={deletedEmpty} / nonEmptyKept={keptNonEmpty}");

                // ---------- 5. 另存为：资产一起带走 ----------
                // 先往旧目录的 Alarms / History / Logs 里各放一份内容——只断言“目录存在”会被 EnsureStructure 的
                // 空目录蒙过去（那不能证明 CopyAlongside 真的复制了）。
                File.WriteAllText(Path.Combine(folder, "Alarms", "AlarmHistory.json"), "{\"probe\":1}");
                File.WriteAllText(Path.Combine(folder, "History", "2026-10-23.csv"), "时间,SN\r\n2026-10-23 08:00:00,SN-1\r\n");
                File.WriteAllText(Path.Combine(folder, "Logs", "probe.log"), "probe");

                var targetParent = Path.Combine(root, "target");
                Directory.CreateDirectory(targetParent);
                SolutionPaths.TryValidateSaveTarget(targetParent, "方案A",
                    out var newFolder, out var newFile, out _);
                var saveAs = service.SaveAsync(solution, newFile).GetAwaiter().GetResult();
                SolutionPaths.EnsureStructure(newFile, out _);
                var copyFailures = SolutionAssetFolders.CopyAlongside(file, newFile);

                Check("[SF18] 另存为把 Image / Alarms / History / Logs 的内容一起带到新方案文件夹",
                    saveAs.Success
                    && copyFailures.Length == 0
                    && File.Exists(Path.Combine(newFolder, "Image", "sample.png"))
                    && File.Exists(Path.Combine(newFolder, "Alarms", "AlarmHistory.json"))
                    && File.Exists(Path.Combine(newFolder, "History", "2026-10-23.csv"))
                    && File.Exists(Path.Combine(newFolder, "Logs", "probe.log")),
                    copyFailures.Length == 0 ? "" : string.Join("；", copyFailures));

                Check("[SF19] 资产清单与目录约定同源（不再有第三份硬编码名字）",
                    SolutionAssetFolders.AssetFolderNames.SequenceEqual(new[]
                    {
                        SolutionPaths.ImageFolderName,
                        SolutionPaths.AlarmsFolderName,
                        SolutionPaths.HistoryFolderName,
                        SolutionPaths.LogsFolderName
                    }),
                    string.Join(",", SolutionAssetFolders.AssetFolderNames));

                // 兼容：文件夹化只约束“新保存”的落点。老方案是散装的 .vms（直接放在某个目录里），
                // 打开后方案根仍是它所在目录——不许因为没同名文件夹就拒绝加载。
                var legacyPath = Path.Combine(root, "老方案.vms");
                File.WriteAllText(legacyPath,
                    SolutionService.Serialize(new SolutionModel { SolutionName = "老方案" }));
                var legacyLoad = service.LoadAsync(legacyPath).GetAwaiter().GetResult();
                Check("[SF21] 散装老方案仍可打开，方案根 = .vms 所在目录",
                    legacyLoad.Success
                    && legacyLoad.Data?.SolutionName == "老方案"
                    && string.Equals(SolutionPaths.GetSolutionRoot(legacyPath), root, StringComparison.Ordinal),
                    legacyLoad.Message);

                Check("[SF23] 散装老方案不被判为文件夹布局（第一次保存会走另存为、搬进文件夹）",
                    !SolutionPaths.IsFolderLayout(legacyPath, "老方案"), legacyPath);

                // ---------- 6. 日志归属：同一行写全局 + 方案 Logs ----------
                //
                // 用真实 LogService 跑一遍：它按 200ms 轮询落盘线程，这里最多等 8 秒。
                // 判据是"方案 Logs 里出现这条标记"，不是"日志目录存在"——目录可能被别的步骤建出来。
                LogService.SolutionRootProvider = () => folder;
                log = new LogService();
                var marker = "SF-log-" + Guid.NewGuid().ToString("N");
                log.Info(marker);

                var solutionLog = Path.Combine(folder, SolutionPaths.LogsFolderName,
                    DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                var globalLog = Path.Combine(log.LogDirectory,
                    DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (DateTime.UtcNow < deadline
                       && !(File.Exists(solutionLog) && ReadAllTextShared(solutionLog).Contains(marker)))
                {
                    System.Threading.Thread.Sleep(100);
                }

                Check("[SF20] 方案日志写进 <方案根>\\Logs，且全局日志同样有这一行（双写）",
                    File.Exists(solutionLog) && ReadAllTextShared(solutionLog).Contains(marker)
                    && File.Exists(globalLog) && ReadAllTextShared(globalLog).Contains(marker),
                    solutionLog + " / " + globalLog);

                // 没有方案时只写全局：先让落盘线程看到 provider 变空（它每 200ms 现读一次），再写。
                // 若这里漏了等待，标记可能正好落在“切换前的那一轮”，断言会假红。
                LogService.SolutionRootProvider = null;
                System.Threading.Thread.Sleep(400);
                var noSolutionMarker = "SF-nosolution-" + Guid.NewGuid().ToString("N");
                log.Info(noSolutionMarker);

                var noSolutionDeadline = DateTime.UtcNow.AddSeconds(8);
                while (DateTime.UtcNow < noSolutionDeadline
                       && !(File.Exists(globalLog) && ReadAllTextShared(globalLog).Contains(noSolutionMarker)))
                {
                    System.Threading.Thread.Sleep(100);
                }

                Check("[SF20b] 没有方案时只写全局，不往上一份方案目录续写",
                    ReadAllTextShared(globalLog).Contains(noSolutionMarker)
                    && !ReadAllTextShared(solutionLog).Contains(noSolutionMarker),
                    globalLog);

                // 切换方案：新目录收到后续行，旧目录不再增长（最容易写错的一段）
                var folderB = Path.Combine(root, "方案B");
                Directory.CreateDirectory(folderB);
                LogService.SolutionRootProvider = () => folderB;
                System.Threading.Thread.Sleep(400);

                var switchMarker = "SF-switch-" + Guid.NewGuid().ToString("N");
                log.Info(switchMarker);

                var logB = Path.Combine(folderB, SolutionPaths.LogsFolderName,
                    DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                var switchDeadline = DateTime.UtcNow.AddSeconds(8);
                while (DateTime.UtcNow < switchDeadline
                       && !(File.Exists(logB) && ReadAllTextShared(logB).Contains(switchMarker)))
                {
                    System.Threading.Thread.Sleep(100);
                }

                Check("[SF20c] 切换方案后日志写新方案目录，旧目录不再收到这一行",
                    File.Exists(logB) && ReadAllTextShared(logB).Contains(switchMarker)
                    && !ReadAllTextShared(solutionLog).Contains(switchMarker),
                    logB);
            }
            finally
            {
                LogService.SolutionRootProvider = previousProvider;
                try { log?.Dispose(); } catch { }
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// 读正在被日志线程追加的文件。必须显式给 <see cref="FileShare.ReadWrite"/>：
        /// 写入端以 <c>FileShare.Read</c> 打开（允许别人读），但 Windows 的共享检查是对称的——
        /// 读端也得允许写入端继续持有写句柄，否则 <c>File.ReadAllText</c> 直接抛共享冲突。
        /// </summary>
        private static string ReadAllTextShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}
