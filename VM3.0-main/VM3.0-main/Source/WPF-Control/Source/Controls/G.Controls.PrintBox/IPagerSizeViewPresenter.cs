namespace G.Controls.PrintBox
{
    public interface IPagerSizeViewPresenter
    {
        ObservableCollection<PageSize> Collection { get; }
        PageSize SelectedPagerSizeData { get; }
    }
}
