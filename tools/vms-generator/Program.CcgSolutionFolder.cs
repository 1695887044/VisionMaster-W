using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// =====================================================================
//  「一个方案一个文件夹」落盘骨架
//
//  为什么要有这一层：方案不是单个 .vms 文件，而是
//      <保存位置>\<方案名>\<方案名>.vms
//      <保存位置>\<方案名>\Image\   Alarms\   History\   Logs\
//  这四层子目录是宿主约定的方案结构（Core\SolutionPaths.cs:57-63 的
//  StructureFolderNames，建目录的口径在 :281 EnsureStructure）。
//
//  资源纪律（docs\UI设计基线\scada-controls\README.md:150 用户裁定、
//  scada-controls-batch3\README.md:163）：
//    ① .vms 里的图片只写方案内相对路径 Image\xxx.png；
//    ② 绝不写绝对路径、绝不引用方案目录之外的文件（含 ..\ 往上爬）；
//    ③ 整个方案目录（.vms + Image\ + ...）一起拷走就能用；
//    ④ 同名同内容按哈希去重，同名不同内容自动改名。
//
//  本文件不复用 Core\SolutionPaths.cs（生成器是独立 net9.0 控制台工程，
//  不引用契约程序集），但每一条规则都对齐它的实现，改名口径逐字一致。
// =====================================================================
internal static partial class Program
{
    /// <summary>方案保存位置（父目录）：<c>D:\C#\VM\解决方案</c></summary>
    const string SolutionsRoot = @"D:\C#\VM\解决方案";

    /// <summary>生成器的样图源目录（只在生成时读，不进方案）</summary>
    const string SourceImageRoot = @"D:\C#\VM\Image\images";

    /// <summary>方案结构子目录（与 Core\SolutionPaths.cs:57-63 逐字一致、顺序一致）</summary>
    static readonly string[] SolutionStructureFolders = { "Image", "Alarms", "History", "Logs" };

    /// <summary>Windows 保留设备名（与 Core\SolutionPaths.cs:69-74 一致）</summary>
    static readonly System.Collections.Generic.HashSet<string> SolutionReservedNames =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

    // ------------------------------------------------------------------
    //  路径口径
    // ------------------------------------------------------------------

    /// <summary>方案名 → 可安全用作文件夹/文件名的名字（对齐 SolutionPaths.SanitizeName）</summary>
    static string SanitizeSolutionName(string solutionName)
    {
        var raw = (solutionName ?? string.Empty).Trim();
        if (raw.Length == 0)
            return "未命名方案";

        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
            sb.Append(System.Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);

        // 只剥末尾的空格与点：Windows 会静默吞掉它们
        var cleaned = sb.ToString().Trim().TrimEnd(' ', '.');
        if (cleaned.Length == 0)
            return "未命名方案";

        var stem = cleaned.Split('.')[0];
        if (SolutionReservedNames.Contains(cleaned) || SolutionReservedNames.Contains(stem))
            cleaned = "_" + cleaned;

        return cleaned;
    }

    /// <summary>方案文件夹：<c>解决方案\&lt;方案名&gt;</c></summary>
    static string SolutionFolderPath(string solutionName)
        => System.IO.Path.Combine(SolutionsRoot, SanitizeSolutionName(solutionName));

    /// <summary>方案配置文件：<c>解决方案\&lt;方案名&gt;\&lt;方案名&gt;.vms</c></summary>
    static string SolutionConfigPath(string solutionName)
        => System.IO.Path.Combine(SolutionFolderPath(solutionName), SanitizeSolutionName(solutionName) + ".vms");

