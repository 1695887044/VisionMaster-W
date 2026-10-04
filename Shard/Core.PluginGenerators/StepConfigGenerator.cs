using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Core.PluginGenerators
{
    /// <summary>
    /// [StepConfig] partial 属性源生成器（partial property 方案，完整版含钩子）。
    ///
    /// 插件源码侧只写声明：
    ///   [StepConfig, DefaultValue(DefaultMinScore)]
    ///   public partial double MinScore { get; set; }
    ///
    ///   partial void OnMinScoreChanging(ref double value) =&gt; value = Math.Clamp(value, 0.01, 1); // 可选：归一化入参
    ///   partial void OnMinScoreChanged(double value) =&gt; UpdateOverlay();                          // 可选：变更后副作用
    ///
    /// 生成物（同一 partial class，编译期展开、运行期零痕迹）：
    ///   private double _minScore = DefaultMinScore;
    ///   partial void OnMinScoreChanging(ref double value);
    ///   partial void OnMinScoreChanged(double value);
    ///   public partial double MinScore
    ///   {
    ///       get =&gt; _minScore;
    ///       set
    ///       {
    ///           var __value = value;
    ///           OnMinScoreChanging(ref __value);            // 未实现时连同调用一起被编译器消除
    ///           if (SetProperty(ref _minScore, __value))
    ///               OnMinScoreChanged(__value);             // 仅在值真的变化后触发（与手写 SetProperty 语义一致）
    ///       }
    ///   }
    ///
    /// 兼容性硬约束（保证 .vms 与界面不受影响）：
    ///   · 属性名不变（= InputValues 持久化键名）、类型不变、[StepConfig] 仍在属性上（基类按"public 实例属性 + 特性"反射发现）；
    ///   · 只处理"定义声明"（无实现体）；用户已手写另一半实现时跳过（让给用户）；
    ///   · 不满足约定一律报编译 Error（CPG0001~CPG0005），不静默跳过 —— 生成器失效必须"响"。
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class StepConfigGenerator : IIncrementalGenerator
    {
        private const string StepConfigAttributeName = "Core.Interfaces.StepConfigAttribute";
        private const string DefaultValueAttributeName = "System.ComponentModel.DefaultValueAttribute";

        /// <summary>生成代码里的类型显示格式：简写内建类型 + 全限定 + 可空标注（生成文件不需要 using）。</summary>
        private static readonly SymbolDisplayFormat TypeFormat =
            SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        private static readonly DiagnosticDescriptor ShapeDescriptor = new(
            id: "CPG0001",
            title: "不支持的 [StepConfig] partial 属性形态",
            messageFormat: "{0}",
            category: "Core.PluginGenerators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ModifierDescriptor = new(
            id: "CPG0002",
            title: "不支持的属性修饰符",
            messageFormat: "{0}",
            category: "Core.PluginGenerators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor GetSetDescriptor = new(
            id: "CPG0003",
            title: "[StepConfig] partial 属性需要 get/set",
            messageFormat: "{0}",
            category: "Core.PluginGenerators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor FieldTakenDescriptor = new(
            id: "CPG0004",
            title: "生成的后备字段名被占用",
            messageFormat: "{0}",
            category: "Core.PluginGenerators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor DefaultValueDescriptor = new(
            id: "CPG0005",
            title: "[DefaultValue] 形态不支持",
            messageFormat: "{0}",
            category: "Core.PluginGenerators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var models = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    StepConfigAttributeName,
                    predicate: static (node, _) => IsPartialDefinitionProperty(node),
                    transform: static (ctx, ct) => Extract(ctx, ct))
                .Where(static m => m is not null)
                .Select(static (m, _) => m!)
                .Collect();

            context.RegisterSourceOutput(models, static (spc, items) => Emit(spc, items));
        }

        /// <summary>只认"定义声明"：带 partial、有访问器列表、且所有访问器都没有实现体。</summary>
        private static bool IsPartialDefinitionProperty(SyntaxNode node)
        {
            if (node is not PropertyDeclarationSyntax property) return false;
            if (!property.Modifiers.Any(SyntaxKind.PartialKeyword)) return false;
            if (property.AccessorList is null || property.AccessorList.Accessors.Count == 0) return false;

            foreach (var accessor in property.AccessorList.Accessors)
            {
                if (accessor.Body is not null || accessor.ExpressionBody is not null) return false;
            }
            return true;
        }

        private static PropertyModel? Extract(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
        {
            if (ctx.TargetNode is not PropertyDeclarationSyntax property) return null;
            if (ctx.SemanticModel.GetDeclaredSymbol(property, ct) is not IPropertySymbol symbol) return null;

            // 已有手写实现（同一属性的另一半声明）→ 让给用户，不生成
            if (symbol.DeclaringSyntaxReferences.Length != 1) return null;

            var type = symbol.ContainingType;
            var model = new PropertyModel
            {
                Order = property.SpanStart,
                PropertyName = symbol.Name,
                PropertyType = symbol.Type.ToDisplayString(TypeFormat),
                IsValueType = symbol.Type.IsValueType,
                Namespace = type.ContainingNamespace is { IsGlobalNamespace: false }
                    ? type.ContainingNamespace.ToDisplayString()
                    : string.Empty,
                ClassName = type.Name,
                ClassAccessibility = AccessibilityText(type.DeclaredAccessibility),
                Accessibility = AccessibilityText(symbol.DeclaredAccessibility),
            };

            var location = LocationInfo.From(property.Identifier.GetLocation());

            if (type.ContainingType is not null || type.TypeParameters.Length > 0
                || type.IsRecord || type.TypeKind != TypeKind.Class)
            {
                return model.Error(ShapeDescriptor, location,
                    $"属性「{symbol.Name}」所在类型「{type.Name}」不在支持范围内：源生成器只支持顶层、非泛型、非 record 的 class（插件类形态）。");
            }

            foreach (var modifier in property.Modifiers)
            {
                if (modifier.IsKind(SyntaxKind.PublicKeyword) || modifier.IsKind(SyntaxKind.InternalKeyword)
                    || modifier.IsKind(SyntaxKind.ProtectedKeyword) || modifier.IsKind(SyntaxKind.PrivateKeyword))
                    continue;
                if (modifier.IsKind(SyntaxKind.PartialKeyword)) continue;

                return model.Error(ModifierDescriptor, location,
                    $"属性「{symbol.Name}」使用了不支持的修饰符「{modifier.Text}」：只支持访问修饰符 + partial；static / virtual / override / new 等形态请保持手写。");
            }

            var hasGet = false;
            var hasSet = false;
            foreach (var accessor in property.AccessorList!.Accessors)
            {
                if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) hasGet = true;
                else if (accessor.IsKind(SyntaxKind.SetAccessorDeclaration)) hasSet = true;
            }

            if (!hasGet || !hasSet)
            {
                return model.Error(GetSetDescriptor, location,
                    $"属性「{symbol.Name}」需要同时声明 get; 与 set;（只读 / 只写配置请保持手写）。");
            }

            model.FieldName = "_" + char.ToLowerInvariant(symbol.Name[0]) + symbol.Name.Substring(1);
            foreach (var member in type.GetMembers())
            {
                if (member.Name == model.FieldName)
                {
                    return model.Error(FieldTakenDescriptor, location,
                        $"属性「{symbol.Name}」将生成后备字段「{model.FieldName}」，但该名字已被现有成员占用：请先删除旧的后备字段（或给旧成员改名）。");
                }
            }

            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != DefaultValueAttributeName) continue;

                var attributeSyntax = attribute.ApplicationSyntaxReference?.GetSyntax(ct) as AttributeSyntax;
                if (attributeSyntax?.ArgumentList is null || attributeSyntax.ArgumentList.Arguments.Count != 1)
                {
                    return model.Error(DefaultValueDescriptor, location,
                        $"属性「{symbol.Name}」的 [DefaultValue] 只支持单参数常量形式（如 [DefaultValue(0.5)]、[DefaultValue(\"\")]、[DefaultValue(MyEnum.Value)]）。");
                }

                // 语法文本原样复制（保留 DefaultMinScore 这类常量引用）；null 常量走 default!
                model.DefaultValueExpression =
                    attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].IsNull
                        ? null
                        : attributeSyntax.ArgumentList.Arguments[0].Expression.ToString();
                break;
            }

            return model;
        }

        private static void Emit(SourceProductionContext spc, ImmutableArray<PropertyModel> items)
        {
            if (items.IsDefaultOrEmpty) return;

            foreach (var item in items)
            {
                if (item.ErrorDescriptor is null) continue;
                var location = item.ErrorLocation?.ToLocation() ?? Location.None;
                spc.ReportDiagnostic(Diagnostic.Create(item.ErrorDescriptor, location, item.ErrorMessage));
            }

            var valid = items.Where(static i => i.ErrorDescriptor is null);
            foreach (var group in valid.GroupBy(static i => i.GroupKey).OrderBy(static g => g.Key, StringComparer.Ordinal))
            {
                var members = group.OrderBy(static m => m.Order).ToList();
                var first = members[0];
                var sb = new StringBuilder();

                sb.AppendLine("// <auto-generated/>");
                sb.AppendLine("// 由 Core.PluginGenerators 源生成器生成：[StepConfig] partial 属性 → 后备字段 + SetProperty + 钩子。");
                sb.AppendLine("// 请勿手工修改；改默认值/副作用请改插件源文件里的 [DefaultValue] 与 On<属性名>Changing/Changed 钩子。");
                sb.AppendLine("#nullable enable");
                sb.AppendLine();

                var hasNamespace = first.Namespace.Length > 0;
                var typeIndent = hasNamespace ? "    " : string.Empty;
                var memberIndent = hasNamespace ? "        " : "    ";

                if (hasNamespace)
                {
                    sb.Append("namespace ").Append(first.Namespace).AppendLine();
                    sb.AppendLine("{");
                }

                var classAccess = first.ClassAccessibility.Length > 0 ? first.ClassAccessibility + " " : string.Empty;
                sb.Append(typeIndent).Append(classAccess).Append("partial class ").Append(first.ClassName).AppendLine();
                sb.Append(typeIndent).AppendLine("{");

                for (var i = 0; i < members.Count; i++)
                {
                    AppendMember(sb, members[i], memberIndent);
                    if (i < members.Count - 1) sb.AppendLine();
                }

                sb.Append(typeIndent).AppendLine("}");
                if (hasNamespace) sb.AppendLine("}");

                var hintName = (hasNamespace ? first.Namespace + "." : string.Empty)
                               + first.ClassName + ".StepConfig.g.cs";
                spc.AddSource(hintName, SourceText.From(sb.ToString(), Encoding.UTF8));
            }
        }

        private static void AppendMember(StringBuilder sb, PropertyModel model, string indent)
        {
            var defaultValue = model.DefaultValueExpression ?? (model.IsValueType ? "default" : "default!");
            var access = model.Accessibility.Length > 0 ? model.Accessibility + " " : string.Empty;

            sb.Append(indent).Append("private ").Append(model.PropertyType).Append(' ')
              .Append(model.FieldName).Append(" = ").Append(defaultValue).AppendLine(";");
            sb.AppendLine();
            sb.Append(indent).Append("partial void On").Append(model.PropertyName).Append("Changing(ref ")
              .Append(model.PropertyType).AppendLine(" value);");
            sb.Append(indent).Append("partial void On").Append(model.PropertyName).Append("Changed(")
              .Append(model.PropertyType).AppendLine(" value);");
            sb.AppendLine();
            sb.Append(indent).Append(access).Append("partial ").Append(model.PropertyType).Append(' ')
              .Append(model.PropertyName).AppendLine();
            sb.Append(indent).AppendLine("{");
            sb.Append(indent).Append("    get => ").Append(model.FieldName).AppendLine(";");
            sb.Append(indent).AppendLine("    set");
            sb.Append(indent).AppendLine("    {");
            sb.Append(indent).AppendLine("        var __value = value;");
            sb.Append(indent).Append("        On").Append(model.PropertyName).AppendLine("Changing(ref __value);");
            sb.Append(indent).Append("        if (SetProperty(ref ").Append(model.FieldName).AppendLine(", __value))");
            sb.Append(indent).Append("            On").Append(model.PropertyName).AppendLine("Changed(__value);");
            sb.Append(indent).AppendLine("    }");
            sb.Append(indent).AppendLine("}");
        }

        private static string AccessibilityText(Accessibility accessibility)
        {
            switch (accessibility)
            {
                case Accessibility.Public: return "public";
                case Accessibility.Internal: return "internal";
                case Accessibility.Protected: return "protected";
                case Accessibility.ProtectedOrInternal: return "protected internal";
                case Accessibility.ProtectedAndInternal: return "private protected";
                default: return string.Empty;
            }
        }

        /// <summary>可缓存的诊断位置（Location 本身不可比较，增量管线里换成可比较的纯数据）。</summary>
        private readonly struct LocationInfo : IEquatable<LocationInfo>
        {
            private readonly string _filePath;
            private readonly int _spanStart;
            private readonly int _spanLength;
            private readonly int _startLine;
            private readonly int _startCharacter;
            private readonly int _endLine;
            private readonly int _endCharacter;

            private LocationInfo(string filePath, int spanStart, int spanLength,
                int startLine, int startCharacter, int endLine, int endCharacter)
            {
                _filePath = filePath;
                _spanStart = spanStart;
                _spanLength = spanLength;
                _startLine = startLine;
                _startCharacter = startCharacter;
                _endLine = endLine;
                _endCharacter = endCharacter;
            }

            public static LocationInfo From(Location location)
            {
                var lineSpan = location.GetLineSpan();
                return new LocationInfo(
                    location.SourceTree?.FilePath ?? string.Empty,
                    location.SourceSpan.Start,
                    location.SourceSpan.Length,
                    lineSpan.StartLinePosition.Line,
                    lineSpan.StartLinePosition.Character,
                    lineSpan.EndLinePosition.Line,
                    lineSpan.EndLinePosition.Character);
            }

            public Location ToLocation() => Location.Create(
                _filePath,
                new TextSpan(_spanStart, _spanLength),
                new LinePositionSpan(
                    new LinePosition(_startLine, _startCharacter),
                    new LinePosition(_endLine, _endCharacter)));

            public bool Equals(LocationInfo other) =>
                _filePath == other._filePath
                && _spanStart == other._spanStart
                && _spanLength == other._spanLength
                && _startLine == other._startLine
                && _startCharacter == other._startCharacter
                && _endLine == other._endLine
                && _endCharacter == other._endCharacter;

            public override bool Equals(object? obj) => obj is LocationInfo other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = _filePath.GetHashCode();
                    hash = (hash * 397) ^ _spanStart;
                    hash = (hash * 397) ^ _spanLength;
                    hash = (hash * 397) ^ _startLine;
                    hash = (hash * 397) ^ _startCharacter;
                    hash = (hash * 397) ^ _endLine;
                    hash = (hash * 397) ^ _endCharacter;
                    return hash;
                }
            }
        }

        private sealed class PropertyModel
        {
            public int Order;
            public string Namespace = string.Empty;
            public string ClassName = string.Empty;
            public string ClassAccessibility = "public";
            public string PropertyName = string.Empty;
            public string PropertyType = string.Empty;
            public string Accessibility = string.Empty;
            public string FieldName = string.Empty;
            public bool IsValueType;
            public string? DefaultValueExpression;

            public DiagnosticDescriptor? ErrorDescriptor;
            public string? ErrorMessage;
            public LocationInfo? ErrorLocation;

            public string GroupKey => Namespace + "|" + ClassName;

            public PropertyModel Error(DiagnosticDescriptor descriptor, LocationInfo location, string message)
            {
                ErrorDescriptor = descriptor;
                ErrorMessage = message;
                ErrorLocation = location;
                return this;
            }
        }
    }
}
