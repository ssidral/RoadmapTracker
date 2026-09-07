using Microsoft.EntityFrameworkCore;
using RoadmapTracker.Data;
using RoadmapTracker.Models;

namespace RoadmapTracker.Services;

public class SqliteLogService
{
    private readonly AppDbContext _db;

    public SqliteLogService(AppDbContext db)
    {
        _db = db;
    }

    public async Task InitializeAsync()
    {
        await _db.Database.EnsureCreatedAsync();

        if (!await _db.Profiles.AnyAsync())
        {
            var defaultProfile = new UserProfile
            {
                Id = "p-dev",
                Username = "DevOps Engineer",
                RoleTitle = "Platform & .NET Transition",
                Targets = new List<LearningTarget>
                {
                    new()
                    {
                        Id = "t-dotnet",
                        Title = "60-Day .NET Core Mastery",
                        TargetDays = 60,
                        Description = "C#, Minimal APIs, and SQLite Integration"
                    },
                    new()
                    {
                        Id = "t-devops",
                        Title = "30-Day Enterprise DevOps",
                        TargetDays = 30,
                        Description = "Containers, RKE2, GitOps, and CI/CD"
                    }
                }
            };

            _db.Profiles.Add(defaultProfile);
            _db.Settings.Add(new AppSetting { Key = "ActiveProfileId", Value = defaultProfile.Id });
            await _db.SaveChangesAsync();
        }
    }

    public async Task<AppDatabase> GetFullDatabaseAsync()
    {
        var profiles = await _db.Profiles
            .Include(p => p.Targets)
            .ThenInclude(t => t.Logs)
            .AsNoTracking()
            .ToListAsync();

        var activeSetting = await _db.Settings.FindAsync("ActiveProfileId");

        return new AppDatabase
        {
            ActiveProfileId = activeSetting?.Value ?? profiles.FirstOrDefault()?.Id ?? "",
            Profiles = profiles
        };
    }

    public async Task<UserProfile> CreateProfileAsync(string username, string roleTitle)
    {
        var profile = new UserProfile
        {
            Username = username,
            RoleTitle = roleTitle,
            Targets = new List<LearningTarget>
            {
                new() { Title = "Default 30-Day Target", TargetDays = 30, Description = "Initial learning roadmap" }
            }
        };

        _db.Profiles.Add(profile);
        await SetActiveProfileAsync(profile.Id);
        await _db.SaveChangesAsync();
        return profile;
    }

    public async Task<bool> DeleteProfileAsync(string profileId)
    {
        if (await _db.Profiles.CountAsync() <= 1) return false;

        var profile = await _db.Profiles.FindAsync(profileId);
        if (profile is null) return false;

        _db.Profiles.Remove(profile);
        await _db.SaveChangesAsync();

        var activeSetting = await _db.Settings.FindAsync("ActiveProfileId");
        if (activeSetting?.Value == profileId)
        {
            var fallback = await _db.Profiles.FirstAsync();
            activeSetting.Value = fallback.Id;
            await _db.SaveChangesAsync();
        }

        return true;
    }

    public async Task SetActiveProfileAsync(string profileId)
    {
        var setting = await _db.Settings.FindAsync("ActiveProfileId");
        if (setting is null)
        {
            _db.Settings.Add(new AppSetting { Key = "ActiveProfileId", Value = profileId });
        }
        else
        {
            setting.Value = profileId;
        }
        await _db.SaveChangesAsync();
    }

    public async Task<LearningTarget> CreateTargetAsync(string profileId, LearningTarget target)
    {
        target.UserProfileId = profileId;
        if (string.IsNullOrWhiteSpace(target.Id))
        {
            target.Id = Guid.NewGuid().ToString("N")[..8];
        }

        _db.Targets.Add(target);
        await _db.SaveChangesAsync();
        return target;
    }

    public async Task<bool> DeleteTargetAsync(string profileId, string targetId)
    {
        var target = await _db.Targets.FirstOrDefaultAsync(t => t.Id == targetId && t.UserProfileId == profileId);
        if (target is null) return false;

        _db.Targets.Remove(target);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task UpsertLogAsync(string targetId, DailyLog log)
    {
        var existing = await _db.DailyLogs
            .FirstOrDefaultAsync(l => l.LearningTargetId == targetId && l.DayNumber == log.DayNumber);

        if (existing is null)
        {
            log.LearningTargetId = targetId;
            _db.DailyLogs.Add(log);
        }
        else
        {
            existing.Date = log.Date;
            existing.Topic = log.Topic;
            existing.TheoryMinutes = log.TheoryMinutes;
            existing.LabMinutes = log.LabMinutes;
            existing.GitPushed = log.GitPushed;
            existing.Notes = log.Notes;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<bool> DeleteLogAsync(string targetId, int dayNumber)
    {
        var log = await _db.DailyLogs
            .FirstOrDefaultAsync(l => l.LearningTargetId == targetId && l.DayNumber == dayNumber);

        if (log is null) return false;

        _db.DailyLogs.Remove(log);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreDatabaseAsync(AppDatabase restoredDb)
    {
        if (restoredDb.Profiles == null || restoredDb.Profiles.Count == 0) return false;

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // Clear existing records
            _db.DailyLogs.RemoveRange(_db.DailyLogs);
            _db.Targets.RemoveRange(_db.Targets);
            _db.Profiles.RemoveRange(_db.Profiles);
            _db.Settings.RemoveRange(_db.Settings);
            await _db.SaveChangesAsync();

            // Re-insert full hierarchy
            foreach (var profile in restoredDb.Profiles)
            {
                foreach (var target in profile.Targets)
                {
                    target.UserProfileId = profile.Id;
                    foreach (var log in target.Logs)
                    {
                        log.Id = 0; // Let SQLite assign incremental primary keys
                        log.LearningTargetId = target.Id;
                    }
                }
            }

            await _db.Profiles.AddRangeAsync(restoredDb.Profiles);

            var activeId = !string.IsNullOrWhiteSpace(restoredDb.ActiveProfileId) &&
                           restoredDb.Profiles.Any(p => p.Id == restoredDb.ActiveProfileId)
                ? restoredDb.ActiveProfileId
                : restoredDb.Profiles.First().Id;

            _db.Settings.Add(new AppSetting { Key = "ActiveProfileId", Value = activeId });

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return true;
        }
        catch
        {
            await tx.RollbackAsync();
            return false;
        }
    }
}
