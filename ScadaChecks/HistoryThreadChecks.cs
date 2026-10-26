using System;
using System.Threading;
using System.Windows.Controls;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Services;
using ScadaEditorVM = VisionMaster.ViewModels.ScadaEditorViewModel;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [HX] 撤销栈的线程边界。
        ///
        /// <see cref="ScadaEditHistory"/> 是**静态全局**栈 + 静态 <c>Changed</c> 事件：谁改文档谁压栈，
        /// 模型层没有任何线程约束；而订阅方（编辑器视图模型）在处理器里碰的全是 UI 绑定状态
        /// （命令的 <c>CanExecuteChanged</c>、基座候选项集合）。一旦通知来自别的线程，
        /// 就会去摸那个线程上的按钮，抛"调用线程无法访问此对象"——后台线程的未处理异常会终止进程，
        /// App 层的 <c>DispatcherUnhandledException</c> 兜不住（那只管 UI 线程）。
        ///
        /// 复现方式刻意做成"死线程 + 活按钮"：一次性 STA 线程里建 VM 与绑了撤销命令的按钮，
        /// 线程退出后在本线程压栈。修复前必崩，修复后被封送回归属线程（或按"归属线程已停"丢弃）。
        /// 按钮要用强引用按住：Prism 的 <c>CanExecuteChanged</c> 是弱事件，被 GC 掉就测不出跨线程。
        /// </summary>
        private static class HistoryThreadChecks
        {
            public static void Run()
            {
                Section("[HX] 撤销栈跨线程：静态事件不许直接摸订阅方的 UI");

                Exception? failure = null;

                var thread = new Thread(() =>
                {
                    try { RunCore(); }
                    catch (Exception ex) { failure = ex; }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                thread.Join();

                if (failure != null)
                    Check("[HX] 跨线程压栈全程未抛异常", false, failure.ToString());
            }

            private static void RunCore()
            {
                Exception? setupFailure = null;
                ScadaEditorVM? deadVm = null;
                Button? boundButton = null;

                // ① 一次性 STA 线程：建 VM + Activate + 造一只绑了撤销命令的按钮，然后让线程退出
                var deadThread = new Thread(() =>
                {
                    try
                    {
                        var workspace = new WorkspaceContext();
                        var doc = new ScadaDocument();
                        doc.AddPage("画面_1");
                        workspace.SwitchSolution(new SolutionModel { Scada = doc });

                        var vm = new ScadaEditorVM(workspace, new FakeRuntimeHost());
                        vm.Activate();

                        // ButtonBase 在 Command 赋值时就订阅 CanExecuteChanged —— 这就是崩溃现场那只按钮
                        var button = new Button { Command = vm.UndoCommand };

                        deadVm = vm;
                        boundButton = button;
                    }
                    catch (Exception ex) { setupFailure = ex; }
                });
                deadThread.SetApartmentState(ApartmentState.STA);
                deadThread.IsBackground = true;
                deadThread.Start();
                deadThread.Join();

                Check("[HX1] 造出「一次性 STA 线程上的编辑器 VM + 绑了撤销命令的按钮」（复现前置）",
                    setupFailure == null && deadVm != null && boundButton != null,
                    setupFailure?.Message ?? "ok");

                // ② 本线程压栈：修复前这里会抛「调用线程无法访问此对象」
                var pushDoc = new ScadaDocument();
                bool threw = false;
                string detail = "";

                try { pushDoc.AddAlarm("跨线程压栈"); }
                catch (Exception ex)
                {
                    threw = true;
                    detail = ex.GetType().Name + "：" + ex.Message;
                }

                Check("[HX2] 别的线程压撤销栈不炸（静态 Changed 不许跨线程摸订阅方的按钮）",
                    !threw,
                    threw ? detail : "ok");

                // ③ 同线程压栈仍要照常刷新：封送不许把正常路径一起吞掉
                var liveDoc = new ScadaDocument();
                var liveWorkspace = new WorkspaceContext();
                liveWorkspace.SwitchSolution(new SolutionModel { Scada = liveDoc });

                var liveVm = new ScadaEditorVM(liveWorkspace, new FakeRuntimeHost());
                liveVm.Activate();

                string? labelBefore = liveVm.UndoLabel;
                liveDoc.AddPage("画面_2");
                string? labelAfter = liveVm.UndoLabel;

                Check("[HX3] 同线程压栈立刻刷新撤销按钮（封送没有把正常路径一起吞掉）",
                    labelAfter != labelBefore
                    && !string.IsNullOrEmpty(labelAfter)
                    && liveVm.UndoCommand.CanExecute(),
                    $"{labelBefore ?? "<null>"} → {labelAfter ?? "<null>"}");

                liveVm.Deactivate();

                // 复现用的死 VM 也摘掉：静态事件持有的是强引用，留着它会让后面每一段压栈
                // 都往一条停摆的 Dispatcher 上投递（修复后无害，但没理由攒垃圾）。
                // 注意必须在 [HX2] 之后摘：那一条要的就是"还挂着订阅的死线程 VM"。
                deadVm?.Deactivate();

                // 按钮与死 VM 必须活到断言结束（弱事件 + GC 会让 [HX2] 变成"有时绿"）
                GC.KeepAlive(boundButton);
                GC.KeepAlive(deadVm);
            }
        }
    }
}
