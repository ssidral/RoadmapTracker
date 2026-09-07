namespace RoadmapTracker.Models;

public class DailyLog
{
    public int DayNumber { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public string Topic { get; set; } = string.Empty;
    public int TheoryMinutes { get; set; }
    public int LabMinutes { get; set; }
    public bool GitPushed { get; set; }
    public string? Notes { get; set; }
}

public class LearningTarget
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = string.Empty;      // e.g. "60-Day .NET Core"
    public int TargetDays { get; set; } = 60;
    public string Description { get; set; } = string.Empty;
    public List<DailyLog> Logs { get; set; } = new();
}

public class UserProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Username { get; set; } = string.Empty;   // e.g. "Prashant"
    public string RoleTitle { get; set; } = "DevOps Engineer";
    public List<LearningTarget> Targets { get; set; } = new();
}

public class AppDatabase
{
    public string ActiveProfileId { get; set; } = string.Empty;
    public List<UserProfile> Profiles { get; set; } = new();
}