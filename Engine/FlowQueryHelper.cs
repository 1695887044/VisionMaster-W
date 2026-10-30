﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.Helpers
{
    /// <summary>
    /// 流程查询帮助类
    /// 提供流程相关的查询和分析功能
    /// </summary>
    public static class FlowQueryHelper
    {
        /// <summary>
        /// 插件提供者（懒解析）。
        ///
        /// 【为什么不裸写 if (x == null) x = Resolve()】那是非原子的"检查-赋值"：
        /// 多线程下两个调用方可能各自 Resolve 一次，且一方可能读到另一方尚未发布完成的引用。
        ///
        /// 【为什么不用 Lazy&lt;T&gt;】Lazy 会**缓存首次 Resolve 抛出的异常** ——
        /// 万一第一次调用发生在容器注册完成之前，之后每次调用都会重抛同一个异常，
        /// 而本类没有任何"容器已就绪"的信号可以等。
        /// CompareExchange 只让其中一个赢，输的一方下次调用会重新尝试，
        /// 行为与原来的懒解析一致，但发布是原子的。
        /// </summary>
        private static IPluginProvider _pluginProvider;

        private static IPluginProvider PluginProvider
        {
            get
            {
                var current = _pluginProvider;
                if (current != null) return current;

                var resolved = ContainerLocator.Container.Resolve<IPluginProvider>();
                Interlocked.CompareExchange(ref _pluginProvider, resolved, null);
                return _pluginProvider;
            }
        }

        /// <summary>
        /// 获取可用于绑定的变量树
        /// 包括全局变量、运行时本地变量和上游步骤的输出端口
        /// </summary>
        public static List<ToolItemModel> GetAvailableVariablesTree(
            IEnumerable<IVariable> globals,
            IEnumerable<StepModel> allSteps,
            StepModel targetStep,
            bool includeUpstreamStepPorts = true
        )
        {
            var treeNodes = new List<ToolItemModel>();

            if (globals != null && globals.Any())
            {
                var globalNode = new ToolItemModel()
                {
                    ModuleGroup = "Global",
                    SourceCategory = "Variable",
                    DefaultLinkKind = LinkKind.GlobalVariable,
                    Name = "全局变量 (Global)",
                    Icon = "\uf0ac",
                    Description = "全局共享变量",
                    OutputDefinitions = globals
                        .Select(gv => new PortDefinition
                        {
                            Name = gv.Name,
                            // 带上稳定身份：绑定弹窗据此把 Id 写进连线，改名后连线仍能命中
                            VariableId = gv.VariableId,
                            // 带上设备：多设备同名点位时，候选行的显示串与「已绑定」判定都要靠它分辨
                            Device = gv is NetworkVariableModel network
                                ? (network.ConnectionName ?? string.Empty)
                                : string.Empty,
                            DataTypeName = gv.DataType.AssemblyQualifiedName,
                            Description = gv.VariableType switch
                            {
                                VariableType.Communication => $"[网络变量] {gv.Description} (连接: {gv.ConnectionName})",
                                VariableType.Io => $"[IO变量] {gv.Description}",
                                VariableType.System => $"[系统变量] {gv.Description}",
                                _ => $"[本地变量] {gv.Description}"
                            }
                        })
                        .ToList(),
                };
                treeNodes.Add(globalNode);
            }

            var upstreamNodes = GetUpstreamNodes(allSteps, targetStep);

            // 运行时本地变量：由上游 VariableDefinitionPlugin 节点动态写入 context.LocalVariables
            // 设计期静态推断变量名/类型，运行期由 RuntimeVariableProxyPort 从 LocalVariables 取值
            var runtimeVars = GetRuntimeVariableDefinitions(upstreamNodes);
            if (runtimeVars.Count > 0)
            {
                var runtimeNode = new ToolItemModel()
                {
                    Id = LinkProtocol.RuntimeVariableMarkerGuid,
                    ModuleGroup = "Runtime",
                    SourceCategory = "Variable",
                    DefaultLinkKind = LinkKind.RuntimeVariable,
                    Name = "运行时变量 (Runtime)",
                    Icon = "\uf085",
                    Description = "流程执行中由变量定义节点动态创建的本地变量",
                    OutputDefinitions = runtimeVars,
                };
                treeNodes.Add(runtimeNode);
            }

            foreach (var node in upstreamNodes)
            {
                // 名字选择模式（写名）根本不需要上游端口节点：调用方传 false 直接跳过。
                // 跳过还有第二个好处——本方法展开上游端口时依赖 PluginProvider（Prism 容器），
                // 关掉这一段后，无容器的断言宿主也能构造候选树。
                if (!includeUpstreamStepPorts)
                    break;

                var data = PluginProvider.ModulePlugins[node.PluginTypeName];
                if (data == null || data.OutputDefinitions == null || !data.OutputDefinitions.Any())
                    continue;

                // 合并动态输出端口：StepModel.OutputPortDefinitions 快照（名字+类型）里的端口
                // 不在静态表里，补进去让绑定界面可选，类型兼容检查按真实类型执行
                var outputDefs = data.OutputDefinitions.ToList();
                if (node.OutputPortDefinitions != null && node.OutputPortDefinitions.Count > 0)
                {
                    var existing = new HashSet<string>(outputDefs.Select(p => p.Name));
                    foreach (var dyn in node.OutputPortDefinitions)
                    {
                        if (string.IsNullOrEmpty(dyn?.Name) || existing.Contains(dyn.Name)) continue;
                        outputDefs.Add(new PortDefinition
                        {
                            Name = dyn.Name,
                            DataTypeName = dyn.DataTypeName ?? typeof(object).AssemblyQualifiedName,
                            Description = dyn.Description ?? "[动态输出]"
                        });
                    }
                }

                var uiNode = new ToolItemModel
                {
                    Id = node.StepID,
                    ModuleGroup = node.StepName,
                    Name = node.StepName,
                    Icon = node.Icon,
                    Description = node.Description,
                    OutputDefinitions = outputDefs,
                };

                treeNodes.Add(uiNode);
            }

            return treeNodes;
        }

        /// <summary>
        /// 按"这个输入端口是否允许接全局/系统变量"过滤候选树。
        ///
        /// C#脚本的输入只接上游数据流：全局/系统变量走它自己的「共享变量」声明表
        /// （按 VariableId 寻址、可声明读写方向）。把「全局变量」节点整块摘掉，
        /// 比"选得到但运行期不生效"清楚得多。
        ///
        /// 抽成纯函数：断言可以直接构造 ToolItemModel 验证过滤口径，不必起绑定窗口。
        /// </summary>
        public static List<ToolItemModel> FilterGlobalNodes(
            IEnumerable<ToolItemModel> nodes,
            bool allowGlobals)
        {
            var result = new List<ToolItemModel>();
            if (nodes == null) return result;

            foreach (var node in nodes)
            {
                if (node == null) continue;
                if (!allowGlobals && string.Equals(node.ModuleGroup, "Global", StringComparison.Ordinal))
                    continue;
                result.Add(node);
            }

            return result;
        }

        /// <summary>
        /// 从上游步骤中静态扫描 VariableDefinitionPlugin 节点，
        /// 提取其声明的运行时变量名与类型，构造可供绑定的 PortDefinition 列表
        /// </summary>
        /// <remarks>
        /// VariableDefinitionPlugin 的 Name 端口值（变量名）和 Type 端口值（类型字符串）
        /// 在设计期由用户填写并持久化到 StepModel.InputValues，
        /// 据此可以在不执行流程的情况下推断出"将被创建"的运行时变量
        /// </remarks>
        private static List<PortDefinition> GetRuntimeVariableDefinitions(IEnumerable<StepModel> upstreamSteps)
        {
            var result = new List<PortDefinition>();
            if (upstreamSteps == null)
                return result;

            var seenNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var step in upstreamSteps)
            {
                // 一个步骤可能声明多个变量（「变量定义与赋值」一行一个声明），
                // 所以走"多值"版本；老的单值接口保留给外部调用方（兼容）。
                foreach (var (varName, dataType) in GetDeclaredVariables(step))
                {
                    if (string.IsNullOrWhiteSpace(varName)) continue;

                    // 同名变量以最后一次定义为准（运行期也是覆盖语义）
                    if (!seenNames.Add(varName))
                    {
                        var existing = result.Find(p => p.Name == varName);
                        if (existing != null)
                            result.Remove(existing);
                    }

                    result.Add(new PortDefinition
                    {
                        Name = varName,
                        DataTypeName = dataType.AssemblyQualifiedName,
                        Description = $"[运行时变量] 由 '{step.StepName}' 声明"
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// 取一个步骤声明出来的**全部**运行时变量（名字 + 类型）。
        ///
        /// 为什么需要多值版本：`TryGetDefinedVariable` 一次只回一个名字，
        /// 而「变量定义与赋值」一个节点可以声明多行（每行一个变量）。
        /// 只用单值版本会让第 2 行以后的声明在候选树里全部丢失。
        /// </summary>
        public static IEnumerable<(string Name, Type Type)> GetDeclaredVariables(StepModel step)
        {
            if (step?.PluginTypeName == null) yield break;

            // 「变量定义与赋值」：Rows 里 Runtime + 自动创建 的行
            if (step.PluginTypeName.Contains("VariableAssignmentPlugin"))
            {
                if (step.InputValues == null) yield break;
                foreach (var row in ReadRuntimeRows(step.InputValues))
                    yield return row;
                yield break;
            }

            // C# 脚本：OutputVars 全部（多行）
            if (step.PluginTypeName.Contains("CSharpScriptPlugin"))
            {
                if (step.InputValues == null || !step.InputValues.TryGetValue("OutputVars", out var raw) || raw == null)
                    yield break;
                foreach (var def in EnumerateObjects(raw))
                {
                    var name = (ReadMember(def, "Name") as string ?? string.Empty).Trim();
                    if (name.Length == 0) continue;
                    yield return (name, ParseVariableType(ReadMember(def, "Type")?.ToString() ?? "object"));
                }
                yield break;
            }

            // 判定表：每条规则一个输出
            if (step.PluginTypeName.Contains("JudgeTablePlugin"))
            {
                if (step.InputValues == null) yield break;
                var tableName = (ReadMember(step.InputValues, "TableName") as string ?? string.Empty).Trim();
                if (tableName.Length == 0) yield break;
                if (!step.InputValues.TryGetValue("Rules", out var raw) || raw == null) yield break;

                foreach (var rule in EnumerateObjects(raw))
                {
                    var ruleName = (ReadMember(rule, "Name") as string ?? string.Empty).Trim();
                    if (ruleName.Length == 0) continue;
                    yield return (tableName + "." + ruleName + ".结果", typeof(bool));
                }
                yield break;
            }

            // 历史形态：「变量定义」插件（单值）
            if (step.PluginTypeName.Contains("VariableDefinitionPlugin"))
            {
                if (TryGetDefinedVariable(step, out var name, out var type) && name != null)
                    yield return (name, type);
            }
        }

        /// <summary>
        /// 判断一个步骤是否为「变量定义」插件节点，并取回它声明的变量名与类型。
        ///
        /// 为什么公开：绑定弹窗的候选树与画布的值输出脚必须认同一批节点、用同一套取名取类型规则，
        /// 两处各写一遍迟早漂移（漂移的表现是"画布能连、弹窗选不到"或反过来）。
        /// </summary>
        /// <remarks>
        /// VariableDefinitionPlugin 的 Name 端口值（变量名）和 Type 端口值（类型字符串）
        /// 在设计期由用户填写并持久化到 StepModel.InputValues，
        /// 据此可以在不执行流程的情况下推断出"将被创建"的运行时变量。
        /// 变量名未填时返回 false —— 名字是这条线的寻址键，空名连上了也取不到值。
        /// </remarks>
        public static bool TryGetDefinedVariable(
            StepModel step,
            out string variableName,
            out Type variableType
        )
        {
            variableName = null;
            variableType = typeof(object);

            if (step?.PluginTypeName == null || step.InputValues == null)
                return false;

            // ① 「变量定义」插件（历史形态，靠类型名识别，避免反射依赖外部插件 DLL）
            if (step.PluginTypeName.Contains("VariableDefinitionPlugin"))
                return TryGetFromVariableDefinition(step, out variableName, out variableType);

            // ② 「变量定义与赋值」（本仓的 v4 形态）：Runtime + 勾了「自动创建」的行就是声明。
            //    为什么必须认它：v4 之前下游只能靠"按名读 LocalVariables"取这个节点的产出，
            //    而那条通道（数据记录/结果上报的「变量」档）已于 2026-10-30 下线——
            //    现在它的产出是 Var_{名} 输出端口，候选树里也必须能选到同一个名字。
            if (step.PluginTypeName.Contains("VariableAssignmentPlugin"))
            {
                // 一行一个声明；这里只取"第一个运行时行"（同名多行会被 OnConfirm 拒绝，
                // 且 GetRuntimeVariableDefinitions 会对同名做"后者覆盖"）。
                foreach (var (name, type) in ReadRuntimeRows(step.InputValues))
                {
                    variableName = name;
                    variableType = type;
                    return true;
                }
                return false;
            }

            // ③ C# 脚本的 OutputVars：真输出端口（同时也写 LocalVariables），
            //    名字静态可读（:299 落 InputValues["OutputVars"]）。
            if (step.PluginTypeName.Contains("CSharpScriptPlugin"))
                return TryGetFromScriptOutputVars(step, out variableName, out variableType);

            // ④ 判定表：规则输出名 = 表名.规则名.结果（由插件算出，静态可预测）
            if (step.PluginTypeName.Contains("JudgeTablePlugin"))
                return TryGetFromJudgeTable(step, out variableName, out variableType);

            return false;
        }

        /// <summary>历史形态：「变量定义」插件的 Name / Type 端口值。</summary>
        private static bool TryGetFromVariableDefinition(
            StepModel step, out string variableName, out Type variableType)
        {
            variableName = null;
            variableType = typeof(object);

            if (!step.InputValues.TryGetValue("Name", out var nameObj))
                return false;
            if (nameObj is not string name || string.IsNullOrWhiteSpace(name))
                return false;

            variableName = name;

            if (step.InputValues.TryGetValue("Type", out var typeObj) && typeObj is string typeStr)
                variableType = ParseVariableType(typeStr);

            return true;
        }

        /// <summary>
        /// 从「变量定义与赋值」的 <c>Rows</c> 快照里读运行时声明（名字 + 声明类型）。
        ///
        /// <c>Rows</c> 在 <c>InputValues</c> 里有两种形态：刚确认过是
        /// <c>ObservableCollection&lt;AssignmentRow&gt;</c>，从 .vms 加载未确认时是 JArray。
        /// 两种都要认（与 FlowCompiler 读 SharedVars 的兼容口径一致）。
        /// </summary>
        private static IEnumerable<(string Name, Type Type)> ReadRuntimeRows(
            IDictionary<string, object> inputValues)
        {
            if (!inputValues.TryGetValue("Rows", out var raw) || raw == null)
                yield break;

            foreach (var row in EnumerateObjects(raw))
            {
                if (row == null) continue;

                var scope = ReadMember(row, "Scope") as string;
                if (!string.Equals(scope, "Runtime", StringComparison.OrdinalIgnoreCase))
                    continue;

                var name = (ReadMember(row, "VariableName") as string ?? string.Empty).Trim();
                if (name.Length == 0) continue;

                // 只有勾了「自动创建」的行才真的"声明"出一个变量；
                // 没勾的行要求上游已经存在同名变量（那是"赋值"，不是"定义"）。
                var create = ReadMember(row, "CreateIfNotExists");
                if (create is bool b && !b) continue;

                var typeName = ReadMember(row, "Type")?.ToString();
                yield return (name, ParseVariableType(typeName ?? VariableValueTypeToName(ReadMember(row, "Type"))));
            }
        }

        /// <summary>枚举成员 → <see cref="ParseVariableType"/> 认得的字面量。</summary>
        private static string VariableValueTypeToName(object raw)
        {
            if (raw == null) return "object";
            // 枚举落盘时是数字，读回来可能是 long / JValue / 字符串成员名
            var text = raw.ToString();
            if (int.TryParse(text, out var n))
            {
                return n switch
                {
                    1 => "int",
                    2 => "double",
                    3 => "bool",
                    4 => "string",
                    _ => "object",
                };
            }
            return text ?? "object";
        }

        /// <summary>
        /// C# 脚本 <c>OutputVars</c>（<c>ObservableCollection&lt;ScriptVarDef&gt;</c> 或 JArray）。
        /// 只取第一个有名字的输出变量——本方法一次只能回一个名字，候选树按步骤逐个调用。
        /// </summary>
        private static bool TryGetFromScriptOutputVars(
            StepModel step, out string variableName, out Type variableType)
        {
            variableName = null;
            variableType = typeof(object);

            if (!step.InputValues.TryGetValue("OutputVars", out var raw) || raw == null)
                return false;

            foreach (var def in EnumerateObjects(raw))
            {
                var name = (ReadMember(def, "Name") as string ?? string.Empty).Trim();
                if (name.Length == 0) continue;
                variableName = name;
                variableType = ParseVariableType(ReadMember(def, "Type")?.ToString() ?? "object");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 判定表规则输出：<c>TableName.RuleName.结果</c>（<c>JudgeTablePlugin.cs:398-412</c> 的算法）。
        /// 规则表在 <c>InputValues["Rules"]</c> 里（确认时落盘）。
        /// </summary>
        private static bool TryGetFromJudgeTable(
            StepModel step, out string variableName, out Type variableType)
        {
            variableName = null;
            variableType = typeof(object);

            if (!step.InputValues.TryGetValue("Rules", out var raw) || raw == null)
                return false;

            var tableName = (ReadMember(step.InputValues, "TableName") as string ?? string.Empty).Trim();
            foreach (var rule in EnumerateObjects(raw))
            {
                var name = (ReadMember(rule, "Name") as string ?? string.Empty).Trim();
                if (name.Length == 0) continue;
                if (tableName.Length == 0) continue;

                variableName = tableName + "." + name + ".结果";
                variableType = typeof(bool);   // 判定表输出是 IsOk（bool）
                return true;
            }
            return false;
        }

        /// <summary>把"可能是一堆对象"的原始值摊平成可枚举（兼容集合 / JArray / 单个对象）。</summary>
        private static IEnumerable<object> EnumerateObjects(object raw)
        {
            if (raw is string || raw is System.Collections.IDictionary) yield break;
            if (raw is System.Collections.IEnumerable list)
            {
                foreach (var item in list)
                {
                    if (item is Newtonsoft.Json.Linq.JValue jv)
                    {
                        yield return jv.Value;
                        continue;
                    }
                    yield return item;
                }
                yield break;
            }
            yield return raw;
        }

        /// <summary>按名字读成员（兼容 POCO 属性与 JObject 键；读不到给 null）。</summary>
        private static object ReadMember(object target, string name)
        {
            if (target == null || string.IsNullOrEmpty(name)) return null;

            if (target is Newtonsoft.Json.Linq.JObject jo)
            {
                var token = jo[name];
                if (token == null || token.Type == Newtonsoft.Json.Linq.JTokenType.Null) return null;
                return token is Newtonsoft.Json.Linq.JValue jv ? jv.Value : token;
            }

            if (target is System.Collections.IDictionary dict)
                return dict.Contains(name) ? dict[name] : null;

            var prop = target.GetType().GetProperty(name);
            return prop?.GetValue(target);
        }

        /// <summary>
        /// 将 VariableDefinitionPlugin 的 Type 端口字符串解析为对应的 Type
        /// 与 VariableDefinitionPlugin.ParseType 保持一致的基础类型集合
        /// </summary>
        private static Type ParseVariableType(string typeName)
        {
            return typeName?.ToLower() switch
            {
                "int" or "int32" => typeof(int),
                "double" => typeof(double),
                "string" => typeof(string),
                "bool" or "boolean" => typeof(bool),
                "datetime" => typeof(DateTime),
                "float" or "single" => typeof(float),
                "long" or "int64" => typeof(long),
                _ => typeof(object)
            };
        }

        /// <summary>
        /// 获取目标步骤之前的所有上游步骤
        /// 支持嵌套容器步骤的递归查找
        /// </summary>
        public static List<StepModel> GetUpstreamNodes(
            IEnumerable<StepModel> steps,
            StepModel targetStep
        )
        {
            var result = new List<StepModel>();
            foreach (var step in steps)
            {
                if (step == targetStep)
                    return result;
                result.Add(step);

                // 递归所有容器（If / While / For），而不是只认 ConditionStep。
                // 旧实现只判 ConditionStep：While 靠继承侥幸覆盖，For 的子层整个漏掉，
                // 表现是"循环体里定义的变量在下游绑定弹窗里选不到"，而运行期 LocalVariables 明明有值。
                // 判接口不判具体类型，以后新增容器步骤不必再来这里补一刀。
                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var childCollection in container.Children)
                    {
                        if (childCollection?.Steps == null)
                            continue;

                        var innerResult = GetUpstreamNodes(childCollection.Steps, targetStep);
                        result.AddRange(innerResult);

                        if (innerResult.Contains(targetStep))
                            return result;
                    }
                }
            }
            return result;
        }
    }
}
