using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using UAI.Data;

namespace UAI;

/// <summary>Lets `dotnet ef migrations` work without booting the whole web host.</summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var raw = Environment.GetEnvironmentVariable("DATABASE_URL");
        var conn = string.IsNullOrWhiteSpace(raw)
            ? "Host=localhost;Database=uai;Username=postgres;Password=postgres"
            : raw;

        var b = new DbContextOptionsBuilder<AppDbContext>();
        b.UseNpgsql(conn);
        return new AppDbContext(b.Options);
    }
}
