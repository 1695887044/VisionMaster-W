// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

global using G.Mvvm.ViewModels.Base;
using System.Collections.ObjectModel;

namespace G.Windows.Ribbon
{
    public class RibbonBindable : BindableBase
    {
        public ObservableCollection<TabBindable> TabDataCollection
        {
            get
            {
                if (_tabDataCollection == null)
                {
                    _tabDataCollection = new ObservableCollection<TabBindable>();
                }
                return _tabDataCollection;
            }
        }
        private ObservableCollection<TabBindable> _tabDataCollection;

        public ObservableCollection<ContextualTabGroupBindable> ContextualTabGroupDataCollection
        {
            get
            {
                if (_contextualTabGroupDataCollection == null)
                {
                    _contextualTabGroupDataCollection = new ObservableCollection<ContextualTabGroupBindable>();
                    _contextualTabGroupDataCollection.Add(new ContextualTabGroupBindable("Grp ") { IsVisible = true });
                }
                return _contextualTabGroupDataCollection;
            }
        }
        private ObservableCollection<ContextualTabGroupBindable> _contextualTabGroupDataCollection;

        public MenuButtonBindable ApplicationMenuData
        {
            get
            {
                if (_applicationMenuData == null)
                {
                    _applicationMenuData = new MenuButtonBindable(true);
                }
                return _applicationMenuData;
            }
        }
        private MenuButtonBindable _applicationMenuData;
    }
}

