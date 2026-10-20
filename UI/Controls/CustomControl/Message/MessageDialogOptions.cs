using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace UI.CustomControl
{
    /// <summary>消息弹窗的语义级别（决定图标块底色、描边与字形）。</summary>
    public enum MessageSeverity
    {
        Info,
        Success,
        Warning,
        Danger,
    }

    /// <summary>主按钮的语气：普通主色 / 危险红。</summary>
    public enum MessageTone
    {
        Primary,
        Danger,
    }

    /// <summary>详情行数量 chip 的语气。</summary>
    public enum MessageChipTone
    {
        Accent,
        Neutral,
        Success,
        Danger,
    }

    /// <summary>
    /// 消息弹窗的关闭方式。
    /// Primary = 主按钮；Secondary = 次按钮 / Esc / 右上角 ×；Close = 窗口被外部关闭。
    /// </summary>
    public enum MessageResult
    {
        Primary,
        Secondary,
        Close,
    }

    /// <summary>
    /// 结构化详情行：标签 + 数量 chip + 可复制的路径。
    /// 设计基线 message-dialog v1 的「详情面板」一行。
    /// </summary>
    public class MessageDetail
    {
        public MessageDetail()
        {
        }

        public MessageDetail(string label, string chip, string path, MessageChipTone tone = MessageChipTone.Neutral)
        {
            Label = label;
            Chip = chip;
            Path = path;
            Tone = tone;
        }

        /// <summary>左侧标签（56 宽，超出省略）。</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>数量 / 状态 chip 文案（如「1 份」「离线」）。</summary>
        public string Chip { get; set; } = string.Empty;

        /// <summary>等宽路径；超长在界面上省略，悬停看全，右侧可一键复制。</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>chip 语气，默认中性。</summary>
        public MessageChipTone Tone { get; set; } = MessageChipTone.Neutral;
    }

    /// <summary>
    /// 公共消息弹窗的选项。
    /// 视觉契约见 docs/UI设计基线/message-dialog/README.md。
    /// </summary>
    public class MessageDialogOptions
    {
        private MessageResult _defaultFocus = MessageResult.Primary;
        private bool _defaultFocusSet;

        /// <summary>语义级别，默认 Info。</summary>
        public MessageSeverity Severity { get; set; } = MessageSeverity.Info;

        /// <summary>标题：一句话说清「这是什么现场」。</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>正文；支持 \n 换行（v1 不做富文本）。</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 结构化详情行；为空时详情面板整块隐藏。
        /// v1 调用方最多传 3 行：视图不做截断，超过 3 行会把卡片撑高（设计基线约定）。
        /// </summary>
        public IList<MessageDetail> Details { get; } = new List<MessageDetail>();

        /// <summary>正文与问题之间的补充说明；为空时隐藏。</summary>
        public string? Hint { get; set; }

        /// <summary>要用户回答的那句话；为空时隐藏。</summary>
        public string? Question { get; set; }

        /// <summary>主按钮文案（动作化文案，如「打开目录」）。</summary>
        public string PrimaryText { get; set; } = "好";

        /// <summary>次按钮文案；null / 空 = 单按钮。</summary>
        public string? SecondaryText { get; set; }

        /// <summary>主按钮语气；Danger 时按钮变红，且默认焦点自动落到次按钮。</summary>
        public MessageTone PrimaryTone { get; set; } = MessageTone.Primary;

        /// <summary>是否显示右上角 ×；默认显示。</summary>
        public bool ShowClose { get; set; } = true;

        /// <summary>
        /// 默认焦点。
        /// 默认值 Primary；当 <see cref="PrimaryTone"/> = Danger 且调用方没有显式设置本属性时，
        /// getter 返回 Secondary（回车不会执行危险动作）。调用点显式赋值可覆盖。
        /// </summary>
        public MessageResult DefaultFocus
        {
            get => !_defaultFocusSet && PrimaryTone == MessageTone.Danger
                ? MessageResult.Secondary
                : _defaultFocus;
            set
            {
                _defaultFocus = value;
                _defaultFocusSet = true;
            }
        }

        /// <summary>卡片宽度：默认 480，长路径 / 多行说明用 560。</summary>
        public double Width { get; set; } = 480;

        /// <summary>宿主窗口；null = 沿用 EasyDialog 的「当前激活窗口 → 主窗口」取法。</summary>
        public Window? Owner { get; set; }

        /// <summary>解析真正生效的默认焦点（等价于 <see cref="DefaultFocus"/>，保留方法名给视图/冒烟显式调用）。</summary>
        public MessageResult ResolveDefaultFocus() => DefaultFocus;
    }

    /// <summary>
    /// 公共消息弹窗入口。
    ///
    /// 不新建第二套弹窗引擎：内部转调 <see cref="EasyDialog"/> 已验证的透明遮罩窗口、
    /// 并发锁、跨线程与取消保护（R120 复用优先），只把卡片视觉树换成 MessageDialogView。
    /// </summary>
    public static class MessageDialog
    {
        /// <summary>同步显示（内部走 EasyDialog 的 DispatcherFrame 安全阻塞）。</summary>
        public static MessageResult Show(MessageDialogOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            // 没有 WPF 应用上下文（设计器 / 单元测试 / 已关机）时返回 Close，不抛。
            if (Application.Current == null) return MessageResult.Close;
            return EasyDialog.ShowMessageSync(options);
        }

        /// <summary>异步显示。</summary>
        public static Task<MessageResult> ShowAsync(MessageDialogOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (Application.Current == null) return Task.FromResult(MessageResult.Close);
            return EasyDialog.ShowMessageAsync(options);
        }
    }
}
