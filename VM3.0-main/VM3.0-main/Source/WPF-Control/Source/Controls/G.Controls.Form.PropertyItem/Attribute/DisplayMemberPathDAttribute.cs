namespace G.Controls.Form.PropertyItem.Attribute
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public class DisplayMemberPathAttribute : System.Attribute
    {
        public DisplayMemberPathAttribute(string path)
        {
            this.Path = path;
        }
        public string Path { get; }
    }

    
}
