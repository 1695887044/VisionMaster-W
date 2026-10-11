namespace G.Controls.OrderBox
{
    public interface IProperty
    {
        bool UseDesc { get; }
        bool IsSelected { get; }
        string PropertyName { get; }
        [System.Text.Json.Serialization.JsonIgnore]

        [System.Xml.Serialization.XmlIgnore]
        PropertyInfo PropertyInfo { get; set; }
    }
}
