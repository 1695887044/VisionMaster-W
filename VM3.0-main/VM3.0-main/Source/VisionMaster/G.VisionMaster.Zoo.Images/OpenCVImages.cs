using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;

namespace G.VisionMaster.Zoo.Images;

public class OpenCVImages
{
    public static IEnumerable<ImageSource> GetImageSources()
    {
        System.Reflection.FieldInfo[] ms = typeof(ImagePath).GetFields();
        TypeConverter c = TypeDescriptor.GetConverter(typeof(ImageSource));
        //string format = "pack://application:,,,/{0}";
        string format = "pack://application:,,,/G.VisionMaster.OpenCV;component/{0}";
        foreach (System.Reflection.FieldInfo item in ms)
        {
            object v = item.GetValue(null);
            string s = string.Format(format, v);
            System.Diagnostics.Debug.WriteLine(s);
            yield return (ImageSource)c.ConvertFrom(s);
        }
    }
    public static IEnumerable<ImageSource> GetFileImageSources()
    {
        TypeConverter c = TypeDescriptor.GetConverter(typeof(ImageSource));
        foreach (string item in GetImageFiles())
        {
            yield return (ImageSource)c.ConvertFrom(item);
        }
    }

    public static IEnumerable<string> GetImageFiles()
    {
        System.Reflection.FieldInfo[] ms = typeof(ImagePath).GetFields();
        foreach (System.Reflection.FieldInfo item in ms)
        {
            object v = item.GetValue(null);
            yield return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, v.ToString());
        }
    }

    public static IEnumerable<string> GetRelativeImageFiles()
    {
        System.Reflection.FieldInfo[] ms = typeof(ImagePath).GetFields();
        foreach (System.Reflection.FieldInfo item in ms)
        {
            yield return item.GetValue(null).ToString();
        }
    }

    public static IEnumerable<string> GetResourceImageFiles()
    {
        System.Reflection.FieldInfo[] ms = typeof(ImagePath).GetFields();
        foreach (System.Reflection.FieldInfo item in ms)
        {
            object v = item.GetValue(null);
            yield return GetResourceFilePath(v.ToString());
        }
    }

    public static string GetResourceFilePath(string dataPath)
    {
        return $"pack://application:,,,/G.VisionMaster.OpenCV;component/{dataPath}";
    }
}
