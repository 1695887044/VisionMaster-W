using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Dialogs;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// <see cref="IScadaAxisPicker"/> 的默认实现：只做"入参打包 / 出参拆包"，
    /// 与 <see cref="ScadaVariablePicker"/> 逐字同构。
    /// </summary>
    public sealed class ScadaAxisPicker : IScadaAxisPicker
    {
        /// <summary>弹窗注册名（<c>App.RegisterTypes</c> 里 RegisterDialog 用的键，两处必须一致）</summary>
        public const string DialogName = "ScadaAxisPicker";

        /// <summary>入参键：打开时要预选的轴</summary>
        public const string CurrentAxesKey = "CurrentAxes";

        /// <summary>出参键：用户确认后选中的轴（<c>List&lt;ScadaAxisRef&gt;</c>）</summary>
        public const string PickedAxesKey = "PickedAxes";

        /// <summary>
        /// 入参键：本次是否为单选模式（<c>bool</c>，缺省 false = 多选）。
        ///
        /// 为什么用入参而不是再注册一个"单选轴弹窗"：弹窗的候选来源、预选口径、搜索框、
        /// 空表提示全都一样，差别只有"勾一行要不要取消其它行"这一处。
        /// 注册两个弹窗等于把同一份候选逻辑抄两遍，而抄的那一份迟早会漏掉某次修正
        /// （比如"已选的轴排前面""停用的轴不列"）。
        /// </summary>
        public const string SingleSelectKey = "SingleSelect";

        private readonly IDialogService _dialogs;

        public ScadaAxisPicker(IDialogService dialogs)
            => _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

        /// <inheritdoc />
        public void Pick(IReadOnlyList<ScadaAxisRef> current, Action<IReadOnlyList<ScadaAxisRef>> onPicked)
        {
            if (onPicked == null) throw new ArgumentNullException(nameof(onPicked));

            var parameters = new DialogParameters
            {
                { CurrentAxesKey, (current ?? Array.Empty<ScadaAxisRef>()).ToList() },
            };

            _dialogs.ShowDialog(DialogName, parameters, result =>
            {
                // 取消 = 什么都不做：不把"清空"藏在取消键里（与变量/画面选择器同一口径）。
                if (result.Result != ButtonResult.OK) return;

                if (!result.Parameters.TryGetValue<List<ScadaAxisRef>>(PickedAxesKey, out var axes) || axes == null)
                    return;

                onPicked(axes);
            });
        }

        /// <inheritdoc />
        public void PickSingle(IReadOnlyList<ScadaAxisRef> current, Action<ScadaAxisRef?> onPicked)
        {
            if (onPicked == null) throw new ArgumentNullException(nameof(onPicked));

            var parameters = new DialogParameters
            {
                { CurrentAxesKey, (current ?? Array.Empty<ScadaAxisRef>()).ToList() },
                // 同一个弹窗、同一个出参键，只多带一个模式位：出参形状不变（仍是有序清单），
                // 这里取第一项——多选模式下的"顺序即下拉顺序"在单选里没有意义，第一项就是唯一那项。
                { SingleSelectKey, true },
            };

            _dialogs.ShowDialog(DialogName, parameters, result =>
            {
                if (result.Result != ButtonResult.OK) return;

                if (!result.Parameters.TryGetValue<List<ScadaAxisRef>>(PickedAxesKey, out var axes)
                    || axes == null || axes.Count == 0)
                {
                    return;
                }

                onPicked(axes[0]);
            });
        }
    }
}
