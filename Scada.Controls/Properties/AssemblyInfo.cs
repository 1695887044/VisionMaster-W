using System.Windows;

// 让自定义控件的"默认样式"能被 WPF 自动找到（themes/generic.xaml 查找链）。
//
// 为什么必须显式声明：DefaultStyleKey 解析默认样式时，WPF 会去查控件所在程序集的
// ThemeInfo 特性来决定去哪本字典里找。缺了这条特性，宿主 App.xaml 又没手工合并
// 本库的 Generic.xaml 时，控件会以"裸 Control"的样子出现（没有模板、什么都没有），
// 而且不报任何错——这类问题最难查，所以在这里钉死。
//
// 第一个参数 None：不提供按系统主题区分的字典（工业现场不做深/浅色自动切换）；
// 第二个参数 SourceAssembly：通用字典在本程序集内，即 Themes/Generic.xaml。
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]
