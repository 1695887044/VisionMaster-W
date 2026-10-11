global using G.Controls.Diagram.Presenter.NodeDatas.Base;
global using G.Mvvm.ViewModels.Base;
using G.Controls.Diagram.Datas;
using G.VisionMaster.ResultPresenter.ResultPresenters;

namespace G.VisionMaster.ResultPresenter;

public static class ResultPresenterExtension
{
    //public static IResultPresenter ToValueResultPresenter<T>(this T value, Action<ValueResultPresenterItem<T>> action = null)
    //{
    //    return value.ToValueResultPresenterItem(action).ToDataGridViewResultData();
    //}

    public static IResultPresenter ToValueResultPresenter<T>(this T value)
    {
        return new ValueResultPresenter<T>(value);
    }

    private static ValueResultPresenterItem<T> ToValueResultPresenterItem<T>(this T value, Action<ValueResultPresenterItem<T>> action = null)
    {
        ValueResultPresenterItem<T> r = new ValueResultPresenterItem<T>() { Value = value };
        action?.Invoke(r);
        return r;
    }

    private static IEnumerable<ValueResultPresenterItem<string>> ToValueResultPresenterItems<T>(this IEnumerable<T> values, Func<T, ValueResultPresenterItem<string>> selector = null)
    {
        foreach (T value in values)
        {
            yield return selector?.Invoke(value);
        }
    }

    private static IEnumerable<ValueResultPresenterItem<T>> ToValueResultPresenterItems<T>(this IEnumerable<T> values, Func<T, ValueResultPresenterItem<T>> selector = null)
    {
        foreach (T value in values)
        {
            yield return selector?.Invoke(value);
        }
    }

    private static IEnumerable<ValueResultPresenterItem<T>> ToValueResultPresenterItems<T>(this IEnumerable<T> values, Action<T, ValueResultPresenterItem<T>> action = null)
    {
        foreach (T value in values)
        {
            ValueResultPresenterItem<T> item = new ValueResultPresenterItem<T>(value);
            action?.Invoke(value, item);
            yield return item;
        }
    }

    private static IResultPresenter ToDataGridValueResultPresenter<T>(this IEnumerable<T> values, Action<T, ValueResultPresenterItem<T>> action = null)
    {
        return values.ToValueResultPresenterItems(action).ToDataGridResultPresenter();
    }

    private static IResultPresenter ToDataGridValueResultPresenter<T>(this IEnumerable<T> values, Func<T, ValueResultPresenterItem<T>> selector = null)
    {
        return values.ToValueResultPresenterItems(selector).ToDataGridResultPresenter();
    }

    private static IResultPresenter ToDataGridValueResultPresenter<T>(this IEnumerable<T> values, Func<T, ValueResultPresenterItem<string>> selector = null)
    {
        return values.ToValueResultPresenterItems(selector).ToDataGridResultPresenter();
    }

    public static IResultPresenter ToDataGridResultPresenter<T>(this IEnumerable<T> values) where T : IResultPresenterItem
    {
        return new DataGridResultPresenter<T>(values);
    }

    private static IResultPresenter ToDataGridResultPresenter<T>(this T value) where T : IResultPresenterItem
    {
        return new DataGridResultPresenter<T>(new ObservableCollection<T>() { value });
    }

    public static IResultPresenter ToDataGridValueResultPresenter<T>(this IEnumerable<T> values, Func<T, string> valueSelector, Func<T, string> nameSelector)
    {
        return values.ToDataGridValueResultPresenter(x => new ValueResultPresenterItem<string>() { Name = nameSelector?.Invoke(x), Value = valueSelector?.Invoke(x) });
    }

    public static IResultPresenter ToDataGridValueResultPresenter<T>(this T value, Func<T, string> valueSelector, Func<T, string> nameSelector) where T : INodeData
    {
        ObservableCollection<T> values = new ObservableCollection<T>() { value };
        return values.ToDataGridValueResultPresenter(valueSelector, nameSelector);
    }

    public static IResultPresenter ToRectangleDataGridResultPresenter(this IEnumerable<Rect> values, Func<Rect, string> nameSelector)
    {
        return values.Select(x => new RectangleResultItem(x) { Name = nameSelector?.Invoke(x) }).ToDataGridResultPresenter();
    }

    public static IResultPresenter ToRectangleDataGridResultPresenter<T>(this IEnumerable<T> values, Func<T, Rect> valueSelector, Func<T, string> nameSelector)
    {
        return values.Select(x => new RectangleResultItem(valueSelector.Invoke(x)) { Name = nameSelector?.Invoke(x) }).ToDataGridResultPresenter();
    }

    public static IResultPresenter ToResultPresenter(this IEnumerable<Tuple<string, double, Rect>> values)
    {
        return values.Select(x => new ScoreRectangleResultItem(x.Item3, x.Item2) { Name = x.Item1 }).ToDataGridResultPresenter();
    }

    public static IResultPresenter ToLineDataGridResultPresenter<T>(this IEnumerable<T> values, Func<T, VisionLine> valueSelector, Func<T, string> nameSelector)
    {
        return values.Select(x => new LineResultItem(valueSelector.Invoke(x).Start, valueSelector.Invoke(x).End) { Name = nameSelector?.Invoke(x) }).ToDataGridResultPresenter();
    }
}
