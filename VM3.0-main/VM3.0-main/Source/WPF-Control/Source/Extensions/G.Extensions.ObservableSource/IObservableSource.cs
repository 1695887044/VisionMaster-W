namespace G.Extensions.ObservableSource;

public interface IObservableSource
{
    int MaxValue { get; set; }
    int MinValue { get; set; }
    int PageCount { get; set; }
    int PageIndex { get; set; }
    int Total { get; set; }
    int TotalPage { get; set; }
    int Count { get; }
    int SelectedIndex { get; set; }
}