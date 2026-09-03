using Core.Events;
using Core.Interfaces;
using HalconDotNet;
using Microsoft.Win32;
using Prism.Commands;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Plugin.ImageAcquisition
{
    /// <summary>
    /// 图像采集插件（Plugin 与配置 ViewModel 合一）
    /// 设计原则：一份实例只存一份数据——
    /// - 数据流参数（可被变量链接）：声明 InputPort，界面绑定 TypedValue
    /// - 纯配置项（永不链接）：[StepConfig] 普通属性，存在自身实例中
    /// - 纯界面状态（预览图/消息）：普通属性，不参与持久化
    /// 三者的持久化/灌值同步均由基类默认实现，键名 = 名称，无需映射
    /// - RunAlgorithm：唯一的执行方法，正式运行与试运行共用
    /// </summary>
    [Display(
        Name = "图像采集",
        GroupName = "常用工具",
        Description = "从文件/文件夹/相机获取图像，供后续视觉处理使用",
        ShortName = "\uf1c5"
    )]
    public class ImageAcquisitionPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        #region 配置属性（纯配置项：持久化到 InputValues、参与灌值，但不进端口/不可变量链接）

        private AcquisitionMode _mode;
        /// <summary>
        /// 采集模式: 0=单图文件, 1=文件夹, 2=相机
        /// </summary>
        [StepConfig]
        public AcquisitionMode Mode
        {
            get => _mode;
            set => SetProperty(ref _mode, value);
        }

        private int _displayViewIndex = 0;
        /// <summary>
        /// 显示窗口索引：采集图像发布到主界面几号视图窗口（1~9），0=不显示
        /// </summary>
        [StepConfig]
        public int DisplayViewIndex
        {
            get => _displayViewIndex;
            set => SetProperty(ref _displayViewIndex, value);
        }

        #endregion

        #region 输入端口（数据流参数：可被变量链接，界面绑定 TypedValue）

        /// <summary>
        /// 单张图像文件路径
        /// </summary>
        public InputPort<string> FilePathPort { get; } = new(
            "FilePath",
            "",
            "单张图像文件完整路径"
        )
        { IsRequired = false };

        /// <summary>
        /// 文件夹路径
        /// </summary>
        public InputPort<string> FolderPathPort { get; } = new(
            "FolderPath",
            "",
            "包含图像的文件夹路径"
        )
        { IsRequired = false };

        /// <summary>
        /// 文件夹内的文件索引（0-based）
        /// </summary>
        public InputPort<int> FileIndexPort { get; } = new(
            "FileIndex",
            0,
            "文件夹内的文件索引（0-based）"
        )
        { IsRequired = false };


        /// <summary>
        /// 相机索引号（预留）
        /// </summary>
        public InputPort<int> CameraIndexPort { get; } = new(
            "CameraIndex",
            0,
            "相机索引号（预留，默认 0）"
        )
        { IsRequired = false };

        #endregion

        #region 输出端口（Success/ErrorMessage 由基类提供）

        /// <summary>
        /// 采集到的图像
        /// </summary>
        public OutputPort<HImage> OutputImage { get; } = new(
            "Image",
            "采集到的图像"
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

        #endregion

        #region 预览与状态（纯界面属性，不参与流程数据流）

        private HImage _previewImage = new();
        /// <summary>
        /// 预览图像
        /// </summary>
        public HImage PreviewImage
        {
            get => _previewImage;
            set => SetProperty(ref _previewImage, value);
        }

        private string _previewImagePath = string.Empty;
        /// <summary>
        /// 预览图像来源路径
        /// </summary>
        public string PreviewImagePath
        {
            get => _previewImagePath;
            set => SetProperty(ref _previewImagePath, value);
        }

        private string _statusMessage = string.Empty;
        /// <summary>
        /// 操作状态消息
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private int _fileCount;
        /// <summary>
        /// 文件夹中图像文件总数
        /// </summary>
        public int FileCount
        {
            get => _fileCount;
            set => SetProperty(ref _fileCount, value);
        }

        private string _currentFileName = string.Empty;
        /// <summary>
        /// 当前索引对应的文件名
        /// </summary>
        public string CurrentFileName
        {
            get => _currentFileName;
            set => SetProperty(ref _currentFileName, value);
        }

        /// <summary>
        /// 浏览选择图像文件（供 LinkableValueEditor 的 BrowseCommand 使用）
        /// </summary>
        public DelegateCommand BrowseFileCommand { get; }

        #endregion

        #region 私有状态

        private List<string> _cachedFiles = new();
        private string _cachedFolderPath = string.Empty;
        private string _cachedExtensions = string.Empty;

        #endregion

        public ImageAcquisitionPlugin()
        {
            BrowseFileCommand = new DelegateCommand(BrowseFile);
        }

        #region IPluginCustomViewProvider

        /// <summary>
        /// 返回插件自定义配置视图（DataContext = 插件自身）
        /// </summary>
        public object GetConfigView(IStepConfigData stepData)
        {
           
            return new ImageAcquisitionView(stepData, this);
        }

        #endregion

        #region 执行核心（唯一一份）

        /// <summary>
        /// 采集执行核心的输出
        /// </summary>
        private sealed class CoreResult
        {
            public bool Success;
            public string Error = "";
            public HImage Image;
            public string CurrentPath;
            public int CurrentIndex;
            public int TotalFiles;
        }

        /// <summary>
        /// 唯一的采集执行核心：参数进 → 结果出，不含端口/界面逻辑
        /// </summary>
        private CoreResult ExecuteCore(
            AcquisitionMode mode,
            string filePath,
            string folderPath,
            int fileIndex,
            int cameraIndex)
        {
            switch (mode)
            {
                case AcquisitionMode.SingleFile:
                    return AcquireSingleFile(filePath);

                case AcquisitionMode.Folder:
                    return AcquireFromFolder(folderPath, fileIndex, ".bmp,.jpg,.jpeg,.png,.tif,.tiff");

                case AcquisitionMode.Camera:
                    return AcquireFromCamera(cameraIndex);

                default:
                    return new CoreResult { Error = $"未知的采集模式: {mode}" };
            }
        }

        private CoreResult AcquireSingleFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return new CoreResult { Error = "文件路径不能为空" };

            if (!File.Exists(path))
                return new CoreResult { Error = $"文件不存在: {path}" };

            HImage image = new HImage();
            image.ReadImage(path);

            image.GetImageSize(out int width, out int height);
            return new CoreResult
            {
                Success = true,
                Image = image,
                CurrentPath = path,
                CurrentIndex = 0,
                TotalFiles = 1,
                Error = $"已加载图像: {path} ({width}x{height})"
            };
        }

        private CoreResult AcquireFromFolder(string folderPath, int fileIndex, string extensions)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return new CoreResult { Error = "文件夹路径不能为空" };

            if (!Directory.Exists(folderPath))
                return new CoreResult { Error = $"文件夹不存在: {folderPath}" };

            var extSet = new HashSet<string>(
                (extensions ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
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

            if (_cachedFiles.Count == 0)
                return new CoreResult { Error = $"文件夹内未找到符合条件的图像文件: {folderPath}", TotalFiles = 0 };

            if (fileIndex < 0) fileIndex = 0;
            if (fileIndex >= _cachedFiles.Count) fileIndex = _cachedFiles.Count - 1;

            var path = _cachedFiles[fileIndex];
            HImage image = new HImage();
            image.ReadImage(path);

            return new CoreResult
            {
                Success = true,
                Image = image,
                CurrentPath = path,
                CurrentIndex = fileIndex,
                TotalFiles = _cachedFiles.Count,
                Error = $"已加载图像 [{fileIndex + 1}/{_cachedFiles.Count}]: {path}"
            };
        }

        private CoreResult AcquireFromCamera(int cameraIndex)
        {
            // TODO: 接入相机SDK (Basler/Hikvision/Daheng等)
            // 占位图：640x480 灰度渐变（含相机索引偏移），保证下游图像处理节点可用
            const int width = 640;
            const int height = 480;
            var pixels = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    pixels[row + x] = (byte)((x / 4 + y / 4 + cameraIndex * 30) % 256);
                }
            }

            IntPtr ptr = Marshal.AllocHGlobal(pixels.Length);
            try
            {
                Marshal.Copy(pixels, 0, ptr, pixels.Length);
                HImage image = new HImage();
                image.GenImage1("byte", width, height, ptr);

                return new CoreResult
                {
                    Success = true,
                    Image = image,
                    CurrentPath = $"Camera_{cameraIndex}",
                    CurrentIndex = cameraIndex,
                    TotalFiles = 0,
                    Error = $"相机采集为占位模式，相机索引: {cameraIndex}"
                };
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        #endregion

        #region 正式运行（唯一执行方法；数据源：端口 = InputValues 灌值 + 链接变量覆盖）

        public override void RunAlgorithm(IExecutionContext context)
        {
            Success.Value = false;
            ErrorMessage.Value = string.Empty;
            OutputImage.Value = null;
            CurrentFilePath.Value = string.Empty;

            try
            {
                var result = ExecuteCore(
                    Mode,
                    FilePathPort.GetTypedValue(),
                    FolderPathPort.GetTypedValue(),
                    FileIndexPort.GetTypedValue(),
                    CameraIndexPort.GetTypedValue());

                Success.Value = result.Success;

                if (result.Success)
                {
                    ErrorMessage.Value = string.Empty;
                    OutputImage.Value = result.Image;
                    CurrentFilePath.Value = result.CurrentPath;
                    CurrentFileIndex.Value = result.CurrentIndex;
                    TotalFiles.Value = result.TotalFiles;
                    context.Logger.Info($"{InstanceName} {result.Error}");

                    // 发布到主程序视图（A1：事件传原图引用，UI 侧复制副本显示；0=不显示）
                    if (DisplayViewIndex > 0)
                    {
                        this.PublishPreview(result.Image, DisplayViewIndex);
                    }
                }
                else
                {
                    ErrorMessage.Value = result.Error ?? string.Empty;
                    context.Logger.Error($"{InstanceName} {result.Error}");
                }
            }
            catch (Exception ex)
            {
                ErrorMessage.Value = ex.Message;
                context.Logger.Error($"{InstanceName} 图像采集异常: {ex.Message}");
            }
        }

        #endregion

        #region 配置生命周期（预览恢复；端口⇄InputValues 同步由基类默认实现）

        public override void Initialize(IStepConfigData stepData)
        {
            base.Initialize(stepData);

            // 恢复预览（值已在 base 中灌入端口与配置属性）
            if (Mode == AcquisitionMode.Folder)
            {
                RefreshFolderFiles();
            }
            else if (Mode == AcquisitionMode.SingleFile && File.Exists(FilePathPort.TypedValue))
            {
                PreviewImagePath = FilePathPort.TypedValue;
                LoadPreview(FilePathPort.TypedValue);
            }
        }

        #endregion

        #region 浏览与预览辅助（配置态，直接读写端口）

        private void BrowseFile()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "图像文件|*.jpg;*.jpeg;*.png;*.bmp;*.tiff;*.gif|所有文件|*.*",
                Title = "选择图像文件"
            };

            if (dlg.ShowDialog() == true)
            {
                FilePathPort.Value = dlg.FileName;
                PreviewImagePath = dlg.FileName;
                LoadPreview(dlg.FileName);
            }
        }

        private void BrowseFolder()
        {
            var dlg = new OpenFolderDialog
            {
                Title = "选择图像文件夹"
            };

            if (dlg.ShowDialog() == true)
            {
                FolderPathPort.Value = dlg.FolderName;
                RefreshFolderFiles();
            }
        }

        private void RefreshFolderFiles()
        {
            var folderPath = FolderPathPort.TypedValue;
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                FileCount = 0;
                CurrentFileName = string.Empty;
                PreviewImage = null;
                return;
            }

            try
            {
                var files = ListFolderImages(folderPath, ".bmp,.jpg,.jpeg,.png,.tif,.tiff");
                FileCount = files.Count;

                if (files.Count == 0)
                {
                    StatusMessage = "文件夹中没有匹配的图像文件";
                    PreviewImage = null;
                    return;
                }

                FileIndexPort.TypedValue = Math.Clamp(FileIndexPort.TypedValue, 0, files.Count - 1);
                var currentFile = files[FileIndexPort.TypedValue];
                CurrentFileName = Path.GetFileName(currentFile);
                PreviewImagePath = currentFile;
                LoadPreview(currentFile);
                StatusMessage = $"找到 {files.Count} 张图像";
            }
            catch (Exception ex)
            {
                StatusMessage = $"读取文件夹失败: {ex.Message}";
            }
        }

        private void LoadPreview(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    StatusMessage = $"文件不存在: {path}";
                    PreviewImage = null;
                    return;
                }

                HImage img = new HImage();
                img.ReadImage(path);
                PreviewImage = img;
                StatusMessage = $"预览: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载预览失败: {ex.Message}";
                PreviewImage = null;
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 列出文件夹中符合扩展名的图像文件（预览辅助与执行核心共用同一过滤逻辑）
        /// </summary>
        private static List<string> ListFolderImages(string folderPath, string extensions)
        {
            var extSet = new HashSet<string>(
                (extensions ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim().ToLower()),
                StringComparer.Ordinal
            );

            return Directory.GetFiles(folderPath)
                .Where(f => extSet.Contains(Path.GetExtension(f).ToLower()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
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

            // 配置实例关闭时释放预览图（非托管资源）
            PreviewImage?.Dispose();
            PreviewImage = null;
        }

        #endregion
    }
}
