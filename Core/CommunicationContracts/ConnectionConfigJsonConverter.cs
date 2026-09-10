using System;
using System.Linq;
using Newtonsoft.Json.Serialization;

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

        public Type BindToType(string? assemblyName, string typeName)
        {
            // 新格式：完整命名空间名（Newtonsoft TypeNameHandling 输出 FullName）
            if (typeName.StartsWith(ModelsPrefix) || typeName.StartsWith(CommsPrefix))
            {
                return ResolveFullName(typeName, assemblyName)
                    ?? throw new Newtonsoft.Json.JsonSerializationException($"类型未找到: {typeName}, {assemblyName}");
            }

            // 旧格式兼容：短类名（无命名空间）——仅在白名单命名空间内解析
            if (!typeName.Contains('.'))
            {
                return ResolveByShortName(typeName)
                    ?? throw new Newtonsoft.Json.JsonSerializationException($"配置文件包含未被允许的类型: {typeName}");
            }

            throw new Newtonsoft.Json.JsonSerializationException($"配置文件包含未被允许的类型: {typeName}");
        }

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
