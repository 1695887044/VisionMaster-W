using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using HalconDotNet;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
// 本文件 using 了 System.Threading，裸写的 ExecutionContext 会和 System.Threading.ExecutionContext 撞名
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 图像变量的所有权与生命周期（2026-10-24 审查抓出的缺陷的回归）。
    ///
    /// 缺陷原貌：`图像赋值` / `变量赋值` / C# 脚本共享变量写入三条路，都把**上游端口上的
    /// HImage 对象本身**交给变量（<c>LocalVariableModel.TryWrite</c> 直接 <c>Value = value</c>）。
    /// 而 <c>VisionPluginBase.DisposePreviousRoundOutputs</c> 会在上游下一轮开始时释放那个对象：
    ///   · 变量里剩下死句柄 → <c>ToBitmapSource</c> 吞异常返回 null → Web `/img` 404「该图元当前没有图像」；
    ///   · 桌面组态靠"写入事件"才读值，所以看起来自愈 —— 这也是一直没被发现的原因。
    ///
    /// 修法：写入即拷贝（变量只持有自己那份副本），释放延迟一代，移出集合 / Clear 时释放。
    /// 本文件钉住五件事：
    ///   ① 写进去的是副本，源图释放后副本仍可用（**修复前这一条是红的**）；
    ///   ② 延迟一代的释放时机（写第 N+1 张时释放第 N-1 张，不是当场释放）；
    ///   ③ 释放口真的有人调（移出集合 / Clear），且不误杀"不是自己拷的"图；
    ///   ④ 非图像写入不做无谓拷贝（不能把整条变量通路都变成深拷贝）；
    ///   ⑤ 端到端：真插件（变量赋值）写全局图像变量，走的是同一个收口。
    /// </summary>
    internal static class ImageVariableOwnershipChecks
    {
        public static void Run()
        {
            Section("[IV] 图像变量：写入即拷贝 / 延迟一代释放 / 释放口有人调");

            CloneOnWrite();
            DelayedRetire();
            NonImageKeepsReference();
            ResetToDefaultReleasesOnlyOwned();
            RemoveFromCollectionReleases();
            ClearCollectionReleases();
            BorrowedValueIsNotReleased();
            UninitializedImageRejected();
            PluginWritePathClones();
        }

        /// <summary>① 写进去的是副本：源图（= 上游端口对象）释放后，变量里的图仍然能用</summary>
        private static void CloneOnWrite()
        {
            var workspace = NewWorkspace(out var target, "Img");

            var source = MakeImage(64, 48);
            var ok = target.TryWrite(source, out var error);

            Check("[IV1] 写入 HImage 成功", ok, $"ok={ok} err=[{error}]");
            Check("[IV2] 变量里不是同一个实例（写入即拷贝）",
                !ReferenceEquals(target.Value, source), Describe(target.Value));

            var stored = target.Value as HImage;
            Check("[IV3] 副本已初始化且尺寸正确", IsAlive(stored) && SizeOf(stored) == "64x48",
                $"{Describe(stored)} 尺寸={SizeOf(stored)}");

            // 等价"上游下一轮开始"：DisposePreviousRoundOutputs 释放端口上的那张
            source.Dispose();

            Check("[IV4] 源图释放后副本仍可读像素（修复前这里是死的）",
                IsAlive(stored) && SizeOf(stored) == "64x48",
                $"{Describe(stored)} 尺寸={SizeOf(stored)}");

            Check("[IV5] Web 取值出口仍能转出位图（用户可见症状的直接断言）",
                HalconImageHelper.ToBitmapSource(stored) != null,
                "ToBitmapSource 返回 null 就等于 Web 端 404「该图元当前没有图像」");

            workspace.GlobalVariables.Clear();
        }

        /// <summary>② 延迟一代：写第 N+1 张时释放第 N-1 张（当场释放会与 UI 线程的按需读撞车）</summary>
        private static void DelayedRetire()
        {
            var workspace = NewWorkspace(out var target, "RetireImg");

            var first = MakeImage(16, 16);
            target.TryWrite(first, out _);
            first.Dispose();
            var clone1 = target.Value as HImage;

            var second = MakeImage(16, 16);
            target.TryWrite(second, out _);
            second.Dispose();
            var clone2 = target.Value as HImage;

            Check("[IV6] 第二次写入后，第一份副本还活着（延迟一代，不当场释放）",
                IsAlive(clone1), $"{Describe(clone1)}");

            var third = MakeImage(16, 16);
            target.TryWrite(third, out _);
            third.Dispose();

            Check("[IV7] 第三次写入后，第一份副本才被释放", !IsAlive(clone1), $"{Describe(clone1)}");
            Check("[IV8] 第二份副本仍在（只淘汰一代）且当前值可用",
                IsAlive(clone2) && IsAlive(target.Value as HImage),
                $"{Describe(clone2)} / 当前={Describe(target.Value)}");

            workspace.GlobalVariables.Clear();
        }

        /// <summary>③ 反向：非 HImage 写入保持引用，不做无谓拷贝</summary>
        private static void NonImageKeepsReference()
        {
            var workspace = new WorkspaceContext();
            workspace.GlobalVariables.Clear();

            var textVar = (LocalVariableModel)VariableFactory.CreateLocal("Txt", typeof(string), "断言用文本变量");
            workspace.GlobalVariables.Add(textVar);

            var text = new string('x', 8);
            textVar.TryWrite(text, out _);

            Check("[IV9] 非 HImage 写入保持引用（不能把整条变量通路变成深拷贝）",
                ReferenceEquals(textVar.Value, text), Describe(textVar.Value));

            workspace.GlobalVariables.Clear();
        }

        /// <summary>④ ResetToDefault：释放自己那份，绝不碰 DefaultValue 那个实例</summary>
        private static void ResetToDefaultReleasesOnlyOwned()
        {
            // 新建 HImage 变量时 DefaultValue 与 Value 是同一个实例（Activator.CreateInstance 出来的空图）；
            // 这里显式用一张**活图**当默认值，把"不许释放 DefaultValue"这条判据钉死。
            var defaultImage = MakeImage(8, 8);
            var variable = new LocalVariableModel
            {
                Name = "ResetImg",
                DataType = typeof(HImage),
                DefaultValue = defaultImage,
                Value = defaultImage
            };

            var source = MakeImage(20, 20);
            variable.TryWrite(source, out _);
            source.Dispose();
            var ownedClone = variable.Value as HImage;

            variable.ResetToDefault();

            Check("[IV10] ResetToDefault 释放本变量自己那份副本", !IsAlive(ownedClone), Describe(ownedClone));
            Check("[IV11] ResetToDefault 不释放 DefaultValue 那个实例（否则恢复初始值拿到已释放对象）",
                IsAlive(defaultImage) && ReferenceEquals(variable.Value, defaultImage),
                $"DefaultValue={Describe(defaultImage)} 当前={Describe(variable.Value)}");

            variable.ReleaseValues();
            defaultImage.Dispose();
        }

        /// <summary>⑤ 变量被移出集合：副本当场释放，当前值置空（不再把死句柄交出去）</summary>
        private static void RemoveFromCollectionReleases()
        {
            var workspace = NewWorkspace(out var variable, "RemoveImg");

            var source = MakeImage(24, 24);
            variable.TryWrite(source, out _);
            source.Dispose();
            var clone = variable.Value as HImage;

            workspace.GlobalVariables.Remove(variable);

            Check("[IV12] 移出集合后变量持有的副本被释放", !IsAlive(clone), Describe(clone));
            Check("[IV13] 移出集合后当前值置空", variable.Value == null, Describe(variable.Value));
        }

        /// <summary>⑥ 集合被 Clear：旧变量没人再引用，副本一并释放</summary>
        private static void ClearCollectionReleases()
        {
            var workspace = NewWorkspace(out var variable, "ClearImg");

            var source = MakeImage(24, 24);
            variable.TryWrite(source, out _);
            source.Dispose();
            var clone = variable.Value as HImage;

            workspace.GlobalVariables.Clear();

            Check("[IV14] Clear() 后变量持有的副本被释放", !IsAlive(clone), Describe(clone));
        }

        /// <summary>⑦ 反向：绕过写入正门塞进来的借用图，变量不释放（只释放自己拷的）</summary>
        private static void BorrowedValueIsNotReleased()
        {
            var borrowed = MakeImage(12, 12);
            var variable = new LocalVariableModel { Name = "Borrowed", DataType = typeof(HImage) };

            variable.Value = borrowed; // 模拟历史代码 / 外部直写：所有权不在变量
            variable.ReleaseValues();

            Check("[IV15] 直接赋值进来的借用图不被变量释放（所有权判定按\"是不是我拷的\"）",
                IsAlive(borrowed), Describe(borrowed));

            borrowed.Dispose();
        }

        /// <summary>⑧ 空句柄图像拒绝写入，并给出可直接上屏的原因</summary>
        private static void UninitializedImageRejected()
        {
            var variable = new LocalVariableModel { Name = "EmptyImg", DataType = typeof(HImage) };
            var empty = new HImage();

            var ok = variable.TryWrite(empty, out var error);

            Check("[IV16] 未初始化图像被拒且带原因",
                !ok && !string.IsNullOrEmpty(error), $"ok={ok} err=[{error}]");

            empty.Dispose();
            variable.ReleaseValues();
        }

        /// <summary>⑨ 端到端：真插件（变量赋值，作用域=全局）写图像变量，走的也是同一个收口</summary>
        private static void PluginWritePathClones()
        {
            var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                          .FirstOrDefault(a => a.GetName().Name == "Plugin.Util")
                      ?? Assembly.LoadFrom(Path.Combine(repoRoot, @"Modules\Plugin.Util.dll"));
            var pluginType = asm.GetType("VisionMaster.Plugins.Util.VariableAssignmentPlugin", true)!;

            var workspace = new WorkspaceContext();
            workspace.GlobalVariables.Clear();
            workspace.GlobalVariables.Add(VariableFactory.CreateLocal("E2EImg", typeof(HImage), "端到端断言用图像变量"));

            var solution = new SolutionModel { SolutionName = "图像变量所有权断言" };
            var flow = new FlowModel { FlowName = "图像变量所有权断言流程" };
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);
            workspace.SwitchFlow(flow);

            var source = MakeImage(40, 30);

            var step = new ActionStep("\uE700", "变量赋值", pluginType.AssemblyQualifiedName!, "图像所有权_0");
            step.SetInputValue("Scope", "Global");
            step.SetInputValue("Value", source);
            step.SetInputValue("Name", "E2EImg");
            step.SetInputValue("CreateIfNotExists", false);
            flow.Steps.Add(step);

            var compile = new FlowCompiler(workspace).Compile(flow.Steps, flow.FlowName);
            var detail = compile.Success ? "" : string.Join(" | ", compile.Errors.Select(x => x.Message));

            HImage stored = null;
            if (compile.Success)
            {
                var session = new FlowSession { FlowName = flow.FlowName, ExecutionEngine = compile.Data };
                session.AddBlueprintsDeep(flow.Steps);
                var ctx = new ExecutionContext(new StubLog(), session, workspace, new CancellationTokenSource().Token);
                compile.Data!.Run(ctx);

                stored = workspace.VariableRegistry?.FindByName("E2EImg")?.Value as HImage;
            }

            Check("[IV17] 插件写全局图像变量：存的是副本",
                compile.Success && stored != null && !ReferenceEquals(stored, source) && IsAlive(stored),
                $"编译={compile.Success} {detail} 变量值={Describe(stored)}");

            source.Dispose();

            Check("[IV18] 插件写完、上游释放源图后，变量里的图仍可用（本条是本次缺陷的直接回归）",
                IsAlive(stored) && SizeOf(stored) == "40x30",
                $"{Describe(stored)} 尺寸={SizeOf(stored)}");

            workspace.GlobalVariables.Clear();
        }

        // ------------------------------------------------------------------ 辅助

        /// <summary>建一个只装一个 HImage 变量（已清掉演示变量）的工作区，并切好方案与流程</summary>
        private static WorkspaceContext NewWorkspace(out LocalVariableModel variable, string name)
        {
            var workspace = new WorkspaceContext();
            workspace.GlobalVariables.Clear();

            variable = (LocalVariableModel)VariableFactory.CreateLocal(name, typeof(HImage), "断言用图像变量");
            workspace.GlobalVariables.Add(variable);

            var solution = new SolutionModel { SolutionName = "图像变量所有权断言" };
            var flow = new FlowModel { FlowName = "图像变量所有权断言流程" };
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);
            workspace.SwitchFlow(flow);
            return workspace;
        }

        /// <summary>造一张恒定灰度图；返回的实例就是"上游端口上那张"，用完由调用方 Dispose</summary>
        private static HImage MakeImage(int width, int height)
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", width, height);
            var image = new HImage(proto);
            proto.Dispose();
            return image;
        }

        /// <summary>
        /// 句柄是否还有效。释放后 <c>IsInitialized()</c> 的两种表现都要按"已死"处理：
        /// 返回 false（本仓实测过的形态）或直接抛 HALCON 异常（部分版本）。
        /// </summary>
        private static bool IsAlive(HImage image)
        {
            if (image == null)
                return false;

            try { return image.IsInitialized(); }
            catch { return false; }
        }

        private static string SizeOf(HImage image)
        {
            if (image == null)
                return "(null)";

            try
            {
                image.GetImageSize(out int width, out int height);
                return $"{width}x{height}";
            }
            catch (Exception ex)
            {
                return $"<{ex.GetType().Name}>";
            }
        }

        private static string Describe(object value)
        {
            if (value == null)
                return "<null>";

            if (value is HImage image)
                return IsAlive(image) ? $"HImage(活) {SizeOf(image)}" : "HImage(已释放)";

            return $"{value} ({value.GetType().Name})";
        }
    }
}
