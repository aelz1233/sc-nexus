using Microsoft.EntityFrameworkCore;
using SCNexus.Models;

namespace SCNexus.Data;

public sealed class NexusDbContext(DbContextOptions<NexusDbContext> options) : DbContext(options)
{
    public DbSet<PersonalSettings> PersonalSettings => Set<PersonalSettings>();
    public DbSet<PersonalShip> PersonalShips => Set<PersonalShip>();
    public DbSet<FlightRecord> FlightRecords => Set<FlightRecord>();
}
