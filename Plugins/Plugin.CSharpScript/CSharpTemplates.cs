namespace Plugin.CSharpScript
{
    /// <summary>
    /// C# 脚本常用代码片段库（编辑器右键菜单"插入代码片段"用）。
    /// 全部片段可在本插件环境直接编译（脚本默认已 import System/Linq/HalconDotNet）。
    /// </summary>
    public sealed class TemplateDef
    {
        public string Category;
        public string Title;
        public string Code;
    }

    public static class CSharpTemplates
    {
        public static readonly TemplateDef[] All =
        {
            new TemplateDef
            {
                Category = "输入输出",
                Title = "取输入并写输出（基本骨架）",
                Code = @"// 输入名与左侧输入变量表一致；输出名与右侧输出变量表一致
double val = Context.GetInput<double>(""in1"");
Context.SetOutput(""out1"", val);
Context.Success($""in1={val} 已写入 out1"");",
            },
            new TemplateDef
            {
                Category = "输入输出",
                Title = "弱类型取输入（手动转换）",
                Code = @"// 不确定类型时用 object 接收，再按需转换
object raw = Context.GetInput(""in1"");
if (raw == null) return; // 端口未连线/变量不存在
double d = Convert.ToDouble(raw);
Context.Info($""in1 = {d}"");",
            },
            new TemplateDef
            {
                Category = "输入输出",
                Title = "运行期变量计数（GetVar/SetVar）",
                Code = @"// 运行期变量：本次流程运行内各插件共享（下游 Variable 类节点也可读）
int count = Context.GetVar<int>(""count"");
count++;
Context.SetVar(""count"", count);
Context.Info($""第 {count} 次运行"");",
            },
            new TemplateDef
            {
                Category = "流程控制",
                Title = "判定 NG（Fail + return）",
                Code = @"// 判 NG：Fail 标记失败后建议立即 return，避免后续代码继续执行
double val = Context.GetInput<double>(""in1"");
if (val > 100)
{
    Context.Fail($""值 {val} 超上限 100"");
    return;
}
Context.SetOutput(""out1"", val);",
            },
            new TemplateDef
            {
                Category = "流程控制",
                Title = "try-catch 保护",
                Code = @"// 包住可能出错的代码：异常时判 NG 而不是报脚本错误
try
{
    // 业务代码
}
catch (Exception ex)
{
    Context.Fail(""执行异常："" + ex.Message);
}",
            },
            new TemplateDef
            {
                Category = "Halcon 图像",
                Title = "阈值分割 + 面积统计",
                Code = @"// 阈值分割：统计亮区面积与质心，超上限判 NG
HImage img = Context.GetInput<HImage>(""图像"");
HRegion region = img.Threshold(128, 255);
double area = region.AreaCenter(out double row, out double col);
Context.Info($""面积 {area}，质心 ({row:F1}, {col:F1})"");
if (area > 50000)
    Context.Fail(""亮区面积超限"");",
            },
            new TemplateDef
            {
                Category = "Halcon 图像",
                Title = "灰度统计（均值/标准差）",
                Code = @"// 整幅图灰度统计（GetDomain 取图像定义域）
HImage img = Context.GetInput<HImage>(""图像"");
double mean = img.Intensity(img.GetDomain(), out HTuple deviation);
Context.Info($""灰度均值 {mean:F1}，标准差 {(double)deviation:F1}"");",
            },
            new TemplateDef
            {
                Category = "Halcon 图像",
                Title = "显示图像到视图窗口",
                Code = @"// 把图像发布到主界面 1 号视图（预览处理效果）
HImage img = Context.GetInput<HImage>(""图像"");
Context.ShowImage(img, 1);",
            },
        };
    }
}
