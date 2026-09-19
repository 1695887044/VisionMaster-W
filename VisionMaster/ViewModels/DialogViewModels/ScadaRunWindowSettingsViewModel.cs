using System;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 「运行窗口设置」弹窗：选组态运行窗口的显示形态（依附主窗口 / 独立窗口）。
    ///
    /// 口径
    /// ---------
    /// ① **写进软件级配置**：落盘到 AppConfig.json（<c>AppConfigModel.RunWindowMode</c>），
    ///    不是 .vms。它是"这台机器怎么用"的偏好，不是"这份方案长什么样"的一部分——
    ///    同一份方案在开发机上想看窗口化、在现场机要全屏，两边不该互相改。
    /// ② **取消不留痕**：只有确定才写盘。改到一半点取消，配置保持原样。
    /// ③ **打开时现读**：编辑态除了构造函数读一次，<see cref="OnDialogOpened"/> 再读一次。
    ///    不依赖"容器每次都新建一个 VM"这个假设——万一哪天注册方式变成复用实例，
    ///    字段里留着的就是上一次点开的那个选项，界面显示的将不是当前真正生效的形态。
    /// ④ **即时生效**：宿主每次 Start 现读配置（见 ScadaRuntimeHost），所以改完不必重启，
    ///    下一次点「运行」就是新形态。已经开着的运行窗口不动——它已经在跑了。
    /// </summary>
    public class ScadaRunWindowSettingsViewModel : BindableBase, IDialogAware
    {
        private readonly AppSettingsService _settings;

        /// <summary>编辑中的形态（未点确定之前只在内存里，不碰配置）</summary>
        private ScadaRunWindowMode _mode;

        public ScadaRunWindowSettingsViewModel(AppSettingsService settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _mode = settings.Current.RunWindowMode;

            ConfirmCommand = new DelegateCommand(OnConfirm);
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
        }

        public DialogCloseListener RequestClose { get; set; }

        public string Title => "运行窗口设置";

        /// <summary>
        /// 独立窗口：带系统标题栏、可缩放，与主界面（含视觉图像面板）并排显示。
        /// 两个选项各自一个布尔属性（而不是一个枚举属性 + 转换器）：
        /// WPF 的 RadioButton 只认 <c>IsChecked</c> 这个布尔，多一个转换器就多一处可能失配的地方。
        /// </summary>
        public bool IsIndependent
        {
            get => _mode == ScadaRunWindowMode.IndependentWindow;
            set { if (value) SetMode(ScadaRunWindowMode.IndependentWindow); }
        }

        /// <summary>依附主窗口：无边框 + 最大化，盖住整个主界面（现场运行形态）</summary>
        public bool IsAttached
        {
            get => _mode == ScadaRunWindowMode.AttachedToMainWindow;
            set { if (value) SetMode(ScadaRunWindowMode.AttachedToMainWindow); }
        }

        public DelegateCommand ConfirmCommand { get; }

        public DelegateCommand CancelCommand { get; }

        /// <summary>
        /// 换形态并通知两个选项。
        /// setter 里只在 <c>value == true</c> 时进来，是因为 RadioButton 失去选中时会往
        /// <c>IsChecked</c> 写 <c>false</c>——那一写必须忽略，否则"选中新的"会紧接着被
        /// "旧的取消选中"覆盖回原值（两个 setter 抢着写同一个字段）。
        /// </summary>
        private void SetMode(ScadaRunWindowMode mode)
        {
            if (_mode == mode) return;
            _mode = mode;
            RaisePropertyChanged(nameof(IsIndependent));
            RaisePropertyChanged(nameof(IsAttached));
        }

        private void OnConfirm()
        {
            _settings.Current.RunWindowMode = _mode;
            _settings.Save();
            RequestClose.Invoke(new DialogParameters(), ButtonResult.OK);
        }

        #region IDialogAware

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        /// <summary>打开时把配置里的当前形态读进编辑态，让界面显示"现在生效的是哪个"</summary>
        public void OnDialogOpened(IDialogParameters parameters) => SetMode(_settings.Current.RunWindowMode);

        #endregion
    }
}
