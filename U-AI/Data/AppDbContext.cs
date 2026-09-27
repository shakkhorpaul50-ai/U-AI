using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace UAI.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<ChatSession>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(160).IsRequired();
            e.Property(x => x.Mode).HasMaxLength(32).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();
            e.HasIndex(x => x.UpdatedAt);
            e.HasOne(x => x.User)
             .WithMany(u => u.Sessions)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ChatMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Role).HasMaxLength(16).IsRequired();
            e.Property(x => x.Content).IsRequired();
            e.HasIndex(x => new { x.SessionId, x.Id });
            e.HasOne(x => x.Session)
             .WithMany(s => s.Messages)
             .HasForeignKey(x => x.SessionId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
