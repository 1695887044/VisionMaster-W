using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Serialization;
using VM.Start.Models;

public class UILayoutManager
{
    public static void SaveAsXaml(UIElement element, string filePath)
    {
        // 使用 XamlWriter 将 UI 元素转换为 XAML 字符串
        string xamlString = XamlWriter.Save(element);

        // 将 XAML 字符串写入文件
        File.WriteAllText(filePath, xamlString);
    }

    public static UIElement LoadAsUIElement(string filePath)
    {
        FileStream fileStream = File.OpenRead(filePath);
        UIElement uiElement = (UIElement)XamlReader.Load(fileStream);
        fileStream.Close();
        fileStream.Dispose();
        return uiElement;
    }
}
