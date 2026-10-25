using System;
using System.Collections.Generic;
using Core.Interfaces;
using Core.Interfaces.Database;
using VisionMaster.Models;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace VisionMaster.Services
{
    /// <summary>
    /// 运行期能力面的**来源集合**（谁手里有这批能力，就从谁那里装进上下文）。
    ///
    /// 为什么要有这个类型，而不是让每个调用方各写一份对象初始化器：
    /// 2026-10-24 评审团 C1 —— 连续执行 / 单次执行 / 并行分支三条路径各自手写 <c>new ExecutionContext { … }</c>，
    /// 单次执行那份漏了 <c>Database</c>，同一个流程"连续跑能查库、单次跑必报没有接入数据库服务"，
    /// 而当时没有任何断言盯着"注入清单是否齐全"。再加一项能力，三份清单靠人眼对齐必然再漏。
    ///
    /// 【只有一个装配入口】<see cref="ExecutionContext.Create"/>：它按
    /// <see cref="ExecutionContext.RuntimeCapabilities"/> 反射比对，**漏搬一项即抛异常**，
    /// 所以"新增能力属性但忘了搬运"在运行期第一跳就炸，而不是静默回落成空实现。
    ///
    /// 守门：FlowCanvasChecks 的 EngineContextWiringChecks。
    /// </summary>
    public sealed class ExecutionContextCapabilitySource
    {
        /// <summary>相机仓库（方案级硬件资源）；null 时回落 <see cref="NullCameraProvider"/></summary>
        public ICameraProvider? Cameras { get; init; }

        /// <summary>运动卡仓库；null 时回落 <see cref="NullMotionProvider"/></summary>
        public IMotionProvider? Motions { get; init; }

        /// <summary>流程调用器（「调用流程」步骤用）；null 时回落 <see cref="NullFlowInvoker"/></summary>
        public IFlowInvoker? FlowInvoker { get; init; }

        /// <summary>通讯变量立刻读写入口；null 时回落 <see cref="NullCommVariableAccess"/></summary>
        public ICommVariableAccess? Comms { get; init; }

        /// <summary>数据库运行期能力面；null 时回落 <see cref="NullDatabaseAccess"/></summary>
        public IDatabaseAccess? Database { get; init; }

        /// <summary>按 <see cref="ExecutionContext.RuntimeCapabilities"/> 的顺序给出非空能力实例（null 已回落成空实现）</summary>
        internal object?[] ResolveCapabilities()
        {
            var values = new object?[ExecutionContext.RuntimeCapabilities.Length];
            for (int i = 0; i < ExecutionContext.RuntimeCapabilities.Length; i++)
            {
                var name = ExecutionContext.RuntimeCapabilities[i].Name;
                values[i] = name switch
                {
                    nameof(Cameras) => Cameras ?? NullCameraProvider.Instance,
                    nameof(Motions) => Motions ?? NullMotionProvider.Instance,
                    nameof(FlowInvoker) => FlowInvoker ?? NullFlowInvoker.Instance,
                    nameof(Comms) => Comms ?? NullCommVariableAccess.Instance,
                    nameof(Database) => Database ?? NullDatabaseAccess.Instance,
                    _ => throw new InvalidOperationException(
                        $"运行期能力面「{name}」已登记到 ExecutionContext.RuntimeCapabilities，" +
                        "但 ExecutionContextCapabilitySource.ResolveCapabilities 没有对应分支——" +
                        "新增能力时必须同时改这两处，否则上下文装配会静默回落成空实现。"),
                };
            }

            return values;
        }

        /// <summary>
        /// 供并行分支上下文等"以父上下文为种子"的场景使用：把父上下文既有的能力面原样搬成一份来源。
        /// 与 <see cref="ExecutionContext.Create"/> 无关——那条路是从宿主服务构造，这条路是从另一份上下文派生。
        /// </summary>
        internal static void CopyInto(ExecutionContext parent, ExecutionContext child)
        {
            if (parent is null) throw new ArgumentNullException(nameof(parent));
            if (child is null) throw new ArgumentNullException(nameof(child));

            for (int i = 0; i < ExecutionContext.RuntimeCapabilities.Length; i++)
            {
                var property = ExecutionContext.RuntimeCapabilities[i];
                var value = property.GetValue(parent);
                if (value is null)
                {
                    throw new InvalidOperationException(
                        $"父上下文的能力面「{property.Name}」为 null：ExecutionContext 上的能力属性都应带非空默认值，" +
                        "为 null 说明有人显式赋了 null，分支上下文无法如实继承。");
                }

                property.SetValue(child, value);
            }
        }
    }
}
