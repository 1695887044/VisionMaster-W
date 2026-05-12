using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public class PermissionAttribute : Attribute
    {
        public string RequiredRole { get; set; }
        public bool HideIfDenied { get; set; } = false; // 默认：没权限则变灰；true：没权限则隐藏
    }
}
