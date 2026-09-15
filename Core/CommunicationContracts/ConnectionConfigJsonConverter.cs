using System;
using System.Linq;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;

namespace VisionMaster.Communications
{
    /// <summary>
    /// $type 反序列化白名单：
    /// Newtonsoft TypeNameHandling 反序列化 $type 时按此白名单限定可实例化的类型，
    /// 防止恶意方案文件（.vms）或配置文件借 $type 执行任意类型构造。
    /// 白名单范围：本项目 Models / Communications 命名空间。
    /// 兼容：旧版（System.Text.Json 时代）方案文件 $type 只有短类名（如 ActionStep），
    /// 短名仅在白名单命名空间内解析，安全边界与全名校验一致。
    /// 注意：ISerializationBinder 位于 Newtonsoft.Json.Serialization 命名空间（非根命名空间）。
    /// </summary>
    public class ConnectionConfigSerializationBinder : ISerializationBinder
    {
        private const string ModelsPrefix = "VisionMaster.Models";
        private const string CommsPrefix = "VisionMaster.Communications";
        // 插件配置类型：.vms 会内嵌插件自定义模型（如图像脚本的过程/变量列表），
        // 仅放行 Plugin.* 命名空间（与插件加载目录命名约定一致）
        private const string PluginPrefix = "Plugin.";

        public Type BindToType(string? assemblyName, string typeName)
        {
            // 新格式：完整命名空间名（Newtonsoft TypeNameHandling 输出 FullName），
            // 含系统集合包装泛型（List`1[[Plugin.x, asm]] 等，内层段同样校验）
            if (IsAllowedFullName(typeName))
            {
                return ResolveFullName(typeName, assemblyName)
                    ?? throw new JsonSerializationException($"类型未找到: {typeName}, {assemblyName}");
            }

            // 旧格式兼容：短类名（无命名空间）——仅在白名单命名空间内解析
            if (!typeName.Contains('.'))
            {
                return ResolveByShortName(typeName)
                    ?? throw new Newtonsoft.Json.JsonSerializationException($"配置文件包含未被允许的类型: {typeName}");
            }

            throw new Newtonsoft.Json.JsonSerializationException($"配置文件包含未被允许的类型: {typeName}");
        }

        /// <summary>
        /// 全名是否可反序列化：白名单命名空间直接放行；
        /// 系统集合泛型包装（如 List`1[[Plugin.ImageScript.EProcedure, Plugin.ImageScript]]）
        /// 要求外层是 System.Collections.*，且内层类型段同样命中白名单；
        /// 一维原素数组（如 System.Int32[]——变量快照 DefaultValue/Value 持数组时 TypeNameHandling 会写出）
        /// 要求元素为无攻击面的已知基元/值类型。
        /// </summary>
        private static bool IsAllowedFullName(string typeName)
        {
            // 数组类型（E1 修复）："System.Int32[]" 这类带 [] 后缀的名字不含 "[["，
            // 旧逻辑走到 Contains('.') 分支被误判为"未允许的类型"→ 含数组变量值的方案保存后无法再打开。
            // 数组本身无构造攻击面（无参数化反序列化），但元素类型仍须收敛在白名单内：只放行基元/常用值类型。
            if (typeName.EndsWith("[]", StringComparison.Ordinal))
            {
                var element = typeName.Substring(0, typeName.Length - 2).Trim();
                return IsAllowedArrayElement(element) || IsAllowedFullName(element);
            }

            int bracket = typeName.IndexOf("[[", StringComparison.Ordinal);
            if (bracket < 0)
                return IsAllowedNamespace(typeName);

            if (!typeName.StartsWith("System.Collections", StringComparison.Ordinal))
                return false;

            // 单类型参数泛型：内层形如 "Full.Type.Name, AssemblyName"
            string inner = typeName.Substring(bracket + 2).TrimEnd(']');
            int comma = inner.LastIndexOf(", ", StringComparison.Ordinal);
            string innerType = comma > 0 ? inner.Substring(0, comma) : inner;
            return IsAllowedFullName(innerType);
        }

        /// <summary>无副作用、无构造攻击面的数组元素类型集合（与 TypeCache/变量类型体系一致）</summary>
        private static readonly HashSet<string> AllowedArrayElements = new(StringComparer.Ordinal)
        {
            "System.Boolean", "System.Byte", "System.SByte",
            "System.Int16", "System.UInt16", "System.Int32", "System.UInt32",
            "System.Int64", "System.UInt64", "System.Single", "System.Double",
            "System.Decimal", "System.Char", "System.String",
            "System.DateTime", "System.Guid", "System.Object",
        };

        private static bool IsAllowedArrayElement(string elementTypeName) =>
            AllowedArrayElements.Contains(elementTypeName);

        private static bool IsAllowedNamespace(string typeName) =>
            typeName.StartsWith(ModelsPrefix, StringComparison.Ordinal)
            || typeName.StartsWith(CommsPrefix, StringComparison.Ordinal)
            || typeName.StartsWith(PluginPrefix, StringComparison.Ordinal);

        private static Type? ResolveFullName(string typeName, string? assemblyName)
        {
            var t = Type.GetType($"{typeName}, {assemblyName}", throwOnError: false);
            if (t != null) return t;

            // 程序集名缺省/别名时在当前域按全名搜索
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(typeName, throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }

        private static Type? ResolveByShortName(string shortName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException ex)
                {
                    types = ex.Types.OfType<Type>().ToArray();
                }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t.Name != shortName) continue;
                    var ns = t.Namespace ?? string.Empty;
                    if (ns.StartsWith(ModelsPrefix) || ns.StartsWith(CommsPrefix))
                        return t;
                }
            }
            return null;
        }

        public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
        {
            // 简化程序集名（不含版本/公钥），提升跨版本方案文件兼容性
            assemblyName = serializedType.Assembly.GetName().Name;
            typeName = serializedType.FullName;
        }
    }
}
