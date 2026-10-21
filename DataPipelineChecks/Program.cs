using System.Text;
using Core.Interfaces;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 数据链路（P0-B）独立断言宿主入口。
    ///
    /// 与 FlowCanvasChecks 共用同一份检查源码（见 csproj 的 Compile Include），
    /// 只是不引用 VisionMaster 宿主：结果上报 / CSV 记录 / Excel 报表都不需要 UI，
    /// 单独跑更快，也不会被宿主里无关模块的半成品改动拖住。
    /// </summary>
    internal static class Program
    {
        private static int _pass, _fail;

        private static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            ResultUploadChecks.Run();
            DataRecordChecks.Run();
            ExcelExportChecks.Run();

            Console.WriteLine();
            Console.WriteLine("========== 结果汇总 ==========");
            Console.WriteLine($"通过: {_pass}  失败: {_fail}");
            Console.WriteLine(_fail == 0 ? ">>> 全部断言通过 <<<" : $">>> 存在 {_fail} 项失败 <<<");
            return _fail == 0 ? 0 : 1;
        }

        internal static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine($"---- {title} ----");
        }

        internal static void Check(string name, bool ok, string detail)
        {
            _pass += ok ? 1 : 0;
            _fail += ok ? 0 : 1;
            Console.WriteLine($"  [{(ok ? "√通过" : "×失败")}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  →  " + detail)}");
        }
    }

    /// <summary>
    /// 日志桩：把 Warn/Error 文案收进内存，供断言"该吼的有没有吼"。
    /// 与 FlowCanvasChecks 的同名类保持同一口径（这里独立一份，避免把整个断言宿主拖进来）。
    /// </summary>
    internal sealed class StubLog : ILogService
    {
        public List<string> Infos { get; } = new();
        public List<string> Warns { get; } = new();
        public List<string> Errors { get; } = new();

        public void Success(params string[] messages) => Add(Infos, messages, "Success");
        public void Info(params string[] messages) => Add(Infos, messages, "Info");
        public void Warn(params string[] messages) => Add(Warns, messages, "Warn");
        public void Error(params string[] messages) => Add(Errors, messages, "Error");
        public void Error(params Exception[] messages) => Errors.Add("Error:" + string.Join(";", messages.Select(e => e.Message)));

        public bool HasWarn(string fragment) => Warns.Any(w => w.Contains(fragment));

        private static void Add(List<string> target, string[] messages, string level)
            => target.Add(level + ":" + string.Join(";", messages));
    }
}
