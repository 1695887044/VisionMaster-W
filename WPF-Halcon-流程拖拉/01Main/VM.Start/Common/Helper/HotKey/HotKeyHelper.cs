using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace VM.Start.Common.Helper
{
    public class HotKeyHelper
    {
        /// <summary>
        /// 注册热键
        /// </summary>
        /// <param name="hotKeyModel">热键待注册项</param>
        /// <param name="hWnd">窗口句柄</param>
        /// <returns>成功返回true，失败返回false</returns>
        public static bool RegisterHotKey(HotKeyModel hotKeyModel, IntPtr hWnd, int ID)
        {
            var fsModifierKey = new ModifierKeys();

            //HotKeyManager.UnregisterHotKey(hWnd, HotKeyManager.SaveTemplate_ID);

            // 注册热键
            if (hotKeyModel.SelectType == EType.None)
                fsModifierKey = ModifierKeys.None;
            else if (hotKeyModel.SelectType == EType.Alt)
                fsModifierKey = ModifierKeys.Alt;
            else if (hotKeyModel.SelectType == EType.Ctrl)
                fsModifierKey = ModifierKeys.Control;
            else if (hotKeyModel.SelectType == EType.Shift)
                fsModifierKey = ModifierKeys.Shift;
            else if (hotKeyModel.SelectType == EType.Windows)
                fsModifierKey = ModifierKeys.Windows;

            var result = HotKeyManager.RegisterHotKey(hWnd, ID, fsModifierKey, (int)hotKeyModel.SelectKey);
            return result;
        }
        public static void UnregisterHotKey(IntPtr hWnd, int ID)
        {
            HotKeyManager.UnregisterHotKey(hWnd, ID);
        }

    }
}
