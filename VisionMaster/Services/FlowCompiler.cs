using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Core.Interfaces;
using VisionMaster.Models;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace VisionMaster.Services
{
    /// <summary>
    /// 编译器 把Step 编译成可执行的Flow
    /// </summary>
    public class FlowCompiler
    {
        private readonly IWorkspaceManager workspaceManager;

        public CompilationResult Compile(IEnumerable<StepModel> blueprints)
        {
            var result = new CompilationResult();
            var lookup = new Dictionary<string, IVisionPlugin>();

            try
            {
                // 1. 实例化所有算子
                var rootInstances = BuildBranch(blueprints, lookup);

                // 2. 接管子 + 安全检查
                LinkPorts(blueprints, lookup, result.Errors);
                result.Success = result.Errors.Count ==0;
                if (result.Success)
                {
                    result.Data = new CompiledFlow(rootInstances, lookup);
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add("系统崩溃级错误: " + ex.Message);
            }
            return result;
        }

        public FlowCompiler(IWorkspaceManager workspaceManager)
        {
            this.workspaceManager = workspaceManager;
        }

        private List<IVisionPlugin> BuildBranch(
            IEnumerable<StepModel> models,
            Dictionary<string, IVisionPlugin> lookup
        )
        {
            var branchInstances = new List<IVisionPlugin>();

            foreach (var model in models)
            {
                if (!model.IsEnabled)
                    continue; // 跳过禁用的算子
                // 1. 反射造实体
                Type type = Type.GetType(model.PluginTypeName);
                var plugin = (IVisionPlugin)Activator.CreateInstance(type);
                plugin.PluginID = model.StepID; // 赋予身份证
                plugin.InstanceName = model.StepName;
                // 登记到全局通讯录
                lookup.Add(plugin.PluginID, plugin);
                // 3. 灌入固定参数
                foreach (var kvp in model.InputValues)
                {
                    if (plugin.Inputs.TryGetValue(kvp.Key, out var inputPort))
                    {
                        inputPort.Value = kvp.Value;
                    }
                }

                // 2. 递归处理嵌套分支
                if (model is ConditionStep conditionModel && plugin is IBranchPlugin branchPlugin)
                {
                    foreach (var childCollection in conditionModel.Children)
                    {
                        // 递归调用自己，把子图纸变成子实体列表
                        var childPlugins = BuildBranch(childCollection.Steps, lookup);
                        branchPlugin.Branches.Add(childCollection.StepName, childPlugins);
                    }
                }

                branchInstances.Add(plugin);
            }
            return branchInstances;
        }

        private void LinkPorts(
            IEnumerable<StepModel> models,
            Dictionary<string, IVisionPlugin> lookup,
            List<string> errors
        )
        {
            foreach (var model in models)
            {
                // 1. 安全过滤
                if (model == null || !model.IsEnabled)
                    continue;

                // 2. 🌟 安全获取插件实例 (防止 KeyNotFoundException)
                if (!lookup.TryGetValue(model.StepID, out var targetPlugin))
                {
                    errors.Add($"[系统错误] 算子图纸存在但找不到运行实例: {model.StepID}");
                    continue;
                }

                // --- 检查 A：处理连线绑定 ---
                foreach (var link in model.LinkedSources)
                {
                    string targetPortName = link.Key;
                    string address = link.Value;

                    if (string.IsNullOrWhiteSpace(address))
                        continue;

                    // 正则解析：支持 Step.Port 和 Step.Port[index]
                    var match = Regex.Match(
                        address,
                        @"^(?<id>[^.]+)\.(?<port>[^\[]+)(\[(?<idx>\d+)\])?$"
                    );
                    if (!match.Success)
                    {
                        errors.Add(
                            $"[地址格式错误] 算子 {model.StepID} 的输入端口 {targetPortName} 绑定地址非法: {address}"
                        );
                        continue;
                    }

                    string upID = match.Groups["id"].Value;
                    string upPortName = match.Groups["port"].Value;
                    int index = match.Groups["idx"].Success
                        ? int.Parse(match.Groups["idx"].Value)
                        : -1;

                    IOutputPort source = null;

                    // 查找源头：全局变量或上游算子
                    if (upID.Equals("Global", StringComparison.OrdinalIgnoreCase))
                    {
                        // 确保 workspaceManager 已在类级别注入
                        source = workspaceManager.GlobalVariables.FirstOrDefault(s =>
                            s.Name == upPortName
                        );
                        if (source == null)
                            errors.Add(
                                $"[绑定失败] 算子 {model.StepID} 找不到全局变量: {upPortName}"
                            );
                    }
                    else if (lookup.TryGetValue(upID, out var upPlugin))
                    {
                        if (!upPlugin.Outputs.TryGetValue(upPortName, out source))
                            errors.Add(
                                $"[绑定失败] 算子 {model.StepID} 找不到上游算子 {upID} 的输出端口: {upPortName}"
                            );
                    }
                    else
                    {
                        errors.Add(
                            $"[绑定失败] 算子 {model.StepID} 链接的上游算子 {upID} 未能成功实例化或不存在"
                        );
                    }
                    if (source != null)
                    {
                        // 如果有索引，套上代理
                        if (index >= 0)
                            source = new ArrayIndexProxyPort(source, index);

                        if (targetPlugin.Inputs.TryGetValue(targetPortName, out var inPort))
                        {
                            inPort.LinkedSource = source;
                        }
                        else
                        {
                            errors.Add(
                                $"[结构错误] 算子 {model.StepID} 并没有名为 {targetPortName} 的输入端口"
                            );
                        }
                    }
                }
                foreach (var input in targetPlugin.Inputs.Values)
                {
                    // 🌟 优化：只有在既没连线
                    if (input.IsRequired && input.LinkedSource == null)
                    {
                        errors.Add(
                            $"[安全检查] 算子 {model.StepID} 的必填参数 {input.Name} 尚未配置！"
                        );
                    }
                }
                if (model is ConditionStep conditionModel && conditionModel.Children != null)
                {
                    foreach (var childBranch in conditionModel.Children)
                    {
                        if (childBranch.Steps != null)
                        {
                            LinkPorts(childBranch.Steps, lookup, errors);
                        }
                    }
                }
            }
        }
    }
}
