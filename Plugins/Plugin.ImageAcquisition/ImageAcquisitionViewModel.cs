using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using UI.Events;
using static Plugin.ImageAcquisition.ImageAcquisitionPlugin;

namespace Plugin.ImageAcquisition
{
    /// <summary>
    /// 图像采集配置视图的 ViewModel
    /// 实现 IPluginConfigView 供 PluginConfigShell 调用
    /// </summary>
    public class ImageAcquisitionViewModel : BindableBase, IPluginConfigView
    {
        private IStepConfigData? _stepData;

        #region 绑定属性

        private AcquisitionMode _mode;
        /// <summary>
        /// 采集模式: 0=单图文件, 1=文件夹, 2=相机
        /// </summary>
        public AcquisitionMode Mode
        {
            get => _mode;
            set => SetProperty(ref _mode, value);
        }

        private string _filePath = string.Empty;
        /// <summary>
        /// 文件路径（单图模式）
        /// </summary>
        public string FilePath
        {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
        }

        private string _folderPath = string.Empty;
        /// <summary>
        /// 文件夹路径（文件夹模式）
        /// </summary>
        public string FolderPath
        {
            get => _folderPath;
            set
            {
                if (SetProperty(ref _folderPath, value))
                {
                    RaisePropertyChanged(nameof(CurrentFileName));
                }
            }
        }

        private string _filePattern = "*.*";
        /// <summary>
        /// 文件过滤模式
        /// </summary>
        public string FilePattern
        {
            get => _filePattern;
            set => SetProperty(ref _filePattern, value);
        }

        private int _folderIndex;
        /// <summary>
        /// 文件夹中文件的索引
        /// </summary>
        public int FolderIndex
        {
            get => _folderIndex;
            set
            {
                if (SetProperty(ref _folderIndex, value))
                {
                    RaisePropertyChanged(nameof(CurrentFileName));
                }
            }
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

        private int _fileCount;
        /// <summary>
        /// 文件夹中图像文件总数
        /// </summary>
        public int FileCount
        {
            get => _fileCount;
            set => SetProperty(ref _fileCount, value);
        }

        private int _cameraId;
        /// <summary>
        /// 相机ID
        /// </summary>
        public int CameraId
        {
            get => _cameraId;
            set => SetProperty(ref _cameraId, value);
        }

        private int _width = 640;
        /// <summary>
        /// 输出图像宽度
        /// </summary>
        public int OutputWidth
        {
            get => _width;
            set => SetProperty(ref _width, value);
        }

        private int _height = 480;
        /// <summary>
        /// 输出图像高度
        /// </summary>
        public int OutputHeight
        {
            get => _height;
            set => SetProperty(ref _height, value);
        }

        private HImage _previewImage = new();
        /// <summary>
        /// 预览图像
        /// </summary>

        public HImage PreviewImage
        {
            get => _previewImage;
            set
            {
                if (SetProperty(ref _previewImage, value))
                    RaisePropertyChanged(nameof(HasPreview));
            }
        }

        public bool HasPreview => PreviewImage != null;

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

        #endregion

        #region 命令

        public DelegateCommand<string> OperatorCommand { get; }

        #endregion

        public ImageAcquisitionViewModel()
        {
            OperatorCommand =new DelegateCommand<string>(ExecuteOperator);
        }

        private void ExecuteOperator(string obj)
        {
            switch (obj)
            {
                case "SelectImage":
                    BrowseFile();
                    break;
                case "SelectLinkPath"://调用变量功能---
                    LinkPath();
                    break;
                case "BrowseFile":
                    BrowseFile();
                    break;
                case "BrowseFolder":
                    BrowseFolder();
                    break;
                case "PrevFile":
                    PrevFile();
                    break;
                case "NextFile":
                    NextFile();
                    break;
                default:
                    StatusMessage = $"未知操作: {obj}";
                    break;
            }
        }
        //发布链接路径事件,并订阅
        private void LinkPath()
        {
            GlobalEventBus.Publish(new LinkPathEvent
            {
               
            });
        }
        #region 浏览文件/文件夹

        private void BrowseFile()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "图像文件|*.jpg;*.jpeg;*.png;*.bmp;*.tiff;*.gif|所有文件|*.*",
                Title = "选择图像文件"
            };

            if (dlg.ShowDialog() == true)
            {

                HImage img = new HImage();
                img.ReadImage(dlg.FileName);
                PreviewImage =img;
                //LoadPreview(FilePath);
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
                FolderPath = dlg.FolderName;
                RefreshFolderFiles();
            }
        }

