using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VisionMaster.Services
{
    /// <summary>
    /// 插件试运行基建（通用，所有插件免费获得）
    /// 流程：解析链接 → 灌端口 → 执行插件唯一的 RunAlgorithm → 收集结果
    /// 与正式运行走同一个 Execute 方法，保证"试运行通过 = 正式运行行为一致"
    ///
    /// 链接解析能力（与 FlowCompiler 相同的分类规则）：
    /// - 常量链接：直接灌入端口
    /// - 全局变量链接：从 GlobalVariables 查找并挂 LinkedSource（取当前值）
    /// - 上游输出/运行时变量链接：试运行无法解析，聚合报错（流程未运行，数据不存在）
    /// </summary>
    public static class PluginTestRunner
    {
        public static PluginExecuteResult Run(
            IVisionPlugin plugin,
            IStepConfigData stepData,
            IWorkspaceManager workspace,
            ILogService logger)
        {
            var sw = Stopwatch.StartNew();

            if (plugin == null)
                return PluginExecuteResult.Fail(0, "插件实例为空");
            if (stepData == null)
                return PluginExecuteResult.Fail(0, "步骤配置数据为空");

            // 1. 解析链接端口（全局变量挂 LinkedSource 取当前值；上游输出/运行时变量试运行无法解析）
            var linkErrors = new List<string>();
            foreach (var port in plugin.Inputs.Values.OfType<IInputPort>())
            {
                var key = port.Name;

                if (!stepData.IsLinked(key)) continue;

                var link = stepData.GetLink(key);
                if (link == null)
                {
                    linkErrors.Add($"[{key}] 链接数据缺失");
                    continue;
                }

                if (link.TargetStepId == FlowCompiler.RuntimeVariableMarkerGuid)
                {
                    linkErrors.Add($"[{key}] 链接了运行时变量 ({link.DisplayAddress})，试运行无法解析，请正式运行验证");
                }
                else if (link.TargetStepId == Guid.Empty)
                {
                    if (!string.IsNullOrEmpty(link.DisplayAddress) && link.DisplayAddress.StartsWith("常量值: "))
                    {
                        // 常量链接：TargetPortName 即常量字符串，端口 Value setter 负责类型转换
                        port.LinkedSource = null;
                        port.Value = link.TargetPortName;
                    }
                    else
                    {
                        // 全局变量链接：按名查找并挂 LinkedSource（取当前值）
                        var global = workspace?.GlobalVariables?.FirstOrDefault(v => v.Name == link.TargetPortName);
                        if (global != null)
                        {
                            port.LinkedSource = global;
                        }
                        else
                        {
                            linkErrors.Add($"[{key}] 找不到全局变量: {link.TargetPortName}");
                        }
                    }
                }
                else
                {
                    linkErrors.Add($"[{key}] 链接了上游输出 ({link.DisplayAddress})，试运行无法解析，请正式运行验证");
                }
            }

            if (linkErrors.Count > 0)
                return PluginExecuteResult.Fail(sw.ElapsedMilliseconds, string.Join("\n", linkErrors));

            // 2. 灌入非链接的固定值（输入端口 + [StepConfig] 配置属性；链接端口跳过，保留上面挂的 LinkedSource）
            if (plugin is VisionPluginBase pluginBase)
                pluginBase.ApplyConfigValues(stepData);
            else
            {
                foreach (var port in plugin.Inputs.Values.OfType<IInputPort>())
                {
                    if (stepData.IsLinked(port.Name)) continue;
                    if (stepData.InputValues.TryGetValue(port.Name, out var value))
                    {
                        port.LinkedSource = null;
                        port.Value = value;
                    }
                }
            }

            // 3. 执行（与正式运行完全相同的入口）
            try
            {
                plugin.Execute(new TestExecutionContext(logger));
            }
            catch (Exception ex)
            {
                return PluginExecuteResult.Fail(sw.ElapsedMilliseconds, $"执行异常: {ex.Message}");
            }

            // 4. 收集结果：按约定读取 Success / ErrorMessage 输出端口（VisionPluginBase 基类自带）
            var success = true;
            var info = string.Empty;

            if (plugin.Outputs.TryGetValue("Success", out var successPort))
                success = successPort.Value is bool b && b;

            if (plugin.Outputs.TryGetValue("ErrorMessage", out var errorPort))
                info = errorPort.Value?.ToString() ?? string.Empty;

            return success
                ? PluginExecuteResult.Ok(sw.ElapsedMilliseconds, string.IsNullOrEmpty(info) ? "执行完成" : info)
                : PluginExecuteResult.Fail(sw.ElapsedMilliseconds, string.IsNullOrEmpty(info) ? "执行失败" : info);
        }
    }
}
