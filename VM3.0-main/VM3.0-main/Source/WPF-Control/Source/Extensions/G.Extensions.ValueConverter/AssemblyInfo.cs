global using G.Extensions.Common;
global using G.Extensions.Mvvm.Commands;
global using System;
global using System.Collections.Generic;
global using System.ComponentModel;
global using System.ComponentModel.DataAnnotations;
global using System.IO;
global using System.Linq;
global using System.Windows;
global using System.Windows.Data;
global using System.Windows.Input;
global using System.Windows.Markup;
global using System.Windows.Media;
global using System.Windows.Media.Imaging;

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None, //where theme specific resource dictionaries are located
                                     //(used if a resource is not found in the page,
                                     // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly //where the generic resource dictionary is located
                                              //(used if a resource is not found in the page,
                                              // app, or any theme specific resource dictionaries)
)]

[assembly: XmlnsDefinition("https://github.com/G", "G.Extensions.ValueConverter")]
[assembly: XmlnsDefinition("https://github.com/G", "G.Extensions.ValueConverter.Files")]
[assembly: XmlnsDefinition("https://github.com/G", "G.Extensions.ValueConverter.Images")]
[assembly: XmlnsPrefix("https://github.com/G", "h")]

[assembly: XmlnsDefinition("http://schemas.microsoft.com/winfx/2006/xaml/presentation", "G.Extensions.ValueConverter")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/winfx/2006/xaml/presentation", "G.Extensions.ValueConverter.Files")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/winfx/2006/xaml/presentation", "G.Extensions.ValueConverter.Images")]
[assembly: XmlnsPrefix("http://schemas.microsoft.com/winfx/2006/xaml/presentation", "h")]
