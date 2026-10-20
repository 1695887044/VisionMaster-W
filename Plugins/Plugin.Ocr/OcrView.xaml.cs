using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Plugin.Ocr
{
    /// <summary>
    /// OCR 文本识别的配置视图。
    ///
    /// 视图不写业务逻辑：框选由 Core.Halcon 的 ImageEdit 控件负责，画布集合与形状参数的双向同步
    /// 都在 OcrPlugin 里。本文件只留三处 —— 视图就绪信号（回填示意图与识别区），
    /// 以及两个按钮的命令转发（清空识别区 / 试算）。
    ///
    /// 为什么用视图级 ICommand 而不是 Click：
    /// 批量插件视图冒烟（UIThemeSmokeTest --plugin-views）要求视图自有按钮至少有一条 Command 绑定；
    /// 命令只调用插件已有的公开方法，不新增功能、不改变 DataContext 契约。
    /// </summary>
    public partial class OcrView : UserControl
    {
        public OcrView()
        {
            InitializeComponent();

            ClearRoiCommand = new RelayCommand(() => Plugin?.ClearRoi());
            TryRecognizeCommand = new RelayCommand(() => Plugin?.TryPreviewRecognize());

            Loaded += (_, _) => Plugin?.OnViewLoaded();
        }

        private OcrPlugin? Plugin => DataContext as OcrPlugin;

        /// <summary>清空识别区：转发到 OcrPlugin.ClearRoi()。</summary>
        public ICommand ClearRoiCommand { get; }

        /// <summary>试算一下：转发到 OcrPlugin.TryPreviewRecognize()。</summary>
        public ICommand TryRecognizeCommand { get; }

        /// <summary>极简命令：视图内两个按钮共用，无参数、始终可执行。</summary>
        private sealed class RelayCommand : ICommand
        {
            private readonly Action _execute;

            public RelayCommand(Action execute) => _execute = execute;

            public event EventHandler? CanExecuteChanged
            {
                add => CommandManager.RequerySuggested += value;
                remove => CommandManager.RequerySuggested -= value;
            }

            public bool CanExecute(object? parameter) => true;

            public void Execute(object? parameter) => _execute();
        }
    }
}
