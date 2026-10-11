global using G.Common.Interfaces;
global using G.Extensions.Mvvm.ViewModels.Base;
global using G.Services.Serializable;
global using System.IO;
global using System.Reflection;
global using System.Xml.Serialization;
using G.Services.AppPath;
using G.Services.Setting;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace G.Extensions.Setting;

public abstract class SettableBase : DisplayBindableBase, ISettable, ILoadable, ISaveable, IDefaultable, IClearable
{
    [XmlIgnore]
    [JsonIgnore]
    [Browsable(false)]
    public bool IsVisibleInSetting { get; set; } = true;

    public override void LoadDefault()
    {
        base.LoadDefault();
    }

    protected virtual string GetDefaultPath()
    {
        return System.IO.Path.Combine(this.GetDefaultFolder(), this.GetType().Name + ".json");
    }

    protected virtual string GetDefaultFolder()
    {
        if (this is ILoginedSplashLoadable)
            return AppPaths.Instance.UserSetting;
        return AppPaths.Instance.Setting;
    }

    public virtual bool Save(out string message)
    {
        message = null;
        string path = this.GetDefaultPath();
        string folder = Path.GetDirectoryName(path);
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);
        this.GetSerializerService()?.Save(path, this);
        return true;
    }

    protected virtual ISerializerService GetSerializerService()
    {
        return new TextJsonSerializerService();
    }

    public virtual bool Load(out string message)
    {
        var path = this.GetDefaultPath();
        if (!File.Exists(path))
        {
            message = "文件不存在";
            return false;
        }
        message = null;
        this.Load(path);
        return true;
    }

    protected virtual void Load(string path)
    {
        if (!File.Exists(path))
            return;
        ISettable load = this.GetSerializerService()?.Load(path, this.GetType()) as ISettable;
        if (load == null)
            return;
        this.Load(load);
    }

    public virtual void Load(ISettable settable)
    {
        PropertyInfo[] ps = this.GetType().GetProperties();
        foreach (PropertyInfo p in ps)
        {
            if (p.GetCustomAttribute<XmlIgnoreAttribute>() != null)
                continue;
            if (p.CanRead && p.CanWrite)
            {
                object v = p.GetValue(settable);
                p.SetValue(this, v);
            }
        }
    }

    public virtual bool IsInit()
    {
        return !File.Exists(this.GetDefaultPath());
    }

    public virtual void Clear()
    {
        var path = this.GetDefaultPath();
        if (File.Exists(path))
            File.Delete(path);
    }
}
