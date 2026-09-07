using Microsoft.EntityFrameworkCore;
using RoadmapTracker.Models;

namespace RoadmapTracker.Data;

public class AppDbContext : DbContext
{
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<UserProfile> Profiles => Set<UserProfile>();
    public DbSet<LearningTarget> Targets => Set<LearningTarget>();
    public DbSet<DailyLog> DailyLogs => Set<DailyLog>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSetting>().HasKey(s => s.Key);

        modelBuilder.Entity<UserProfile>()
            .HasMany(p => p.Targets)
            .WithOne()
            .HasForeignKey(t => t.UserProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LearningTarget>()
            .HasMany(t => t.Logs)
            .WithOne()
            .HasForeignKey(l => l.LearningTargetId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DailyLog>()
            .HasIndex(l => new { l.LearningTargetId, l.DayNumber })
            .IsUnique();
    }
}
