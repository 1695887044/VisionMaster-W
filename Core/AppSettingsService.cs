using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 软件级配置服务（AppConfig.json，位于程序目录，与 Layout.xml 同级）
    /// 职责：方案清单与默认启动方案的读写，独立于解决方案文件
    /// </summary>
    public class AppSettingsService
    {
        private static readonly string ConfigPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppConfig.json");

        private static readonly JsonSerializerSettings JsonOptions = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public AppSettingsService()
        {
            Load();
        }

        /// <summary>
        /// 当前软件配置（启动时自动从磁盘加载）
        /// </summary>
        public AppConfigModel Current { get; private set; } = new();

        /// <summary>
        /// 从磁盘加载（文件不存在或损坏时使用默认配置）
        /// </summary>
        public void Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    Current = new AppConfigModel();
                }
                else
                {
                    var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                    Current = JsonConvert.DeserializeObject<AppConfigModel>(json, JsonOptions) ?? new AppConfigModel();
                }
            }
            catch (Exception ex)
            {
                // 配置损坏不应阻断启动
                Console.WriteLine($"软件配置加载失败，使用默认配置。原因：{ex.Message}");
                Current = new AppConfigModel();
            }

            SyncParallelConfig();
        }

        /// <summary>
        /// 保存到磁盘（原子写：先写临时文件再替换，断电不丢旧配置）
        /// </summary>
        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(Current, JsonOptions);
                var tmp = ConfigPath + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);
                if (File.Exists(ConfigPath)) WriteReplacing(tmp, ConfigPath);
                else File.Move(tmp, ConfigPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"软件配置保存失败：{ex.Message}");
                throw;
            }

            SyncParallelConfig();
        }

        /// <summary>
        /// 把临时文件 <paramref name="tmp"/> 的内容落到 <paramref name="dest"/>，替换已有文件。
        ///
        /// 为什么不直接用裸 <c>File.Replace</c>：它走 Win32 <c>ReplaceFile</c>，要求**能删除目标文件**。
        /// 只要运行中的宿主（或杀软/索引器）以"允许读、允许写、但不允许删除"的方式开着
        /// AppConfig.json，ReplaceFile 就会以 ERROR_UNABLE_TO_REMOVE_REPLACED(1177) 失败
        /// （2026-10-14 真机：点工具箱收藏即崩，栈落在 <see cref="Save"/> 的 File.Replace 上）。
        /// 退路是覆盖写：<c>File.Copy(overwrite:true)</c> 只要求写权限、**不要求删除目标**，
        /// 正好补上 1177 这一档。代价是这条冷路径上原子性略降（中途断电可能留半份文件），
        /// 但比"保存直接失败"强，且只在 Replace 抛 IOException 时才走到。
        /// </summary>
        private static void WriteReplacing(string tmp, string dest)
        {
            try
            {
                File.Replace(tmp, dest, null);
            }
            catch (IOException)
            {
                File.Copy(tmp, dest, overwrite: true);
                try { File.Delete(tmp); } catch { /* 残留 .tmp 无害：下次覆盖写 */ }
            }
        }

        /// <summary>
        /// 【二期真并行】把 ParallelExecution 节灌进静态策略 GlobalParallelConfig（与 RunWindowMode 同口径）。
        /// 为什么运行期装配：编译期读配置会传染全部 new FlowCompiler 调用点，且断言宿主会在
        /// bin 写 AppConfig.json 破坏密闭性；运行期装配 = 改配置下一轮生效、无需重编译。
        /// null 防御：老配置缺节时 ParallelExecution 带 = new() 初始化器不会为 null，双保险取 false。
        /// </summary>
        private void SyncParallelConfig()
        {
            VisionMaster.Models.GlobalParallelConfig.FailFastByDefault
                = Current?.ParallelExecution?.FailFastByDefault ?? false;

            // 进程级总闸（2026-10-11 补上装配线）：现场排障时把 AppConfig.json 的
            // ParallelExecution.ForceSequential 写成 true，重启即全体并行组退化为顺序执行
            VisionMaster.Models.GlobalParallelConfig.ForceSequential
                = Current?.ParallelExecution?.ForceSequential ?? false;
        }

        /// <summary>
        /// 画布「随用户」视图态的唯一写路径（评审清单 B 包）：两个显隐开关写进
        /// FlowCanvas 节并立即落盘——与 RunWindowMode 同一条"改完即存"的保存周期。
        /// 调用方是 FlowCanvasViewModel 的两个开关 setter；断言宿主据此做
        /// "改值 → 落盘 → 重读一致"的往返断言。缺节防御同上：初始化器保证非 null，双保险跳过。
        /// </summary>
        public void SyncFlowCanvasSettings(bool showDataLinks, bool showOrderLinks)
        {
            var node = Current?.FlowCanvasSettings;
            if (node == null) return;

            node.ShowDataLinks = showDataLinks;
            node.ShowOrderLinks = showOrderLinks;

            Save();
        }

        /// <summary>
        /// 工具箱收藏清单的唯一写路径（工具箱面板的星标）：整份覆写 Toolbox 节并立即落盘——
        /// 与 <see cref="SyncFlowCanvasSettings"/> 同一条"改完即存"的保存周期。
        /// 传进来的 id 是 <c>ToolItemModel.ModuleTypeName</c>，调用方（ToolViewModel）保证它来自
        /// 当前已注册插件；缺节防御同上：初始化器保证非 null，双保险跳过。
        /// </summary>
        public void SyncToolboxFavorites(IEnumerable<string> ids)
        {
            var node = Current?.Toolbox;
            if (node == null) return;

            node.Favorites = ids?.ToList() ?? new List<string>();

            Save();
        }
    }
}