    /// <summary>
    /// 样图库里的源文件相对位置（相对 <see cref="SourceImageRoot"/>）→ 复制进方案 <c>Image\</c> 后用的文件名。
    ///
    /// 为什么要有映射而不是直接把相对路径照抄：样图库是按主题分子目录的
    /// （<c>blister\</c>、<c>caliper\</c>、<c>label\</c>、<c>packaging\</c>、<c>brake_disk\</c>），
    /// 而方案自己的 <c>Image\</c> 是**平铺**的一层——宿主的资产口径就是 <c>Image\文件名</c>
    /// （见 VisionMaster\Services\SolutionAssetService.cs:232-240：相对路径拼到方案根后必须落在
    /// <c>Image\</c> 里面）。把子目录层级带进方案，等于让 .vms 里出现 <c>Image\caliper\a.png</c>
    /// 这种"方案里其实没有这层目录"的引用，搬迁后就会缺图。
    ///
    /// 表里没有的键 = 生成器写错了图名，直接抛（不静默跳过，否则方案里留一个指向空文件的引用）。
    /// </summary>
    static readonly System.Collections.Generic.Dictionary<string, string> SourceImageMap =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            [@"bga_14x14_defects.png"] = @"bga_14x14_defects.png",
            [@"bga_14x14_model.png"] = @"bga_14x14_model.png",
            [@"blister\blister_01.png"] = @"blister_01.png",
            [@"blister\blister_reference.png"] = @"blister_reference.png",
            [@"packaging\cookie_box_01.png"] = @"cookie_box_01.png",
            [@"label\label_01.png"] = @"label_01.png",
            [@"caliper\caliper_1.png"] = @"caliper_1.png",
            [@"caliper\caliper_with_tilt.png"] = @"caliper_with_tilt.png",
            [@"caliper\caliper_calib_01.png"] = @"caliper_calib_01.png",
            [@"brake_disk\brake_disk_part_01.png"] = @"brake_disk_part_01.png",
        };

    /// <summary>
    /// 把方案内的 <c>Image\文件名</c> 还原成生成期可读的源文件绝对路径。
    /// 反查 <see cref="SourceImageMap"/>：同名条目必须唯一，否则无法确定该复制哪一张。
    /// </summary>
    static string SourceImagePathOf(string relativePath)
    {
        var normalized = (relativePath ?? string.Empty).Replace('/', '\\').TrimStart('\\');

        // 相对路径必须是 Image\ 打头（方案内资产），且只剩一层文件名
        if (!normalized.StartsWith("Image\\", System.StringComparison.OrdinalIgnoreCase))
            throw new System.InvalidOperationException("方案内图片路径必须以 Image\\ 打头：" + relativePath);

        var fileName = normalized.Substring("Image\\".Length);
        if (fileName.IndexOf('\\') >= 0)
            throw new System.InvalidOperationException(
                "方案内图片必须是平铺的 Image\\文件名（不带子目录）：" + relativePath);

        string? source = null;
        var hits = 0;
        foreach (var pair in SourceImageMap)
        {
            if (!string.Equals(pair.Value, fileName, System.StringComparison.OrdinalIgnoreCase))
                continue;

            hits++;
            source = pair.Key;
        }

        if (hits == 0)
            throw new System.InvalidOperationException(
                "样图映射里没有这张图：" + relativePath + "（请在 SourceImageMap 里登记源文件位置）");
        if (hits > 1)
            throw new System.InvalidOperationException(
                "样图映射里有多张源文件对应同一个方案内文件名：" + fileName + "（映射有歧义）");

        return System.IO.Path.Combine(SourceImageRoot, source!);
    }

    // ------------------------------------------------------------------
    //  方案文件夹化落盘
    // ------------------------------------------------------------------

    /// <summary>
    /// 把方案写进「一个方案一个文件夹」布局：
    /// ① 建 &lt;方案名&gt;\ 与 Image/Alarms/History/Logs；
    /// ② 把 .vms 里出现的每个 Image\xxx.png 从样图库复制进方案自己的 Image\；
    /// ③ 写 .vms（UTF-8 无 BOM）；
    /// ④ 逐项自检：任何绝对路径 / ..\ 都判失败。
    /// 返回方案配置文件全路径，失败抛异常（生成器要显式红着停，不静默产出半成品）。
    /// </summary>
    static string WriteFolderSolution(JObject solution, string solutionName)
    {
        if (solution["SolutionName"]?.Value<string>() != solutionName)
            throw new System.InvalidOperationException(
                "SolutionName 与落盘名不一致：" + solution["SolutionName"] + " / " + solutionName);

        var folder = SolutionFolderPath(solutionName);
        var configPath = SolutionConfigPath(solutionName);

        System.IO.Directory.CreateDirectory(folder);
        foreach (var sub in SolutionStructureFolders)
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(folder, sub));

        var imageFolder = System.IO.Path.Combine(folder, "Image");

        // 收集方案里引用的所有图片相对路径（Hmi.Picture 的 SourcePath、图像采集的 FilePath、标定的 SourceImagePath）
        var referenced = CollectImageReferences(solution);
        var copied = new System.Collections.Generic.List<string>();
        foreach (var rel in referenced)
        {
            var source = SourceImagePathOf(rel);
            if (!System.IO.File.Exists(source))
                throw new System.IO.FileNotFoundException("样图不存在：" + source);

            var fileName = System.IO.Path.GetFileName(rel);
            var dest = System.IO.Path.Combine(imageFolder, fileName);

            if (System.IO.File.Exists(dest))
            {
                // 同名同内容按哈希去重；同名不同内容自动改名（不覆盖已有资产）
                if (!SameContent(source, dest))
                {
                    var alt = AlternativeName(imageFolder, fileName, source);
                    System.IO.File.Copy(source, System.IO.Path.Combine(imageFolder, alt), false);
                    throw new System.InvalidOperationException(
                        "同名不同内容：" + fileName + " → 自动改名候选 " + alt + "，请把方案里的引用改过去后重跑");
                }
            }
            else
            {
                System.IO.File.Copy(source, dest, false);
            }

            copied.Add(fileName);
        }

        var json = JsonConvert.SerializeObject(solution, Formatting.Indented);
        AssertNoExternalPath(json, configPath);
        System.IO.File.WriteAllText(configPath, json, new UTF8Encoding(false));

        System.Console.WriteLine("WROTE " + configPath + " bytes=" + new System.IO.FileInfo(configPath).Length);
        System.Console.WriteLine("  方案文件夹 " + folder);
        System.Console.WriteLine("  结构子目录 " + string.Join(" / ", SolutionStructureFolders));
        System.Console.WriteLine("  方案内图片 " + copied.Count + " 张：" + string.Join(", ", copied));
        return configPath;
    }

    /// <summary>
    /// 找出方案里所有「方案内图片相对路径」。
    /// 三个来源：Hmi.Picture 的 SourcePath 属性、Plugin.ImageAcquisition 的 FilePath 参数、Plugin.Calibration 的 SourceImagePath 参数。
    /// 只认以 <c>Image\</c> 打头且不含冒号的值（冒号 = 盘符绝对路径，一律不认）。
    /// </summary>
    static System.Collections.Generic.List<string> CollectImageReferences(JObject solution)
    {
        var found = new System.Collections.Generic.List<string>();
        Walk(solution);

        void Walk(JToken token)
        {
            switch (token)
            {
                case JObject obj:
                    foreach (var prop in obj.Properties())
                    {
                        if (IsImagePathKey(prop.Name) && prop.Value.Type == JTokenType.String)
                        {
                            var value = (prop.Value.Value<string>() ?? string.Empty).Replace('/', '\\');
                            if (value.StartsWith("Image\\", System.StringComparison.OrdinalIgnoreCase))
                            {
                                if (!found.Contains(value))
                                    found.Add(value);
                                continue;
                            }
                        }

                        Walk(prop.Value);
                    }
                    break;
                case JArray arr:
                    foreach (var item in arr)
                        Walk(item);
                    break;
            }
        }

        return found;
    }

    static bool IsImagePathKey(string key)
        => string.Equals(key, "SourcePath", System.StringComparison.Ordinal)
           || string.Equals(key, "FilePath", System.StringComparison.Ordinal)
           || string.Equals(key, "SourceImagePath", System.StringComparison.Ordinal);

    /// <summary>
    /// 这些键的值是**方案内资产**：只能是 <c>Image\文件名</c>，绝对路径与 <c>..\</c> 一律判红。
    /// 与 <c>FilePath</c> 分开管：后者是图像采集步骤的运行期读盘参数，按插件口径是"单张图像文件完整路径"
    /// （Plugins\Plugin.ImageAcquisition\ImageAcquisitionPlugin.cs:96-101），宿主不做方案根相对解析，
    /// 所以它写相对路径反而读不到图——强制它相对化会把方案做坏。
    /// </summary>
    static bool IsSolutionAssetPathKey(string key)
        => string.Equals(key, "SourcePath", System.StringComparison.Ordinal)
           || string.Equals(key, "SourceImagePath", System.StringComparison.Ordinal);

    /// <summary>
    /// 这些键的值是「方案内相对目录」：可以是相对路径，也可以留空；
    /// 但不许是盘符绝对路径、也不许 <c>..\</c> 爬出方案目录（用户裁定：一个方案一个文件夹，资源都在里面）。
    /// </summary>
    static bool IsRelativeOutputPathKey(string key)
        => string.Equals(key, "DirectoryTemplate", System.StringComparison.Ordinal)
           || string.Equals(key, "SourcePathTemplate", System.StringComparison.Ordinal)
           || string.Equals(key, "OutputDirectoryTemplate", System.StringComparison.Ordinal);

    /// <summary>
    /// 方案内资产路径的合法形状：<c>Image\文件名</c>（只有一层，不允许再套子目录。
    /// 宿主 VisionMaster\Services\SolutionAssetService.cs 的 TryResolve 用
    /// <c>candidate.StartsWith(imageRoot + 分隔符)</c> 判越界，套子目录虽然能过前缀检查，
    /// 但方案构建时只会把文件平铺复制进 Image\，带子目录的引用就成了空引用）。
    /// </summary>
    static bool IsFlatImageReference(string normalized)
    {
        if (!normalized.StartsWith("Image\\", System.StringComparison.OrdinalIgnoreCase))
            return false;
        var rest = normalized.Substring("Image\\".Length);
        return rest.Length > 0 && rest.IndexOf('\\') < 0;
    }

    // ------------------------------------------------------------------
    //  方案内相对输出目录（CSV 账本 / Excel 报表）
    //
    //  为什么不用 Program.CsvRecord/Excel：那两个是全仓共用的辅助，把账本写到
    //  D:\Records\...（绝对路径）。本方案的用户裁定是"一个方案一个文件夹，资源都在里面"，
    //  所以这里另给一套只写相对路径的版本，落点对齐宿主的方案结构：
    //      History\{StepName}\{yyyy-MM}\           —— Core\SolutionPaths.cs:48 HistoryFolderName
    //      History\{StepName}\{yyyy-MM}\报表\
    //  读侧口径一致：ScadaMonitoringHost 用 SolutionPaths.GetHistoryDirectory(方案文件路径)
    //  取 <方案根>\History 喂给数据表格的"历史源"。
    //
    //  已知边界（写进文档，不静默）：RecordingHub 展开模板后走 Path.GetFullPath，
    //  没有方案根锚点，所以相对目录是相对**宿主进程当前目录**解析的。
    //  方案运行时宿主 exe 就在 <仓库根>\VisionMaster\bin\<配置>\，要让它准确落进
    //  方案自己的 History\，得把宿主当前目录设到方案根（或用绝对路径覆盖本模板）。
    //  本生成器保证的是"配置里不含任何绝对路径与 ..\"，可整包搬迁。
    // ------------------------------------------------------------------

    /// <summary>方案内 CSV 账本目录模板：<c>History\{StepName}\{yyyy-MM}\</c></summary>
    const string HistoryDirectoryTemplate = @"History\{StepName}\{yyyy-MM}\";

    /// <summary>方案内 Excel 读 CSVs 的源模板：<c>History\{源步骤}\{yyyy-MM}\{yyyy-MM-dd}.csv</c></summary>
    const string HistorySourcePathTemplate = @"History\{源步骤}\{yyyy-MM}\{yyyy-MM-dd}.csv";

    /// <summary>方案内 Excel 报表输出目录模板：<c>History\{StepName}\{yyyy-MM}\报表\</c></summary>
    const string HistoryOutputDirectoryTemplate = @"History\{StepName}\{yyyy-MM}\报表\";

    /// <summary>CSV 记录（方案内相对输出）。列定义沿用 <see cref="CsvRecord"/> 那套口径。</summary>
    static JObject CcgCsvRecord(string seed, string name, JArray columns)
        => Action(seed, name, "数据处理", P_CSV, "CSV记录", "\uf15c", new JObject
        {
            ["DirectoryTemplate"] = HistoryDirectoryTemplate,
            ["FileNamePattern"] = "{yyyy-MM-dd}.csv",
            ["WriteHeader"] = true,
            ["KeepDays"] = 30,
            ["BlockOnFailure"] = false,
            ["FlushEveryRow"] = false,
            ["Columns"] = columns,
            ["ImageSaveMode"] = "Off",
            ["ResultColumn"] = "结果",
            ["NgValue"] = "NG",
            ["ImageDirName"] = "img",
            ["ImageNameTemplate"] = "{序号}_{结果}",
            ["ImageFormat"] = "png",
        });

    /// <summary>Excel 报表（方案内相对输出/输入）。</summary>
    static JObject CcgExcel(string seed, string name)
        => Action(seed, name, "数据处理", P_EXCEL, "Excel导出", "\uf1c3", new JObject
        {
            ["SourcePathTemplate"] = HistorySourcePathTemplate,
            ["OutputDirectoryTemplate"] = HistoryOutputDirectoryTemplate,
            ["FileNamePattern"] = "报表_{yyyy-MM-dd}.xlsx",
            ["WorkbookFormat"] = "Xlsx",
            ["MergeSameDay"] = false,
            ["IncludeHeader"] = true,
            ["AutoFitColumns"] = true,
            ["OpenAfterExport"] = false,
        });

    static bool SameContent(string a, string b)
        => string.Equals(Sha256Of(a), Sha256Of(b), System.StringComparison.OrdinalIgnoreCase);

    static string Sha256Of(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    static string AlternativeName(string folder, string fileName, string source)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var ext = System.IO.Path.GetExtension(fileName);
        var tag = Sha256Of(source).Substring(0, 8);
        for (var i = 1; i < 100; i++)
        {
            var candidate = stem + "_" + tag + (i == 1 ? string.Empty : "_" + i) + ext;
            if (!System.IO.File.Exists(System.IO.Path.Combine(folder, candidate)))
                return candidate;
        }

        throw new System.InvalidOperationException("自动改名失败：" + fileName);
    }

    /// <summary>
    /// 账本式自检：方案目录之外的路径一律判红。分两级：
    /// ① 方案内资产（<see cref="IsSolutionAssetPathKey"/>）：必须 <c>Image\文件名</c>；
    /// ② 相对输出目录（<see cref="IsRelativeOutputPathKey"/>）：不许盘符、不许 <c>..\</c>；
    /// ③ 其余任何字符串：不许盘符绝对路径、不许 <c>..\</c>（含 FilePath 采集路径——那条口径见
    ///    <see cref="IsSolutionAssetPathKey"/> 注释，生成器不写绝对路径）。
    /// </summary>
    static void AssertNoExternalPath(string json, string configPath)
    {
        var offenders = new System.Collections.Generic.List<string>();
        void Walk(JToken token)
        {
            switch (token)
            {
                case JObject obj:
                    foreach (var prop in obj.Properties())
                        Walk(prop.Value);
                    break;
                case JArray arr:
                    foreach (var item in arr)
                        Walk(item);
                    break;
                case JValue value when value.Type == JTokenType.String:
                    var text = value.Value<string>() ?? string.Empty;
                    var hasDrive = text.Length >= 2 && text[1] == ':' && char.IsLetter(text[0]);
                    var climbs = text.Contains("..\\") || text.Contains("../");
                    if (hasDrive || climbs)
                        offenders.Add(text);
                    break;
            }
        }

        Walk(JToken.Parse(json));

        // 方案内资产另加一条：只许 Image\文件名（平铺，不带子目录），否则搬迁后宿主按
        // SolutionAssetService.TryResolve 的 imageRoot 前缀检查会认不出来
        void WalkAssets(JToken token, string currentKey)
        {
            switch (token)
            {
                case JObject obj:
                    foreach (var prop in obj.Properties())
                        WalkAssets(prop.Value, prop.Name);
                    break;
                case JArray arr:
                    foreach (var item in arr)
                        WalkAssets(item, currentKey);
                    break;
                case JValue value when value.Type == JTokenType.String && IsSolutionAssetPathKey(currentKey):
                    var text = (value.Value<string>() ?? string.Empty).Replace('/', '\\');
                    if (text.Length > 0 && !IsFlatImageReference(text))
                        offenders.Add("[资产] " + text);
                    break;
            }
        }

        WalkAssets(JToken.Parse(json), string.Empty);

        if (offenders.Count > 0)
        {
            System.Console.Error.WriteLine("方案内出现指向目录之外的路径（" + offenders.Count + " 处）：");
            foreach (var item in offenders)
                System.Console.Error.WriteLine("  · " + item);
            throw new System.InvalidOperationException("外部路径自检失败：" + configPath);
        }
    }
}
