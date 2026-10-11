namespace G.Controls.Form.PropertyItem.TextPropertyItems
{
    public class DeleteSystemPathTextPropertyItem : OpenDeleteSystemPathTextPropertyItem
    {
        public DeleteSystemPathTextPropertyItem(PropertyInfo property, object obj) : base(property, obj)
        {

        }

        protected override IEnumerable<IDisplayCommand> CreateCommands()
        {
            yield return this.ClearCommand;
        }
    }
}
