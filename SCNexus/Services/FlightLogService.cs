using Microsoft.EntityFrameworkCore;
using SCNexus.Models;

namespace SCNexus.Services;

public sealed class FlightLogService(SettingsService settingsService)
{
    public async Task<(List<ShipSummary> Ships, List<FlightRecord> Flights)> LoadAsync()
    {
        await using var db = settingsService.CreateDbContext();
        var ships = await db.PersonalShips.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        var flights = await db.FlightRecords.AsNoTracking().OrderByDescending(x => x.StartedAtUtc).ToListAsync();
        var summaries = ships.Select(ship => new ShipSummary(ship,
            flights.Where(x => x.ShipId == ship.Id && x.EndedAtUtc != null).Sum(x => x.Profit))).ToList();
        return (summaries, flights);
    }

    public async Task<PersonalShip> AddShipAsync(string name, int cargoScu, string role, string buildNotes)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Укажи название корабля.");
        if (cargoScu < 0) throw new ArgumentException("Грузовой объём не может быть отрицательным.");
        await using var db = settingsService.CreateDbContext();
        var ship = new PersonalShip
        {
            Name = name.Trim(), CargoScu = cargoScu,
            Role = role.Trim(), BuildNotes = buildNotes.Trim()
        };
        db.PersonalShips.Add(ship);
        await db.SaveChangesAsync();
        return ship;
    }

    public async Task DeleteShipAsync(int id)
    {
        await using var db = settingsService.CreateDbContext();
        var ship = await db.PersonalShips.FindAsync(id);
        if (ship is null) return;
        db.PersonalShips.Remove(ship);
        await db.SaveChangesAsync();
    }

    public async Task<FlightRecord> StartFlightAsync(int? shipId, string shipName, string origin,
        string destination, string commodity, decimal investment, DateTime? startedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(shipName)) throw new ArgumentException("Выбери корабль.");
        if (investment < 0) throw new ArgumentException("Вложение не может быть отрицательным.");
        await using var db = settingsService.CreateDbContext();
        if (await db.FlightRecords.AnyAsync(x => x.EndedAtUtc == null))
            throw new InvalidOperationException("Сначала заверши текущий рейс.");
        var flight = new FlightRecord
        {
            ShipId = shipId, ShipName = shipName.Trim(), Origin = origin.Trim(),
            Destination = destination.Trim(), Commodity = commodity.Trim(), Investment = investment,
            StartedAtUtc = startedAtUtc ?? DateTime.UtcNow
        };
        db.FlightRecords.Add(flight);
        await db.SaveChangesAsync();
        return flight;
    }

    public async Task<FlightRecord> FinishFlightAsync(int id, decimal investment, decimal revenue,
        decimal expenses, decimal losses, DateTime? endedAtUtc = null)
    {
        if (investment < 0 || revenue < 0 || expenses < 0 || losses < 0)
            throw new ArgumentException("Суммы не могут быть отрицательными.");
        await using var db = settingsService.CreateDbContext();
        var flight = await db.FlightRecords.FindAsync(id) ?? throw new InvalidOperationException("Рейс не найден.");
        if (flight.EndedAtUtc != null) throw new InvalidOperationException("Рейс уже завершён.");
        var end = endedAtUtc ?? DateTime.UtcNow;
        if (end <= flight.StartedAtUtc) throw new ArgumentException("Время завершения должно быть позже начала.");
        flight.Investment = investment;
        flight.Revenue = revenue;
        flight.Expenses = expenses;
        flight.Losses = losses;
        flight.EndedAtUtc = end;
        await db.SaveChangesAsync();
        return flight;
    }
}
