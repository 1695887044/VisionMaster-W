using System;
using System.Linq;
using System.Reflection;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 反射 dump Nodify 7.3 NodifyEditor / ItemContainer 公开 API。
    /// 仅供段3 开发期一次性确认命令属性签名，不参与断言。
    /// </summary>
    internal static class NodifyReflection
    {
        public static void Dump()
        {
            var asm = Assembly.LoadFrom(@"e:\VM\VisionMaster-W-master\VisionMaster\bin\Debug\net9.0-windows\Nodify.dll");
            var editor = asm.GetType("Nodify.NodifyEditor");
            if (editor == null) { Console.WriteLine("NodifyEditor 类型未找到"); return; }

            Console.WriteLine($"=== {editor.FullName} 的 ICommand 属性 ===");
            foreach (var p in editor.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.PropertyType == typeof(System.Windows.Input.ICommand)))
            {
                Console.WriteLine($"  ICommand {p.Name} {{ get; set; }}");
            }

            var container = asm.GetType("Nodify.ItemContainer");
            if (container != null)
            {
                Console.WriteLine();
                Console.WriteLine($"=== {container.FullName} 的关键属性 ===");
                string[] names = { "Location", "DataContext", "IsSelected", "IsDraggable", "Selectable", "Item" };
                foreach (var p in container.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    Console.WriteLine($"  {p.PropertyType.FullName} {p.Name}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== NodifyEditor 的 Drag 相关事件 ===");
            foreach (var e in editor.GetEvents(BindingFlags.Public | BindingFlags.Instance))
            {
                if (e.Name.Contains("Drag") || e.Name.Contains("Item"))
                    Console.WriteLine($"  {e.EventHandlerType?.FullName} {e.Name}");
            }
        }
    }
}
