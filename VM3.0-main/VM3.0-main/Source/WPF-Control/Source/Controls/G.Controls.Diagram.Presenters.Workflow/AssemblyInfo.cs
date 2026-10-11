using System.Windows;
using System.Windows.Markup;

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None, //where theme specific resource dictionaries are located
                                     //(used if a resource is not found in the page,
                                     // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly //where the generic resource dictionary is located
                                              //(used if a resource is not found in the page,
                                              // app, or any theme specific resource dictionaries)
)]

[assembly: XmlnsDefinition("https://github.com/G", "G.Controls.Diagram.Presenters.Workflow")]
[assembly: XmlnsPrefix("https://github.com/G", "h")]

[assembly: XmlnsDefinition("https://github.com/G", "G.Controls.Diagram.Presenters.Workflow.Commands")]
[assembly: XmlnsDefinition("https://github.com/G", "G.Controls.Diagram.Presenters.Workflow.Workflows")]
[assembly: XmlnsDefinition("https://github.com/G", "G.Controls.Diagram.Presenters.Workflow.NodeDatas")]
[assembly: XmlnsDefinition("https://github.com/G", "G.Controls.Diagram.Presenters.Workflow")]
[assembly: XmlnsPrefix("https://github.com/G", "h")]

[assembly: XmlnsDefinition("http://schemas.microsoft.com/winfx/2006/xaml/presentation", "G.Controls.Diagram.Presenters.Workflow.Commands")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/winfx/2006/xaml/presentation", "G.Controls.Diagram.Presenters.Workflow.Workflows")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/winfx/2006/xaml/presentation", "G.Controls.Diagram.Presenters.Workflow.NodeDatas")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/winfx/2006/xaml/presentation", "G.Controls.Diagram.Presenters.Workflow")]
[assembly: XmlnsPrefix("http://schemas.microsoft.com/winfx/2006/xaml/presentation", "h")]

