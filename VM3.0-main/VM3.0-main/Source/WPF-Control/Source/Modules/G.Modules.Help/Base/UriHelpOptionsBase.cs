// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using G.Controls.Form.Attributes;
using G.Controls.Form.PropertyItem.TextPropertyItems;
using G.Extensions.Setting;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;

namespace G.Modules.Help.Base;

public class UriHelpOptionsBase<T> : IocOptionInstance<T>, IUriHelpOptions where T : class, new()
{
    private string _uri;
    [System.Text.Json.Serialization.JsonIgnore]
    [XmlIgnore]
    [ReadOnly(true)]
    [PropertyItem(typeof(HyperlinkPropertyItem))]
    [Display(Name = "地址")]
    public string Uri
    {
        get { return _uri; }
        set
        {
            _uri = value;
            RaisePropertyChanged();
        }
    }
}


