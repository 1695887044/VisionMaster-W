using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Interfaces;
using Newtonsoft.Json;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 插件清单缓存（2026-10-16 层 3 启动提速）：把插件扫描的产物按 DLL 分组存成 JSON，
    /// 下次启动指纹没变就直接按清单注册，跳过 <c>GetTypes()</c> 反射与 42 个插件的
    /// <c>Activator.CreateInstance</c> 真实例化——插桩实测这两项合计约 2.5 秒，是大头。
    ///
    /// 缓存命中路径**仍会 <c>Assembly.LoadFrom</c> 全部插件程序集**：下游三处（相机/运动卡自动连接、
    /// 流程编译期 <c>FlowCompiler</c>、帮助手册合并读内嵌资源）都依赖"程序集已在内存里"，
    /// 缓存只省反射与实例化，不省加载。这是设计成立的前提，不是疏漏。
    ///
    /// 失效判据（任一不满足即全量重扫并重写缓存，不做"部分失效"——两份状态并存比慢更糟）：
    ///  ① SchemaVersion 对不上（清单结构变了）；
    ///  ② manifest 里登记的某个 DLL 在磁盘上找不到，或大小/mtime 对不上；
    ///  ③ 磁盘上多出 manifest 里没登记的 Plugin*.dll（新增插件必须被看见）；
    ///  ④ 宿主契约程序集版本变了（插件二进制对它们链接，契约变了清单里的类型名可能已失效）。
    /// 读失败（文件损坏/手改）按"缓存不存在"处理，不报错。
    /// </summary>
    internal static class PluginCache
    {
        /// <summary>清单结构版本：结构变了（比如给条目加字段）就整体失效重扫</summary>
        internal const int SchemaVersion = 1;

        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            // 端口表/类型名里可能出现 '\uXXXX' 这类已被转义的序列，默认行为会再转义一次
            //（把"\\u"存成"\\\\u"），读回来就错位。Round-trip 必须逐字节保真。
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
        };

        /// <summary>一份缓存清单：宿主契约版本 + 每个插件 DLL 的指纹与注册产物</summary>
        internal sealed class Manifest
        {
            public int Schema { get; set; }
            public string ContractVersion { get; set; } = string.Empty;
            public List<PluginDllEntry> Dlls { get; set; } = new();
        }

        /// <summary>一个插件 DLL 的指纹 + 它扫描出的全部注册产物</summary>
        internal sealed class PluginDllEntry
        {
            public string FileName { get; set; } = string.Empty;
            public long Length { get; set; }
            public long LastWriteTimeUtcTicks { get; set; }

            public List<ToolItemModel> Modules { get; set; } = new();
            public List<ToolItemModel> Cameras { get; set; } = new();
            public List<ToolItemModel> Motions { get; set; } = new();
            public List<ToolItemModel> Lasers { get; set; } = new();
        }

        /// <summary>读缓存并核对指纹；可用则返回清单，不可用/不存在/损坏都返回 null（调用方全量重扫）</summary>
        /// <param name="missReason">非 null 时把失效原因写进去（日志用）——"缓存失效"本身不是故障，
        /// 但现场要能看出是"指纹对不上"还是"文件损坏"，不然只能猜。</param>
        internal static Manifest? TryLoad(string pluginsDir, string contractVersion, Action<string>? missReason = null)
        {
            void Miss(string why) => missReason?.Invoke(why);
            try
            {
                var path = ManifestPath(pluginsDir);
                if (!File.Exists(path)) { Miss("无缓存文件"); return null; }

                var manifest = JsonConvert.DeserializeObject<Manifest>(
                    File.ReadAllText(path), JsonSettings);
                if (manifest == null) { Miss("反序列化为 null"); return null; }
                if (manifest.Schema != SchemaVersion) { Miss($"SchemaVersion {manifest.Schema} != {SchemaVersion}"); return null; }
                if (!string.Equals(manifest.ContractVersion, contractVersion, StringComparison.Ordinal))
                { Miss($"契约版本 {manifest.ContractVersion} != {contractVersion}"); return null; }

                // ② manifest 里登记的每个 DLL 都得在、且指纹对得上
                foreach (var entry in manifest.Dlls)
                {
                    var fi = new FileInfo(Path.Combine(pluginsDir, entry.FileName));
                    if (!fi.Exists) { Miss($"缺文件 {entry.FileName}"); return null; }
                    if (fi.Length != entry.Length) { Miss($"大小变了 {entry.FileName}"); return null; }
                    if (fi.LastWriteTimeUtc.Ticks != entry.LastWriteTimeUtcTicks) { Miss($"mtime 变了 {entry.FileName}"); return null; }
                }

                // ③ 磁盘上不许多出没登记的 Plugin*.dll
                var known = new HashSet<string>(
                    manifest.Dlls.Select(d => d.FileName), StringComparer.OrdinalIgnoreCase);
                foreach (var dllPath in Directory.GetFiles(pluginsDir, "Plugin*.dll"))
                {
                    if (!known.Contains(Path.GetFileName(dllPath)))
                    { Miss($"多出未登记的 {Path.GetFileName(dllPath)}"); return null; }
                }

                return manifest;
            }
            catch (Exception ex)
            {
                // 读失败（损坏/手改/权限）= 缓存不存在，全量重扫。不该让坏缓存挡住启动。
                Miss("读缓存异常：" + ex.GetType().Name);
                return null;
            }
        }

        /// <summary>把全量扫描的产物写成清单（下次启动即命中）。写失败不抛——缓存只是加速，不是必需品。</summary>
        internal static void Save(string pluginsDir, string contractVersion, IReadOnlyList<PluginDllEntry> entries)
        {
            try
            {
                var manifest = new Manifest
                {
                    Schema = SchemaVersion,
                    ContractVersion = contractVersion,
                    Dlls = entries.ToList(),
                };
                var path = ManifestPath(pluginsDir);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonConvert.SerializeObject(manifest, JsonSettings));
            }
            catch
            {
                // 写不进去（只读目录/磁盘满）只是下次还得重扫，不是故障
            }
        }

        internal static string ManifestPath(string pluginsDir)
            => Path.Combine(pluginsDir, "PluginCache", "manifest.json");

        /// <summary>一个 DLL 的当前指纹（写缓存与核对用同一口径，大小+修改时间）</summary>
        internal static PluginDllEntry FingerprintOf(string dllPath)
        {
            var fi = new FileInfo(dllPath);
            return new PluginDllEntry
            {
                FileName = fi.Name,
                Length = fi.Length,
                LastWriteTimeUtcTicks = fi.LastWriteTimeUtc.Ticks,
            };
        }
    }
}
