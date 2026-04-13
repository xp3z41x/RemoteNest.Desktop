using Microsoft.EntityFrameworkCore;
using RemoteNest.Models;

namespace RemoteNest.Data;

public class AppDbContext : DbContext
{
    public DbSet<ConnectionProfile> ConnectionProfiles => Set<ConnectionProfile>();

    private static readonly string DefaultDbPath = GetDefaultDbPath();

    public AppDbContext()
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
        {
            options.UseSqlite($"Data Source={DefaultDbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConnectionProfile>(entity =>
        {
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.Group);
            entity.HasIndex(e => e.Host);
        });
    }

    private static string GetDefaultDbPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "RemoteNest");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "remotenest.db");
    }
}
