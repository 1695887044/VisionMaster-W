namespace G.Controls.Form.PropertyItem.Attribute
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public class SelectedValuePathAttribute : System.Attribute
    {
        public SelectedValuePathAttribute(string path)
        {
            this.Path = path;
        }
        public string Path { get; }
    }

    
}
