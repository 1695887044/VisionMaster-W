using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;

// 一次性工具：反射 Modules\Plugin*.dll，导出插件显示名 / 配置键 / 端口。
// 输出是生成 .vms 的唯一元数据依据；用完可删（R167/R168）。

var modules = args.Length > 0 ? args[0] : @"D:\C#\VM\Modules";
var probeDirs = new List<string>
{
    modules,
    @"D:\C#\VM\bin",
    @"D:\C#\VM\VisionMaster\bin\Debug\net9.0-windows",
    @"D:\C#\VM\DLL\Halcon",
};
AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
{
    var name = new AssemblyName(e.Name).Name;
    foreach (var d in probeDirs)
    {
        if (!Directory.Exists(d)) continue;
        var candidate = Path.Combine(d, name + ".dll");
        if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
    }
    return null;
};
var files = Directory.GetFiles(modules, "Plugin*.dll").OrderBy(x => x).ToArray();

Console.WriteLine("== " + files.Length + " plugin assemblies ==");

foreach (var file in files)
{
    Assembly asm;
    try { asm = Assembly.LoadFrom(file); }
    catch (Exception ex) { Console.WriteLine($"!! load {Path.GetFileName(file)}: {ex.Message}"); continue; }

    Type[] types;
    try { types = asm.GetTypes(); }
    catch (ReflectionTypeLoadException ex)
    {
        types = ex.Types.Where(t => t != null).ToArray()!;
        Console.WriteLine($"!! partial {Path.GetFileName(file)}: {ex.LoaderExceptions.FirstOrDefault()?.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"!! types {Path.GetFileName(file)}: {ex.GetType().Name}: {ex.Message}");
        continue;
    }

    Console.WriteLine($"-- {Path.GetFileName(file)} types={types.Length}");

    foreach (var t in types.OrderBy(t => t.FullName))
    {
        var dispData = t.GetCustomAttributesData()
            .FirstOrDefault(a => a.AttributeType.FullName == "System.ComponentModel.DataAnnotations.DisplayAttribute");
        if (dispData == null) continue;
        var name = dispData.NamedArguments.FirstOrDefault(x => x.MemberName == "Name").TypedValue.Value as string;
        var group = dispData.NamedArguments.FirstOrDefault(x => x.MemberName == "GroupName").TypedValue.Value as string;
        if (string.IsNullOrEmpty(group)) continue;
        var disp = new { Name = name };

        var aqn = t.AssemblyQualifiedName;
        var parallel = t.GetCustomAttributes().Any(a => a.GetType().Name == "ParallelSafeAttribute");
        Console.WriteLine();
        Console.WriteLine($"### {disp.Name} | {group} | {t.FullName}");
        Console.WriteLine($"AQN={aqn}");
        Console.WriteLine($"PARALLEL_SAFE={parallel}");

        var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttributes().Any(a => a.GetType().Name == "StepConfigAttribute"))
            .OrderBy(p => p.MetadataToken);

        foreach (var p in props)
        {
            var dv = p.GetCustomAttribute<DefaultValueAttribute>();
            var dvText = dv == null ? "" : " default=" + Format(dv.Value);
            Console.WriteLine($"  CFG {p.Name} : {Friendly(p.PropertyType)}{dvText}");
        }

        object instance = null;
        try { instance = Activator.CreateInstance(t); }
        catch (Exception ex) { Console.WriteLine($"  !! ctor: {ex.GetType().Name}: {ex.Message}"); }

        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.MetadataToken))
        {
            var pt = p.PropertyType;
            if (pt.IsGenericType && pt.GetGenericTypeDefinition().Name == "InputPort`1")
            {
                var portName = p.Name;
                var required = "?";
                try
                {
                    var port = p.GetValue(instance);
                    if (port != null)
                    {
                        portName = port.GetType().GetProperty("Name")?.GetValue(port) as string ?? p.Name;
                        required = port.GetType().GetProperty("IsRequired")?.GetValue(port)?.ToString() ?? "?";
                    }
                }
                catch { }
                Console.WriteLine($"  IN  {p.Name} -> \"{portName}\" : {Friendly(pt.GetGenericArguments()[0])} required={required}");
            }
            else if (pt.IsGenericType && pt.GetGenericTypeDefinition().Name == "OutputPort`1")
            {
                var portName = p.Name;
                try
                {
                    var port = p.GetValue(instance);
                    if (port != null)
                        portName = port.GetType().GetProperty("Name")?.GetValue(port) as string ?? p.Name;
                }
                catch { }
                Console.WriteLine($"  OUT {p.Name} -> \"{portName}\" : {Friendly(pt.GetGenericArguments()[0])}");
            }
        }
    }
}

static string Friendly(Type t)
{
    if (t.IsArray) return Friendly(t.GetElementType()!) + "[]";
    if (t.IsGenericType)
    {
        var name = t.Name.Split('`')[0];
        return name + "<" + string.Join(",", t.GetGenericArguments().Select(Friendly)) + ">";
    }
    return t.Name;
}

static string Format(object v)
{
    if (v == null) return "null";
    if (v is string s) return "\"" + s + "\"";
    if (v is IEnumerable e && v is not string)
        return "[" + string.Join(",", e.Cast<object>().Select(Format)) + "]";
    return v.ToString() ?? "";
}
