using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 右键菜单的一项：一份与 WPF 控件无关的「菜单长什么样」的描述。
    ///
    /// 为什么不让 <see cref="ScadaEditorViewModel"/> 直接造 <c>MenuItem</c>：
    /// 视图模型一碰控件类型，就把「菜单内容能不能被离屏断言」和「UI 框架是谁」绑死在一起。
    /// 断言工程里刻意没有 Application / Dispatcher（见 ScadaChecks 的约定），
    /// 造控件那条路在那里根本走不通 —— 而菜单内容（有哪些图层、哪一项打勾、哪一项判灰）
    /// 恰恰是最该被断言的部分。
    ///
    /// 于是切成两半：本类型只描述「是什么」，宿主视图负责把描述翻译成控件。
    /// 翻译是纯机械的（见 ScadaEditorView.xaml.cs），不会有第二种解释。
    ///
    /// 三种形态用三个命名工厂表达，而不是三个重载构造：
    /// 调用点上「这是一个可勾选的归属项 / 一个动作项 / 一个带子菜单的分组」一眼可读，
    /// 也顺带避开了「(string, string, ICommand) 与 (string, string, IEnumerable) 谁更匹配」
    /// 这类只有编译器才关心的重载消解问题。
    /// </summary>
    public sealed class ScadaMenuItem
    {
        private static readonly ScadaMenuItem[] NoChildren = Array.Empty<ScadaMenuItem>();

        private ScadaMenuItem(
            string name,
            string? icon,
            ICommand? command,
            bool isCheckable,
            bool isChecked,
            IEnumerable<ScadaMenuItem>? children)
        {
            Name = name;
            Icon = icon;
            Command = command;
            IsCheckable = isCheckable;
            IsChecked = isChecked;
            Children = children?.ToArray() ?? NoChildren;
        }

        /// <summary>菜单文字</summary>
        public string Name { get; }

        /// <summary>图标字形（Font Awesome 码点）；为 null 表示这一项不占图标位</summary>
        public string? Icon { get; }

        /// <summary>点击执行的命令；分组项为 null</summary>
        public ICommand? Command { get; }

        /// <summary>是否可勾选（用于「所属图层」这种单选式归属）</summary>
        public bool IsCheckable { get; }

        /// <summary>当前是否处于勾选态</summary>
        public bool IsChecked { get; }

        /// <summary>子菜单项；空表表示这是叶子项</summary>
        public IReadOnlyList<ScadaMenuItem> Children { get; }

        /// <summary>有没有子项：宿主据此决定渲染成叶子模板还是带 Popup 的分组模板</summary>
        public bool HasChildren => Children.Count > 0;

        /// <summary>带子菜单的分组项（如「图层」「叠放次序」两栏的标题行）</summary>
        public static ScadaMenuItem Submenu(string name, string? icon, IEnumerable<ScadaMenuItem> children)
            => new(name, icon, command: null, isCheckable: false, isChecked: false, children);

        /// <summary>
        /// 可勾选的归属项（如「归到某图层」）。
        ///
        /// 勾选态由调用方按当前模型算好传进来，本类型不自己维护 —— 菜单每次弹出都重新组装一份，
        /// 不持有任何会过期的状态，也就没有「菜单还开着、模型被别处改了」这类劈叉。
        /// </summary>
        public static ScadaMenuItem Choice(string name, ICommand command, bool isChecked)
            => new(name, icon: null, command, isCheckable: true, isChecked, children: null);

        /// <summary>纯动作项（如「置顶」「上移一层」）</summary>
        public static ScadaMenuItem Action(string name, string? icon, ICommand command)
            => new(name, icon, command, isCheckable: false, isChecked: false, children: null);
    }
}
