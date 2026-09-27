namespace SCNexus.Models;

public sealed class PersonalShip
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int CargoScu { get; set; }
    public string Role { get; set; } = "";
    public string BuildNotes { get; set; } = "";
}

public sealed class FlightRecord
{
    public int Id { get; set; }
    public int? ShipId { get; set; }
    public string ShipName { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Destination { get; set; } = "";
    public string Commodity { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public decimal Investment { get; set; }
    public decimal Revenue { get; set; }
    public decimal Expenses { get; set; }
    public decimal Losses { get; set; }
    public decimal Profit => Revenue - Investment - Expenses - Losses;
    public double DurationHours => EndedAtUtc is { } end ? Math.Max(0, (end - StartedAtUtc).TotalHours) : 0;
    public decimal ProfitPerHour => DurationHours > 0 ? Profit / (decimal)DurationHours : 0;
    public string DateDisplay => StartedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public string ProfitDisplay => $"{Profit:+#,##0;-#,##0;0} aUEC";
    public string DurationDisplay => EndedAtUtc is { } end
        ? $"{(int)(end - StartedAtUtc).TotalHours} ч {(end - StartedAtUtc).Minutes} мин" : "В пути";
}

public sealed record ShipSummary(PersonalShip Ship, decimal Earned)
{
    public string Name => Ship.Name;
    public string Role => Ship.Role;
    public string BuildNotes => Ship.BuildNotes;
    public string CargoDisplay => $"{Ship.CargoScu} SCU";
    public string EarnedDisplay => $"{Earned:+#,##0;-#,##0;0} aUEC";
}
