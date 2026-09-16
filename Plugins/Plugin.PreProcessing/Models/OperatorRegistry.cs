using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Plugin.PreProcessing.Models
{
    /// <summary>
    /// 算子声明特性：贴在一个 <see cref="PreprocessOperator"/> 派生类上，
    /// 即完成"注册 + 菜单展示 + 创建"三件事——新增算子只写一个类，不必改任何 switch（开闭原则）
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PreprocessOperatorAttribute : Attribute
    {
        public PreprocessOperatorAttribute(string key, string displayName, string category)
        {
            Key = key;
            DisplayName = displayName;
            Category = category;
        }

        /// <summary>存盘标识：一经发布不要改名，否则旧方案里的算子会加载不到</summary>
        public string Key { get; }

        /// <summary>界面上显示的中文算子名</summary>
        public string DisplayName { get; }

        /// <summary>算子库分组（菜单一级项）</summary>
        public string Category { get; }

        /// <summary>悬浮提示 / 用途说明</summary>
        public string Description { get; set; } = "";

        /// <summary>组内排序（越小越前）</summary>
        public int Order { get; set; }
    }

    /// <summary>一个算子的"出厂信息"：显示名、分组、以及怎么造出一个实例</summary>
    public sealed class OperatorDescriptor
    {
        public string Key { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string Category { get; init; } = "";
        public string Description { get; init; } = "";
        public int Order { get; init; }

        /// <summary>造一个新算子实例（默认参数）</summary>
        public Func<PreprocessOperator> Factory { get; init; } = null!;

        /// <summary>给菜单/列表用的一次性显示文本</summary>
        public string MenuText => DisplayName;
    }

    /// <summary>
    /// 算子注册表：扫描本程序集内所有打了 <see cref="PreprocessOperatorAttribute"/> 的算子类。
    /// 静态构造只跑一次，反射结果缓存成 List，之后全是纯内存查表。
    /// </summary>
    public static class OperatorRegistry
    {
        private static readonly List<OperatorDescriptor> _descriptors = new();
        private static readonly Dictionary<string, OperatorDescriptor> _byKey =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>分组名 → 该组算子（已按 Order 排好）</summary>
        public static List<OperatorCategoryGroup> Categories { get; private set; } = new();

        static OperatorRegistry()
        {
            var baseType = typeof(PreprocessOperator);

            var types = typeof(OperatorRegistry).Assembly
                .GetTypes()
                .Where(t => !t.IsAbstract && baseType.IsAssignableFrom(t));

            foreach (var type in types)
            {
                var attr = type.GetCustomAttribute<PreprocessOperatorAttribute>();
                if (attr == null) continue; // 忘了贴特性 = 不对外暴露（抽象中间基类也会被跳过）

                var descriptor = new OperatorDescriptor
                {
                    Key = attr.Key,
                    DisplayName = attr.DisplayName,
                    Category = attr.Category,
                    Description = attr.Description,
                    Order = attr.Order,
                    Factory = () => (PreprocessOperator)Activator.CreateInstance(type)!,
                };

                _descriptors.Add(descriptor);
                _byKey[descriptor.Key] = descriptor;
            }

            // 分组顺序按"组内最小 Order"排：Order 全库统一递增分配，
            // 不依赖 Assembly.GetTypes() 的返回顺序（那个顺序 CLR 并不保证）
            Categories = _descriptors
                .GroupBy(d => d.Category)
                .OrderBy(g => g.Min(d => d.Order))
                .Select(g => new OperatorCategoryGroup(g.Key, g.OrderBy(d => d.Order).ToList()))
                .ToList();
        }

        public static IReadOnlyList<OperatorDescriptor> All => _descriptors;

        /// <summary>按存盘 Key 造实例；Key 不认识（版本回退/手改方案文件）返回 null 由上层决定降级策略</summary>
        public static PreprocessOperator? Create(string? key)
            => key != null && _byKey.TryGetValue(key, out var d) ? d.Factory() : null;

        public static OperatorDescriptor? Find(string? key)
            => key != null && _byKey.TryGetValue(key, out var d) ? d : null;
    }

    /// <summary>算子库的一个分组（菜单一级项）</summary>
    public sealed class OperatorCategoryGroup
    {
        public OperatorCategoryGroup(string name, List<OperatorDescriptor> items)
        {
            Name = name;
            Items = items;
        }

        public string Name { get; }
        public List<OperatorDescriptor> Items { get; }
    }
}
