global using G.Extensions.Common;
global using G.Extensions.Mvvm.ViewModels.Base;
global using System.Reflection;
using G.Common.Interfaces;

namespace G.Presenters.Design.Base;

public abstract class CloneableDesignPresenterBase : DesignPresenterBase, ICloneable, ICloneable<CloneableDesignPresenterBase>, ICloneableDesignPresenter
{
    //public virtual object Clone()
    //{
    //    return this.CloneBy(x => x.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false);
    //    //return this.CloneXml();
    //    //string txt = JsonSerializer.Serialize(this);
    //    //return JsonSerializer.Deserialize(txt, GetType());
    //}

    public CloneableDesignPresenterBase Clone()
    {
        return ((ICloneable)this).Clone() as CloneableDesignPresenterBase;
    }

    object ICloneable.Clone()
    {
        return this.CloneBy(x => x.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false);
    }

    ICloneableDesignPresenter ICloneableDesignPresenter.Clone()
    {
        return this.Clone();
    }
}

