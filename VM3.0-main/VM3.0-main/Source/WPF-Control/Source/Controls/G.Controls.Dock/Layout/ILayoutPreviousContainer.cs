








namespace G.Controls.Dock.Layout
{
    internal interface ILayoutPreviousContainer
    {
        ILayoutContainer PreviousContainer { get; set; }

        string PreviousContainerId { get; set; }
    }
}