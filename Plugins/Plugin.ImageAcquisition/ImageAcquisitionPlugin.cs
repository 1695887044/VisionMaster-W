using Core.Interfaces;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.IO;
using System.Linq;

namespace Plugin.ImageAcquisition
{
    /// <summary>
    /// 图像采集插件
    /// 支持三种采集模式：指定单张图像、文件夹批量采集、相机采集（占位）
    /// 输出 System.Drawing.Bitmap 供下游视觉算法使用
    /// 实现 IPluginCustomViewProvider 提供自定义配置视图
    /// </summary>
    [Display(
        Name = "图像采集",
        GroupName = "常用工具",
        Description = "从文件/文件夹/相机获取图像，供后续视觉处理使用",
        ShortName = "\uf1c5"
    )]
    public class ImageAcquisitionPlugin : VisionPluginBase, IPluginCustomViewProvider
    {


        #region 输入端口

        /// <summary>
        /// 采集模式
        /// </summary>
        public InputPort<AcquisitionMode> Mode { get; } = new(
            "Mode",
            AcquisitionMode.SingleFile,
            "采集模式：指定图像/文件目录/相机采集"
        ) { IsFunctionalEnum = true };

        /// <summary>
        /// 单张图像文件路径
        /// </summary>
        public InputPort<string> FilePath { get; } = new(
            "FilePath",
            "",
            "单张图像文件完整路径"
        );

        /// <summary>
        /// 文件夹路径
        /// </summary>
        public InputPort<string> FolderPath { get; } = new(
            "FolderPath",
            "",
            "包含图像的文件夹路径"
        );

        /// <summary>
        /// 文件夹内的文件索引（0-based）
        /// </summary>
        public InputPort<int> FileIndex { get; } = new(
            "FileIndex",
            0,
            "文件夹内的文件索引（0-based）"
        );

        /// <summary>
        /// 相机索引号（预留）
        /// </summary>
        public InputPort<int> CameraIndex { get; } = new(
            "CameraIndex",
            0,
            "相机索引号（预留，默认 0）"
        );

        /// <summary>
        /// 支持的文件扩展名
        /// </summary>
        public InputPort<string> FileExtensions { get; } = new(
            "FileExtensions",
            ".bmp,.jpg,.jpeg,.png,.tif,.tiff",
            "支持的图像文件扩展名（逗号分隔）"
        );

        #endregion

        #region 输出端口

        /// <summary>
        /// 采集到的图像
        /// </summary>
        public OutputPort<HImage> OutputImage { get; } = new(
            "Image",
            "采集到的图像（System.Drawing.Bitmap）"
        );

        /// <summary>
        /// 当前文件路径
        /// </summary>
        public OutputPort<string> CurrentFilePath { get; } = new(
            "CurrentPath",
            "当前采集到的文件路径"
        );

        /// <summary>
        /// 当前文件索引
        /// </summary>
        public OutputPort<int> CurrentFileIndex { get; } = new(
            "CurrentIndex",
            "当前采集的文件索引"
        );

        /// <summary>
        /// 文件夹内文件总数
        /// </summary>
        public OutputPort<int> TotalFiles { get; } = new(
            "TotalFiles",
            "文件夹内符合条件的文件总数"
        );

        /// <summary>
        /// 采集是否成功
        /// </summary>
        public OutputPort<bool> Success { get; } = new(
            "Success",
            "是否采集成功"
        );

        /// <summary>
        /// 错误信息
        /// </summary>
        public OutputPort<string> ErrorMessage { get; } = new(
            "ErrorMessage",
            "错误信息"
        );

        #endregion

        #region 私有状态

        private List<string> _cachedFiles = new();
        private string _cachedFolderPath = string.Empty;
        private string _cachedExtensions = string.Empty;

        #endregion

        #region IPluginCustomViewProvider

        /// <summary>
        /// 返回插件自定义配置视图
        /// </summary>
        public object GetConfigView(IStepConfigData stepData)
        {
            return new ImageAcquisitionView(stepData);
        }

        #endregion

        #region 核心逻辑

