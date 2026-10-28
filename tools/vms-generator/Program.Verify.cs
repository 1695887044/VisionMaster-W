using Newtonsoft.Json.Linq;
using System.Text;

internal static partial class Program
{
    // ==================================================================
    //  --verify <目录>：校验**已存在**的 .vms（生成期自检管不到存量方案）
    //
    //  为什么按「字段作用域」分级，而不是"全文不许有盘符"：
    //    锚定能力目前只存在于资产管线，两个作用域的解析路径不同——
    //      · 资产引用（SourcePath / SourceImagePath / *Assets[].RelativePath）
    //          宿主 SolutionAssetService.TryResolve 按方案根解析，导入时平铺进
    //          <根>\Image\ 或 <根>\Assets\{Templates,Models}\。写绝对路径 = 方案搬走就断图，
    //          所以这是**硬判据**（ERROR）。
    //      · 步骤级路径（InputValues.FilePath / FolderPath / DirectoryTemplate）
    //          IExecutionContext 上没有方案根成员；ImageAcquisition 只在 ImageAssetId 非空时
    //          走 ProjectAssetPathResolver，Id 为空时原样使用传入路径。所以绝对路径是
    //          **已知边界**（WARN），不是生成器的疏忽。关掉它要改公共契约
    //          （IExecutionContext 加方案根 + 宿主实现 + 全插件重建），属 R12 大改动。
    //
    //  退出码：0 = 无 ERROR（WARN 不影响）；1 = 有 ERROR；2 = 用法错。
    // ==================================================================

