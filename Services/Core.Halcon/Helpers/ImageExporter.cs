using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Core.Halcon.Models;
using HalconDotNet;

namespace Core.Halcon.Helpers
{
    /// <summary>
    /// 图像集落盘：把一批 <see cref="ImageFrame"/> 按"流程分文件夹"导出为 PNG。
    ///
    /// 为什么按流程分文件夹
    /// ---------
    /// 图集本身按流程分组，落盘沿用同一层级，现场翻目录时"哪个流程出了什么问题"一眼可辨；
    /// 全平铺在一个文件夹里，几十张图之后只能靠文件名猜。
    ///
    /// 为什么失败要逐张吞掉
    /// ---------
    /// 单张写失败（磁盘满、文件被占用、路径过长）不该让一次"导出全部"整体作废：
    /// 能落的先落下来，返回值告诉调用方成功了几张，由调用方决定怎么提示。
    /// </summary>
    public static class ImageExporter
    {
        /// <summary>
        /// 导出若干帧到 <paramref name="rootFolder"/>（不存在会自动创建）。
        /// </summary>
        /// <param name="frames">要导出的帧（null / 未初始化图像会被跳过）</param>
        /// <param name="rootFolder">目标根目录</param>
        /// <param name="groupByFlow">是否按流程名建子文件夹（默认是）</param>
        /// <returns>成功写出的文件数</returns>
        public static int SaveFrames(IEnumerable<ImageFrame>? frames, string rootFolder, bool groupByFlow = true)
        {
            if (frames == null || string.IsNullOrWhiteSpace(rootFolder)) return 0;

            var list = frames.Where(f => f != null).ToList();
            if (list.Count == 0) return 0;

            try
            {
                Directory.CreateDirectory(rootFolder);
            }
            catch
            {
                return 0;
            }

            int saved = 0;
            int sameNameIndex = 0;

            foreach (var frame in list)
            {
                if (frame.Image == null) continue;

                try
                {
                    if (!frame.Image.IsInitialized()) continue;

                    var folder = rootFolder;
                    if (groupByFlow && !string.IsNullOrWhiteSpace(frame.FlowName))
                        folder = Path.Combine(rootFolder, Sanitize(frame.FlowName));

                    Directory.CreateDirectory(folder);

                    // 文件名：步骤_端口_时分秒毫秒；同毫秒重名（历史帧模式下可能）再补序号
                    var baseName = $"{Sanitize(frame.StepName)}_{Sanitize(frame.PortName)}_{frame.Timestamp:HHmmss_fff}";
                    var path = Path.Combine(folder, baseName + ".png");
                    while (File.Exists(path))
                    {
                        sameNameIndex++;
                        path = Path.Combine(folder, $"{baseName}_{sameNameIndex}.png");
                    }

                    // HALCON 原生写图：格式/位深由引擎决定，避免在托管侧另写一套编码器
                    HOperatorSet.WriteImage(frame.Image, "png", 0, path);
                    saved++;
                }
                catch
                {
                    // 单张失败跳过（理由见类注释）
                }
            }

            return saved;
        }

        /// <summary>把 HImage 的宽高读出来（失败返回 false）；导出的同时可给界面提示用</summary>
        public static bool TryGetSize(HImage? image, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (image == null) return false;
            try
            {
                if (!image.IsInitialized()) return false;
                image.GetImageSize(out width, out height);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>把任意字符串收敛成合法文件名片段（非法字符替换为下划线）</summary>
        private static string Sanitize(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "未命名";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
                sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);

            var result = sb.ToString().Trim();
            return result.Length == 0 ? "未命名" : result;
        }
    }
}