        public override void RunAlgorithm(IExecutionContext context)
        {
            var mode = Mode.GetTypedValue();
            Success.Value = false;
            ErrorMessage.Value = string.Empty;
            OutputImage.Value = null;
            CurrentFilePath.Value = string.Empty;
            try
            {
                switch (mode)
                {
                    case AcquisitionMode.SingleFile:
                        AcquireSingleFile(context);
                        break;
                    case AcquisitionMode.Folder:
                        AcquireFromFolder(context);
                        break;
                    case AcquisitionMode.Camera:
                        AcquireFromCamera(context);
                        break;
                    default:
                        ErrorMessage.Value = $"未知的采集模式: {mode}";
                        context.Logger.Error($"{InstanceName} {ErrorMessage.Value}");
                        break;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage.Value = ex.Message;
                context.Logger.Error($"{InstanceName} 图像采集异常: {ex.Message}");
            }
        }

        private void AcquireSingleFile(IExecutionContext context)
        {
            string path = FilePath.GetTypedValue();
            if (string.IsNullOrWhiteSpace(path))
            {
                ErrorMessage.Value = "文件路径不能为空";
                return;
            }

            if (!File.Exists(path))
            {
                ErrorMessage.Value = $"文件不存在: {path}";
                return;
            }

            using var bitmap = new Bitmap(path);
            OutputImage.Value = (Bitmap)bitmap.Clone();
            CurrentFilePath.Value = path;
            CurrentFileIndex.Value = 0;
            Success.Value = true;
            context.Logger.Info($"{InstanceName} 已加载图像: {path} ({bitmap.Width}x{bitmap.Height})");
        }

        private void AcquireFromFolder(IExecutionContext context)
        {
            string folderPath = FolderPath.GetTypedValue();
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                ErrorMessage.Value = "文件夹路径不能为空";
                return;
            }

            if (!Directory.Exists(folderPath))
            {
                ErrorMessage.Value = $"文件夹不存在: {folderPath}";
                return;
            }

            string extensions = FileExtensions.GetTypedValue();
            var extSet = new HashSet<string>(
                extensions.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim().ToLower()),
                StringComparer.Ordinal
            );

            if (_cachedFolderPath != folderPath || _cachedExtensions != extensions)
            {
                _cachedFiles = Directory.GetFiles(folderPath)
                    .Where(f => extSet.Contains(Path.GetExtension(f).ToLower()))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                _cachedFolderPath = folderPath;
                _cachedExtensions = extensions;
            }

            TotalFiles.Value = _cachedFiles.Count;

            if (_cachedFiles.Count == 0)
            {
                ErrorMessage.Value = $"文件夹内未找到符合条件的图像文件: {folderPath}";
                return;
            }

            int index = FileIndex.GetTypedValue();
            if (index < 0) index = 0;
            if (index >= _cachedFiles.Count) index = _cachedFiles.Count - 1;

            string filePath = _cachedFiles[index];
            using var bitmap = new Bitmap(filePath);
            OutputImage.Value = (Bitmap)bitmap.Clone();
            CurrentFilePath.Value = filePath;
            CurrentFileIndex.Value = index;
            Success.Value = true;
            context.Logger.Info(
                $"{InstanceName} 已加载图像 [{index + 1}/{_cachedFiles.Count}]: {filePath} ({bitmap.Width}x{bitmap.Height})"
            );
        }

        private void AcquireFromCamera(IExecutionContext context)
        {
            int cameraIdx = CameraIndex.GetTypedValue();

            // TODO: 接入相机SDK (Basler/Hikvision/Daheng等)
            using var placeholder = new Bitmap(640, 480);
            using (var g = Graphics.FromImage(placeholder))
            {
                g.Clear(Color.DarkGray);
                using var font = new Font("Arial", 16);
                using var brush = new SolidBrush(Color.White);
                string text = $"相机 {cameraIdx} - 占位图";
                var textSize = g.MeasureString(text, font);
                g.DrawString(text, font, brush,
                    (placeholder.Width - textSize.Width) / 2,
                    (placeholder.Height - textSize.Height) / 2);
            }

            OutputImage.Value = (Bitmap)placeholder.Clone();
            CurrentFilePath.Value = $"Camera_{cameraIdx}";
            CurrentFileIndex.Value = cameraIdx;
            TotalFiles.Value = 0;
            Success.Value = true;
            context.Logger.Info($"{InstanceName} 相机采集为占位模式，相机索引: {cameraIdx}。");
        }

        #endregion

        #region 生命周期

        public override void Initialize()
        {
            _cachedFiles.Clear();
            _cachedFolderPath = string.Empty;
            _cachedExtensions = string.Empty;
        }

        public override void Dispose()
        {
            if (OutputImage.Value is HImage bmp)
            {
                bmp.Dispose();
                OutputImage.Value = null;
            }
        }

        #endregion
    }
}
