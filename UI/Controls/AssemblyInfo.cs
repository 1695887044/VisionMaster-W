using System.Windows;

// 自定义控件（CardPropertyGrid / FlatPropertyGrid 等 DefaultStyleKey 控件）的
// 默认样式查找依据。此前 UI 程序集没有这一声明，主题字典只能靠宿主 App.xaml
// 手动合并 Themes/Generic.xaml 才生效——宿主一旦漏合并，控件静默渲染成空白。
// 声明后 WPF 会直接到本程序集的 Themes/generic.xaml 找默认样式，任何宿主都可用。
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly)]
