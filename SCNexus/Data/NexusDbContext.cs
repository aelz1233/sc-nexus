using Microsoft.EntityFrameworkCore;
using SCNexus.Models;

namespace SCNexus.Data;

public sealed class NexusDbContext(DbContextOptions<NexusDbContext> options) : DbContext(options)
{
    public DbSet<PersonalSettings> PersonalSettings => Set<PersonalSettings>();
    public DbSet<PersonalShip> PersonalShips => Set<PersonalShip>();
    public DbSet<FlightRecord> FlightRecords => Set<FlightRecord>();
    public DbSet<DataObservation> DataObservations => Set<DataObservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataObservation>().HasIndex(x => x.Fingerprint).IsUnique();
        modelBuilder.Entity<DataObservation>().HasIndex(x => x.TimestampUnixMs);
        modelBuilder.Entity<DataObservation>().HasIndex(x => new { x.Kind, x.RecordKey, x.TimestampUnixMs });
    }
}
