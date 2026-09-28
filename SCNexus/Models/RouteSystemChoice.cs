using CommunityToolkit.Mvvm.ComponentModel;

namespace SCNexus.Models;

public partial class RouteSystemChoice(string name, bool selected = true) : ObservableObject
{
    public string Name { get; } = name;
    public string Display => Name.Equals("Pyro", StringComparison.OrdinalIgnoreCase) ? "Pyro · опасно" : Name;
    [ObservableProperty] private bool isSelected = selected;
}