        private void RefreshFolderFiles()
        {
            if (string.IsNullOrEmpty(FolderPath) || !Directory.Exists(FolderPath))
            {
                FileCount = 0;
                CurrentFileName = string.Empty;
                PreviewImage = null;
                return;
            }

            try
            {
                var validExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".gif" };

                var files = Directory.GetFiles(FolderPath, FilePattern)
                    .Where(f => validExtensions.Contains(Path.GetExtension(f)))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                FileCount = files.Count;

                if (files.Count == 0)
                {
                    StatusMessage = $"文件夹中没有匹配 {FilePattern} 的图像文件";
                    PreviewImage = null;
                    return;
                }

                FolderIndex = Math.Clamp(FolderIndex, 0, files.Count - 1);
                var currentFile = files[FolderIndex];
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

        private void PrevFile()
        {
            if (FolderIndex > 0)
            {
                FolderIndex--;
                LoadCurrentFolderFile();
            }
        }

        private void NextFile()
        {
            if (FolderIndex < FileCount - 1)
            {
                FolderIndex++;
                LoadCurrentFolderFile();
            }
        }

        private void LoadCurrentFolderFile()
        {
            if (string.IsNullOrEmpty(FolderPath) || !Directory.Exists(FolderPath))
                return;

            try
            {
                var validExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".gif" };

                var files = Directory.GetFiles(FolderPath, FilePattern)
                    .Where(f => validExtensions.Contains(Path.GetExtension(f)))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (FolderIndex >= 0 && FolderIndex < files.Count)
                {
                    var file = files[FolderIndex];
                    CurrentFileName = Path.GetFileName(file);
                    PreviewImagePath = file;
                    LoadPreview(file);
                }
            }
            catch { }
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

                using var stream = File.OpenRead(path);
                var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                bitmap.Freeze();
                //PreviewImage = bitmap;
                StatusMessage = $"预览: {Path.GetFileName(path)} ({bitmap.PixelWidth}x{bitmap.PixelHeight})";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载预览失败: {ex.Message}";
                PreviewImage = null;
            }
        }

        #endregion

        #region IPluginConfigView

        public void Initialize(IStepConfigData stepData)
        {
            _stepData = stepData;
            if (stepData?.InputValues == null)
                return;

            var values = stepData.InputValues;
            if (values.TryGetValue("Mode", out var modeObj) && modeObj is AcquisitionMode modeVal)
                Mode = modeVal;
            if (values.TryGetValue("FilePath", out var fpObj) && fpObj is string fp)
                FilePath = fp;
            if (values.TryGetValue("FilePath", out var fpObj2) && fpObj2 is string fp2 && Mode == AcquisitionMode.Folder)
                FolderPath = fp2;
            if (values.TryGetValue("Pattern", out var pObj) && pObj is string p)
                FilePattern = p;
            if (values.TryGetValue("Index", out var idxObj) && idxObj is int idx)
                FolderIndex = idx;
            if (values.TryGetValue("CameraId", out var camObj) && camObj is int cam)
                CameraId = cam;
            if (values.TryGetValue("Width", out var wObj) && wObj is int w)
                OutputWidth = w;
            if (values.TryGetValue("Height", out var hObj) && hObj is int h)
                OutputHeight = h;

            // 如果有路径，自动加载预览
            var pathToPreview = Mode switch
            {
                AcquisitionMode.SingleFile => FilePath,
                AcquisitionMode.Folder => FolderPath,
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(pathToPreview))
            {
                if (Mode ==  AcquisitionMode.SingleFile)
                    RefreshFolderFiles();
                else if (File.Exists(pathToPreview))
                    LoadPreview(pathToPreview);
            }
        }

        public PluginExecuteResult OnExecute()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                string sourcePath;
                string sourceType;

                switch (Mode)
                {
                    case AcquisitionMode.SingleFile: // 单图文件
                        sourcePath = FilePath;
                        sourceType = "文件";
                        if (!File.Exists(sourcePath))
                            return PluginExecuteResult.Fail(stopwatch.ElapsedMilliseconds, $"文件不存在: {sourcePath}");
                        break;

                    case AcquisitionMode.Folder: // 文件夹
                        sourcePath = FolderPath;
                        sourceType = $"文件夹 (索引 {FolderIndex})";
                        if (!Directory.Exists(sourcePath))
                            return PluginExecuteResult.Fail(stopwatch.ElapsedMilliseconds, $"文件夹不存在: {sourcePath}");
                        RefreshFolderFiles();
                        if (FileCount == 0)
                            return PluginExecuteResult.Fail(stopwatch.ElapsedMilliseconds, "文件夹中没有匹配的图像");
                        break;

                    case AcquisitionMode.Camera: // 相机
                        sourceType = $"相机 {CameraId} (占位)";
                        break;

                    default:
                        return PluginExecuteResult.Fail(stopwatch.ElapsedMilliseconds, $"未知模式: {Mode}");
                }

                StatusMessage = $"执行成功: {sourceType}";
                return PluginExecuteResult.Ok(stopwatch.ElapsedMilliseconds, sourceType);
            }
            catch (Exception ex)
            {
                StatusMessage = $"执行失败: {ex.Message}";
                return PluginExecuteResult.Fail(stopwatch.ElapsedMilliseconds, ex.Message);
            }
        }

        public void OnConfirm(IStepConfigData stepData)
        {
            var values = new Dictionary<string, object>
            {
                ["Mode"] = Mode,
                ["FilePath"] = Mode ==  AcquisitionMode.SingleFile ? FilePath : FolderPath,
                ["Pattern"] = FilePattern,
                ["Index"] = FolderIndex,
                ["CameraId"] = CameraId,
                ["Width"] = OutputWidth,
                ["Height"] = OutputHeight,
            };

            foreach (var kvp in values)
            {
                stepData.InputValues[kvp.Key] = kvp.Value;
            }
        }

        public void OnCancel()
        {
            // 丢弃修改，无需特殊处理
        }

        #endregion
    }
}
