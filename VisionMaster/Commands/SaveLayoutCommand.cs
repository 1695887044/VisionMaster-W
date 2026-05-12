using AvalonDock;
using AvalonDock.Layout.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using UI.Core;

namespace VisionMaster.Commands
{
    internal class SaveLayoutCommand : MarkupCommandBase
    {
        // 🚨 补充1：定义布局配置文件的标准保存路径 (程序运行目录下的 Layout.xml)
        private readonly string _layoutFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Layout.xml");

        public override void Execute(object parameter)
        {
            // 情况 A：通过鼠标事件触发 (比如附加行为 Behavior 传进来的事件参数)
            if (parameter is MouseButtonEventArgs args && args.OriginalSource is FrameworkElement element)
            {
                LoadLayoutFromElement(element);
            }
            // 情况 B：通过 CommandParameter 传递 UI 元素本身 (更推荐的 MVVM 写法)
            else if (parameter is FrameworkElement fe)
            {
                LoadLayoutFromElement(fe);
            }
        }

        /// <summary>
        /// 执行核心加载逻辑
        /// </summary>
        private void LoadLayoutFromElement(FrameworkElement element)
        {
            try
            {
                Window root = Window.GetWindow(element);

                // 保险 2：如果还是跨越不过去（比如在极其复杂的右键菜单里）
                // 直接向全局应用程序要 MainWindow！(在工控单窗口软件中，这是最稳的方法)
                if (root == null)
                {
                    root = Application.Current.MainWindow;
                }

                // 防呆保护
                if (root == null) return;


                // 2. 向下搜索：在主窗口中寻找 DockingManager (建议变量名见名知意，改为 dockManager)
                DockingManager dockManager = root.GetChildren<DockingManager>().FirstOrDefault();
                if (dockManager == null) return;

                // 3. 执行反序列化
                var serializer = new XmlLayoutSerializer(dockManager);
                using (var stream = new StreamWriter(_layoutFilePath))
                {
                    serializer.Serialize(stream);
                }

                // 可选：这里可以通过全局消息/日志系统记录 "布局加载成功"
            }
            catch (Exception ex)
            {
                // 🚨 补充3：工业现场保护 - 捕捉由于断电导致 XML 损坏引起的异常
                // 如果 XML 损坏，反序列化会崩溃。我们捕获它，软件依然能正常启动（只是使用默认布局）
                Console.WriteLine($"布局加载失败，将使用默认布局。原因：{ex.Message}");

                // 极端情况下，如果文件已损坏导致每次启动都报错，可以选择将其物理删除
                // File.Delete(_layoutFilePath); 
            }
        }
    }
}
