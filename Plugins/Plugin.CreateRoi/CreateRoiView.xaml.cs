using Core.Halcon.Controls;
using Core.Halcon.Models;
using HalconDotNet;
using Plugin.CreateRoi.Models;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Plugin.CreateRoi
{
    /// <summary>布尔反转转换器（“原图/掩膜预览” RadioButton 互斥绑定用）</summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is bool b ? !b : value;
    }

    /// <summary>
    /// ROI 配置视图——纯桥接层（MVVM）：
    /// 所有业务逻辑（选中、参数编辑、掩膜预览、RoiList 同步）都在 CreateRoiPlugin（ViewModel），
    /// XAML 绑定驱动 UI；本文件只做 ImageEdit 控件无法绑定表达的事件桥接。
    /// </summary>
    public partial class CreateRoiView : UserControl
    {
        private CreateRoiPlugin? _plugin;
        private bool _isRestoring; // 恢复显示时抑制同步，防止循环

        public CreateRoiView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _plugin = DataContext as CreateRoiPlugin;
            if (_plugin == null) return;

            var src = _plugin.SrcImage.ActualValue;
            if (src != null && src.IsInitialized()) _plugin.PreviewImage = src;

            ImageEditor.DrawObjectList.CollectionChanged += OnDrawObjectChanged;
            ImageEditor.RoiChanged += (s, info) => _plugin?.UpdateRoiFromDrag(info);
            ImageEditor.RoiSelected += OnRoiSelected;
            ImageEditor.SmearChanged += OnSmearChanged;
            _plugin.RoiParamEditedRequested += OnRoiParamEditedRequested;
            _plugin.PropertyChanged += OnPluginPropertyChanged;
            PreviewKeyDown += OnPreviewKeyDown;

            RestoreRois();

            // 恢复涂擦：插件反序列化持久化快照 → 副本交画布显示
            _plugin.RestoreSmear();
            var (draw, erase) = _plugin.CopySmearRegions();
            ImageEditor.SetSmearRegions(draw, erase);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_plugin != null)
            {
                _plugin.RoiParamEditedRequested -= OnRoiParamEditedRequested;
                _plugin.PropertyChanged -= OnPluginPropertyChanged;
            }
            ImageEditor.DrawObjectList.CollectionChanged -= OnDrawObjectChanged;
            ImageEditor.SmearChanged -= OnSmearChanged;
            PreviewKeyDown -= OnPreviewKeyDown;
        }

        #region ImageEdit 事件桥接（控件事件 → 插件方法）

        /// <summary>一次涂/擦笔画结束：控件区域副本交给插件并刷新掩膜</summary>
        private void OnSmearChanged(object? sender, EventArgs e)
        {
            var (draw, erase) = ImageEditor.CopySmearRegions();
            _plugin?.UpdateSmear(draw, erase);
        }

        /// <summary>涂擦模式切换（涂/擦优先于 ROI 选中，由控件内部处理）</summary>
        private void SmearMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            var mode = SmearModeType.None;
            if (sender is RadioButton rb)
            {
                if (rb.Content.ToString() == "绘制涂抹") mode = SmearModeType.Draw;
                else if (rb.Content.ToString() == "擦除涂抹") mode = SmearModeType.Erase;
            }
            ImageEditor.SmearMode = mode;
        }

        private void Brush_Changed(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(BrushBox.Text, out var v) && v >= 1)
                ImageEditor.BrushRadius = v;
        }

        private void Brush_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Brush_Changed(sender, e);
        }

        private void ClearSmear_Click(object sender, RoutedEventArgs e)
        {
            ImageEditor.ClearSmear();          // 清画布显示层
            _plugin?.ClearSmear();             // 清插件副本并刷新掩膜
        }

        private void OnDrawObjectChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_isRestoring || _plugin == null) return;
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add when e.NewItems != null:
                    foreach (DrawingObjectInfo info in e.NewItems) _plugin.SyncFromCanvasAdd(info);
                    break;
                case NotifyCollectionChangedAction.Remove when e.OldItems != null:
                    foreach (DrawingObjectInfo info in e.OldItems) _plugin.SyncFromCanvasRemove(info);
                    break;
                case NotifyCollectionChangedAction.Reset:
                    _plugin.SyncFromCanvasReset();
                    break;
            }
        }

        private void OnRoiSelected(object? sender, DrawingObjectInfo info)
        {
            if (_plugin == null || info == null) return;
            _plugin.SelectFromCanvas(info.RoiName); // 列表 SelectedItem 绑定自动定位
            Dispatcher.BeginInvoke(() => RoiListBox.ScrollIntoView(RoiListBox.SelectedItem));
        }

        /// <summary>
        /// 列表选中 → 画布同步编辑（双向联动的另一半）：
        /// SelectedRoi 变化时在画布上挂接对应句柄；无循环风险——
        /// 画布点选先设 activeRoi 再发 RoiSelected，回环 SelectRoi 命中"同一对象即返回"短路
        /// </summary>
        private void OnPluginPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_plugin == null || e.PropertyName != nameof(CreateRoiPlugin.SelectedRoi)) return;
            var roi = _plugin.SelectedRoi;
            var info = roi == null
                ? null
                : ImageEditor.DrawObjectList.FirstOrDefault(x => x.RoiName == roi.Name);
            ImageEditor.SelectRoi(info);
        }

        private void OnRoiParamEditedRequested(RoiItem roi)
        {
            var info = ImageEditor.DrawObjectList.FirstOrDefault(x => x.RoiName == roi.Name);
            if (info != null)
            {
                info.HTuples = roi.Params.Select(p => new HTuple(p)).ToArray();
                ImageEditor.UpdateRoiParams(info); // 数值框精调 → 同步画布句柄并重绘
            }
            _plugin?.ScheduleMaskPreview();
        }

        #endregion

        #region 按钮与快捷键（调插件方法，画布 Remove 事件回环同步）

        private void DeleteRoi_Click(object sender, RoutedEventArgs e) => DeleteSelected();

        private void ClearRois_Click(object sender, RoutedEventArgs e)
        {
            _isRestoring = true;
            try { ImageEditor.ClearRois(); } // Reset 动作抑制回环
            finally { _isRestoring = false; }
            _plugin?.SyncFromCanvasReset();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && Keyboard.FocusedElement is not TextBox) DeleteSelected();
        }

        private void DeleteSelected()
        {
            if (_plugin?.SelectedRoi is not RoiItem roi) return;
            var info = ImageEditor.DrawObjectList.FirstOrDefault(x => x.RoiName == roi.Name);
            if (info != null) ImageEditor.RemoveRoi(info); // Remove 事件 → SyncFromCanvasRemove
            else _plugin.SyncFromCanvasRemove(new DrawingObjectInfo(roi.ShapeType, Array.Empty<HTuple>(), roi.Name));
        }

        /// <summary>窗口打开时按 RoiList 恢复画布轮廓（含编辑句柄）</summary>
        private void RestoreRois()
        {
            if (_plugin == null) return;
            _isRestoring = true;
            try
            {
                ImageEditor.DrawObjectList.Clear();
                foreach (var roi in _plugin.RoiList)
                    ImageEditor.DrawObjectList.Add(
                        new DrawingObjectInfo(roi.ShapeType, roi.Params.Select(p => new HTuple(p)).ToArray(), roi.Name));
            }
            finally { _isRestoring = false; }
        }

        private void MaskPreview_Checked(object sender, RoutedEventArgs e)
        {
            if (IsLoaded && _plugin != null)
                _plugin.IsMaskPreview = ((RadioButton)sender).Content.ToString() == "掩膜预览";
        }

        #endregion
    }
}
