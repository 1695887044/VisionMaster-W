using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using Core.Interfaces;

namespace Plugin.Comm
{
    /// <summary>
    /// 立刻读：对通讯变量执行**插队读**——优先于常规读写队列与轮询，立即从设备读取当前值。
    ///
    /// 与普通读的区别：
    ///   · 普通读（变量连线 / 轮询镜像）拿的是"最近一次轮询到"的值；
    ///   · 立刻读走通讯层的最高优先级车道，并在轮询段与段之间让路，拿到的是**此刻设备上的值**。
    ///
    /// 参数全部是端口（不用 [StepConfig]）：没有自定义视图的插件由 AutoPortConfigView 渲染，
    /// 而它只列输入端口——[StepConfig] 参数在界面上没有入口（见 AutoPortConfigView 的说明）。
    /// 「变量名」是名字类端口：IsFunctionalEnum=true 让 LinkableValueEditor 渲染成"只能选"的下拉，
    /// 并自动隐藏 🔗（名字不能由上游值提供，否则运行期会拿值当名字，见 R33）。
    /// </summary>
    [Display(
        Name = "立刻读",
        GroupName = "通讯",
        Description = "对通讯变量执行插队读：优先于常规读写队列与轮询，立即从设备读取当前值",
        ShortName = "\uf06e")]
    public class CommReadNowPlugin : VisionPluginBase
    {
        /// <summary>
        /// 目标通讯变量名。
        /// IsRequired 必须显式写 false：编译期必填检查只看"有没有连线"，不看"有没有填值"，
        /// 默认 true 会让正常用法（手选变量名）在编译期报"必填参数未配置"。
        /// </summary>
        public InputPort<string> VariableName { get; } =
            new("VariableName", "", "通讯变量名（插队读）")
            {
                IsRequired = false,
                IsFunctionalEnum = true,
                OptionKind = StepConfigOptionKind.CommunicationVariableName,
            };

        public OutputPort<object> Value { get; } = new("Value", "读到的工程值（类型 = 变量声明类型）");
        public OutputPort<string> ValueText { get; } = new("ValueText", "值的文本表示（byte[] 为十六进制）");
        public OutputPort<string> TypeName { get; } = new("TypeName", "变量声明类型短名");
        public OutputPort<string> ConnectionName { get; } = new("ConnectionName", "变量所属连接名");
        public OutputPort<double> ElapsedMs { get; } = new("ElapsedMs", "本次插队读耗时(ms，含排队)");

        public override void RunAlgorithm(IExecutionContext context)
        {
            // 输出端口跨轮保留，必须先清空：否则本轮失败时下游会读到上一轮的旧值
            Value.Value = null;
            ValueText.Value = string.Empty;
            TypeName.Value = string.Empty;
            ConnectionName.Value = string.Empty;
            ElapsedMs.Value = 0;

            var name = VariableName.GetTypedValue()?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                Fail("通讯变量名不能为空");
                return;
            }

            var comms = context.Comms;
            if (comms == null)
            {
                Fail("当前执行环境没有接入通讯层，无法执行立刻读");
                return;
            }

            // 本地快照校验：变量不存在 / 不是通讯变量 / 缺连接或地址 / 连接离线，
            // 都在发起 I/O 之前失败——不占用插队队列，也不让设备白跑一次。
            if (!comms.TryDescribe(name, out var info, out var describeError))
            {
                Fail(string.IsNullOrWhiteSpace(describeError) ? $"无法读取变量「{name}」" : describeError!);
                context.Logger.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            ConnectionName.Value = info.ConnectionName;
            TypeName.Value = info.TypeName;

            if (!info.IsConnected)
            {
                Fail($"连接 [{info.ConnectionName}] 离线，立刻读未执行");
                context.Logger.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            if (context.CancellationToken.IsCancellationRequested)
            {
                Fail("流程已取消，立刻读未执行");
                return;
            }

            var sw = Stopwatch.StartNew();
            var ok = comms.TryReadNow(name, out var value, out var readError);
            sw.Stop();
            ElapsedMs.Value = sw.Elapsed.TotalMilliseconds;

            if (!ok)
            {
                Fail(string.IsNullOrWhiteSpace(readError) ? $"立刻读「{name}」失败" : readError!);
                context.Logger.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            Value.Value = value;
            ValueText.Value = FormatValue(value);
            if (string.IsNullOrEmpty(TypeName.Value as string) && value != null)
                TypeName.Value = value.GetType().Name;

            Success.Value = true;
            context.Logger.Info($"{InstanceName} 立刻读 {name} = {ValueText.Value}（{ElapsedMs.Value:F1} ms）");
        }

        /// <summary>
        /// 值的文本表示：byte[] 用十六进制（逗号换成空格）；数值数组展开成 "1, 2, 3"；
        /// 其余按不变区域性转换。
        /// </summary>
        private static string FormatValue(object? value) => value switch
        {
            null => string.Empty,
            byte[] bytes => BitConverter.ToString(bytes).Replace('-', ' '),
            Array array => FormatArray(array),
            bool b => b ? "True" : "False",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

        /// <summary>数组展开成不变区域性文本；空数组返回 "[]"，避免日志里只剩一个类型名。</summary>
        private static string FormatArray(Array array)
        {
            if (array.Length == 0) return "[]";

            var parts = new string[array.Length];
            for (int i = 0; i < array.Length; i++)
                parts[i] = Convert.ToString(array.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;
            return string.Join(", ", parts);
        }

        public override void Initialize() { }
        public override void Dispose() { }
    }
}
