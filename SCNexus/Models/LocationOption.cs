namespace SCNexus.Models;

public sealed record LocationOption(string System, string Name)
{
    public string Display => $"{System} · {Name}";
}
