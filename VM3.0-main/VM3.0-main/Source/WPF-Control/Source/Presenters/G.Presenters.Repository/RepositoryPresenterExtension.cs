using System.Windows.Markup;

namespace G.Presenters.Repository;

public class RepositoryPresenterExtension : MarkupExtension
{
    public Type Type { get; set; }
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new RepositoryPresenter(this.Type);
    }
}
