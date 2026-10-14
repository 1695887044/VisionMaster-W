using System;
using Prism.Dialogs;

namespace VisionMaster.Services
{
    /// <summary>
    /// <see cref="IScadaFlowPicker"/> 的默认实现：把"入参打包 / 出参拆包"这一段接在 Prism 弹窗上。
    ///
    /// 与 <see cref="ScadaPagePicker"/> / <see cref="ScadaVariablePicker"/> 逐字同构
    /// （本类不含任何界面逻辑，只做翻译）：调用方给一对 (Id, 名字)，本类转成弹窗参数；
    /// 弹窗回一对 (Id, 名字)，本类转成一次回调。弹窗自己长什么样、有什么列，
    /// 全在 <c>ScadaFlowPickerView</c> / <c>ScadaFlowPickerViewModel</c> 里。
    /// </summary>
    public sealed class ScadaFlowPicker : IScadaFlowPicker
    {
        /// <summary>弹窗注册名（<c>App.RegisterTypes</c> 里 RegisterDialog 用的键，两处必须一致）</summary>
        public const string DialogName = "ScadaFlowPicker";

        /// <summary>入参键：打开时要预选的目标流程</summary>
        public const string CurrentIdKey = "CurrentFlowId";
        public const string CurrentNameKey = "CurrentFlowName";

        /// <summary>出参键：用户确认后选中的流程</summary>
        public const string PickedIdKey = "PickedFlowId";
        public const string PickedNameKey = "PickedFlowName";

        private readonly IDialogService _dialogs;

        public ScadaFlowPicker(IDialogService dialogs)
            => _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

        /// <inheritdoc />
        public void Pick(string? currentFlowId, string? currentName, Action<string, string?> onPicked)
        {
            if (onPicked == null) throw new ArgumentNullException(nameof(onPicked));

            var parameters = new DialogParameters
            {
                { CurrentIdKey, currentFlowId },
                { CurrentNameKey, currentName },
            };

            _dialogs.ShowDialog(DialogName, parameters, result =>
            {
                // 取消 = 什么都不做。这里刻意不回写一个空值：取消的语义是"当我没点过"，
                // 而不是"清掉原来选的那个"——后者是破坏性动作，不能藏在取消键里。
                if (result.Result != ButtonResult.OK) return;

                // 只有 Id 是权威的：拿不到（或空白）就整条放弃，绝不退化成"按名字猜一个"。
                if (!result.Parameters.TryGetValue<string>(PickedIdKey, out var id)
                    || string.IsNullOrWhiteSpace(id)) return;

                result.Parameters.TryGetValue<string>(PickedNameKey, out var name);
                onPicked(id, name);
            });
        }
    }
}
