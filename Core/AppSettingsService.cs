using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
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
            NullValueHandling = NullValueHandling.Ignore,
            // 集合属性必须「替换」而不是「追加」：PopulateObject 默认对已存在的 List<T> 追加，
            // 而本类每次 Load 都跑一遍（构造器 + ConfigCheck），不清就会让清单每次启动翻倍。
            // 详见 ReplaceCollectionsContractResolver 的注释。
            ContractResolver = new ReplaceCollectionsContractResolver()
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
        /// 最近一次 <see cref="Load"/> 是否清理过重复/空白条目。
        ///
        /// 用途：历史上 PopulateObject 的集合追加缺陷把 AppConfig 写成过 4096 / 16384 / 65536 份
        /// 同一个方案（2 的幂 = 每次 Load 翻一倍）。缺陷已由 <see cref="ReplaceCollectionsContractResolver"/>
        /// 修掉，但**存量文件里那份重复还在**——不清理的话，启动自检与方案列表显示的数量仍然是天文数字。
        /// 宿主（ConfigCheck）据此决定是否把清理后的配置回写一次，让老文件自愈。
        /// </summary>
        public bool LastLoadNormalized { get; private set; }

        /// <summary>
        /// 从磁盘加载（文件不存在或损坏时使用默认配置）
        /// </summary>
        public void Load()
        {
            LastLoadNormalized = false;

            try
            {
                if (!File.Exists(ConfigPath))
                {
                    Current = new AppConfigModel();
                }
                else
                {
                    var json = File.ReadAllText(ConfigPath, Encoding.UTF8);

                    // ★ 原地填充，不替换 Current。
                    // 可选模块（如 ScadaWeb）在 Initialize 里拿的是 Current.ScadaWeb 这个对象引用，
                    // 而 ConfigCheck 在模块初始化之后还会再 Load 一次。若这里 Current = 新对象，
                    // 模块手里的引用就永久停在旧值——运行期改 AllowCommands/AllowAnonymousView
                    // 对已启动的服务完全不生效（2026-10-21 终审 P0-B）。
                    // PopulateObject 只覆盖 JSON 里出现的属性，引用身份保持不变。
                    JsonConvert.PopulateObject(json, Current, JsonOptions);
                }
            }
            catch (Exception ex)
            {
                // 配置损坏不应阻断启动
                Console.WriteLine($"软件配置加载失败，使用默认配置。原因：{ex.Message}");
                Current = new AppConfigModel();
            }

            // JSON 里显式写 null 的节不能把下游打成 NullReference；缺节时保持默认对象。
            Current.ScadaWeb ??= new ScadaWebSettings();
            Current.DebugMode ??= new DebugModeSettings();
            Current.Solutions ??= new List<AppSolutionEntry>();
            Current.Toolbox ??= new ToolboxSettings();
            Current.Toolbox.Favorites ??= new List<string>();

            // 存量自愈：去重 + 去空白 + 路径首尾空白归一。重复项不参与任何判定（列表按 Path 唯一）。
            LastLoadNormalized = NormalizeLoadedCollections();

            SyncParallelConfig();
        }

        /// <summary>
        /// 清理磁盘读回来的重复/空白条目。返回是否真的改过（宿主据此决定要不要回写）。
        ///
        /// 口径：
        ///  · 方案清单按 <c>Path</c> 去重（忽略大小写），保留第一次出现的顺序与 Name/Comment；
        ///  · 空 <c>Path</c> 的条目直接丢掉（它打不开，也不该占一个计数）；
        ///  · <c>Path</c> 首尾空白先 Trim（手工编辑 JSON 常带）；
        ///  · 工具箱收藏按 id 去重（区分大小写，id 是类型全名）。
        /// </summary>
        private bool NormalizeLoadedCollections()
        {
            var changed = false;

            var solutions = Current.Solutions ?? new List<AppSolutionEntry>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keptSolutions = new List<AppSolutionEntry>(solutions.Count);

            foreach (var entry in solutions)
            {
                if (entry == null)
                {
                    changed = true;
                    continue;
                }

                var path = (entry.Path ?? string.Empty).Trim();
                if (path.Length == 0)
                {
                    changed = true;
                    continue;
                }

                if (!string.Equals(path, entry.Path, StringComparison.Ordinal))
                {
                    entry.Path = path;
                    changed = true;
                }

                if (!seenPaths.Add(path))
                {
                    changed = true;
                    continue;
                }

                keptSolutions.Add(entry);
            }

            if (keptSolutions.Count != solutions.Count)
            {
                Current.Solutions = keptSolutions;
                changed = true;
            }

            var favorites = Current.Toolbox?.Favorites;
            if (favorites != null)
            {
                var seenIds = new HashSet<string>(StringComparer.Ordinal);
                var keptFavorites = new List<string>(favorites.Count);
                foreach (var id in favorites)
                {
                    if (string.IsNullOrWhiteSpace(id) || !seenIds.Add(id))
                    {
                        changed = true;
                        continue;
                    }

                    keptFavorites.Add(id);
                }

                if (keptFavorites.Count != favorites.Count)
                {
                    Current.Toolbox!.Favorites = keptFavorites;
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>
        /// 保存到磁盘（原子写：先写临时文件再替换，断电不丢旧配置）
        /// </summary>
        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(Current, JsonOptions);
                // 临时文件名带进程 Id：两个实例（两个会话各跑一遍断言宿主、或软件与断言宿主同时跑）
                // 共用一个 `AppConfig.json.tmp` 会互相锁住，症状是"保存配置失败 + 未处理异常"直接崩掉进程。
                var tmp = ConfigPath + "." + Environment.ProcessId + ".tmp";
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
                return;
            }
            catch (IOException)
            {
                // 落到覆盖写
            }

            // 覆盖写也可能失败：目标文件正被另一个进程开着（软件与断言宿主同时跑、
            // 或同一台机器上两个会话各跑一遍宿主）。这时**有界重试**，别把"保存配置"
            // 变成"未处理异常崩进程"（2026-10-27 实测：gate 里 FlowCanvasChecks 崩在这里）。
            Exception? last = null;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    File.Copy(tmp, dest, overwrite: true);
                    try { File.Delete(tmp); } catch { /* 残留 .tmp 无害：下次覆盖写 */ }
                    return;
                }
                catch (IOException ex)
                {
                    last = ex;
                    System.Threading.Thread.Sleep(100);
                }
                catch (UnauthorizedAccessException ex)
                {
                    last = ex;
                    System.Threading.Thread.Sleep(100);
                }
            }

            throw last ?? new IOException("保存配置失败：" + dest);
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
        /// 帮助手册阅读态的唯一写路径（帮助窗口工具栏的「显示开发章节」）：
        /// 与 <see cref="SyncFlowCanvasSettings"/> 同一条"改完即存"的保存周期。
        /// 缺节防御同上：初始化器保证非 null，双保险跳过。
        /// </summary>
        public void SyncHelpSettings(bool showDeveloperDocs)
        {
            var node = Current?.Help;
            if (node == null) return;

            node.ShowDeveloperDocs = showDeveloperDocs;

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

        /// <summary>
        /// 调试模式开关的唯一写路径（调试面板的开关）：整份覆写 <c>DebugMode</c> 节并立即落盘——
        /// 与 <see cref="SyncHelpSettings"/> 同一条"改完即存"的保存周期。
        ///
        /// <b>不写时间失效判据</b>：这里只记"谁在什么时候开的"（展示用），
        /// 是否失效只看 <c>Enabled</c> 这一个布尔。加一个"有效期"字段，
        /// 现场就会出现"调完过一阵自己关了"——那不是本功能要的语义（见 <see cref="DebugModeSettings"/>）。
        /// </summary>
        /// <returns>true = 已写入并落盘；false = 配置节缺失（老文件被手工改成 null），未写盘</returns>
        /// <remarks>
        /// <b>写盘失败必须回滚内存</b>：先改节点再 <see cref="Save"/>，Save 抛异常时若不还原，
        /// 内存里就是"已开启"而磁盘上仍是旧值——界面亮着调试徽标、重启又消失，
        /// 比"这次开关没生效"难查得多。回滚后内存与磁盘始终是同一份事实。
        /// </remarks>
        public bool SyncDebugModeSettings(bool enabled, string enabledBy, string enabledAt)
        {
            var node = Current?.DebugMode;
            if (node == null) return false;

            var previous = (node.Enabled, node.EnabledBy, node.EnabledAt);

            node.Enabled = enabled;
            if (enabled)
            {
                node.EnabledBy = enabledBy ?? string.Empty;
                node.EnabledAt = enabledAt ?? string.Empty;
            }

            try
            {
                Save();
            }
            catch
            {
                (node.Enabled, node.EnabledBy, node.EnabledAt) = previous;
                throw;
            }

            return true;
        }

        /// <summary>
        /// 让 <see cref="JsonConvert.PopulateObject"/> 对**集合属性**做替换而不是追加。
        ///
        /// 为什么必须有这一层：<see cref="Load"/> 刻意用 PopulateObject 原地填充，保住
        /// <see cref="Current"/> 与 <c>ScadaWeb</c> 等嵌套对象的引用身份（见 Load 里的注释）；
        /// 但 Newtonsoft 对已存在的 <c>List&lt;T&gt;</c> 默认是**追加**——实测 1 项，
        /// Populate 一次变 3 项、再一次变 5 项。启动链上 Load 至少跑两次（本类构造器 +
        /// <c>ConfigCheck</c>），于是 <c>Solutions</c> 与 <c>Toolbox.Favorites</c> 每次启动翻一倍，
        /// 随后任意一次 <see cref="Save"/> 把翻倍结果写回磁盘（2026-10-25 实测：方案清单
        /// 32768 项、AppConfig.json 27.4 MB，日志里逐次 2048→4096→8192→16384→32768）。
        ///
        /// 只对集合开 Replace：对象属性仍走 Populate，引用身份不变。字符串本身是 IEnumerable，
        /// 必须排除，否则会被当成集合处理。本类当前只有两个集合属性（Solutions / Favorites），
        /// 都是可写属性；将来新增集合属性自动继承这条规则，不需要再记得手写 Clear。
        /// </summary>
        private sealed class ReplaceCollectionsContractResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);

                var type = property.PropertyType;
                if (type != null
                    && type != typeof(string)
                    && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
                {
                    property.ObjectCreationHandling = ObjectCreationHandling.Replace;
                }

                return property;
            }
        }
    }
}
