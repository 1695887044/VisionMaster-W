using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Core.Interfaces;
using Plugin.Comm;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 「立刻读 / 立刻写」插件的契约断言（不依赖真设备）。
    ///
    /// 通讯层的插队顺序 / 队列满 / 段间让路由 CommChecks 的 C29 组守；
    /// 本文件只守**插件面**：
    ///   · 元数据与端口面（工具箱分组、候选类别、端口数量）；
    ///   · 成功/失败路径的端口输出（Success / ErrorMessage / Value / WrittenValue / ElapsedMs）；
    ///   · 发起前短路（空名、只读区、离线、取消）不调用通讯访问；
    ///   · 插件工程只引用 Core.Interfaces（不把宿主/通讯实现拖进插件边界）。
    ///
    /// 用 Stub 实现 <see cref="ICommVariableAccess"/>，不造真实连接：
    /// 插件只认这个契约，Stub 能精确控制"描述/读/写"三条路径的返回与调用次数。
    /// </summary>
    internal static class CommOperatorChecks
    {
        public static void Run()
        {
            Section("[CN] 通讯立刻读 / 立刻写（插件契约）");

            var readPlugin = new CommReadNowPlugin();
            var writePlugin = new CommWriteNowPlugin();

            // ---- CN1：元数据 ----
            var readDisplay = typeof(CommReadNowPlugin).GetCustomAttribute<DisplayAttribute>();
            var writeDisplay = typeof(CommWriteNowPlugin).GetCustomAttribute<DisplayAttribute>();
            Check("[CN1] 两个算子都有工具箱元数据（名称/分组「通讯」/说明）",
                readDisplay is { Name: "立刻读", GroupName: "通讯" }
                && writeDisplay is { Name: "立刻写", GroupName: "通讯" }
                && !string.IsNullOrWhiteSpace(readDisplay!.Description)
                && !string.IsNullOrWhiteSpace(writeDisplay!.Description),
                $"读：{readDisplay?.Name}/{readDisplay?.GroupName}；写：{writeDisplay?.Name}/{writeDisplay?.GroupName}");

            // ---- CN2：插件工程只引用 Core.Interfaces ----
            var root = RepoRoot();
            var csproj = root == null
                ? null
                : Path.Combine(root, "Plugins", "Plugin.Comm", "Plugin.Comm.csproj");
            var refs = csproj != null && File.Exists(csproj)
                ? Regex.Matches(File.ReadAllText(csproj), "<ProjectReference\\s+Include=\"([^\"]+)\"")
                    .Select(m => m.Groups[1].Value)
                    .ToList()
                : new System.Collections.Generic.List<string>();
            var nonContractRefs = refs
                .Where(p => !p.EndsWith("Core.Interfaces.csproj", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Check("[CN2] 插件工程只引用 Core.Interfaces（不引用宿主/通讯实现）",
                csproj != null && File.Exists(csproj) && nonContractRefs.Count == 0,
                csproj == null ? "找不到 csproj" : string.Join(", ", nonContractRefs));

            // ---- CN10：端口面 ----
            Check("[CN10] 端口面：立刻读 1 入 7 出（含基类 Success/ErrorMessage）；立刻写 3 入 7 出",
                readPlugin.Inputs.Count == 1 && readPlugin.Outputs.Count == 7
                && readPlugin.Inputs.ContainsKey("VariableName")
                && readPlugin.Outputs.ContainsKey("Value")
                && readPlugin.Outputs.ContainsKey("ValueText")
                && readPlugin.Outputs.ContainsKey("TypeName")
                && readPlugin.Outputs.ContainsKey("ConnectionName")
                && readPlugin.Outputs.ContainsKey("ElapsedMs")
                && writePlugin.Inputs.Count == 3 && writePlugin.Outputs.Count == 7
                && writePlugin.Inputs.ContainsKey("VariableName")
                && writePlugin.Inputs.ContainsKey("Value")
                && writePlugin.Inputs.ContainsKey("WaitForCompletion")
                && writePlugin.Outputs.ContainsKey("WrittenValue")
                && writePlugin.Outputs.ContainsKey("Completion")
                && writePlugin.Outputs.ContainsKey("TypeName")
                && writePlugin.Outputs.ContainsKey("ConnectionName")
                && writePlugin.Outputs.ContainsKey("ElapsedMs"),
                $"读 {readPlugin.Inputs.Count}/{readPlugin.Outputs.Count}，写 {writePlugin.Inputs.Count}/{writePlugin.Outputs.Count}");

            Check("[CN10b] 变量名端口是「只能选」且候选类别正确（读=可读 / 写=可写）",
                readPlugin.Inputs["VariableName"].IsFunctionalEnum
                && readPlugin.Inputs["VariableName"].OptionKind == StepConfigOptionKind.CommunicationVariableName
                && writePlugin.Inputs["VariableName"].IsFunctionalEnum
                && writePlugin.Inputs["VariableName"].OptionKind == StepConfigOptionKind.CommunicationWritableVariableName,
                $"读={readPlugin.Inputs["VariableName"].OptionKind}，写={writePlugin.Inputs["VariableName"].OptionKind}");

            // 布尔二选一：必须是封闭下拉（IsFunctionalEnum + BooleanChoice），
            // 否则 bool 端口会渲染成文本框 + 🔗（手输 true/false 易错、还能被上游灌值）。
            var boolOptions = StepConfigOptionSource.GetOptionItems(StepConfigOptionKind.BooleanChoice);
            Check("[CN10c] WaitForCompletion 是 True/False 下拉（IsFunctionalEnum + BooleanChoice），候选恰好两项",
                writePlugin.Inputs["WaitForCompletion"].IsFunctionalEnum
                && writePlugin.Inputs["WaitForCompletion"].OptionKind == StepConfigOptionKind.BooleanChoice
                && boolOptions.Count == 2
                && boolOptions.Any(o => o.Value == "True")
                && boolOptions.Any(o => o.Value == "False"),
                string.Join(", ", boolOptions.Select(o => $"{o.Value}={o.Display}")));

            // 真实控件探针：光有 IsFunctionalEnum 还不够，必须确认 LinkableValueEditor 真的
            // 走了"只能选"分支（文本框 + 🔗 会被它自己隐藏）。与 MotionZMotionChecks 的轴名探针同法。
            string optionProbe = "未执行";
            var optionThread = new Thread(() =>
            {
                var editor = new Core.Controls.LinkableValueEditor
                {
                    Port = writePlugin.WaitForCompletion,
                };
                editor.Measure(new System.Windows.Size(520, 40));
                editor.Arrange(new System.Windows.Rect(0, 0, 520, 40));
                editor.UpdateLayout();

                optionProbe = $"只能选={editor.IsOptionPort} 候选数={editor.EnumOptions.Count}"
                              + $" 首项={editor.EnumOptions.FirstOrDefault()?.Display}";
            });
            optionThread.SetApartmentState(ApartmentState.STA);
            optionThread.Start();
            optionThread.Join(8000);

            Check("[CN10d] 真实 LinkableValueEditor 把 WaitForCompletion 渲染为「只能选」下拉（不是文本框 + 链接）",
                optionProbe.Contains("只能选=True") && optionProbe.Contains("候选数=2"),
                optionProbe);

            // ---- CN3：立刻读成功路径 ----
            var readStub = new StubCommAccess
            {
                Info = new CommVariableInfo("SpeedSetpoint", "PLC_1", "Int32", false, true),
                ReadValue = 42,
            };
            var readContext = NewContext(readStub, out _, default);
            readPlugin.VariableName.Value = "SpeedSetpoint";
            bool readOk = readPlugin.Execute(readContext);
            Check("[CN3] 立刻读成功：Success=true、Value/ValueText/TypeName/ConnectionName/ElapsedMs 都落地",
                readOk
                && Equals(readPlugin.Success.Value, true)
                && Equals(readPlugin.Value.Value, 42)
                && Equals(readPlugin.ValueText.Value, "42")
                && Equals(readPlugin.TypeName.Value, "Int32")
                && Equals(readPlugin.ConnectionName.Value, "PLC_1")
                && readPlugin.ElapsedMs.Value is double readMs && readMs >= 0
                && readStub.ReadCalls == 1,
                $"成功={readOk}，值={readPlugin.Value.Value}，文本={readPlugin.ValueText.Value}，调用={readStub.ReadCalls}");

            // ---- CN4：立刻读变量不存在 ----
            var readMissingStub = new StubCommAccess
            {
                DescribeResult = false,
                DescribeError = "找不到变量「NOPE」",
            };
            var readMissingPlugin = new CommReadNowPlugin { VariableName = { Value = "NOPE" } };
            bool readMissingOk = readMissingPlugin.Execute(NewContext(readMissingStub, out _, default));
            Check("[CN4] 立刻读变量不存在：Success=false、ErrorMessage 带原因、不发起读",
                !readMissingOk
                && Equals(readMissingPlugin.Success.Value, false)
                && (readMissingPlugin.ErrorMessage.Value as string)?.Contains("找不到变量") == true
                && readMissingStub.ReadCalls == 0,
                $"成功={readMissingOk}，错误={readMissingPlugin.ErrorMessage.Value}，读调用={readMissingStub.ReadCalls}");

            // ---- CN5：立刻写成功路径（插件把原值交给契约，WrittenValue 用契约返回的解析值） ----
            var writeStub = new StubCommAccess
            {
                Info = new CommVariableInfo("SpeedSetpoint", "PLC_1", "Int32", false, true),
                WriteWritten = 21,
            };
            var writeContext = NewContext(writeStub, out _, default);
            writePlugin.VariableName.Value = "SpeedSetpoint";
            writePlugin.Value.Value = "21";
            bool writeOk = writePlugin.Execute(writeContext);
            Check("[CN5] 立刻写成功：WrittenValue=21（契约已按声明类型解析）、原值原样交给契约",
                writeOk
                && Equals(writePlugin.Success.Value, true)
                && Equals(writePlugin.WrittenValue.Value, 21)
                && Equals(writeStub.LastWriteValue, "21")
                && Equals(writePlugin.TypeName.Value, "Int32")
                && writeStub.WriteCalls == 1,
                $"成功={writeOk}，写入={writePlugin.WrittenValue.Value}，契约收到={writeStub.LastWriteValue}");

            // ---- CN11：立刻写不等待模式 → 只入队、Completion=Queued、不调用阻塞写 ----
            var queueStub = new StubCommAccess
            {
                Info = new CommVariableInfo("SpeedSetpoint", "PLC_1", "Int32", false, true),
                QueueWritten = 21,
            };
            var queuePlugin = new CommWriteNowPlugin
            {
                VariableName = { Value = "SpeedSetpoint" },
                Value = { Value = "21" },
                WaitForCompletion = { Value = false },
            };
            bool queueOk = queuePlugin.Execute(NewContext(queueStub, out _, default));
            Check("[CN11] 立刻写不等待：Success=true、Completion=Queued、只调用入队、不调用阻塞写",
                queueOk
                && Equals(queuePlugin.Success.Value, true)
                && Equals(queuePlugin.Completion.Value, "Queued")
                && Equals(queuePlugin.WrittenValue.Value, 21)
                && queueStub.QueueCalls == 1
                && queueStub.WriteCalls == 0,
                $"成功={queueOk}，方式={queuePlugin.Completion.Value}，入队={queueStub.QueueCalls}，阻塞写={queueStub.WriteCalls}");

            // ---- CN6：立刻写只读区 → 发起前拒绝 ----
            var readOnlyStub = new StubCommAccess
            {
                Info = new CommVariableInfo("DiscreteIn", "PLC_1", "Boolean", true, true),
            };
            var readOnlyPlugin = new CommWriteNowPlugin
            {
                VariableName = { Value = "DiscreteIn" },
                Value = { Value = true },
            };
            bool readOnlyOk = readOnlyPlugin.Execute(NewContext(readOnlyStub, out _, default));
            Check("[CN6] 立刻写只读区：Success=false、不调用写、错误说明只读",
                !readOnlyOk
                && (readOnlyPlugin.ErrorMessage.Value as string)?.Contains("只读") == true
                && readOnlyStub.WriteCalls == 0,
                $"成功={readOnlyOk}，错误={readOnlyPlugin.ErrorMessage.Value}，写调用={readOnlyStub.WriteCalls}");

            // ---- CN7：名字为空 → 不调用任何契约方法 ----
            var emptyStub = new StubCommAccess();
            var emptyPlugin = new CommWriteNowPlugin();
            bool emptyOk = emptyPlugin.Execute(NewContext(emptyStub, out _, default));
            Check("[CN7] 名字为空：Success=false、Describe/Write 都不调用",
                !emptyOk
                && (emptyPlugin.ErrorMessage.Value as string)?.Contains("不能为空") == true
                && emptyStub.DescribeCalls == 0
                && emptyStub.WriteCalls == 0,
                $"成功={emptyOk}，错误={emptyPlugin.ErrorMessage.Value}");

            // ---- CN8：取消令牌已取消 → 描述后短路，不发起读 ----
            var cancelStub = new StubCommAccess();
            var cancelPlugin = new CommReadNowPlugin { VariableName = { Value = "SpeedSetpoint" } };
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            bool cancelOk = cancelPlugin.Execute(NewContext(cancelStub, out _, cts.Token));
            Check("[CN8] 流程已取消：Success=false、Describe 1 次、Read 0 次",
                !cancelOk
                && (cancelPlugin.ErrorMessage.Value as string)?.Contains("已取消") == true
                && cancelStub.DescribeCalls == 1
                && cancelStub.ReadCalls == 0,
                $"成功={cancelOk}，错误={cancelPlugin.ErrorMessage.Value}，Describe={cancelStub.DescribeCalls}，Read={cancelStub.ReadCalls}");

            // ---- CN9：立刻写离线 → 发起前拒绝 ----
            var offlineStub = new StubCommAccess
            {
                Info = new CommVariableInfo("SpeedSetpoint", "PLC_1", "Int32", false, false),
            };
            var offlinePlugin = new CommWriteNowPlugin
            {
                VariableName = { Value = "SpeedSetpoint" },
                Value = { Value = 1 },
            };
            bool offlineOk = offlinePlugin.Execute(NewContext(offlineStub, out _, default));
            Check("[CN9] 立刻写连接离线：Success=false、不调用写、错误含离线",
                !offlineOk
                && (offlinePlugin.ErrorMessage.Value as string)?.Contains("离线") == true
                && offlineStub.WriteCalls == 0,
                $"成功={offlineOk}，错误={offlinePlugin.ErrorMessage.Value}，写调用={offlineStub.WriteCalls}");
        }

        private static VisionMaster.Services.ExecutionContext NewContext(StubCommAccess stub, out StubLog log, CancellationToken token)
        {
            log = new StubLog();
            return new VisionMaster.Services.ExecutionContext(log)
            {
                Comms = stub,
                CancellationToken = token,
            };
        }

        /// <summary>仓库根（以 AGENTS.md 为锚；范式与其它断言文件一致）</summary>
        private static string? RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        /// <summary>
        /// <see cref="ICommVariableAccess"/> 的桩：三条路径的返回与调用次数都可控。
        /// 不造真实连接——插件只认契约，桩足以验证"发起前短路"与"输出落地"。
        /// </summary>
        private sealed class StubCommAccess : ICommVariableAccess
        {
            public CommVariableInfo Info { get; set; } = new("SpeedSetpoint", "PLC_1", "Int32", false, true);
            public bool DescribeResult { get; set; } = true;
            public string? DescribeError { get; set; }

            public bool ReadResult { get; set; } = true;
            public object? ReadValue { get; set; }
            public string? ReadError { get; set; }

            public bool WriteResult { get; set; } = true;
            public object? WriteWritten { get; set; }
            public string? WriteError { get; set; }

            public bool QueueResult { get; set; } = true;
            public object? QueueWritten { get; set; }
            public string? QueueError { get; set; }

            public int DescribeCalls { get; private set; }
            public int ReadCalls { get; private set; }
            public int WriteCalls { get; private set; }
            public int QueueCalls { get; private set; }
            public object? LastWriteValue { get; private set; }

            public bool TryDescribe(string variableName, out CommVariableInfo info, out string? error)
            {
                DescribeCalls++;
                info = Info;
                error = DescribeError;
                return DescribeResult;
            }

            public bool TryReadNow(string variableName, out object? value, out string? error)
            {
                ReadCalls++;
                value = ReadValue;
                error = ReadError;
                return ReadResult;
            }

            public bool TryWriteNow(string variableName, object? value, out object? writtenValue, out string? error)
            {
                WriteCalls++;
                LastWriteValue = value;
                writtenValue = WriteWritten ?? value;
                error = WriteError;
                return WriteResult;
            }

            public bool TryQueueWriteNow(string variableName, object? value, out object? writtenValue, out string? error)
            {
                QueueCalls++;
                LastWriteValue = value;
                writtenValue = QueueWritten ?? value;
                error = QueueError;
                return QueueResult;
            }
        }
    }
}
