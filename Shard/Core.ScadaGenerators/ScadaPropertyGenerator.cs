using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Core.ScadaGenerators
{
    /// <summary>
    /// [ScadaProperty] partial 属性源生成器：一处声明产出三样东西。
    ///
    /// 图元源码侧只写声明：
    ///   [ScadaProperty(Key = "OffColor", DisplayName = "熄灭色", Kind = ElementPropertyKind.Color,
    ///                  Group = "状态", DefaultValue = "#FF3A3A3A", IsBindable = true)]
    ///   public partial Brush? OffColor { get; set; }
    ///
    ///   partial void OnOffColorPropertyChanged() =&gt; UpdateLampBrush();   // 可选副作用
    ///
    /// 生成物（同一 partial class，编译期展开、运行期零反射）：
    ///   ① public static readonly DependencyProperty OffColorProperty（含 FrameworkPropertyMetadata）；
    ///   ② public Brush? OffColor 的 CLR 包装（get =&gt; GetValue / set =&gt; SetValue）；
    ///   ③ AddOwnGeneratedProperties(List&lt;ElementPropertyDescriptor&gt;) —— 描述符条目。
    ///
    /// 为什么第 ③ 样必须一起生成
    /// ---------
    /// 仓库原先的写法是"控件类手写 DP + BuiltInElements 手写一条描述符"，两处都要写同一个 Key、
    /// 同一个默认值、同一个 TargetProperty。实测 17 个图元 187 条属性声明，其中 8 个通用属性
    /// 在 15 个图元里各抄一遍（99 处 / 630 行）。而两处写岔了<b>不报任何错</b>：
    /// Key 不一致 = 属性面板改了不生效；TargetProperty 漏写 = 值只进属性袋不落控件（静默）。
    /// 把描述符交给生成器，Key / 默认值 / 目标属性就只有一处事实来源。
    ///
    /// 兼容性硬约束（保证 .vms 与界面不受影响）：
    ///   · Key 是写进 .vms 的长期标识，必须显式给（不许默认成属性名）——属性改名而 Key 不改是允许的，
    ///     但"忘了给 Key"会让全类属性的落盘键默默跟着属性名走，那正是要防的静默失败；
    ///   · 属性名不变（默认属性面板标签来源）、类型不变、特性仍在属性上；
    ///   · 只处理"定义声明"（无实现体）；用户已手写另一半实现时跳过（让给用户）；
    ///   · 不满足约定一律报编译 Error（SPG0001~SPG0005），不静默跳过 —— 生成器失效必须"响"。
    ///
    /// 与 Core.PluginGenerators（[StepConfig]）的关系：管线骨架、诊断纪律、"生成器不引用被生成工程"
    /// 三条照搬；生成内容完全不同——那边产出"后备字段 + SetProperty + 钩子"（POCO 属性），
    /// 这边产出"依赖属性 + CLR 包装 + 描述符条目"（WPF 控件属性）。
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class ScadaPropertyGenerator : IIncrementalGenerator
    {
        /// <summary>特性全名。生成器<b>不引用</b> Scada.Controls（会形成反向引用），只按全名相认。</summary>
        private const string AttributeName = "VisionMaster.Scada.Controls.ScadaPropertyAttribute";

        /// <summary>Min/Max 的哨兵值：特性参数必须是编译期常量，double.NegativeInfinity 不能用。</summary>
        private const double SentinelMin = -1e308;
        private const double SentinelMax = 1e308;

        /// <summary>生成代码里的类型显示格式：全限定 + 可空标注（生成文件不需要 using）。</summary>
        private static readonly SymbolDisplayFormat TypeFormat =
            SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        private static readonly DiagnosticDescriptor ShapeDescriptor = new(
            id: "SPG0001",
            title: "不支持的 [ScadaProperty] 形态",
            messageFormat: "{0}",
            category: "Core.ScadaGenerators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ModifierDescriptor = new(
            id: "SPG0002",
            title: "不支持的属性修饰符",
            messageFormat: "{0}",
            category: "Core.ScadaGenerators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor GetSetDescriptor = new(
            id: "SPG0003",
            title: "[ScadaProperty] partial 属性需要 get/set",
            messageFormat: "{0}",
            category: "Core.ScadaGenerators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor NameTakenDescriptor = new(
            id: "SPG0004",
            title: "生成的成员名被占用",
            messageFormat: "{0}",
            category: "Core.ScadaGenerators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ArgumentDescriptor = new(
            id: "SPG0005",
            title: "[ScadaProperty] 参数不合法",
            messageFormat: "{0}",
            category: "Core.ScadaGenerators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ShadowDescriptor = new(
            id: "SPG0006",
            title: "[ScadaProperty] 遮蔽了基类已有的依赖属性",
            messageFormat: "{0}",
            category: "Core.ScadaGenerators",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var models = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    AttributeName,
                    // 谓词刻意只判"是不是属性声明"，把"必须是 partial 定义声明"留给 Extract 去判并报错。
                    //
                    // 为什么不能在这里过滤掉非 partial 属性：谓词返回 false 的节点会被整条管线丢掉，
                    // 于是 `[ScadaProperty] public double X { get; set; }`（漏了 partial）会变成
                    // **完全静默**——属性照旧编译（它只是个普通自动属性）、依赖属性没生成、
                    // 描述符里少一条，界面上看不出任何异常。这正是本生成器要防的静默失败，
                    // 所以必须让 Extract 看见它并报 SPG0001。
                    predicate: static (node, _) => node is PropertyDeclarationSyntax,
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
                // typeof(...) 上不许带可空引用标注（Brush? 会报 CS8639），
                // 而依赖属性的"类型"本来就该是剥掉标注的那个：可空性是编译期注解，
                // 运行期 DP 类型只有 Brush。值类型（含 int?）原样保留。
                TypeOfType = symbol.Type.IsReferenceType
                    ? symbol.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat)
                    : symbol.Type.ToDisplayString(TypeFormat),
                Accessibility = AccessibilityText(symbol.DeclaredAccessibility),
                Namespace = type.ContainingNamespace is { IsGlobalNamespace: false }
                    ? type.ContainingNamespace.ToDisplayString()
                    : string.Empty,
                ClassName = type.Name,
                ClassAccessibility = AccessibilityText(type.DeclaredAccessibility),
            };

            var location = LocationInfo.From(property.Identifier.GetLocation());

            // 谓词放行了全部属性声明，所以"不是 partial 定义声明"要在这里判并报错。
            // 漏掉这条检查的后果是整个特性静默失效（属性照旧编译成普通自动属性，
            // 依赖属性与描述符条目都不生成），见 Initialize 里 predicate 处的注释。
            if (!property.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return model.Error(ShapeDescriptor, location,
                    $"属性「{symbol.Name}」用了 [ScadaProperty] 但不是 partial：" +
                    "生成器要把依赖属性与包装写进属性的另一半实现，请把声明改成 `public partial 类型 名称 { get; set; }`。");
            }

            if (property.AccessorList is null || property.AccessorList.Accessors.Count == 0)
            {
                return model.Error(ShapeDescriptor, location,
                    $"属性「{symbol.Name}」没有访问器列表（[ScadaProperty] 要求 get; set; 形态）。");
            }

            foreach (var accessor in property.AccessorList.Accessors)
            {
                if (accessor.Body is not null || accessor.ExpressionBody is not null)
                {
                    return model.Error(ShapeDescriptor, location,
                        $"属性「{symbol.Name}」的 {accessor.Keyword.Text} 访问器带了实现体：" +
                        "[ScadaProperty] 只处理纯声明（只有 `get;` / `set;`），实现由生成器产出。");
                }
            }

            if (type.ContainingType is not null || type.TypeParameters.Length > 0
                || type.IsRecord || type.TypeKind != TypeKind.Class)
            {
                return model.Error(ShapeDescriptor, location,
                    $"属性「{symbol.Name}」所在类型「{type.Name}」不在支持范围内：源生成器只支持顶层、非泛型、非 record 的 class（图元控件形态）。");
            }

            if (!IsDeclaredPartial(type, ct))
            {
                return model.Error(ShapeDescriptor, location,
                    $"属性「{symbol.Name}」用了 [ScadaProperty]，但类型「{type.Name}」没有声明为 partial：" +
                    "生成器要把依赖属性与包装写进同一个类型的另一半，请把类声明改成 partial。");
            }

            foreach (var modifier in property.Modifiers)
            {
                if (modifier.IsKind(SyntaxKind.PublicKeyword) || modifier.IsKind(SyntaxKind.InternalKeyword)
                    || modifier.IsKind(SyntaxKind.ProtectedKeyword) || modifier.IsKind(SyntaxKind.PrivateKeyword))
                    continue;
                if (modifier.IsKind(SyntaxKind.PartialKeyword)) continue;

                return model.Error(ModifierDescriptor, location,
                    $"属性「{symbol.Name}」使用了不支持的修饰符「{modifier.Text}」：只支持访问修饰符 + partial；" +
                    "static / virtual / override / new 等形态请保持手写。");
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
                    $"属性「{symbol.Name}」需要同时声明 get; 与 set;（只读的推导属性请保持手写：它是显示结果，不是可配属性）。");
            }

            model.PropertyBagKey = symbol.Name;

            if (ReadArguments(ctx, symbol, model, location) is { } error)
                return error;

            foreach (var member in type.GetMembers())
            {
                if (member.Name == model.DependencyPropertyName)
                {
                    return model.Error(NameTakenDescriptor, location,
                        $"属性「{symbol.Name}」将生成依赖属性字段「{model.DependencyPropertyName}」，但该名字已被现有成员占用：" +
                        "请先删除手写的旧依赖属性（或给旧成员改名）。");
                }
            }

            // 遮蔽检查：基类（含间接基类）已有同名依赖属性时不许再生成一份。
            //
            // 为什么这是 Error 而不是放行：生成出来的会是<b>另一个</b>依赖属性，
            // 而 XAML 里的 `{TemplateBinding Fill}` / `{Binding Fill}` 全部绑到子类这一份上，
            // 基类那一份永远不动——表现为"属性面板改了没反应"或"模板绑的值不对"，
            // 且编译期全绿、运行期不报错。这类静默错位正是本生成器要挡的。
            //
            // 想暴露基类已有的外观属性（填色/边框/字号/文字），用 AppearanceProperties /
            // TextProperties 那两个打包助手，它们产出指向基类依赖属性的描述符条目。
            if (FindInheritedProperty(type, model.PropertyName) is { } inherited)
            {
                return model.Error(ShadowDescriptor, location,
                    $"属性「{symbol.Name}」与基类「{inherited.ContainingType.Name}」已有的依赖属性同名：" +
                    "生成器会产出第二个同名依赖属性，遮蔽基类那一份（XAML 绑到新的，基类的永远不动）。" +
                    "请删掉这个 [ScadaProperty] 声明；若要暴露基类的外观属性，" +
                    "用 AppearanceProperties / TextProperties 打包助手产出描述符条目。");
            }

            return model;
        }

        /// <summary>
        /// 沿基类链找一个公开的依赖属性字段（<c>XxxProperty</c>）。
        ///
        /// 只认字段形状（<c>static readonly DependencyProperty</c> + 名字匹配），
        /// 不做"是不是真的 DP"的强校验：真 DP 必然是那个形状，
        /// 而这里判错的代价只是多报一条错（保守方向是对的——宁可要求改名，也不要静默遮蔽）。
        /// </summary>
        private static IFieldSymbol? FindInheritedProperty(INamedTypeSymbol type, string propertyName)
        {
            var fieldName = propertyName + "Property";

            for (var baseType = type.BaseType; baseType is not null && baseType.SpecialType != SpecialType.System_Object;
                 baseType = baseType.BaseType)
            {
                foreach (var member in baseType.GetMembers(fieldName))
                {
                    if (member is IFieldSymbol field && field.IsStatic)
                        return field;
                }
            }

            return null;
        }

        /// <summary>把特性上的具名参数抄进模型；任何一条不合法都返回带错误的模型（由 Emit 上报）。</summary>
        private static PropertyModel? ReadArguments(
            GeneratorAttributeSyntaxContext ctx, IPropertySymbol symbol, PropertyModel model, LocationInfo location)
        {
            foreach (var argument in ctx.Attributes[0].NamedArguments)
            {
                var value = argument.Value;
                switch (argument.Key)
                {
                    case "Key":
                        model.PropertyBagKey = value.Value as string ?? string.Empty;
                        break;
                    case "DisplayName":
                        model.DisplayName = value.Value as string ?? string.Empty;
                        break;
                    case "Kind":
                        model.KindName = EnumMemberName(value);
                        break;
                    case "Group":
                        model.Group = value.Value as string ?? string.Empty;
                        break;
                    case "DefaultValue":
                        model.DefaultValue = value.Value as string ?? string.Empty;
                        break;
                    case "Description":
                        model.Description = value.Value as string;
                        break;
                    case "IsBindable":
                        model.IsBindable = value.Value is true;
                        break;
                    case "AffectsRender":
                        model.AffectsRender = value.Value is true;
                        break;
                    case "Min":
                        model.Min = value.Value is double min ? min : SentinelMin;
                        break;
                    case "Max":
                        model.Max = value.Value is double max ? max : SentinelMax;
                        break;
                    case "Choices":
                        model.Choices = value.Values
                            .Select(static c => c.Value as string ?? string.Empty)
                            .ToImmutableArray();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(model.PropertyBagKey))
            {
                return model.Error(ArgumentDescriptor, location,
                    $"属性「{symbol.Name}」的 [ScadaProperty] 缺少 Key。" +
                    "Key 是写进 .vms 的长期标识，必须显式给：不填就等于让落盘键默默跟着属性名走，改属性名会静默丢配置。");
            }

            if (model.PropertyBagKey!.StartsWith("$", StringComparison.Ordinal))
            {
                return model.Error(ArgumentDescriptor, location,
                    $"属性「{symbol.Name}」的 Key「{model.PropertyBagKey}」以 $ 开头，那是保留几何键（$X/$Y/$Width/$Height/$Rotation/$Name）的地盘：" +
                    "几何属性由 GeometryProperties 统一声明，不要用 [ScadaProperty] 再声明一遍。");
            }

            if (string.IsNullOrWhiteSpace(model.DisplayName))
            {
                return model.Error(ArgumentDescriptor, location,
                    $"属性「{symbol.Name}」的 [ScadaProperty] 缺少 DisplayName（属性面板上的标签）。");
            }

            if (string.IsNullOrWhiteSpace(model.KindName))
            {
                return model.Error(ArgumentDescriptor, location,
                    $"属性「{symbol.Name}」的 [ScadaProperty] 缺少 Kind（属性面板用哪种编辑器）。");
            }

            if (model.KindName == "Choice" && model.Choices.IsDefaultOrEmpty)
            {
                return model.Error(ArgumentDescriptor, location,
                    $"属性「{symbol.Name}」是 Choice 型但没给 Choices（下拉候选项）。");
            }

            if (model.KindName != "Number" && (model.Min != SentinelMin || model.Max != SentinelMax))
            {
                return model.Error(ArgumentDescriptor, location,
                    $"属性「{symbol.Name}」不是 Number 型，不应给 Min/Max（只有数值型有量程）。");
            }

            if (model.KindName == "Number" && model.Min > model.Max)
            {
                return model.Error(ArgumentDescriptor, location,
                    $"属性「{symbol.Name}」的 Min/Max 非法（Min={model.Min}, Max={model.Max}）。");
            }

            return null;
        }

        private static bool IsDeclaredPartial(INamedTypeSymbol type, CancellationToken ct)
        {
            foreach (var reference in type.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(ct) is TypeDeclarationSyntax declaration
                    && declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    return true;
                }
            }

            // 没有语法可查（元数据类型）时放行：报错留给编译器，生成器不越权。
            return type.DeclaringSyntaxReferences.Length == 0;
        }

        private static string? EnumMemberName(TypedConstant constant)
        {
            if (constant.Type is not INamedTypeSymbol enumType || enumType.TypeKind != TypeKind.Enum)
                return null;

            foreach (var member in enumType.GetMembers())
            {
                if (member is IFieldSymbol field && field.HasConstantValue
                    && Equals(field.ConstantValue, constant.Value))
                {
                    return field.Name;
                }
            }

            return null;
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
                sb.AppendLine("// 由 Core.ScadaGenerators 源生成器生成：[ScadaProperty] partial 属性 → 依赖属性 + CLR 包装 + 描述符条目。");
                sb.AppendLine("// 请勿手工修改；改默认值/分组/绑定口径请改图元源文件里的特性参数，改副作用请实现 On<属性名>PropertyChanged 钩子。");
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

                foreach (var member in members)
                    AppendMember(sb, member, memberIndent);

                AppendCatalog(sb, members, memberIndent);

                sb.Append(typeIndent).AppendLine("}");
                if (hasNamespace) sb.AppendLine("}");

                var hintName = (hasNamespace ? first.Namespace + "." : string.Empty)
                               + first.ClassName + ".ScadaProperty.g.cs";
                spc.AddSource(hintName, SourceText.From(sb.ToString(), Encoding.UTF8));
            }
        }

        private static void AppendMember(StringBuilder sb, PropertyModel model, string indent)
        {
            var options = model.AffectsRender
                ? "global::System.Windows.FrameworkPropertyMetadataOptions.AffectsRender"
                : "global::System.Windows.FrameworkPropertyMetadataOptions.None";

            sb.AppendLine();
            sb.Append(indent).Append("/// <summary>").Append(model.DisplayName)
              .Append("（由 [ScadaProperty] 生成的依赖属性）</summary>").AppendLine();
            sb.Append(indent).Append("public static readonly global::System.Windows.DependencyProperty ")
              .Append(model.DependencyPropertyName).AppendLine(" =");
            sb.Append(indent).Append("    global::System.Windows.DependencyProperty.Register(").AppendLine();
            sb.Append(indent).Append("        nameof(").Append(model.PropertyName).Append("), typeof(")
              .Append(model.TypeOfType).Append("), typeof(").Append(model.OwnerTypeName).AppendLine("),");
            sb.Append(indent).AppendLine("        new global::System.Windows.FrameworkPropertyMetadata(");
            sb.Append(indent).Append("            global::VisionMaster.Scada.Controls.ScadaPropertyDefaults.Resolve(typeof(")
              .Append(model.TypeOfType).Append("), \"").Append(Escape(model.DefaultValue)).AppendLine("\"),");
            sb.Append(indent).Append("            ").Append(options).AppendLine(",");
            sb.Append(indent).Append("            static (d, e) => ((").Append(model.OwnerTypeName)
              .Append(")d).").Append(model.ChangedHookName).AppendLine("()));");
            sb.AppendLine();

            sb.Append(indent).Append("/// <summary>").Append(model.DisplayName)
              .Append("（见 ").Append(model.DependencyPropertyName).Append("）</summary>").AppendLine();
            // partial：这里只是"另一半实现"，声明仍在图元源文件里。
            // 漏掉 partial 会得到 CS0102（成员已定义）——正是探针图元在第一次编译时抓到的那条。
            // 访问修饰符跟着源声明走（与声明不一致会得到 CS0262），不是无条件 public。
            sb.Append(indent).Append(model.Accessibility.Length > 0 ? model.Accessibility + " " : string.Empty)
              .Append("partial ").Append(model.PropertyType).Append(' ')
              .Append(model.PropertyName).AppendLine();
            sb.Append(indent).AppendLine("{");
            sb.Append(indent).Append("    get => (").Append(model.PropertyType).Append(")GetValue(")
              .Append(model.DependencyPropertyName).AppendLine(");");
            sb.Append(indent).Append("    set => SetValue(").Append(model.DependencyPropertyName).AppendLine(", value);");
            sb.Append(indent).AppendLine("}");
            sb.AppendLine();

            sb.Append(indent).Append("/// <summary>").Append(model.PropertyName)
              .AppendLine(" 变更后的副作用钩子（可选实现；未实现时连同上面的调用一起被编译器消除）</summary>");
            sb.Append(indent).Append("partial void ").Append(model.ChangedHookName).AppendLine("();");
        }

        /// <summary>
        /// 描述符条目：一处声明，属性面板 / 绑定引擎 / 渲染落地三处共用同一份元数据。
        /// 方法名固定为 <c>AddOwnGeneratedProperties</c> —— <see cref="VisionMaster.Scada.Controls.ScadaPropertyCatalog"/>
        /// 按这个名字沿继承链收集（含 StateVisualElement 这类中间基类），所以改名要两边一起改。
        /// </summary>
        private static void AppendCatalog(StringBuilder sb, List<PropertyModel> members, string indent)
        {
            sb.AppendLine();
            sb.Append(indent).AppendLine("/// <summary>本类（不含基类）全部 [ScadaProperty] 声明的属性描述符，按源码次序。</summary>");
            sb.Append(indent).Append("internal static void AddOwnGeneratedProperties(")
              .Append("global::System.Collections.Generic.List<global::VisionMaster.Scada.Controls.ElementPropertyDescriptor> sink)").AppendLine();
            sb.Append(indent).AppendLine("{");

            foreach (var model in members)
            {
                sb.Append(indent).Append("    sink.Add(new global::VisionMaster.Scada.Controls.ElementPropertyDescriptor").AppendLine();
                sb.Append(indent).AppendLine("    {");
                sb.Append(indent).Append("        Key = \"").Append(Escape(model.PropertyBagKey!)).AppendLine("\",");
                sb.Append(indent).Append("        DisplayName = \"").Append(Escape(model.DisplayName!)).AppendLine("\",");
                sb.Append(indent).Append("        Kind = global::VisionMaster.Scada.Controls.ElementPropertyKind.")
                  .Append(model.KindName).AppendLine(",");
                sb.Append(indent).Append("        Group = \"").Append(Escape(model.Group!)).AppendLine("\",");
                sb.Append(indent).Append("        DefaultValue = \"").Append(Escape(model.DefaultValue)).AppendLine("\",");

                if (model.IsBindable)
                    sb.Append(indent).AppendLine("        IsBindable = true,");

                if (model.Description is { Length: > 0 } description)
                    sb.Append(indent).Append("        Description = \"").Append(Escape(description)).AppendLine("\",");

                if (model.KindName == "Number")
                {
                    if (model.Min != SentinelMin)
                        sb.Append(indent).Append("        Min = ").Append(Number(model.Min)).AppendLine(",");
                    if (model.Max != SentinelMax)
                        sb.Append(indent).Append("        Max = ").Append(Number(model.Max)).AppendLine(",");
                }

                if (model.Choices is { Length: > 0 } choices)
                {
                    sb.Append(indent).Append("        Choices = new[] { ");
                    for (var i = 0; i < choices.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append('"').Append(Escape(choices[i])).Append('"');
                    }
                    sb.AppendLine(" },");
                }

                sb.Append(indent).Append("        TargetProperty = ").Append(model.DependencyPropertyName).AppendLine(",");
                sb.Append(indent).AppendLine("    });");
            }

            sb.Append(indent).AppendLine("}");
        }

        /// <summary>生成代码里的字符串字面量转义（描述里带引号或反斜杠时不至于生成语法错误的文件）。</summary>
        private static string Escape(string? text)
            => (text ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");

        /// <summary>数值字面量：往返格式，保证生成代码里的字面量与源码里的取值逐位一致。</summary>
        private static string Number(double value)
            => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

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

            /// <summary>给 typeof(...) 用的类型名：与 PropertyType 相同，但引用类型剥掉可空标注（CS8639）。</summary>
            public string TypeOfType = string.Empty;

            /// <summary>属性的访问修饰符（生成的包装与源声明保持同一档）</summary>
            public string Accessibility = "public";

            public string? PropertyBagKey;
            public string? DisplayName;
            public string? KindName;
            public string? Group = "外观";
            public string DefaultValue = string.Empty;
            public string? Description;
            public bool IsBindable;
            public bool AffectsRender = true;
            public double Min = SentinelMin;
            public double Max = SentinelMax;
            public ImmutableArray<string> Choices = ImmutableArray<string>.Empty;

            public DiagnosticDescriptor? ErrorDescriptor;
            public string? ErrorMessage;
            public LocationInfo? ErrorLocation;

            /// <summary>生成的依赖属性字段名（= 属性名 + Property）</summary>
            public string DependencyPropertyName => PropertyName + "Property";

            /// <summary>生成的副作用钩子名</summary>
            public string ChangedHookName => "On" + PropertyName + "PropertyChanged";

            /// <summary>生成代码里 typeof(...) 用的全限定类型名</summary>
            public string OwnerTypeName =>
                (Namespace.Length > 0 ? Namespace + "." : string.Empty) + ClassName;

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
