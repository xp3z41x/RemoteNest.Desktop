namespace RemoteNest.Data;

/// <summary>
/// Creates short-lived AppDbContext instances for each service operation.
/// </summary>
public class AppDbContextFactory
{
    public AppDbContext Create() => new();
}
