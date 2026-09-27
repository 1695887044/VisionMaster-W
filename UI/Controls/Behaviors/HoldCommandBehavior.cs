using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace UI.Behaviors
{
    /// <summary>
    /// 「按住才执行、松手即停」的命令行为。
    ///
    /// 用途：点动（Jog）、长按微调这类**动作必须有终点**的交互 —— 松手不停就等于机器一直动。
    ///
    /// 为什么做成 UI 库的公共行为，而不是在调试面板的 code-behind 里写事件：
    ///   ① 这类交互在点动、对位、试跑等场景会反复出现，写一遍共用；
    ///   ② 本项目一贯保持视图"零逻辑"，交互细节收在库里，日后统一改。
    ///
    /// **四条"松开"路径都必须处理**（漏一条就会一直动）：
    ///   1. 在按钮上松开鼠标；
    ///   2. 按住后把鼠标移出按钮再松开 —— 用 <c>Mouse.Capture</c> 把后续事件拉回按钮，这条被 1 覆盖；
    ///   3. 窗口失焦（Alt+Tab、被其他窗口盖住）—— 靠 <c>LostMouseCapture</c> 兜底；
    ///   4. 控件被禁用或从树上移除 —— 同上。
    ///
    /// 另外按住期间会按 <see cref="RepeatIntervalMsProperty"/> 周期**重复执行** HoldCommand：
    /// 这既是"持续点动"的语义（有些控制器要求周期重发速度指令），
    /// 也给上层的心跳兜底提供了脉冲来源 —— 万一上面四条全部失效，
    /// 上层的看门狗仍会在超时后停住设备（安全不能只赌界面事件）。
    /// </summary>
    public static class HoldCommandBehavior
    {
        /// <summary>按下时执行（按住期间按固定周期重复执行）</summary>
        public static readonly DependencyProperty HoldCommandProperty =
            DependencyProperty.RegisterAttached(
                "HoldCommand", typeof(ICommand), typeof(HoldCommandBehavior),
                new PropertyMetadata(null, OnHoldCommandChanged));

        public static void SetHoldCommand(DependencyObject element, ICommand? value)
            => element.SetValue(HoldCommandProperty, value);

        public static ICommand? GetHoldCommand(DependencyObject element)
            => (ICommand?)element.GetValue(HoldCommandProperty);

        /// <summary>松开时执行（松手、移出后松开、失焦都会触发；未按过则不触发）</summary>
        public static readonly DependencyProperty ReleaseCommandProperty =
            DependencyProperty.RegisterAttached(
                "ReleaseCommand", typeof(ICommand), typeof(HoldCommandBehavior),
                new PropertyMetadata(null));

        public static void SetReleaseCommand(DependencyObject element, ICommand? value)
            => element.SetValue(ReleaseCommandProperty, value);

        public static ICommand? GetReleaseCommand(DependencyObject element)
            => (ICommand?)element.GetValue(ReleaseCommandProperty);

        /// <summary>
        /// 传给 HoldCommand 的参数（可选）。
        /// 不用它时传元素自身；点动这类需要区分方向的场合，用它传 "+" / "-" 比在 VM 里反查按钮更直接。
        /// </summary>
        public static readonly DependencyProperty HoldCommandParameterProperty =
            DependencyProperty.RegisterAttached(
                "HoldCommandParameter", typeof(object), typeof(HoldCommandBehavior),
                new PropertyMetadata(null));

        public static void SetHoldCommandParameter(DependencyObject element, object? value)
            => element.SetValue(HoldCommandParameterProperty, value);

        public static object? GetHoldCommandParameter(DependencyObject element)
            => element.GetValue(HoldCommandParameterProperty);

        /// <summary>按住期间的重复间隔（ms）。默认 150 —— 足够快以覆盖上层心跳，又不至于刷爆命令队列</summary>
        public static readonly DependencyProperty RepeatIntervalMsProperty =
            DependencyProperty.RegisterAttached(
                "RepeatIntervalMs", typeof(int), typeof(HoldCommandBehavior),
                new PropertyMetadata(150));

        public static void SetRepeatIntervalMs(DependencyObject element, int value)
            => element.SetValue(RepeatIntervalMsProperty, value);

        public static int GetRepeatIntervalMs(DependencyObject element)
            => (int)element.GetValue(RepeatIntervalMsProperty);

        /// <summary>每个元素自己的按住状态（挂在元素上，避免静态字典带来的泄漏与串台）</summary>
        private static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached(
                "State", typeof(HoldState), typeof(HoldCommandBehavior), new PropertyMetadata(null));

        private sealed class HoldState
        {
            public bool Holding;
            public DispatcherTimer? Timer;
        }

        private static void OnHoldCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element) return;

            element.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            element.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
            element.LostMouseCapture -= OnLostMouseCapture;

            if (e.NewValue != null)
            {
                element.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
                element.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
                element.LostMouseCapture += OnLostMouseCapture;
            }
            else
            {
                Release(element, "HoldCommand 被移除");
            }
        }

        private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not UIElement element) return;

            var state = GetOrCreateState(element);
            state.Holding = true;

            // 捕获鼠标：这样"按住后移出按钮再松开"的 MouseUp 仍会回到本元素，
            // 不会因为鼠标在外面而漏掉松开事件
            try { element.CaptureMouse(); } catch { /* 某些容器不允许捕获，忽略（还有 LostMouseCapture 与看门狗兜底） */ }

            ExecuteHold(element);

            state.Timer ??= new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(30, GetRepeatIntervalMs(element))),
            };
            state.Timer.Tick -= OnRepeatTick;
            state.Timer.Tick += OnRepeatTick;
            state.Timer.Tag = element;
            state.Timer.Start();
        }

        private static void OnRepeatTick(object? sender, EventArgs e)
        {
            if (sender is not DispatcherTimer timer || timer.Tag is not UIElement element) return;

            var state = GetState(element);
            if (state == null || !state.Holding)
            {
                timer.Stop();
                return;
            }

            ExecuteHold(element);
        }

        /// <summary>执行 HoldCommand：参数取 HoldCommandParameter，未设时传元素自身</summary>
        private static void ExecuteHold(UIElement element)
            => Execute(GetHoldCommand(element), GetHoldCommandParameter(element) ?? element);

        private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
            => Release(sender as UIElement, "松开");

        private static void OnLostMouseCapture(object sender, MouseEventArgs e)
            => Release(sender as UIElement, "失去鼠标捕获（窗口失焦/控件被禁用）");

        /// <summary>统一的收尾路径（<paramref name="reason"/> 仅用于可读性，不参与逻辑）</summary>
        private static void Release(UIElement? element, string reason)
        {
            _ = reason;
            if (element == null) return;

            var state = GetState(element);
            if (state == null || !state.Holding) return;   // 重入保护：ReleaseMouseCapture 会再触发 LostMouseCapture

            state.Holding = false;
            state.Timer?.Stop();

            if (Mouse.Captured == element)
            {
                try { element.ReleaseMouseCapture(); } catch { /* 已释放 */ }
            }

            Execute(GetReleaseCommand(element), element);
        }

        private static HoldState GetOrCreateState(UIElement element)
        {
            if (element.GetValue(StateProperty) is HoldState state) return state;

            state = new HoldState();
            element.SetValue(StateProperty, state);
            return state;
        }

        private static HoldState? GetState(UIElement element)
            => element.GetValue(StateProperty) as HoldState;

        private static void Execute(ICommand? command, object parameter)
        {
            if (command == null) return;
            if (!command.CanExecute(parameter)) return;

            try { command.Execute(parameter); }
            catch { /* 行为里的异常绝不能冒泡到输入事件（会让整个 UI 输入卡住） */ }
        }
    }
}
