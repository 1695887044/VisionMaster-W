using System;
using System.Windows;
using UI.CustomControl;
using VisionMaster.Scada.Controls;

namespace VisionMaster.Services
{
    /// <summary>
    /// SCADA 二次确认的宿主实现：走已确认的 <see cref="MessageDialog"/> 基线。
    ///
    /// 为什么放在宿主而不是控件库：控件库（<c>VM.Scada.Controls</c>）不认识 UI 主题与弹窗引擎，
    /// 它只认 <see cref="IScadaConfirmationService"/>；断言工程则换一个假实现，
    /// 在无窗口环境里把"确认 / 取消"两条路都跑一遍。
    ///
    /// 字段映射（与 docs/UI设计基线/scada-confirm-pause 一致）：
    /// Title / Message / Hint / Question → MessageDialogOptions 同名槽；
    /// Danger → Severity=Danger + PrimaryTone=Danger；默认焦点始终在取消。
    /// </summary>
    public sealed class ScadaConfirmationService : IScadaConfirmationService
    {
        /// <summary>
        /// 是否允许弹交互式确认框。方案级监控在“没有 SCADA 运行窗口”时置 false：
        /// 无人值守的后台变量事件如果弹一个没人点的模态框，会把 UI 线程卡死。
        /// 默认 true（窗口路径 / 旧行为）；窗口打开时由宿主置回 true。
        /// </summary>
        public static bool InteractiveAllowed { get; set; } = true;

        /// <inheritdoc />
        public bool Confirm(string title, string message)
            => Confirm(new ScadaConfirmationRequest(title, message));

        /// <inheritdoc />
        public bool Confirm(string title, string message, bool danger)
            => Confirm(new ScadaConfirmationRequest(title, message, Danger: danger));

        /// <inheritdoc />
        public bool Confirm(ScadaConfirmationRequest request)
        {
            if (request == null)
                return false;

            // 无 WPF 应用上下文（断言宿主 / 设计器 / 已关机）：没有弹窗可弹。
            // 返回 false 让调用方如实拒绝执行——静默放行等于把防误触开关悄悄关掉。
            // 方案级监控在没有运行窗口时也不许弹：无人值守下会卡住 UI 线程。
            if (!InteractiveAllowed || Application.Current == null)
                return false;

            var options = new MessageDialogOptions
            {
                Severity = request.Danger ? MessageSeverity.Danger : MessageSeverity.Warning,
                Title = request.Title,
                Message = request.Message,
                Hint = request.Hint,
                Question = string.IsNullOrWhiteSpace(request.Question) ? "是否继续？" : request.Question,
                // 主按钮文案由动作语义给（确认停止 / 确认启动 / 确认执行）；
                // 服务端不再对所有危险动作写死“确认停止”，否则“确认启动流程？”会配错按钮。
                PrimaryText = string.IsNullOrWhiteSpace(request.PrimaryText)
                    ? (request.Danger ? "确认" : "确认执行")
                    : request.PrimaryText!,
                SecondaryText = "取消",
                PrimaryTone = request.Danger ? MessageTone.Danger : MessageTone.Primary,
                // 二次确认一律把默认焦点放在取消：Enter/Y 都不会执行动作。
                DefaultFocus = MessageResult.Secondary,
                ShowClose = true,
                // 长后果提示用 560 档，短文案保持 480；两档都是 message-dialog 基线已确认的宽度。
                Width = (request.Hint?.Length ?? 0) > 36 || (request.Message?.Length ?? 0) > 36
                    ? 560
                    : 480,
            };

            try
            {
                // 确认来自按钮点击，本来就在 UI 线程；这里再兜一层，
                // 将来若有脚本/后台路径调用也不会跨线程碰控件。
                var result = Application.Current.Dispatcher.CheckAccess()
                    ? MessageDialog.Show(options)
                    : Application.Current.Dispatcher.Invoke(() => MessageDialog.Show(options));

                return result == MessageResult.Primary;
            }
            catch
            {
                // 契约要求实现不抛异常：窗口正在关闭 / Owner 失效 / Dispatcher 停摆时，
                // 把异常交给调用方会沿 UI 线程冒到进程级（本仓没有 DispatcherUnhandledException 兜底）。
                // 按“取消”处理是失败安全：宁可这次不执行，也不静默放行。
                return false;
            }
        }
    }
}
