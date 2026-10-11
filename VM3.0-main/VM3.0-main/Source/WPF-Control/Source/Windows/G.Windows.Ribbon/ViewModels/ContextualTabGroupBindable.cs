// Copyright 漏 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using G.Extensions.Mvvm.ViewModels.Base;
using G.Mvvm.ViewModels.Base;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace G.Windows.Ribbon
{
    public class ContextualTabGroupBindable : BindableBase
    {
        public ContextualTabGroupBindable()
            : this(null)
        {
        }

        public ContextualTabGroupBindable(string header)
        {
            this.Header = header;
        }
        public string Header
        {
            get
            {
                return _header;
            }

            set
            {
                if (_header != value)
                {
                    _header = value;
                    RaisePropertyChanged();
                }
            }
        }
        private string _header;

        public bool IsVisible
        {
            get
            {
                return _isVisible;
            }

            set
            {
                if (_isVisible != value)
                {
                    _isVisible = value;
                    RaisePropertyChanged();
                }
            }
        }
        private bool _isVisible;

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
    }
}

