using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using Core.Interfaces;

namespace Plugin.Comm
{
    /// <summary>
    /// 立刻写：对通讯变量执行**插队写**——优先于常规读写队列与轮询，立即把值写入设备。
    ///
    /// 语义是"命令"而不是"状态赋值"：
    ///   · 同值也照常下发（设备侧可能被外部改回同值，"再写一次"是命令）；
    ///   · 不自动重试（重试可能重复下发命令）；
    ///   · 写成功后镜像值同步更新，变量管理 / 组态画面看到的就是刚写入的值。
    ///
    /// 值端口是 InputPort&lt;object&gt;：既可以手填常量（界面产出文本，由宿主按变量声明类型解析），
    /// 也可以链接上游结果。变量名端口是"名字类"端口：只能从方案候选里选，不提供 🔗（R33）。
    /// </summary>
    [Display(
        Name = "立刻写",
        GroupName = "通讯",
        Description = "对通讯变量执行插队写：优先于常规读写队列与轮询，立即把值写入设备",
        ShortName = "\uf044")]
    public class CommWriteNowPlugin : VisionPluginBase
    {
        /// <summary>
        /// 目标通讯变量名。候选来自"可写"类别：只读区（离散输入 / S7 输入映像 I）不会出现在下拉里，
        /// 避免用户选到一个必然失败的选项。
        /// </summary>
        public InputPort<string> VariableName { get; } =
            new("VariableName", "", "通讯变量名（插队写）")
            {
                IsRequired = false,
                IsFunctionalEnum = true,
                OptionKind = StepConfigOptionKind.CommunicationWritableVariableName,
            };

        /// <summary>
        /// 要写入的值：常量文本或上游链接。
        /// IsRequired=false 的理由同「变量名」——必填检查只看连线，不看手填值。
        /// </summary>
        public InputPort<object> Value { get; } =
            new InputPort<object>("Value", null, "要写入的值（常量文本或上游链接）")
            {
                IsRequired = false,
            };

        /// <summary>
        /// 是否等待设备返回。默认 true（现场大多数写需要确认失败）。
        /// false = 只把命令放进插队写队列就返回：Success 只代表"已受理"，
        /// 设备晚到的失败只进日志/通讯错误事件，本步骤无法再观测。
        /// </summary>
        public InputPort<bool> WaitForCompletion { get; } =
            new("WaitForCompletion", true, "是否等待设备返回（True=等写成功；False=只入队立即返回）")
            {
                IsRequired = false,
                // 封闭二选一：渲染成 True/False 下拉，并隐藏 🔗/✕（布尔值不该由上游运行时决定）
                IsFunctionalEnum = true,
                OptionKind = StepConfigOptionKind.BooleanChoice,
            };

        public OutputPort<object> WrittenValue { get; } = new("WrittenValue", "实际写入的工程值（文本已按声明类型解析）");
        public OutputPort<string> Completion { get; } = new("Completion", "完成方式：Completed=已等设备返回；Queued=只入队未等待");
        public OutputPort<string> TypeName { get; } = new("TypeName", "变量声明类型短名");
        public OutputPort<string> ConnectionName { get; } = new("ConnectionName", "变量所属连接名");
        public OutputPort<double> ElapsedMs { get; } = new("ElapsedMs", "本次插队写耗时(ms，含排队)");

        public override void RunAlgorithm(IExecutionContext context)
        {
            // 输出端口跨轮保留，必须先清空
            WrittenValue.Value = null;
            Completion.Value = string.Empty;
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
                Fail("当前执行环境没有接入通讯层，无法执行立刻写");
                return;
            }

            if (!comms.TryDescribe(name, out var info, out var describeError))
            {
                Fail(string.IsNullOrWhiteSpace(describeError) ? $"无法写入变量「{name}」" : describeError!);
                context.Logger.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            ConnectionName.Value = info.ConnectionName;
            TypeName.Value = info.TypeName;

            // 只读区在宿主侧还会再拦一次（NetworkVariableModel.TryWriteCore）：
            // 这里先拦是为了在界面上给出更贴近当前步骤的原因，不占用插队队列。
            if (info.IsReadOnly)
            {
                Fail($"变量「{name}」位于只读存储区（离散输入 / S7 输入映像 I），不能写入");
                context.Logger.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            if (!info.IsConnected)
            {
                Fail($"连接 [{info.ConnectionName}] 离线，立刻写未执行");
                context.Logger.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            if (context.CancellationToken.IsCancellationRequested)
            {
                Fail("流程已取消，立刻写未执行");
                return;
            }

            var value = Value.GetTypedValue();
            if (value == null)
            {
                Fail("要写入的值不能为空");
                return;
            }

            bool waitForCompletion = WaitForCompletion.GetTypedValue();

            var sw = Stopwatch.StartNew();
            bool ok;
            object? written;
            string? writeError;
            if (waitForCompletion)
            {
                // 等待模式：设备返回成功才算成功
                ok = comms.TryWriteNow(name, value, out written, out writeError);
            }
            else
            {
                // 只入队：成功只代表"已受理"，设备晚到的失败由宿主记日志/上报通讯错误
                ok = comms.TryQueueWriteNow(name, value, out written, out writeError);
            }
            sw.Stop();
            ElapsedMs.Value = sw.Elapsed.TotalMilliseconds;

            if (!ok)
            {
                string fallback = waitForCompletion
                    ? $"立刻写「{name}」失败"
                    : $"立刻写「{name}」入队失败";
                Fail(string.IsNullOrWhiteSpace(writeError) ? fallback : writeError!);
                context.Logger.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            WrittenValue.Value = written;
            Completion.Value = waitForCompletion ? "Completed" : "Queued";
            Success.Value = true;

            if (waitForCompletion)
                context.Logger.Info($"{InstanceName} 立刻写 {name} = {FormatValue(written)}（{ElapsedMs.Value:F1} ms）");
            else
                context.Logger.Info($"{InstanceName} 立刻写已入队 {name} = {FormatValue(written)}（不等待设备返回）");
        }

        private static string FormatValue(object? value) => value switch
        {
            null => string.Empty,
            byte[] bytes => BitConverter.ToString(bytes).Replace('-', ' '),
            bool b => b ? "True" : "False",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

        public override void Initialize() { }
        public override void Dispose() { }
    }
}
