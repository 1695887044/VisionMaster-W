using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using VM.Start.Attributes;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Models;

namespace VM.Start.Services
{
    public class PluginService
    {
        /// <summary>
        /// 模块插件字典
        /// </summary>
        public static Dictionary<string, PluginsInfo> PluginDic_Module =
            new Dictionary<string, PluginsInfo>();

        /// <summary>
        /// 相机插件字典
        /// </summary>
        public static Dictionary<string, PluginsInfo> PluginDic_Camera =
            new Dictionary<string, PluginsInfo>();

        /// <summary>
        /// 激光插件字典
        /// </summary>
        public static Dictionary<string, PluginsInfo> PluginDic_Laser =
            new Dictionary<string, PluginsInfo>();

        /// <summary>
        /// 轴卡插件字典
        /// </summary>
        public static Dictionary<string, PluginsInfo> PluginDic_Motion =
            new Dictionary<string, PluginsInfo>();

        public static void InitPlugin()
        {
            string PlugInsDir = Path.Combine(System.Environment.CurrentDirectory, "Plugins\\");
            if (Directory.Exists(PlugInsDir) == false)
                return; //判断是否存在

            //判断是否是UI.dll
            foreach (var dllFile in Directory.GetFiles(PlugInsDir))
            {
                try
                {
                    FileInfo fi = new FileInfo(dllFile);
                    //判断是否是Plugin.xxxxxxx.dll
                    if (!fi.Name.StartsWith("Plugin.") || !fi.Name.EndsWith(".dll"))
                        continue;

                    Assembly assemPlugIn = AppDomain.CurrentDomain.Load(
                        Assembly.LoadFile(fi.FullName).GetName()
                    ); // 该方法会占用文件 但可以调试

                    //判断是否继承ModuleBase或者CameraBase
                    foreach (Type type in assemPlugIn.GetTypes())
                    {
                        if (
                            typeof(ModuleBase).IsAssignableFrom(type)
                            || typeof(CameraBase).IsAssignableFrom(type)
                            || typeof(MotionBase).IsAssignableFrom(type)
                            || typeof(ILaserDevice).IsAssignableFrom(type)
                        ) //是ModuleBase或者CameraBase的子类
                        {
                            PluginsInfo info = new PluginsInfo();
                            //获取插件名称
                            if (GetPluginInfo(assemPlugIn, type, ref info))
                            {
                                if (info.Category == "相机")
                                {
                                    PluginDic_Camera[info.ModuleName] = info;
                                }
                                else if (info.Category == "轴卡")
                                {
                                    PluginDic_Motion[info.ModuleName] = info;
                                }
                                else if (info.Category == "激光")
                                {
                                    PluginDic_Laser[info.ModuleName] = info;
                                }
                                else
                                {
                                    PluginDic_Module[info.ModuleName] = info;
                                }
                            }
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.AddLog(ex.ToString());

                }
            }
        }

        /// <summary>
        /// 获取插件类别
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static bool GetPluginInfo(Assembly assemPlugIn, Type type, ref PluginsInfo info)
        {
            try
            {
                System.Attribute[] attributeObjs = System.Attribute.GetCustomAttributes(type);
                foreach (var item in attributeObjs)
                {
                    if (item is ModuleImageNameAttribute)
                    {
                        info.ImageName = (item as ModuleImageNameAttribute).ImageName;
                    }
                    else if (item is CategoryAttribute)
                    {
                        info.Category = (item as CategoryAttribute).Category;
                    }
                    else if (item is DisplayNameAttribute)
                    {
                        info.ModuleName = (item as DisplayNameAttribute).DisplayName;
                    }
                }
                info.ModuleType = type;

                //判断是否包含 ModuleViewBase
                foreach (Type tempType in assemPlugIn.GetTypes())
                {
                    if (typeof(ModuleViewBase).IsAssignableFrom(tempType)) //是ModuleViewBase的子类
                    {
                        info.ModuleViewType = tempType;
                        return true;
                    }
                }
                return true;
            }
            catch (Exception ex) { }
            return false;
        }
    }
}
