using G.Common.Interfaces;

namespace G.Presenters.Design.Base;

public interface ICloneableDesignPresenter : IDesignPresenter
{
    ICloneableDesignPresenter Clone();
}

