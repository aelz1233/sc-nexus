using CommunityToolkit.Mvvm.ComponentModel;

namespace SCNexus.Models;

public partial class RouteSystemChoice(string name, bool selected = true) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] private bool isSelected = selected;
}
