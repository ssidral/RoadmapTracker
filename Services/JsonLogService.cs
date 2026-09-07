using System.Text.Json;
using RoadmapTracker.Models;

namespace RoadmapTracker.Services;

public class JsonLogService
{
    private readonly string _filePath;
    private static readonly SemaphoreSlim _fileLock = new(1, 1);
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonLogService()
    {
        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR") 
                      ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "roadmap.json");

        if (!File.Exists(_filePath))
        {
            var defaultProfile = new UserProfile
            {
                Id = "p-dev",
                Username = "DevOps Engineer",
                RoleTitle = "Platform & .NET Transition",
                Targets = new List<LearningTarget>
                {
                    new() { Id = "t-dotnet", Title = "60-Day .NET Core Mastery", TargetDays = 60, Description = "C#, Minimal APIs, and testing" },
                    new() { Id = "t-devops", Title = "30-Day Enterprise DevOps", TargetDays = 30, Description = "Containers, RKE2, GitOps, and ACS" }
                }
            };

            var seed = new AppDatabase
            {
                ActiveProfileId = defaultProfile.Id,
                Profiles = new List<UserProfile> { defaultProfile }
            };
            File.WriteAllText(_filePath, JsonSerializer.Serialize(seed, _jsonOptions));
        }
    }

    private async Task<AppDatabase> ReadDbUnsafeAsync()
    {
        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<AppDatabase>(stream, _jsonOptions) ?? new AppDatabase();
    }

    private async Task WriteDbUnsafeAsync(AppDatabase db)
    {
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, db, _jsonOptions);
    }

    public async Task<AppDatabase> GetFullDatabaseAsync()
    {
        await _fileLock.WaitAsync();
        try { return await ReadDbUnsafeAsync(); }
        finally { _fileLock.Release(); }
    }

    public async Task<UserProfile> CreateProfileAsync(string username, string roleTitle)
    {
        await _fileLock.WaitAsync();
        try
        {
            var db = await ReadDbUnsafeAsync();
            var profile = new UserProfile
            {
                Username = username,
                RoleTitle = roleTitle,
                Targets = new List<LearningTarget>
                {
                    new() { Title = "Default 30-Day Target", TargetDays = 30, Description = "Initial learning roadmap" }
                }
            };
            db.Profiles.Add(profile);
            db.ActiveProfileId = profile.Id;
            await WriteDbUnsafeAsync(db);
            return profile;
        }
        finally { _fileLock.Release(); }
    }

    public async Task<bool> DeleteProfileAsync(string profileId)
    {
        await _fileLock.WaitAsync();
        try
        {
            var db = await ReadDbUnsafeAsync();
            if (db.Profiles.Count <= 1) return false;

            db.Profiles.RemoveAll(p => p.Id == profileId);
            if (db.ActiveProfileId == profileId)
            {
                db.ActiveProfileId = db.Profiles.First().Id;
            }
            await WriteDbUnsafeAsync(db);
            return true;
        }
        finally { _fileLock.Release(); }
    }

    public async Task SetActiveProfileAsync(string profileId)
    {
        await _fileLock.WaitAsync();
        try
        {
            var db = await ReadDbUnsafeAsync();
            if (db.Profiles.Any(p => p.Id == profileId))
            {
                db.ActiveProfileId = profileId;
                await WriteDbUnsafeAsync(db);
            }
        }
        finally { _fileLock.Release(); }
    }

    public async Task<LearningTarget> CreateTargetAsync(string profileId, LearningTarget target)
    {
        await _fileLock.WaitAsync();
        try
        {
            var db = await ReadDbUnsafeAsync();
            var profile = db.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile is null) throw new InvalidOperationException("Profile not found");

            if (string.IsNullOrWhiteSpace(target.Id)) target.Id = Guid.NewGuid().ToString("N")[..8];
            profile.Targets.Add(target);
            await WriteDbUnsafeAsync(db);
            return target;
        }
        finally { _fileLock.Release(); }
    }

    public async Task<bool> DeleteTargetAsync(string profileId, string targetId)
    {
        await _fileLock.WaitAsync();
        try
        {
            var db = await ReadDbUnsafeAsync();
            var profile = db.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile is null) return false;

            var removed = profile.Targets.RemoveAll(t => t.Id == targetId);
            await WriteDbUnsafeAsync(db);
            return removed > 0;
        }
        finally { _fileLock.Release(); }
    }

    public async Task UpsertLogAsync(string profileId, string targetId, DailyLog log)
    {
        await _fileLock.WaitAsync();
        try
        {
            var db = await ReadDbUnsafeAsync();
            var profile = db.Profiles.FirstOrDefault(p => p.Id == profileId);
            var target = profile?.Targets.FirstOrDefault(t => t.Id == targetId);
            if (target is null) return;

            var idx = target.Logs.FindIndex(l => l.DayNumber == log.DayNumber);
            if (idx >= 0) target.Logs[idx] = log;
            else target.Logs.Add(log);

            await WriteDbUnsafeAsync(db);
        }
        finally { _fileLock.Release(); }
    }

    public async Task<bool> DeleteLogAsync(string profileId, string targetId, int dayNumber)
    {
        await _fileLock.WaitAsync();
        try
        {
            var db = await ReadDbUnsafeAsync();
            var profile = db.Profiles.FirstOrDefault(p => p.Id == profileId);
            var target = profile?.Targets.FirstOrDefault(t => t.Id == targetId);
            if (target is null) return false;

            var removed = target.Logs.RemoveAll(l => l.DayNumber == dayNumber);
            await WriteDbUnsafeAsync(db);
            return removed > 0;
        }
        finally { _fileLock.Release(); }
    }

    public async Task<bool> RestoreDatabaseAsync(AppDatabase restoredDb)
    {
        if (restoredDb.Profiles == null || restoredDb.Profiles.Count == 0)
        {
            return false;
        }

        await _fileLock.WaitAsync();
        try
        {
            // Fallback active profile ID if missing
            if (string.IsNullOrWhiteSpace(restoredDb.ActiveProfileId) || 
                !restoredDb.Profiles.Any(p => p.Id == restoredDb.ActiveProfileId))
            {
                restoredDb.ActiveProfileId = restoredDb.Profiles.First().Id;
            }

            await WriteDbUnsafeAsync(restoredDb);
            return true;
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
