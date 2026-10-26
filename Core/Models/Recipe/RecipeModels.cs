using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Core.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace VisionMaster.Models
{
    /// <summary>配方条目类型。v1 只启用 <see cref="StepInput"/>；其余为 v1.1 预留。</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum RecipeTargetKind
    {
        /// <summary>步骤输入端口 / [StepConfig] 参数（v1 唯一启用的类型）</summary>
        StepInput = 0,

        /// <summary>本地全局变量（v1.1 预留；v1 出现即整单拒绝）</summary>
        LocalVariable = 1,

        /// <summary>运动点位（v1.1 预留；v1 出现即整单拒绝）</summary>
        MotionPoint = 2,
    }

    /// <summary>配方作用域。v1 只有方案级。</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum RecipeScope
    {
        Solution = 0,
    }

    /// <summary>
    /// 一条配方条目：把某个稳定目标上的某个键写成某个值。
    ///
    /// 键只认 Id（<see cref="TargetId"/>）+ <see cref="Key"/>；名字只作显示/审计/导入匹配。
    /// 值存规范 <see cref="JToken"/>；<see cref="ValueIsNull"/> 显式承载 null，
    /// 因为 <c>NullValueHandling.Ignore</c> 会把 <c>Value:null</c> 抹成缺字段。
    /// </summary>
    public sealed class RecipeEntry : BindableBase
    {
        private RecipeTargetKind _kind = RecipeTargetKind.StepInput;
        private Guid _targetId;
        private string _key = string.Empty;
        private string _targetName = string.Empty;
        private string _pluginTypeName = string.Empty;
        private string _declaredType = string.Empty;
        private JToken? _value;
        private bool _valueIsNull;

        public RecipeTargetKind Kind
        {
            get => _kind;
            set => SetProperty(ref _kind, value);
        }

        /// <summary>StepInput = StepID；LocalVariable = VariableId；MotionPoint = AxisId。</summary>
        public Guid TargetId
        {
            get => _targetId;
            set => SetProperty(ref _targetId, value);
        }

        /// <summary>端口名 / [StepConfig] 属性名；变量与点位为空。</summary>
        public string Key
        {
            get => _key;
            set => SetProperty(ref _key, value ?? string.Empty);
        }

        /// <summary>捕获时的步骤名/变量名；仅显示与审计，不作寻址。</summary>
        public string TargetName
        {
            get => _targetName;
            set => SetProperty(ref _targetName, value ?? string.Empty);
        }

        /// <summary>捕获时的插件类型规范化键；用于发现“步骤被换成别的插件”。</summary>
        public string PluginTypeName
        {
            get => _pluginTypeName;
            set => SetProperty(ref _pluginTypeName, value ?? string.Empty);
        }

        /// <summary>捕获时的目标声明类型规范化键；应用前必须与当前声明类型兼容。</summary>
        public string DeclaredType
        {
            get => _declaredType;
            set => SetProperty(ref _declaredType, value ?? string.Empty);
        }

        /// <summary>规范 JSON 值。</summary>
        public JToken? Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        /// <summary>显式 null 位；与 <see cref="Value"/> 必须一致。</summary>
        public bool ValueIsNull
        {
            get => _valueIsNull;
            set => SetProperty(ref _valueIsNull, value);
        }

        [JsonIgnore]
        public string DisplayTarget => string.IsNullOrWhiteSpace(TargetName)
            ? (TargetId == Guid.Empty ? "（未命名目标）" : TargetId.ToString("N"))
            : TargetName;

        [JsonIgnore]
        public string DisplayKey => string.IsNullOrWhiteSpace(Key) ? "（空键）" : Key;

        [JsonIgnore]
        public string DisplayValue => ValueIsNull ? "null" : Value?.ToString(Formatting.None) ?? "（缺失）";
    }

    /// <summary>运动点位配方条目（v1.1 预留；v1 只序列化、不应用）。</summary>
    public sealed class RecipePoint : BindableBase
    {
        private Guid _axisId;
        private int _index;
        private double _positionMm;
        private double _speedMmPerS;
        private double _accelMmPerS2;
        private string _axisNameSnapshot = string.Empty;
        private string _cardNameSnapshot = string.Empty;
        private int _physicalIndexSnapshot;

        public Guid AxisId
        {
            get => _axisId;
            set => SetProperty(ref _axisId, value);
        }

        public int Index
        {
            get => _index;
            set => SetProperty(ref _index, value);
        }

        public double PositionMm
        {
            get => _positionMm;
            set => SetProperty(ref _positionMm, value);
        }

        public double SpeedMmPerS
        {
            get => _speedMmPerS;
            set => SetProperty(ref _speedMmPerS, value);
        }

        public double AccelMmPerS2
        {
            get => _accelMmPerS2;
            set => SetProperty(ref _accelMmPerS2, value);
        }

        public string AxisNameSnapshot
        {
            get => _axisNameSnapshot;
            set => SetProperty(ref _axisNameSnapshot, value ?? string.Empty);
        }

        public string CardNameSnapshot
        {
            get => _cardNameSnapshot;
            set => SetProperty(ref _cardNameSnapshot, value ?? string.Empty);
        }

        public int PhysicalIndexSnapshot
        {
            get => _physicalIndexSnapshot;
            set => SetProperty(ref _physicalIndexSnapshot, value);
        }
    }

    /// <summary>
    /// 一份配方：身份 + 元信息 + 条目集合。
    /// 值集变化（条目增删/值变/类型变）才递增 <see cref="ContentVersion"/>；
    /// 改名/备注/应用都不递增。
    /// </summary>
    public sealed class RecipeDefinition : BindableBase
    {
        private Guid _recipeId = Guid.NewGuid();
        private string _name = string.Empty;
        private string _code = string.Empty;
        private RecipeScope _scope = RecipeScope.Solution;
        private int _contentVersion = 1;
        private DateTime _createdAtUtc = DateTime.UtcNow;
        private string _createdBy = string.Empty;
        private DateTime? _updatedAtUtc;
        private string _updatedBy = string.Empty;
        private DateTime? _lastAppliedAtUtc;
        private string _lastAppliedBy = string.Empty;
        private int? _lastAppliedContentVersion;
        private string _notes = string.Empty;
        private bool _isFrozen;
        private string _textContent = string.Empty;
        private List<RecipeEntry> _entries = new();
        private List<RecipePoint> _points = new();

        public Guid RecipeId
        {
            get => _recipeId;
            set => SetProperty(ref _recipeId, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value ?? string.Empty);
        }

        public string Code
        {
            get => _code;
            set => SetProperty(ref _code, value ?? string.Empty);
        }

        public RecipeScope Scope
        {
            get => _scope;
            set => SetProperty(ref _scope, value);
        }

        public int ContentVersion
        {
            get => _contentVersion;
            set => SetProperty(ref _contentVersion, value);
        }

        public DateTime CreatedAtUtc
        {
            get => _createdAtUtc;
            set => SetProperty(ref _createdAtUtc, value);
        }

        public string CreatedBy
        {
            get => _createdBy;
            set => SetProperty(ref _createdBy, value ?? string.Empty);
        }

        public DateTime? UpdatedAtUtc
        {
            get => _updatedAtUtc;
            set => SetProperty(ref _updatedAtUtc, value);
        }

        public string UpdatedBy
        {
            get => _updatedBy;
            set => SetProperty(ref _updatedBy, value ?? string.Empty);
        }

        public DateTime? LastAppliedAtUtc
        {
            get => _lastAppliedAtUtc;
            set => SetProperty(ref _lastAppliedAtUtc, value);
        }

        public string LastAppliedBy
        {
            get => _lastAppliedBy;
            set => SetProperty(ref _lastAppliedBy, value ?? string.Empty);
        }

        public int? LastAppliedContentVersion
        {
            get => _lastAppliedContentVersion;
            set => SetProperty(ref _lastAppliedContentVersion, value);
        }

        public string Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value ?? string.Empty);
        }

        /// <summary>
        /// 文本配方形态（如颜色检测的「产品名 = 颜色1,颜色2」表）。
        ///
        /// 为什么不是一个新资产类型
        /// ---------
        /// 参数配方（<see cref="Entries"/>/<see cref="Points"/>）与文本配方是同一件东西的两种
        /// 表达：都是"被生产引用、要冻结/版本化/审计的内容"。另开一类资产会让资产页多一个
        /// 页签、导入导出各多一条分支，而冻结/归档/默认/版本这些逻辑完全一样。
        /// 所以用同一个类型 + 一个可空内容字段区分：<b>TextContent 非空 = 文本配方</b>，
        /// 此时 Entries/Points 应为空（两者同时有内容属于配置错误）。
        /// </summary>
        public string TextContent
        {
            get => _textContent;
            set => SetProperty(ref _textContent, value ?? string.Empty);
        }

        /// <summary>
        /// 冻结：冻结后内容不应再改，只能解冻后编辑。与标定的 <c>IsLocked</c>、
        /// 文件资产的 <c>IsFrozen</c> 同一语义，字段名沿用各自既有命名。
        /// </summary>
        public bool IsFrozen
        {
            get => _isFrozen;
            set => SetProperty(ref _isFrozen, value);
        }

        public List<RecipeEntry> Entries
        {
            get => _entries;
            set => SetProperty(ref _entries, value ?? new());
        }

        public List<RecipePoint> Points
        {
            get => _points;
            set => SetProperty(ref _points, value ?? new());
        }

        [JsonIgnore]
        public int EntryCount => Entries?.Count ?? 0;

        [JsonIgnore]
        public int PointCount => Points?.Count ?? 0;

        [JsonIgnore]
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "（未命名配方）" : Name;

        [JsonIgnore]
        public string DisplayVersion => $"v{ContentVersion}";

        /// <summary>值集发生实质变化：递增内容版本并记录修改人/时间。</summary>
        public void TouchEntries(string? actor)
        {
            ContentVersion = Math.Max(1, ContentVersion) + 1;
            UpdatedAtUtc = DateTime.UtcNow;
            UpdatedBy = actor ?? string.Empty;
        }

        /// <summary>
        /// 资产版本快照（JSON）。**唯一出处**：宿主写版本历史与逐字段差异都读这一份，
        /// 两处各写一份形状迟早漂移，diff 会开始报"字段被移除"这种假变化。
        /// </summary>
        public string CreateAssetSnapshot()
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                Name,
                Code,
                Scope = Scope.ToString(),
                ContentVersion,
                IsFrozen,
                TextContent,
                Entries = Entries?.Select(e => new
                {
                    Kind = e.Kind.ToString(),
                    e.TargetId,
                    e.Key,
                    Value = e.Value?.ToString(Newtonsoft.Json.Formatting.None),
                }),
                Points = Points?.Select(p => new
                {
                    p.AxisId,
                    p.Index,
                    p.PositionMm,
                    p.SpeedMmPerS,
                    p.AccelMmPerS2,
                }),
            }, Newtonsoft.Json.Formatting.None);
        }

        public RecipeEntry? FindEntry(RecipeTargetKind kind, Guid targetId, string key)
            => Entries?.FirstOrDefault(e => e != null
                && e.Kind == kind
                && e.TargetId == targetId
                && string.Equals(e.Key ?? string.Empty, key ?? string.Empty, StringComparison.Ordinal));
    }

    /// <summary>
    /// 配方 JSON 口径：不写 $type、不执行任意类型构造。
    /// 配方值只存规范 JToken；DTO 类型在 <c>VisionMaster.Models</c>，binder 白名单放行。
    /// </summary>
    public static class RecipeJson
    {
        public static readonly JsonSerializerSettings Settings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            TypeNameHandling = TypeNameHandling.None,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            Converters = { new StringEnumConverter() },
        };

        public static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);
    }

    /// <summary>
    /// 类型键与值编解码。规则集中在这里，服务/断言/导入导出共用一份，
    /// 避免“捕获时一个口径、应用时另一个口径”。
    /// </summary>
    public static class RecipeTypeKey
    {
        /// <summary>规范化类型键：程序集无关、泛型递归展开、无 Version/PublicKeyToken。</summary>
        public static string Normalize(Type? type)
        {
            if (type == null) return string.Empty;
            if (type.IsArray)
                return Normalize(type.GetElementType()) + "[]";

            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
                return "System.Nullable`1[" + Normalize(underlying) + "]";

            if (type.IsGenericType)
            {
                var def = type.GetGenericTypeDefinition();
                var defName = def.FullName ?? def.Name;
                var args = string.Join(",", type.GetGenericArguments().Select(Normalize));
                return defName + "[" + args + "]";
            }

            return type.FullName ?? type.Name;
        }

        /// <summary>
        /// 把规范化类型键解析回 <see cref="Type"/>（只用于兜底/兼容判断；解析不到返回 null，不猜）。
        /// 支持基元、枚举（程序集扫描）、数组、List/ObservableCollection/Nullable 泛型。
        /// </summary>
        public static Type? TryResolveType(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;

            var known = key switch
            {
                "System.Boolean" => typeof(bool),
                "System.Byte" => typeof(byte),
                "System.SByte" => typeof(sbyte),
                "System.Int16" => typeof(short),
                "System.UInt16" => typeof(ushort),
                "System.Int32" => typeof(int),
                "System.UInt32" => typeof(uint),
                "System.Int64" => typeof(long),
                "System.UInt64" => typeof(ulong),
                "System.Single" => typeof(float),
                "System.Double" => typeof(double),
                "System.Decimal" => typeof(decimal),
                "System.String" => typeof(string),
                "System.Char" => typeof(char),
                "System.DateTime" => typeof(DateTime),
                "System.Guid" => typeof(Guid),
                "System.TimeSpan" => typeof(TimeSpan),
                "System.Object" => typeof(object),
                _ => null,
            };
            if (known != null) return known;

            if (key.EndsWith("[]", StringComparison.Ordinal))
            {
                var element = TryResolveType(key.Substring(0, key.Length - 2));
                return element?.MakeArrayType();
            }

            var open = key.IndexOf('[');
            if (open > 0 && key.EndsWith("]", StringComparison.Ordinal))
            {
                var head = key.Substring(0, open);
                var inner = key.Substring(open + 1, key.Length - open - 2);
                var element = TryResolveType(inner);
                if (element == null) return null;

                var definition = head switch
                {
                    "System.Collections.Generic.List`1" => typeof(List<>),
                    "System.Collections.ObjectModel.ObservableCollection`1" => typeof(ObservableCollection<>),
                    "System.Collections.Generic.IList`1" => typeof(IList<>),
                    "System.Collections.Generic.ICollection`1" => typeof(ICollection<>),
                    "System.Collections.Generic.IEnumerable`1" => typeof(IEnumerable<>),
                    "System.Collections.Generic.IReadOnlyList`1" => typeof(IReadOnlyList<>),
                    "System.Collections.Generic.IReadOnlyCollection`1" => typeof(IReadOnlyCollection<>),
                    "System.Nullable`1" => typeof(Nullable<>),
                    _ => null,
                };

                if (definition != null)
                {
                    try { return definition.MakeGenericType(element); }
                    catch { return null; }
                }
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(key, throwOnError: false);
                if (type != null) return type;
            }

            return null;
        }

        /// <summary>类型图是否封闭：只允许基元/枚举/集合/白名单命名空间下的可 JSON 往返对象。</summary>
        public static bool IsSupportedType(Type? type, out string reason)
        {
            return IsSupportedType(type, 0, new HashSet<Type>(), out reason);
        }

        private static bool IsSupportedType(Type? type, int depth, HashSet<Type> visited, out string reason)
        {
            reason = string.Empty;
            if (type == null) { reason = "类型为空"; return false; }
            if (depth > 12) { reason = "类型嵌套过深"; return false; }

            // 元数据不可用时的兜底：JToken 本身就是规范 JSON，允许原样捕获/应用。
            if (type == typeof(JToken) || type == typeof(JValue)
                || type == typeof(JArray) || type == typeof(JObject))
                return true;

            if (type == typeof(object)) { reason = "object 槽位无法确定目标类型"; return false; }
            if (type == typeof(void)) { reason = "void 不是值类型"; return false; }
            if (type == typeof(IntPtr) || type == typeof(UIntPtr)) { reason = "指针类型不可序列化"; return false; }
            if (typeof(IDisposable).IsAssignableFrom(type)) { reason = "IDisposable 类型不可进配方"; return false; }
            if (type.Namespace != null && type.Namespace.StartsWith("HalconDotNet", StringComparison.Ordinal))
            {
                reason = "HALCON 句柄/图像类型不可进配方";
                return false;
            }

            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
                || type == typeof(DateTime) || type == typeof(Guid) || type == typeof(TimeSpan))
                return true;

            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
                return IsSupportedType(underlying, depth + 1, visited, out reason);

            if (type.IsArray)
                return IsSupportedType(type.GetElementType(), depth + 1, visited, out reason);

            if (type.IsGenericType)
            {
                var def = type.GetGenericTypeDefinition();
                if (def == typeof(List<>) || def == typeof(ObservableCollection<>)
                    || def == typeof(IList<>) || def == typeof(ICollection<>)
                    || def == typeof(IEnumerable<>) || def == typeof(IReadOnlyList<>)
                    || def == typeof(IReadOnlyCollection<>))
                {
                    return IsSupportedType(type.GetGenericArguments()[0], depth + 1, visited, out reason);
                }

                reason = "不支持的泛型容器：" + (def.FullName ?? def.Name);
                return false;
            }

            if (type.IsInterface || type.IsAbstract)
            {
                reason = "接口/抽象类型不可进配方";
                return false;
            }

            if (!IsWhitelistedObjectNamespace(type))
            {
                reason = "类型不在配方白名单命名空间：" + (type.FullName ?? type.Name);
                return false;
            }

            if (!visited.Add(type))
                return true;

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                if (prop.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                if (!prop.CanRead || !prop.CanWrite) continue;
                if (!IsSupportedType(prop.PropertyType, depth + 1, visited, out reason))
                {
                    reason = (prop.Name + "：" + reason);
                    return false;
                }
            }

            return true;
        }

        private static bool IsWhitelistedObjectNamespace(Type type)
        {
            var ns = type.Namespace ?? string.Empty;
            return ns == "VisionMaster.Models"
                || ns.StartsWith("VisionMaster.Models.", StringComparison.Ordinal)
                || ns == "VisionMaster.Communications"
                || ns.StartsWith("VisionMaster.Communications.", StringComparison.Ordinal)
                || ns == "VisionMaster.Scada"
                || ns.StartsWith("VisionMaster.Scada.", StringComparison.Ordinal)
                || ns == "Plugin"
                || ns.StartsWith("Plugin.", StringComparison.Ordinal);
        }

        /// <summary>把值转成规范 JToken；枚举写成员名，NaN/Inf 直接拒绝。</summary>
        public static bool TryToCanonicalToken(object? value, Type declaredType, out JToken? token, out string error)
        {
            token = null;
            error = string.Empty;

            if (value == null)
            {
                token = JValue.CreateNull();
                return true;
            }

            var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
            if (type.IsEnum)
            {
                var name = value is string s ? s : Enum.GetName(type, value);
                if (name == null || !Enum.IsDefined(type, value))
                {
                    error = $"枚举值「{value}」不在 {type.Name} 的已定义成员内";
                    return false;
                }
                token = new JValue(name);
                return true;
            }

            if (value is double d && (double.IsNaN(d) || double.IsInfinity(d)))
            {
                error = "NaN/Infinity 不能进配方";
                return false;
            }
            if (value is float f && (float.IsNaN(f) || float.IsInfinity(f)))
            {
                error = "NaN/Infinity 不能进配方";
                return false;
            }

            try
            {
                if (value is JToken jt)
                {
                    token = jt.DeepClone();
                    return true;
                }

                token = JToken.FromObject(value, RecipeJson.Serializer);
                return true;
            }
            catch (Exception ex)
            {
                error = $"值无法序列化为 JSON：{ex.Message}";
                return false;
            }
        }

        /// <summary>把规范 JToken 转成目标声明类型；枚举做 IsDefined 校验。</summary>
        public static bool TryConvertToDeclared(JToken? token, Type declaredType, out object? value, out string error)
        {
            value = null;
            error = string.Empty;

            if (token == null || token.Type == JTokenType.Null)
                return true;

            var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

            if (type.IsEnum)
            {
                if (token.Type == JTokenType.String)
                {
                    var text = token.ToString();
                    if (!Enum.IsDefined(type, text))
                    {
                        error = $"枚举成员「{text}」不在 {type.Name} 内";
                        return false;
                    }
                    value = Enum.Parse(type, text);
                    return true;
                }

                if (token.Type == JTokenType.Integer)
                {
                    var raw = token.ToObject<long>();
                    var underlying = Enum.GetUnderlyingType(type);
                    var converted = Convert.ChangeType(raw, underlying);
                    var enumValue = Enum.ToObject(type, converted);
                    if (!Enum.IsDefined(type, enumValue))
                    {
                        error = $"枚举数值「{raw}」不在 {type.Name} 内";
                        return false;
                    }
                    value = enumValue;
                    return true;
                }

                error = $"枚举值必须是成员名或整数，实际是 {token.Type}";
                return false;
            }

            if (type == typeof(JToken) || type == typeof(object))
            {
                value = token.DeepClone();
                return true;
            }

            try
            {
                value = ValueConverter.Convert(token, type);
                return true;
            }
            catch (Exception ex)
            {
                error = $"值无法转换为 {type.Name}：{ex.Message}";
                return false;
            }
        }

        /// <summary>值比较：先转成声明类型再比，避免 JToken/CLR 文本差异造成假偏离。</summary>
        public static bool ValuesEqual(JToken? left, JToken? right, Type declaredType)
        {
            var l = left ?? JValue.CreateNull();
            var r = right ?? JValue.CreateNull();
            if (JToken.DeepEquals(l, r)) return true;

            if (TryConvertToDeclared(l, declaredType, out var lv, out _)
                && TryConvertToDeclared(r, declaredType, out var rv, out _))
            {
                return Equals(lv, rv);
            }

            return false;
        }

        /// <summary>捕获类型与当前类型是否兼容（v1：同键 / 数值放宽 / 集合同形状）。</summary>
        public static bool IsDeclaredTypeCompatible(string capturedKey, string currentKey)
        {
            if (string.IsNullOrWhiteSpace(capturedKey) || string.IsNullOrWhiteSpace(currentKey))
                return false;
            if (string.Equals(capturedKey, currentKey, StringComparison.Ordinal))
                return true;

            var capturedRank = NumericRank(capturedKey);
            var currentRank = NumericRank(currentKey);
            if (capturedRank >= 0 && currentRank >= 0)
                return currentRank >= capturedRank;

            // List<T> / ObservableCollection<T> / T[] 视为同形状；元素类型必须一致。
            var capturedElement = CollectionElementKey(capturedKey);
            var currentElement = CollectionElementKey(currentKey);
            if (capturedElement != null && currentElement != null)
                return string.Equals(capturedElement, currentElement, StringComparison.Ordinal);

            return false;
        }

        private static int NumericRank(string key) => key switch
        {
            "System.Byte" or "System.SByte" => 0,
            "System.Int16" or "System.UInt16" => 1,
            "System.Int32" or "System.UInt32" => 2,
            "System.Int64" or "System.UInt64" => 3,
            "System.Single" => 4,
            "System.Double" => 5,
            _ => -1,
        };

        private static string? CollectionElementKey(string key)
        {
            if (key.EndsWith("[]", StringComparison.Ordinal))
                return key.Substring(0, key.Length - 2);

            var open = key.IndexOf('[');
            if (open < 0 || !key.EndsWith("]", StringComparison.Ordinal))
                return null;

            var head = key.Substring(0, open);
            if (head != "System.Collections.Generic.List`1"
                && head != "System.Collections.ObjectModel.ObservableCollection`1"
                && head != "System.Collections.Generic.IList`1"
                && head != "System.Collections.Generic.ICollection`1"
                && head != "System.Collections.Generic.IEnumerable`1"
                && head != "System.Collections.Generic.IReadOnlyList`1"
                && head != "System.Collections.Generic.IReadOnlyCollection`1")
                return null;

            return key.Substring(open + 1, key.Length - open - 2);
        }
    }

    /// <summary>方案上的配方集合便捷操作。所有“写集合”都收在这里，避免多处各自 new。</summary>
    public static class RecipeSolutionExtensions
    {
        public static IReadOnlyList<RecipeDefinition> RecipesOrEmpty(this SolutionModel solution)
            => solution?.Recipes ?? (IReadOnlyList<RecipeDefinition>)Array.Empty<RecipeDefinition>();

        public static RecipeDefinition? FindRecipe(this SolutionModel solution, Guid recipeId)
            => solution?.Recipes?.FirstOrDefault(r => r != null && r.RecipeId == recipeId);

        public static RecipeDefinition? ActiveRecipe(this SolutionModel solution)
        {
            if (solution?.ActiveRecipeId is not Guid id || id == Guid.Empty) return null;
            return solution.FindRecipe(id);
        }

        /// <summary>确保配方集合/结构版本存在；首次实体化时把方案格式版本抬到 2.0。</summary>
        public static List<RecipeDefinition> EnsureRecipes(this SolutionModel solution)
        {
            if (solution == null) throw new ArgumentNullException(nameof(solution));
            solution.Recipes ??= new List<RecipeDefinition>();
            solution.RecipeSchemaVersion ??= 1;
            if (solution.Version < 2.0) solution.Version = 2.0;
            return solution.Recipes;
        }

        public static void ClearActiveRecipe(this SolutionModel solution)
        {
            if (solution == null) return;
            solution.ActiveRecipeId = null;
        }
    }
}
