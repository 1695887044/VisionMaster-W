namespace G.Extensions.Mvvm.Commands;

public interface IDisplayCommand
{
    string Name { get; set; }
    string Icon { get; set; }
    string Description { get; set; }
    string GroupName { get; set; }
    int Order { get; set; }
}
