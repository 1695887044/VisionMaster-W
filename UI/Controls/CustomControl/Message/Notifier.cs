using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace UI.CustomControl
{
    public enum NotificationType { Info, Success, Warning, Error }

    // 1. 通知数据模型
    public  class NotificationMessage
    {
        public string Message { get; set; }
        public NotificationType Type { get; set; }
        public string IconPath { get; set; }
        public Brush IconColor { get; set; }

        public bool IsHovered { get; set; }

        public string Time { get; } = DateTime.Now.ToString("HH:mm:ss");
    }

    // 2. 全局静态管理器
    public static class Notifier
    {
        // 绑定到前台的全局消息队列
        public static ObservableCollection<NotificationMessage> Messages { get; } = new ObservableCollection<NotificationMessage>();

        // 各种极其方便的快捷调用
        public static void ShowInfo(string message) => Show(message, NotificationType.Info);
        public static void ShowSuccess(string message) => Show(message, NotificationType.Success);
        public static void ShowWarning(string message) => Show(message, NotificationType.Warning);
        public static void ShowError(string message) => Show(message, NotificationType.Error);

        // 核心执行方法
        public static void Show(string message, NotificationType type)
        {
            Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var msg = new NotificationMessage
                {
                    Message = message,
                    Type = type,
                    IconPath = GetIconPath(type),
                    IconColor = GetIconColor(type)
                };

                Messages.Add(msg);

                // 1. 先等待默认的 3 秒
                await Task.Delay(3000);

                // 2. 🚨 核心逻辑：循环检查
                // 如果发现鼠标正悬停在上面，就进入“等待模式”，每隔 0.5 秒检查一次
                while (msg.IsHovered)
                {
                    await Task.Delay(500);
                }

                // 3. 鼠标移开后，再执行删除
                if (Messages.Contains(msg))
                {
                    Messages.Remove(msg);
                }
            });
        }

        // Win11 Fluent 图标库 (SVG Path)
        private static string GetIconPath(NotificationType type)
        {
            return type switch
            {
                NotificationType.Success => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z", // ✅
                NotificationType.Error => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm5 13.59L15.59 17 12 13.41 8.41 17 7 15.59 10.59 12 7 8.41 8.41 7 12 10.59 15.59 7 17 8.41 13.41 12 17 15.59z", // ❌
                NotificationType.Warning => "M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z", // ⚠️
                _ => "M11 7h2v2h-2zm0 4h2v6h-2zm1-9C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z" // ℹ️
            };
        }

        // Win11 极其克制的高级配色
        private static Brush GetIconColor(NotificationType type)
        {
            return type switch
            {
                NotificationType.Success => new SolidColorBrush(Color.FromRgb(16, 124, 16)),  // 微软绿
                NotificationType.Error => new SolidColorBrush(Color.FromRgb(216, 59, 1)),     // 微软红
                NotificationType.Warning => new SolidColorBrush(Color.FromRgb(255, 140, 0)),  // 微软橙
                _ => new SolidColorBrush(Color.FromRgb(0, 95, 184))                           // 微软蓝
            };
        }
    }
}
