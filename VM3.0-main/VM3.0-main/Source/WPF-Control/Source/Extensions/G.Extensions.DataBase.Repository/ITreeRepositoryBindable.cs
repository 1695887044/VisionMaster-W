using G.Extensions.ObservableSource;
using G.Extensions.Mvvm.ViewModels.Tree;

namespace G.Extensions.DataBase.Repository
{
    public interface ITreeRepositoryBindable<TEntity> : IRepositoryBindableBase<TEntity> where TEntity : StringEntityBase, new()
    {
        IObservableSource<TreeNodeBase<TEntity>> Collection { get; set; }
        TreeNodeBase<TEntity> SelectedTreeItem { get; set; }
    }
}