    static int RunVerify(string dir)
    {
        Console.OutputEncoding = new UTF8Encoding(false);

        if (string.IsNullOrWhiteSpace(dir))
        {
            Console.WriteLine("用法: GenSolution --verify <方案目录>");
            return 2;
        }

        var root = Path.GetFullPath(dir);
        if (!Directory.Exists(root))
        {
            Console.WriteLine("目录不存在: " + root);
            return 2;
        }

        var files = Directory.GetFiles(root, "*.vms", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        int totalErr = 0, totalWarn = 0;
        foreach (var file in files)
        {
            var (errs, warns) = VerifyOne(file);
            totalErr += errs.Count;
            totalWarn += warns.Count;

            var rel = Path.GetRelativePath(root, file);
            Console.WriteLine($"[{(errs.Count > 0 ? "FAIL" : "OK  ")}] {rel}  错误={errs.Count} 警告={warns.Count}");
            foreach (var e in errs) Console.WriteLine("   ERROR " + e);
            foreach (var w in warns) Console.WriteLine("   WARN  " + w);
        }

        Console.WriteLine();
        Console.WriteLine($"RESULT: {files.Length} 个方案 / 错误 {totalErr} / 警告 {totalWarn}");
        return totalErr > 0 ? 1 : 0;
    }

    static (List<string> errs, List<string> warns) VerifyOne(string file)
    {
        var errs = new List<string>();
        var warns = new List<string>();

        string text;
        try { text = File.ReadAllText(file, Encoding.UTF8); }
        catch (Exception ex)
        {
            errs.Add("读不出文件: " + ex.Message);
            return (errs, warns);
        }

        var root = Path.GetDirectoryName(Path.GetFullPath(file)) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(file);
        var folder = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        // ---- 文件夹布局（与 SolutionPaths.IsFolderLayout 同一口径）----
        if (!string.Equals(folder, stem, StringComparison.OrdinalIgnoreCase))
        {
            warns.Add($"不在同名文件夹里（<方案名>\\<方案名>.vms）：当前 {folder}\\{Path.GetFileName(file)}"
                      + " —— 已知边界：搬进文件夹要同时改 .vms 内路径与加载方");
        }

        // ---- 全文：不许出现上跳 ----
        if (text.Contains("..\\") || text.Contains("../"))
            errs.Add("含上跳路径 '..\\'（方案必须整包可搬）");

        JObject rootObj;
        try { rootObj = JObject.Parse(text); }
        catch (Exception ex)
        {
            errs.Add("不是合法 JSON: " + ex.Message);
            return (errs, warns);
        }

        // ---- 声明集合：供 AssetId 反查 ----
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var coll in new[] { "ImageAssets", "TemplateAssets", "ModelAssets" })
        {
            if (rootObj[coll] is JArray arr)
            {
                foreach (var item in arr)
                {
                    var id = (string)item["AssetId"];
                    if (!string.IsNullOrWhiteSpace(id)) declared.Add(id.Trim());
                }
            }
        }

        int stepAbs = 0;
        Walk(rootObj, prop =>
        {
            var name = prop.Name;
            var value = prop.Value.Type == JTokenType.String ? (string)prop.Value : null;

            // ---- 资产引用：硬判据 ----
            if (name == "SourcePath" || name == "SourceImagePath")
            {
                if (!string.IsNullOrWhiteSpace(value))
                    CheckFlatAsset(errs, root, name, value, @"Image");
                return;
            }
            // ---- 资产集合：逐项校验 RelativePath（平铺 + 存在）----
            // 注意：数组项里的 RelativePath 往上第三层才是集合属性
            // （RelativePath <- JObject 项 <- JArray <- 集合属性），按祖先链判会漏。
            if ((name == "ImageAssets" || name == "TemplateAssets" || name == "ModelAssets")
                && prop.Value is JArray coll)
            {
                var expected = name == "ImageAssets" ? @"Image"
                    : name == "TemplateAssets" ? @"Assets\Templates"
                    : @"Assets\Models";
                foreach (var item in coll)
                {
                    var rp = (string)item["RelativePath"];
                    if (!string.IsNullOrWhiteSpace(rp))
                        CheckFlatAsset(errs, root, name + ".RelativePath", rp, expected);
                }
                return;
            }
            if (name == "RelativePath")
                return;   // 由上面的集合分支处理，避免重复报错

            // ---- AssetId：必须是 Guid，且模板/模型 Id 能反查 ----
            if (name.EndsWith("AssetId", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(value))
            {
                if (!Guid.TryParse(value.Trim(), out _))
                {
                    errs.Add($"{name} 不是合法 Guid：{value}");
                }
                else if ((name == "TemplateAssetId" || name == "ModelAssetId") && !declared.Contains(value.Trim()))
                {
                    errs.Add($"{name} 在方案里找不到对应资产：{value}");
                }
                return;
            }

            // ---- 步骤级路径：已知边界，只计数 ----
            if ((name == "FilePath" || name == "FolderPath" || name == "DirectoryTemplate")
                && !string.IsNullOrWhiteSpace(value)
                && System.Text.RegularExpressions.Regex.IsMatch(value, @"^[A-Za-z]:\\"))
            {
                stepAbs++;
            }
        });

        if (stepAbs > 0)
        {
            warns.Add($"步骤级路径有 {stepAbs} 处盘符路径 —— 已知边界：IExecutionContext 无方案根，"
                      + "ImageAcquisition 的原始 FilePath/FolderPath 不做锚定（改契约才能收）");
        }

        return (errs, warns);
    }

    static void CheckFlatAsset(List<string> errs, string root, string field, string value, string expectedDir)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^[A-Za-z]:\\"))
        {
            errs.Add($"{field} 是盘符路径：{value}");
            return;
        }

        var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(expectedDir) + @"\\[^\\]+$";
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, pattern))
        {
            errs.Add($"{field} 必须是 {expectedDir}\\<文件名>（平铺一层、不许子目录）：{value}");
            return;
        }

        var abs = Path.Combine(root, value);
        if (!File.Exists(abs))
            errs.Add($"{field} 指向的文件不存在：{value}");
    }

    static void Walk(JToken token, Action<JProperty> onProperty)
    {
        if (token is JObject obj)
        {
            foreach (var prop in obj.Properties())
            {
                onProperty(prop);
                Walk(prop.Value, onProperty);
            }
        }
        else if (token is JArray arr)
        {
            foreach (var item in arr) Walk(item, onProperty);
        }
    }
}
