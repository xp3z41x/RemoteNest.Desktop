using Microsoft.EntityFrameworkCore;

namespace RemoteNest.Data;

/// <summary>
/// Creates short-lived <see cref="AppDbContext"/> instances for each service operation.
/// Holds a single <see cref="DbContextOptions{AppDbContext}"/> so tests can inject an
/// alternative provider (e.g. SQLite in-memory) without touching production wiring.
/// </summary>
public class AppDbContextFactory
{
    private readonly DbContextOptions<AppDbContext>? _options;

    /// <summary>
    /// Production ctor — contexts use <see cref="AppDbContext.OnConfiguring"/> to resolve the
    /// default on-disk path under %APPDATA%\RemoteNest\remotenest.db.
    /// </summary>
    public AppDbContextFactory() { }

    /// <summary>
    /// Test/DI ctor — contexts are built from the supplied options, bypassing
    /// <see cref="AppDbContext.OnConfiguring"/>.
    /// </summary>
    public AppDbContextFactory(DbContextOptions<AppDbContext> options)
    {
        _options = options;
    }

    public AppDbContext Create() =>
        _options is null ? new AppDbContext() : new AppDbContext(_options);
}
